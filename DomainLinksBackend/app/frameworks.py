from __future__ import annotations

from .config import Settings
from .db import fetch_all, fetch_one


DEFAULT_FRAMEWORK_CODE = "MANDATE-CAPABILITY-STRATEGY"


def list_frameworks(settings: Settings) -> list[dict[str, object]]:
    return fetch_all(
        settings,
        """
        SELECT
            f.FrameworkId,
            f.FrameworkCode,
            f.FrameworkName,
            f.LifecycleStatus,
            published.FrameworkVersionId,
            published.VersionText,
            published.VersionStatus,
            published.PublishedAtUtc
        FROM dbo.Frameworks f
        OUTER APPLY (
            SELECT TOP (1)
                fv.FrameworkVersionId,
                fv.VersionText,
                fv.VersionStatus,
                fv.PublishedAtUtc
            FROM dbo.FrameworkVersions fv
            WHERE fv.FrameworkId = f.FrameworkId
              AND fv.VersionStatus = 'Published'
            ORDER BY fv.PublishedAtUtc DESC, fv.CreatedAtUtc DESC
        ) published
        WHERE f.LifecycleStatus = 'Active'
        ORDER BY f.FrameworkName
        """,
    )


def get_framework(
    settings: Settings,
    framework_code: str = DEFAULT_FRAMEWORK_CODE,
    version_text: str | None = None,
) -> dict[str, object]:
    version = fetch_one(
        settings,
        """
        SELECT TOP (1)
            f.FrameworkId,
            f.FrameworkCode,
            f.FrameworkName,
            f.LifecycleStatus,
            fv.FrameworkVersionId,
            fv.VersionText,
            fv.SummaryText,
            fv.VersionStatus,
            fv.BasedOnVersionId,
            fv.PublishedAtUtc,
            fv.PublishedBy,
            fv.CreatedAtUtc,
            fv.CreatedBy
        FROM dbo.Frameworks f
        JOIN dbo.FrameworkVersions fv ON fv.FrameworkId = f.FrameworkId
        WHERE f.FrameworkCode = ?
          AND (? IS NULL OR fv.VersionText = ?)
          AND (? IS NOT NULL OR fv.VersionStatus = 'Published')
        ORDER BY
            CASE WHEN fv.VersionStatus = 'Published' THEN 0 ELSE 1 END,
            fv.PublishedAtUtc DESC,
            fv.CreatedAtUtc DESC
        """,
        [framework_code, version_text, version_text, version_text],
    )
    if version is None:
        raise LookupError(f"Framework '{framework_code}' was not found.")

    version_id = version["FrameworkVersionId"]
    element_rows = fetch_all(
        settings,
        """
        SELECT
            e.FrameworkElementId,
            e.ParentElementId,
            e.ElementCode,
            e.ElementType,
            e.ElementName,
            e.StatementText,
            e.DefinitionText,
            e.IsFoundational,
            e.DisplayOrder,
            fpl.RelationshipType AS PrincipleRelationshipType,
            fpl.ApplicabilityText,
            p.PrincipleId,
            p.PrincipleCode,
            p.Name AS PrincipleName,
            p.StatementText AS PrincipleStatementText,
            p.ShortStatementText,
            p.RationaleText,
            p.IsImmutable
        FROM dbo.FrameworkElements e
        LEFT JOIN dbo.FrameworkPrincipleLinks fpl
            ON fpl.FrameworkElementId = e.FrameworkElementId
        LEFT JOIN dbo.Principles p ON p.PrincipleId = fpl.PrincipleId
        WHERE e.FrameworkVersionId = ?
        ORDER BY e.DisplayOrder, e.ElementName, p.PrincipleCode
        """,
        [version_id],
    )

    elements_by_id: dict[str, dict[str, object]] = {}
    for row in element_rows:
        element_id = str(row["FrameworkElementId"])
        element = elements_by_id.get(element_id)
        if element is None:
            element = {
                "frameworkElementId": row["FrameworkElementId"],
                "parentElementId": row.get("ParentElementId"),
                "elementCode": row["ElementCode"],
                "elementType": row["ElementType"],
                "elementName": row["ElementName"],
                "statementText": row.get("StatementText"),
                "definitionText": row.get("DefinitionText"),
                "isFoundational": bool(row.get("IsFoundational")),
                "displayOrder": int(row.get("DisplayOrder") or 0),
                "principles": [],
            }
            elements_by_id[element_id] = element
        if row.get("PrincipleId"):
            element["principles"].append(_principle_payload(row))

    relations = fetch_all(
        settings,
        """
        SELECT
            r.FrameworkElementRelationId,
            source.ElementCode AS FromElementCode,
            target.ElementCode AS ToElementCode,
            r.RelationType,
            r.RationaleText
        FROM dbo.FrameworkElementRelations r
        JOIN dbo.FrameworkElements source
            ON source.FrameworkElementId = r.FromElementId
        JOIN dbo.FrameworkElements target
            ON target.FrameworkElementId = r.ToElementId
        WHERE source.FrameworkVersionId = ?
        ORDER BY source.DisplayOrder, target.DisplayOrder, r.RelationType
        """,
        [version_id],
    )
    rules = fetch_all(
        settings,
        """
        SELECT
            cr.FrameworkContextRuleId,
            e.ElementCode,
            cr.ArtifactType,
            cr.ActivityType,
            cr.TriggerStage,
            cr.DeliveryMode,
            cr.IsRequired,
            cr.Priority,
            cr.InstructionText
        FROM dbo.FrameworkContextRules cr
        JOIN dbo.FrameworkElements e
            ON e.FrameworkElementId = cr.FrameworkElementId
        WHERE e.FrameworkVersionId = ?
        ORDER BY cr.Priority, e.DisplayOrder
        """,
        [version_id],
    )
    return {
        "framework": version,
        "elements": list(elements_by_id.values()),
        "relations": relations,
        "contextRules": rules,
    }


def resolve_framework_context(
    settings: Settings,
    *,
    activity_type: str,
    artifact_type: str | None = None,
    source_record_id: str | None = None,
    framework_code: str = DEFAULT_FRAMEWORK_CODE,
    delivery_mode: str = "AIContext",
) -> dict[str, object]:
    framework = get_framework(settings, framework_code)
    version = framework["framework"]
    version_id = version["FrameworkVersionId"]
    normalized_activity = (activity_type or "General").strip()
    normalized_artifact = (artifact_type or "").strip() or None

    rows = fetch_all(
        settings,
        """
        SELECT DISTINCT
            e.FrameworkElementId,
            e.ElementCode,
            e.ElementType,
            e.ElementName,
            e.StatementText,
            e.DefinitionText,
            e.IsFoundational,
            e.DisplayOrder,
            cr.FrameworkContextRuleId,
            cr.ActivityType,
            cr.ArtifactType,
            cr.DeliveryMode,
            cr.IsRequired,
            cr.Priority,
            cr.InstructionText,
            p.PrincipleId,
            p.PrincipleCode,
            p.Name AS PrincipleName,
            p.StatementText AS PrincipleStatementText,
            p.ShortStatementText,
            p.RationaleText,
            p.IsImmutable,
            fpl.RelationshipType AS PrincipleRelationshipType,
            fpl.ApplicabilityText
        FROM dbo.FrameworkElements e
        JOIN dbo.FrameworkContextRules cr
            ON cr.FrameworkElementId = e.FrameworkElementId
        LEFT JOIN dbo.FrameworkPrincipleLinks fpl
            ON fpl.FrameworkElementId = e.FrameworkElementId
        LEFT JOIN dbo.Principles p ON p.PrincipleId = fpl.PrincipleId
        WHERE e.FrameworkVersionId = ?
          AND cr.ActivityType IN ('*', ?)
          AND (cr.ArtifactType IS NULL OR cr.ArtifactType = '*' OR cr.ArtifactType = ?)
          AND cr.DeliveryMode IN ('Both', ?)
        ORDER BY cr.Priority, e.DisplayOrder, p.PrincipleCode
        """,
        [version_id, normalized_activity, normalized_artifact, delivery_mode],
    )

    explicit_element_ids: set[str] = set()
    if source_record_id:
        explicit_rows = fetch_all(
            settings,
            """
            SELECT DISTINCT fal.FrameworkElementId
            FROM dbo.FrameworkArtifactLinks fal
            JOIN dbo.SemanticArtifacts sa
                ON sa.SemanticArtifactId = fal.SemanticArtifactId
            JOIN dbo.FrameworkElements e
                ON e.FrameworkElementId = fal.FrameworkElementId
            WHERE e.FrameworkVersionId = ?
              AND CONVERT(nvarchar(50), sa.SourceRecordId) = ?
              AND (? IS NULL OR sa.ArtifactType = ?)
            """,
            [version_id, source_record_id, normalized_artifact, normalized_artifact],
        )
        explicit_element_ids = {
            str(row["FrameworkElementId"])
            for row in explicit_rows
        }

    elements_by_id: dict[str, dict[str, object]] = {}
    for row in rows:
        element_id = str(row["FrameworkElementId"])
        element = elements_by_id.get(element_id)
        if element is None:
            element = {
                "frameworkElementId": row["FrameworkElementId"],
                "elementCode": row["ElementCode"],
                "elementType": row["ElementType"],
                "elementName": row["ElementName"],
                "statementText": row.get("StatementText"),
                "definitionText": row.get("DefinitionText"),
                "isFoundational": bool(row.get("IsFoundational")),
                "isRequired": bool(row.get("IsRequired")),
                "isExplicitlyLinked": element_id in explicit_element_ids,
                "priority": int(row.get("Priority") or 100),
                "instructionText": row.get("InstructionText"),
                "principles": [],
            }
            elements_by_id[element_id] = element
        if row.get("PrincipleId"):
            principle_id = str(row["PrincipleId"])
            if not any(str(item["principleId"]) == principle_id for item in element["principles"]):
                element["principles"].append(_principle_payload(row))

    for framework_element in framework["elements"]:
        element_id = str(framework_element["frameworkElementId"])
        if element_id not in explicit_element_ids or element_id in elements_by_id:
            continue
        elements_by_id[element_id] = {
            **framework_element,
            "isRequired": True,
            "isExplicitlyLinked": True,
            "priority": 50,
            "instructionText": "Apply the explicit relationship between this framework element and the current instrument.",
        }

    ordered_elements = sorted(
        elements_by_id.values(),
        key=lambda item: (int(item.get("priority") or 100), int(item.get("displayOrder") or 0)),
    )

    return {
        "frameworkCode": version["FrameworkCode"],
        "frameworkName": version["FrameworkName"],
        "versionText": version["VersionText"],
        "activityType": normalized_activity,
        "artifactType": normalized_artifact,
        "sourceRecordId": source_record_id,
        "elements": ordered_elements,
    }


def format_framework_context(context: dict[str, object]) -> str:
    elements = context.get("elements") or []
    if not elements:
        return ""

    lines = [
        "Authoritative organizational framework context",
        f"Framework: {context.get('frameworkName')} v{context.get('versionText')}",
        "Apply this context within the supplied organizational scope. Do not invent a specific mandate, strategy, authority, owner, or capability that was not supplied.",
    ]
    for element in elements:
        if not isinstance(element, dict):
            continue
        lines.append(f"\n{element.get('elementName')} [{element.get('elementCode')}]")
        if element.get("statementText"):
            lines.append(str(element["statementText"]))
        if element.get("definitionText"):
            lines.append(f"Definition: {element['definitionText']}")
        for principle in element.get("principles") or []:
            if not isinstance(principle, dict):
                continue
            statement = principle.get("statementText")
            if statement:
                lines.append(f"Principle: {statement}")
            if principle.get("shortStatementText"):
                lines.append(f"Working expression: {principle['shortStatementText']}")
        if element.get("instructionText"):
            lines.append(f"Application: {element['instructionText']}")
    return "\n".join(lines).strip()


def _principle_payload(row: dict[str, object]) -> dict[str, object]:
    return {
        "principleId": row["PrincipleId"],
        "principleCode": row["PrincipleCode"],
        "principleName": row["PrincipleName"],
        "statementText": row["PrincipleStatementText"],
        "shortStatementText": row.get("ShortStatementText"),
        "rationaleText": row.get("RationaleText"),
        "isImmutable": bool(row.get("IsImmutable")),
        "relationshipType": row.get("PrincipleRelationshipType"),
        "applicabilityText": row.get("ApplicabilityText"),
    }
