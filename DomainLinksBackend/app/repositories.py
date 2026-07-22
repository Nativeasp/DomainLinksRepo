from __future__ import annotations

from pathlib import Path
import re

from .config import Settings
from .db import fetch_all, fetch_one, get_connection, normalize_database_record


_CONTROL_TYPE_SORT_ORDER = {
    "DIRECTIVE": 10,
    "PREVENTIVE": 20,
    "DETERRENT": 30,
    "DETECTIVE": 40,
    "CORRECTIVE": 50,
    "COMPENSATING": 60,
}


def list_domains(settings: Settings) -> list[dict[str, object]]:
    rows = fetch_all(
        settings,
        """
        WITH ActiveDomains AS (
            SELECT
                d.DomainId,
                d.DomainParentId,
                d.DomainTypeId,
                d.DomainOrientationId,
                d.DisplayOrder,
                d.DomainCode,
                d.DisplayName,
                d.Description,
                d.Status
            FROM dbo.Domains d
            WHERE d.Status = 'Active'
        ),
        DomainClosure AS (
            SELECT
                d.DomainId AS AncestorDomainId,
                d.DomainId AS DescendantDomainId
            FROM ActiveDomains d

            UNION ALL

            SELECT
                dc.AncestorDomainId,
                child.DomainId AS DescendantDomainId
            FROM DomainClosure dc
            JOIN ActiveDomains child
                ON child.DomainParentId = dc.DescendantDomainId
        ),
        DirectCollectionCounts AS (
            SELECT
                c.DomainId,
                COUNT(*) AS DirectCollectionCount
            FROM dbo.Collections c
            JOIN ActiveDomains d
                ON d.DomainId = c.DomainId
            WHERE c.Status = 'Active'
            GROUP BY c.DomainId
        ),
        DirectPolicyCounts AS (
            SELECT
                p.RootDomainId AS DomainId,
                COUNT(*) AS DirectPolicyCount
            FROM dbo.Policies p
            JOIN ActiveDomains d
                ON d.DomainId = p.RootDomainId
            WHERE p.Status IN ('Draft', 'Active')
            GROUP BY p.RootDomainId
        ),
        DirectControlCounts AS (
            SELECT
                dc.DomainId,
                COUNT(*) AS DirectControlCount
            FROM dbo.DomainControls dc
            JOIN dbo.Controls c
                ON c.ControlId = dc.ControlId
            JOIN ActiveDomains d
                ON d.DomainId = dc.DomainId
            WHERE c.Status IN ('Draft', 'Active')
            GROUP BY dc.DomainId
        ),
        BranchCounts AS (
            SELECT
                dc.AncestorDomainId AS DomainId,
                SUM(COALESCE(coll.DirectCollectionCount, 0)) AS BranchCollectionCount,
                SUM(COALESCE(pol.DirectPolicyCount, 0)) AS BranchPolicyCount,
                SUM(COALESCE(ctrl.DirectControlCount, 0)) AS BranchControlCount
            FROM DomainClosure dc
            LEFT JOIN DirectCollectionCounts coll
                ON coll.DomainId = dc.DescendantDomainId
            LEFT JOIN DirectPolicyCounts pol
                ON pol.DomainId = dc.DescendantDomainId
            LEFT JOIN DirectControlCounts ctrl
                ON ctrl.DomainId = dc.DescendantDomainId
            GROUP BY dc.AncestorDomainId
        )
        SELECT
            d.DomainId,
            d.DomainParentId,
            d.DomainTypeId,
            d.DomainOrientationId,
            d.DisplayOrder,
            d.DomainCode,
            d.DisplayName,
            d.Description,
            d.Status,
            dt.NAME AS DomainType,
            dor.CODE AS DomainOrientationCode,
            dor.NAME AS DomainOrientation,
            COALESCE(bc.BranchCollectionCount, 0) AS BranchCollectionCount,
            COALESCE(bc.BranchPolicyCount, 0) AS BranchPolicyCount,
            COALESCE(bc.BranchControlCount, 0) AS BranchControlCount
        FROM ActiveDomains d
        LEFT JOIN dbo.DomainTypes dt
            ON dt.ID = d.DomainTypeId
        LEFT JOIN dbo.DomainOrientations dor
            ON dor.ID = d.DomainOrientationId
        LEFT JOIN BranchCounts bc
            ON bc.DomainId = d.DomainId
        ORDER BY
            CASE WHEN d.DomainCode = 'workspace-memory' THEN 0 ELSE 1 END,
            d.DisplayOrder,
            d.DisplayName
        OPTION (MAXRECURSION 32767)
        """,
    )
    return [_normalize_row(row) for row in rows]


def list_domain_orientations(settings: Settings) -> list[dict[str, object]]:
    rows = fetch_all(
        settings,
        """
        SELECT
            ID,
            CODE,
            NAME,
            DESCRIPTION,
            DISPLAY_ORDER
        FROM dbo.DomainOrientations
        ORDER BY DISPLAY_ORDER, NAME
        """,
    )
    return [_normalize_row(row) for row in rows]


def list_domain_types(settings: Settings) -> list[dict[str, object]]:
    rows = fetch_all(
        settings,
        """
        SELECT
            ID,
            CODE,
            NAME,
            DESCRIPTION,
            DOMAIN_LEVEL,
            DISPLAY_ORDER
        FROM dbo.DomainTypes
        WHERE
            (EFFECTIVE_END_DATE IS NULL OR EFFECTIVE_END_DATE >= CAST(SYSDATETIME() AS date))
        ORDER BY DISPLAY_ORDER, NAME
        """,
    )
    return [_normalize_row(row) for row in rows]


def create_domain_type(
    settings: Settings,
    *,
    name: str,
    description: str | None = None,
) -> dict[str, object]:
    normalized_name = name.strip()
    if not normalized_name:
        raise ValueError("Domain type name is required.")

    normalized_code = re.sub(r"[^A-Z0-9]+", "_", normalized_name.upper()).strip("_")
    if not normalized_code:
        raise ValueError("Domain type code could not be generated from the provided name.")

    with get_connection(settings) as conn:
        cursor = conn.cursor()
        cursor.execute(
            """
            IF EXISTS (
                SELECT 1
                FROM dbo.DomainTypes
                WHERE CODE = ?
                   OR NAME = ?
            )
            BEGIN
                THROW 51000, 'A domain type with this code or name already exists.', 1;
            END;

            DECLARE @NextDomainLevel INT =
                COALESCE((SELECT MAX(DOMAIN_LEVEL) + 1 FROM dbo.DomainTypes), 1);
            DECLARE @NextDisplayOrder INT =
                COALESCE((SELECT MAX(DISPLAY_ORDER) + 10 FROM dbo.DomainTypes), 10);

            INSERT INTO dbo.DomainTypes (
                CODE,
                NAME,
                DOMAIN_LEVEL,
                PRIMARY_FOCUS,
                KEY_QUESTION,
                DESCRIPTION,
                DISPLAY_ORDER,
                EFFECTIVE_START_DATE
            )
            OUTPUT
                inserted.ID,
                inserted.CODE,
                inserted.NAME,
                inserted.DESCRIPTION,
                inserted.DOMAIN_LEVEL,
                inserted.DISPLAY_ORDER
            VALUES (
                ?,
                ?,
                @NextDomainLevel,
                ?,
                ?,
                ?,
                @NextDisplayOrder,
                CAST(SYSDATETIME() AS date)
            );
            """,
            [
                normalized_code,
                normalized_name,
                normalized_code,
                normalized_name,
                f"{normalized_name} knowledge, planning, governance, work, and supporting context.",
                f"What belongs in {normalized_name}?",
                description,
            ],
        )
        row = cursor.fetchone()
        conn.commit()

    if row is None:
        raise ValueError("Domain type was not created.")

    return _normalize_row(_row_to_dict(cursor, row))


def list_control_types(settings: Settings) -> list[dict[str, object]]:
    rows = fetch_all(
        settings,
        """
        SELECT
            ID,
            CODE,
            NAME,
            DESCRIPTION,
            DISPLAY_ORDER
        FROM dbo.ControlTypes
        WHERE
            (EFFECTIVE_END_DATE IS NULL OR EFFECTIVE_END_DATE >= CAST(SYSDATETIME() AS date))
        ORDER BY DISPLAY_ORDER, NAME
        """,
    )
    normalized_rows = [_normalize_row(row) for row in rows]
    return sorted(
        normalized_rows,
        key=lambda row: (
            _CONTROL_TYPE_SORT_ORDER.get(str(row.get("CODE") or "").strip().upper(), 999),
            int(row.get("DISPLAY_ORDER") or 0),
            str(row.get("NAME") or "").lower(),
        ),
    )


def list_controls_for_branch(settings: Settings, branch_root_domain_code: str) -> list[dict[str, object]]:
    context = get_control_suggestion_context(settings, branch_root_domain_code)
    root_domain = context["rootDomain"]
    root_domain_code = str(root_domain.get("DomainCode") or "")
    domain_codes = [
        str(domain.get("DomainCode") or "")
        for domain in context.get("branchDomains", [])
        if domain.get("DomainCode")
    ]
    if not domain_codes:
        return []

    placeholders = ", ".join("?" for _ in domain_codes)
    rows = fetch_all(
        settings,
        f"""
        SELECT
            c.ControlId,
            c.ControlTypeId,
            c.ControlCode,
            c.DisplayName,
            c.Description,
            c.ControlObjective,
            c.EvidenceExpectation,
            c.Status,
            ct.CODE AS ControlTypeCode,
            ct.NAME AS ControlTypeName,
            ct.DESCRIPTION AS ControlTypeDescription,
            d.DomainCode,
            d.DisplayName AS DomainDisplayName,
            CASE
                WHEN d.DomainCode = ? THEN CAST(1 AS bit)
                ELSE CAST(0 AS bit)
            END AS IsCurrentDomainControl
        FROM dbo.Controls c
        JOIN dbo.ControlTypes ct
            ON ct.ID = c.ControlTypeId
        JOIN dbo.DomainControls dc
            ON dc.ControlId = c.ControlId
        JOIN dbo.Domains d
            ON d.DomainId = dc.DomainId
        WHERE d.DomainCode IN ({placeholders})
          AND c.Status IN ('Draft', 'Active')
        ORDER BY
            CASE WHEN d.DomainCode = ? THEN 0 ELSE 1 END,
            CASE UPPER(ct.CODE)
                WHEN 'DIRECTIVE' THEN 10
                WHEN 'PREVENTIVE' THEN 20
                WHEN 'DETERRENT' THEN 30
                WHEN 'DETECTIVE' THEN 40
                WHEN 'CORRECTIVE' THEN 50
                WHEN 'COMPENSATING' THEN 60
                ELSE 999
            END,
            d.DisplayName,
            c.DisplayName
        """,
        [root_domain_code, *domain_codes, root_domain_code],
    )
    normalized_rows = [_normalize_row(row) for row in rows]
    deduped_rows: list[dict[str, object]] = []
    seen_control_ids: set[str] = set()
    for row in normalized_rows:
        control_id = str(row.get("ControlId") or "").strip().lower()
        if not control_id or control_id in seen_control_ids:
            continue
        seen_control_ids.add(control_id)
        deduped_rows.append(row)
    return deduped_rows


def list_controls_report_rows(settings: Settings) -> list[dict[str, object]]:
    rows = fetch_all(
        settings,
        """
        SELECT
            d.DomainId,
            d.DomainCode,
            d.DisplayName AS DomainDisplayName,
            d.Description AS DomainDescription,
            d.DisplayOrder AS DomainDisplayOrder,
            d.Status AS DomainStatus,
            p.DisplayName AS ParentDisplayName,
            gp.DisplayName AS GrandparentDisplayName,
            dc.DomainControlId,
            dc.RelationshipType,
            dc.IsPrimary,
            dc.DisplayOrder AS DomainControlDisplayOrder,
            c.ControlId,
            c.ControlTypeId,
            c.ControlCode,
            c.DisplayName,
            c.Description,
            c.ControlObjective,
            c.Owner,
            c.EvidenceExpectation,
            c.Status,
            ct.CODE AS ControlTypeCode,
            ct.NAME AS ControlTypeName,
            ct.DESCRIPTION AS ControlTypeDescription
        FROM dbo.DomainControls dc
        JOIN dbo.Domains d
            ON d.DomainId = dc.DomainId
        LEFT JOIN dbo.Domains p
            ON p.DomainId = d.DomainParentId
        LEFT JOIN dbo.Domains gp
            ON gp.DomainId = p.DomainParentId
        JOIN dbo.Controls c
            ON c.ControlId = dc.ControlId
        JOIN dbo.ControlTypes ct
            ON ct.ID = c.ControlTypeId
        WHERE d.Status = 'Active'
          AND c.Status IN ('Draft', 'Active')
        ORDER BY
            d.DisplayOrder,
            d.DisplayName,
            dc.DisplayOrder,
            c.DisplayName
        """,
    )
    return [_normalize_row(row) for row in rows]


def get_control_suggestion_context(settings: Settings, branch_root_domain_code: str) -> dict[str, object]:
    normalized_code = _slug_code(branch_root_domain_code)
    domains = list_domains(settings)
    root_domain = next((row for row in domains if row.get("DomainCode") == normalized_code), None)
    if root_domain is None:
        raise ValueError(f"Active domain not found for code '{branch_root_domain_code}'.")

    child_domains_by_parent_id: dict[str, list[dict[str, object]]] = {}
    for domain in domains:
        parent_id = str(domain.get("DomainParentId") or "").strip()
        if parent_id:
            child_domains_by_parent_id.setdefault(parent_id, []).append(domain)

    branch_domains: list[dict[str, object]] = []

    def append_branch(domain: dict[str, object]) -> None:
        branch_domains.append(domain)
        domain_id = str(domain.get("DomainId") or "").strip()
        for child in child_domains_by_parent_id.get(domain_id, []):
            append_branch(child)

    append_branch(root_domain)

    domain_codes = [str(domain.get("DomainCode") or "") for domain in branch_domains if domain.get("DomainCode")]
    existing_controls: list[dict[str, object]] = []
    if domain_codes:
        placeholders = ", ".join("?" for _ in domain_codes)
        existing_controls = fetch_all(
            settings,
            f"""
            SELECT
                c.ControlCode,
                c.DisplayName,
                c.Description,
                c.ControlObjective,
                c.EvidenceExpectation,
                c.Status,
                ct.CODE AS ControlTypeCode,
                ct.NAME AS ControlTypeName,
                d.DomainCode,
                d.DisplayName AS DomainDisplayName
            FROM dbo.Controls c
            JOIN dbo.ControlTypes ct
                ON ct.ID = c.ControlTypeId
            JOIN dbo.DomainControls dc
                ON dc.ControlId = c.ControlId
            JOIN dbo.Domains d
                ON d.DomainId = dc.DomainId
            WHERE d.DomainCode IN ({placeholders})
              AND c.Status IN ('Draft', 'Active')
            ORDER BY d.DisplayName, c.DisplayName
            """,
            domain_codes,
        )

    return {
        "rootDomain": root_domain,
        "branchDomains": [_normalize_row(domain) for domain in branch_domains],
        "existingControls": [_normalize_row(control) for control in existing_controls],
    }


def create_control_from_suggestion(
    settings: Settings,
    domain_code: str,
    control_type_code: str,
    control_code: str,
    display_name: str,
    description: str | None,
    control_objective: str | None,
    evidence_expectation: str | None,
) -> dict[str, object]:
    normalized_domain_code = _slug_code(domain_code)
    normalized_control_code = _slug_code(control_code)
    normalized_type_code = re.sub(r"[^A-Z0-9_]+", "_", control_type_code.strip().upper()).strip("_")

    with get_connection(settings) as connection:
        cursor = connection.cursor()
        cursor.execute(
            """
            SET NOCOUNT ON;

            DECLARE @InsertedControls TABLE (
                ControlId UNIQUEIDENTIFIER,
                ControlTypeId INT,
                ControlCode NVARCHAR(100),
                DisplayName NVARCHAR(255),
                Description NVARCHAR(MAX),
                ControlObjective NVARCHAR(MAX),
                EvidenceExpectation NVARCHAR(MAX),
                Status NVARCHAR(30)
            );

            INSERT INTO dbo.Controls (
                ControlTypeId,
                ControlCode,
                DisplayName,
                Description,
                ControlObjective,
                EvidenceExpectation,
                Status
            )
            OUTPUT
                inserted.ControlId,
                inserted.ControlTypeId,
                inserted.ControlCode,
                inserted.DisplayName,
                inserted.Description,
                inserted.ControlObjective,
                inserted.EvidenceExpectation,
                inserted.Status
            INTO @InsertedControls
            SELECT
                ct.ID,
                ?,
                ?,
                ?,
                ?,
                ?,
                'Active'
            FROM dbo.ControlTypes ct
            WHERE ct.CODE = ?
              AND NOT EXISTS (
                  SELECT 1
                  FROM dbo.Controls existing
                  WHERE existing.ControlCode = ?
              );

            IF NOT EXISTS (SELECT 1 FROM @InsertedControls)
            BEGIN
                THROW 51000, 'Control code already exists or control type was not found.', 1;
            END;

            INSERT INTO dbo.DomainControls (
                DomainId,
                ControlId,
                RelationshipType,
                IsPrimary,
                DisplayOrder
            )
            SELECT
                d.DomainId,
                c.ControlId,
                'Primary',
                1,
                COALESCE((
                    SELECT MAX(existing.DisplayOrder) + 10
                    FROM dbo.DomainControls existing
                    WHERE existing.DomainId = d.DomainId
                ), 10)
            FROM dbo.Domains d
            CROSS JOIN @InsertedControls c
            WHERE d.DomainCode = ?;

            IF @@ROWCOUNT = 0
            BEGIN
                THROW 51001, 'Domain was not found for control link.', 1;
            END;

            SELECT
                c.ControlId,
                c.ControlTypeId,
                c.ControlCode,
                c.DisplayName,
                c.Description,
                c.ControlObjective,
                c.EvidenceExpectation,
                c.Status,
                ct.CODE AS ControlTypeCode,
                ct.NAME AS ControlTypeName,
                d.DomainCode,
                d.DisplayName AS DomainDisplayName
            FROM @InsertedControls c
            JOIN dbo.ControlTypes ct
                ON ct.ID = c.ControlTypeId
            JOIN dbo.DomainControls dc
                ON dc.ControlId = c.ControlId
            JOIN dbo.Domains d
                ON d.DomainId = dc.DomainId;
            """,
            [
                normalized_control_code,
                display_name.strip(),
                description,
                control_objective,
                evidence_expectation,
                normalized_type_code,
                normalized_control_code,
                normalized_domain_code,
            ],
        )
        row = cursor.fetchone()
        connection.commit()

    if row is None:
        raise ValueError("Control was not created.")

    return _normalize_row(_row_to_dict(cursor, row))


def update_control(
    settings: Settings,
    control_id: str,
    control_type_code: str,
    display_name: str,
    description: str | None,
    control_objective: str | None,
    evidence_expectation: str | None,
) -> dict[str, object]:
    normalized_control_id = str(control_id or "").strip()
    normalized_type_code = re.sub(r"[^A-Z0-9_]+", "_", control_type_code.strip().upper()).strip("_")
    normalized_display_name = display_name.strip()

    if not normalized_control_id:
        raise ValueError("ControlId is required.")
    if not normalized_type_code:
        raise ValueError("Control type is required.")
    if not normalized_display_name:
        raise ValueError("Display name is required.")

    with get_connection(settings) as connection:
        cursor = connection.cursor()
        cursor.execute(
            """
            SET NOCOUNT ON;

            UPDATE c
            SET
                c.ControlTypeId = ct.ID,
                c.DisplayName = ?,
                c.Description = ?,
                c.ControlObjective = ?,
                c.EvidenceExpectation = ?,
                c.UpdatedAtUtc = SYSUTCDATETIME()
            FROM dbo.Controls c
            JOIN dbo.ControlTypes ct
                ON ct.CODE = ?
            WHERE c.ControlId = ?;

            IF @@ROWCOUNT = 0
            BEGIN
                THROW 51000, 'Control was not found or control type was invalid.', 1;
            END;

            SELECT TOP (1)
                c.ControlId,
                c.ControlTypeId,
                c.ControlCode,
                c.DisplayName,
                c.Description,
                c.ControlObjective,
                c.EvidenceExpectation,
                c.Status,
                ct.CODE AS ControlTypeCode,
                ct.NAME AS ControlTypeName,
                ct.DESCRIPTION AS ControlTypeDescription,
                d.DomainCode,
                d.DisplayName AS DomainDisplayName,
                CAST(1 AS bit) AS IsCurrentDomainControl
            FROM dbo.Controls c
            JOIN dbo.ControlTypes ct
                ON ct.ID = c.ControlTypeId
            JOIN dbo.DomainControls dc
                ON dc.ControlId = c.ControlId
            JOIN dbo.Domains d
                ON d.DomainId = dc.DomainId
            WHERE c.ControlId = ?
            ORDER BY dc.IsPrimary DESC, dc.DisplayOrder, d.DisplayName;
            """,
            [
                normalized_display_name,
                description,
                control_objective,
                evidence_expectation,
                normalized_type_code,
                normalized_control_id,
                normalized_control_id,
            ],
        )
        row = cursor.fetchone()
        connection.commit()

    if row is None:
        raise ValueError("Control was not updated.")

    return _normalize_row(_row_to_dict(cursor, row))


def delete_control(settings: Settings, control_id: str) -> dict[str, object]:
    normalized_control_id = str(control_id or "").strip()
    if not normalized_control_id:
        raise ValueError("ControlId is required.")

    with get_connection(settings) as connection:
        cursor = connection.cursor()
        cursor.execute(
            """
            SET NOCOUNT ON;

            DECLARE @DeletedControl TABLE (
                ControlId UNIQUEIDENTIFIER,
                ControlCode NVARCHAR(100),
                DisplayName NVARCHAR(255)
            );

            INSERT INTO @DeletedControl (ControlId, ControlCode, DisplayName)
            SELECT ControlId, ControlCode, DisplayName
            FROM dbo.Controls
            WHERE ControlId = ?;

            IF NOT EXISTS (SELECT 1 FROM @DeletedControl)
            BEGIN
                THROW 51000, 'Control was not found.', 1;
            END;

            DELETE FROM dbo.PolicyControlStatements
            WHERE ControlId = ?;

            DELETE FROM dbo.DomainControls
            WHERE ControlId = ?;

            DELETE FROM dbo.Controls
            WHERE ControlId = ?;

            SELECT ControlId, ControlCode, DisplayName
            FROM @DeletedControl;
            """,
            [
                normalized_control_id,
                normalized_control_id,
                normalized_control_id,
                normalized_control_id,
            ],
        )
        row = cursor.fetchone()
        connection.commit()

    if row is None:
        raise ValueError("Control was not deleted.")

    return _normalize_row(_row_to_dict(cursor, row))


def get_domain_assist_context(settings: Settings, domain_code: str) -> dict[str, object]:
    normalized_code = _slug_code(domain_code)
    domains = list_domains(settings)
    domain = next((row for row in domains if row.get("DomainCode") == normalized_code), None)
    if domain is None:
        raise ValueError(f"Active domain not found for code '{domain_code}'.")

    domain_lookup = {
        str(row.get("DomainId")): row
        for row in domains
        if row.get("DomainId")
    }

    path_parts: list[str] = []
    current_parent_id = str(domain.get("DomainParentId") or "").strip()
    while current_parent_id and current_parent_id in domain_lookup:
        parent = domain_lookup[current_parent_id]
        path_parts.insert(0, str(parent.get("DisplayName") or parent.get("DomainCode") or ""))
        current_parent_id = str(parent.get("DomainParentId") or "").strip()

    child_domains = [
        {
            "displayName": row.get("DisplayName"),
            "domainCode": row.get("DomainCode"),
            "domainType": row.get("DomainType"),
        }
        for row in domains
        if str(row.get("DomainParentId") or "").strip() == str(domain.get("DomainId") or "").strip()
    ]

    collections = fetch_all(
        settings,
        """
        SELECT
            c.CollectionCode,
            c.DisplayName,
            c.Description,
            COUNT(d.DocumentId) AS DocumentCount
        FROM dbo.Collections c
        JOIN dbo.Domains dm
            ON dm.DomainId = c.DomainId
        LEFT JOIN dbo.Documents d
            ON d.CollectionId = c.CollectionId AND d.Status = 'Active'
        WHERE dm.DomainCode = ? AND c.Status = 'Active'
        GROUP BY
            c.CollectionCode,
            c.DisplayName,
            c.Description
        ORDER BY c.DisplayName
        """,
        [normalized_code],
    )

    documents = fetch_all(
        settings,
        """
        SELECT TOP (25)
            d.SourceName,
            d.SourceType,
            d.UpdatedAtUtc,
            c.DisplayName AS CollectionDisplayName,
            COUNT(cu.ContentUnitId) AS ChunkCount
        FROM dbo.Documents d
        JOIN dbo.Collections c
            ON c.CollectionId = d.CollectionId
        JOIN dbo.Domains dm
            ON dm.DomainId = c.DomainId
        LEFT JOIN dbo.ContentUnits cu
            ON cu.DocumentId = d.DocumentId AND cu.Status = 'Active'
        WHERE dm.DomainCode = ? AND d.Status = 'Active' AND c.Status = 'Active'
        GROUP BY
            d.SourceName,
            d.SourceType,
            d.UpdatedAtUtc,
            c.DisplayName
        ORDER BY d.UpdatedAtUtc DESC
        """,
        [normalized_code],
    )

    controls = fetch_all(
        settings,
        """
        SELECT
            c.ControlCode,
            c.DisplayName,
            c.Description,
            c.ControlObjective,
            ct.Code AS ControlTypeCode
        FROM dbo.DomainControls dc
        JOIN dbo.Domains dm
            ON dm.DomainId = dc.DomainId
        JOIN dbo.Controls c
            ON c.ControlId = dc.ControlId
        JOIN dbo.ControlTypes ct
            ON ct.ID = c.ControlTypeId
        WHERE dm.DomainCode = ? AND c.Status IN ('Draft', 'Active')
        ORDER BY dc.DisplayOrder, c.DisplayName
        """,
        [normalized_code],
    )

    policies = fetch_all(
        settings,
        """
        SELECT
            p.PolicyCode,
            p.PolicyTitle,
            p.VersionText,
            p.Status
        FROM dbo.Policies p
        JOIN dbo.Domains dm
            ON dm.DomainId = p.RootDomainId
        WHERE dm.DomainCode = ?
        ORDER BY p.PolicyTitle, p.VersionText
        """,
        [normalized_code],
    )

    return {
        "domain": domain,
        "parentPath": " / ".join(path_parts),
        "childDomains": [_normalize_row(row) for row in child_domains],
        "collections": [_normalize_row(row) for row in collections],
        "documents": [_normalize_row(row) for row in documents],
        "controls": [_normalize_row(row) for row in controls],
        "policies": [_normalize_row(row) for row in policies],
    }


def list_collections(
    settings: Settings,
    domain_code: str | None = None,
) -> list[dict[str, object]]:
    params: list[object] = []
    where_clause = "WHERE c.Status = 'Active' AND d.Status = 'Active'"
    if domain_code:
        where_clause += " AND d.DomainCode = ?"
        params.append(domain_code)

    rows = fetch_all(
        settings,
        f"""
        SELECT
            c.CollectionId,
            c.CollectionCode,
            c.DisplayName,
            c.Description,
            c.Status,
            d.DomainId,
            d.DomainParentId,
            d.DomainTypeId,
            d.DisplayOrder,
            d.DomainCode,
            d.DisplayName AS DomainDisplayName
        FROM dbo.Collections c
        JOIN dbo.Domains d
            ON d.DomainId = c.DomainId
        {where_clause}
        ORDER BY d.DisplayOrder, d.DisplayName, c.DisplayName
        """,
        params,
    )
    return [_normalize_row(row) for row in rows]


def list_retrieval_profiles(settings: Settings) -> list[dict[str, object]]:
    rows = fetch_all(
        settings,
        """
        SELECT
            RetrievalProfileId,
            ProfileCode,
            DisplayName,
            RetrievalMode,
            TopK,
            MaxContextTokens,
            IncludeSummaries,
            IncludeChunks,
            IncludeWholeDocs,
            Status
        FROM dbo.RetrievalProfiles
        WHERE Status = 'Active'
        ORDER BY DisplayName
        """,
    )
    return [_normalize_row(row) for row in rows]


def get_retrieval_profile(settings: Settings, profile_code: str) -> dict[str, object] | None:
    row = fetch_one(
        settings,
        """
        SELECT
            RetrievalProfileId,
            ProfileCode,
            DisplayName,
            RetrievalMode,
            TopK,
            MaxContextTokens,
            IncludeSummaries,
            IncludeChunks,
            IncludeWholeDocs,
            Status
        FROM dbo.RetrievalProfiles
        WHERE ProfileCode = ? AND Status = 'Active'
        """,
        [profile_code],
    )
    return _normalize_row(row) if row else None


def get_default_embedding_profile(settings: Settings) -> dict[str, object]:
    row = fetch_one(
        settings,
        """
        SELECT TOP 1
            EmbeddingProfileId,
            ProfileCode,
            Provider,
            ModelName,
            VectorDimension,
            DistanceMetric,
            IsDefault,
            Status
        FROM dbo.EmbeddingProfiles
        WHERE Status = 'Active'
        ORDER BY CASE WHEN IsDefault = 1 THEN 0 ELSE 1 END, ProfileCode
        """,
    )
    if not row:
        raise ValueError("No active embedding profile is configured.")
    return _normalize_row(row)


def list_unembedded_content_units(
    settings: Settings,
    *,
    embedding_profile_id: str,
    limit: int = 100,
    collection_codes: list[str] | None = None,
    document_id: str | None = None,
) -> list[dict[str, object]]:
    filters = [
        "cu.Status = 'Active'",
        "d.Status = 'Active'",
        "c.Status = 'Active'",
        "emb.ContentUnitId IS NULL",
    ]
    params: list[object] = [embedding_profile_id]

    if collection_codes:
        cleaned_codes = [_slug_code(code) for code in collection_codes if code and code.strip()]
        if cleaned_codes:
            placeholders = ", ".join("?" for _ in cleaned_codes)
            filters.append(f"c.CollectionCode IN ({placeholders})")
            params.extend(cleaned_codes)

    if document_id:
        filters.append("d.DocumentId = ?")
        params.append(document_id)

    query = f"""
        SELECT TOP ({max(1, min(limit, 2000))})
            cu.ContentUnitId,
            cu.DocumentId,
            cu.UnitOrdinal,
            cu.UnitType,
            cu.TokenCount,
            cu.BodyText,
            d.SourceName,
            c.CollectionCode,
            c.DisplayName AS CollectionDisplayName
        FROM dbo.ContentUnits cu
        JOIN dbo.Documents d
            ON d.DocumentId = cu.DocumentId
        JOIN dbo.Collections c
            ON c.CollectionId = d.CollectionId
        LEFT JOIN dbo.ContentUnitEmbeddings768 emb
            ON emb.ContentUnitId = cu.ContentUnitId
           AND emb.EmbeddingProfileId = ?
        WHERE {" AND ".join(filters)}
        ORDER BY d.CreatedAtUtc, cu.UnitOrdinal
    """
    rows = fetch_all(settings, query, params)
    return [_normalize_row(row) for row in rows]


def upsert_content_unit_embeddings(
    settings: Settings,
    *,
    embedding_profile_id: str,
    vector_dimension: int,
    embeddings: list[dict[str, object]],
) -> dict[str, object]:
    inserted_count = 0
    updated_count = 0
    if not embeddings:
        return {"insertedCount": 0, "updatedCount": 0}

    with get_connection(settings) as conn:
        cursor = conn.cursor()
        for item in embeddings:
            content_unit_id = str(item.get("contentUnitId") or "").strip()
            vector_text = str(item.get("vectorText") or "").strip()
            embedding_hash = item.get("embeddingHash")
            if not content_unit_id or not vector_text:
                continue

            cursor.execute(
                """
                SELECT COUNT(1)
                FROM dbo.ContentUnitEmbeddings768
                WHERE ContentUnitId = ? AND EmbeddingProfileId = ?
                """,
                [content_unit_id, embedding_profile_id],
            )
            exists = int(cursor.fetchone()[0] or 0) > 0
            cursor.execute(
                """
                DELETE FROM dbo.ContentUnitEmbeddings768
                WHERE ContentUnitId = ? AND EmbeddingProfileId = ?
                """,
                [content_unit_id, embedding_profile_id],
            )
            cursor.execute(
                f"""
                INSERT INTO dbo.ContentUnitEmbeddings768 (
                    ContentUnitId,
                    EmbeddingProfileId,
                    EmbeddingVector,
                    EmbeddingHash
                )
                VALUES (?, ?, CAST(CONVERT(nvarchar(max), ?) AS VECTOR({vector_dimension})), ?)
                """,
                [content_unit_id, embedding_profile_id, vector_text, embedding_hash],
            )
            if exists:
                updated_count += 1
            else:
                inserted_count += 1
        conn.commit()

    return {
        "insertedCount": inserted_count,
        "updatedCount": updated_count,
    }


def search_similar_content_units(
    settings: Settings,
    *,
    embedding_profile_id: str,
    vector_dimension: int,
    query_vector_text: str,
    collection_codes: list[str],
    top_k: int = 8,
) -> list[dict[str, object]]:
    cleaned_codes = [_slug_code(code) for code in collection_codes if code and code.strip()]
    if not cleaned_codes:
        return []

    placeholders = ", ".join("?" for _ in cleaned_codes)
    params: list[object] = [query_vector_text, embedding_profile_id, *cleaned_codes]
    rows = fetch_all(
        settings,
        f"""
        SELECT TOP ({max(1, min(top_k, 100))})
            c.CollectionCode,
            c.DisplayName AS CollectionDisplayName,
            d.DocumentId,
            d.SourceName,
            cu.ContentUnitId,
            cu.UnitType,
            cu.UnitOrdinal,
            cu.TokenCount,
            cu.BodyText,
            CAST(VECTOR_DISTANCE('cosine', emb.EmbeddingVector, CAST(CONVERT(nvarchar(max), ?) AS VECTOR({vector_dimension}))) AS float) AS Distance
        FROM dbo.ContentUnitEmbeddings768 emb
        JOIN dbo.ContentUnits cu
            ON cu.ContentUnitId = emb.ContentUnitId
        JOIN dbo.Documents d
            ON d.DocumentId = cu.DocumentId
        JOIN dbo.Collections c
            ON c.CollectionId = d.CollectionId
        WHERE emb.EmbeddingProfileId = ?
          AND c.CollectionCode IN ({placeholders})
          AND c.Status = 'Active'
          AND d.Status = 'Active'
          AND cu.Status = 'Active'
        ORDER BY Distance ASC, d.CreatedAtUtc DESC, cu.UnitOrdinal ASC
        """,
        params,
    )
    return [_normalize_row(row) for row in rows]


def list_embedding_status(
    settings: Settings,
    *,
    embedding_profile_id: str,
) -> dict[str, object]:
    totals = fetch_one(
        settings,
        """
        SELECT
            COUNT(1) AS TotalContentUnitCount,
            SUM(COALESCE(cu.TokenCount, 0)) AS TotalTokenCount,
            SUM(CASE WHEN emb.ContentUnitId IS NOT NULL THEN 1 ELSE 0 END) AS EmbeddedContentUnitCount,
            SUM(CASE WHEN emb.ContentUnitId IS NOT NULL THEN COALESCE(cu.TokenCount, 0) ELSE 0 END) AS EmbeddedTokenCount
        FROM dbo.ContentUnits cu
        JOIN dbo.Documents d
            ON d.DocumentId = cu.DocumentId
        JOIN dbo.Collections c
            ON c.CollectionId = d.CollectionId
        LEFT JOIN dbo.ContentUnitEmbeddings768 emb
            ON emb.ContentUnitId = cu.ContentUnitId
           AND emb.EmbeddingProfileId = ?
        WHERE c.Status = 'Active'
          AND d.Status = 'Active'
          AND cu.Status = 'Active'
        """,
        [embedding_profile_id],
    ) or {}

    per_collection = fetch_all(
        settings,
        """
        SELECT
            c.CollectionCode,
            c.DisplayName AS CollectionDisplayName,
            COUNT(1) AS TotalContentUnitCount,
            SUM(COALESCE(cu.TokenCount, 0)) AS TotalTokenCount,
            SUM(CASE WHEN emb.ContentUnitId IS NOT NULL THEN 1 ELSE 0 END) AS EmbeddedContentUnitCount,
            SUM(CASE WHEN emb.ContentUnitId IS NOT NULL THEN COALESCE(cu.TokenCount, 0) ELSE 0 END) AS EmbeddedTokenCount
        FROM dbo.ContentUnits cu
        JOIN dbo.Documents d
            ON d.DocumentId = cu.DocumentId
        JOIN dbo.Collections c
            ON c.CollectionId = d.CollectionId
        LEFT JOIN dbo.ContentUnitEmbeddings768 emb
            ON emb.ContentUnitId = cu.ContentUnitId
           AND emb.EmbeddingProfileId = ?
        WHERE c.Status = 'Active'
          AND d.Status = 'Active'
          AND cu.Status = 'Active'
        GROUP BY c.CollectionCode, c.DisplayName
        ORDER BY c.DisplayName, c.CollectionCode
        """,
        [embedding_profile_id],
    )

    normalized_totals = _normalize_row(totals)
    total_content_unit_count = int(normalized_totals.get("TotalContentUnitCount") or 0)
    embedded_content_unit_count = int(normalized_totals.get("EmbeddedContentUnitCount") or 0)
    total_token_count = int(normalized_totals.get("TotalTokenCount") or 0)
    embedded_token_count = int(normalized_totals.get("EmbeddedTokenCount") or 0)
    return {
        "totalContentUnitCount": total_content_unit_count,
        "embeddedContentUnitCount": embedded_content_unit_count,
        "unembeddedContentUnitCount": max(0, total_content_unit_count - embedded_content_unit_count),
        "totalTokenCount": total_token_count,
        "embeddedTokenCount": embedded_token_count,
        "unembeddedTokenCount": max(0, total_token_count - embedded_token_count),
        "collections": [
            {
                **_normalize_row(row),
                "UnembeddedContentUnitCount": max(
                    0,
                    int(row.get("TotalContentUnitCount") or 0) - int(row.get("EmbeddedContentUnitCount") or 0),
                ),
                "UnembeddedTokenCount": max(
                    0,
                    int(row.get("TotalTokenCount") or 0) - int(row.get("EmbeddedTokenCount") or 0),
                ),
            }
            for row in per_collection
        ],
    }


def list_policies(settings: Settings) -> list[dict[str, object]]:
    rows = fetch_all(
        settings,
        """
        SELECT
            p.PolicyId,
            p.PolicyCode,
            p.PolicyTitle,
            p.VersionText,
            p.Status,
            p.TemplatePath,
            p.SourceModelName,
            p.CreatedAtUtc,
            p.UpdatedAtUtc,
            d.DomainCode AS RootDomainCode,
            d.DisplayName AS RootDomainName,
            pt.TemplateCode,
            pt.TemplateName,
            (
                SELECT COUNT(1)
                FROM dbo.PolicySections ps
                WHERE ps.PolicyId = p.PolicyId
            ) AS SectionCount,
            (
                SELECT COUNT(1)
                FROM dbo.PolicyObjectives po
                JOIN dbo.PolicySections ps
                    ON ps.PolicySectionId = po.PolicySectionId
                WHERE ps.PolicyId = p.PolicyId
            ) AS ObjectiveCount,
            (
                SELECT COUNT(1)
                FROM dbo.PolicyPrinciples pp
                JOIN dbo.PolicySections ps
                    ON ps.PolicySectionId = pp.PolicySectionId
                WHERE ps.PolicyId = p.PolicyId
            ) AS PrincipleCount,
            (
                SELECT COUNT(1)
                FROM dbo.PolicyControlStatements pcs
                WHERE pcs.PolicyId = p.PolicyId
            ) AS ControlStatementCount
        FROM dbo.Policies p
        JOIN dbo.Domains d
            ON d.DomainId = p.RootDomainId
        LEFT JOIN dbo.PolicyTemplates pt
            ON pt.PolicyTemplateId = p.PolicyTemplateId
        ORDER BY
            COALESCE(p.UpdatedAtUtc, p.CreatedAtUtc) DESC,
            p.PolicyTitle,
            p.PolicyCode
        """,
    )
    return [_normalize_row(row) for row in rows]


def get_policy_presentation_data(settings: Settings, policy_id: str) -> dict[str, object]:
    policy_row = fetch_one(
        settings,
        """
        SELECT
            p.PolicyId,
            p.PolicyCode,
            p.PolicyTitle,
            p.VersionText,
            p.Status,
            p.TemplatePath,
            p.SourceModelName,
            p.CreatedAtUtc,
            p.UpdatedAtUtc,
            d.DomainCode AS RootDomainCode,
            d.DisplayName AS RootDomainName,
            pt.TemplateCode,
            pt.TemplateName
        FROM dbo.Policies p
        JOIN dbo.Domains d
            ON d.DomainId = p.RootDomainId
        LEFT JOIN dbo.PolicyTemplates pt
            ON pt.PolicyTemplateId = p.PolicyTemplateId
        WHERE p.PolicyId = ?
        """,
        [policy_id],
    )
    if not policy_row:
        raise ValueError(f"Policy not found for id '{policy_id}'.")

    with get_connection(settings) as conn:
        cursor = conn.cursor()
        has_policy_control_group_label = _column_exists(cursor, "dbo.PolicyControlStatements", "GroupLabel")
        has_policy_control_ordering = _column_exists(cursor, "dbo.PolicyControlStatements", "GroupDisplayOrder")

    if has_policy_control_group_label and has_policy_control_ordering:
        control_statements_query = """
        SELECT
            pcs.PolicyControlStatementId,
            pcs.StatementText,
            pcs.DisplayOrder,
            pcs.ReviewStatus,
            pcs.GroupLabel,
            pcs.GroupDisplayOrder,
            pcs.ControlDisplayOrder,
            c.ControlCode,
            c.DisplayName AS ControlName,
            ct.CODE AS ControlTypeCode,
            ct.NAME AS ControlTypeName
        FROM dbo.PolicyControlStatements pcs
        JOIN dbo.Controls c
            ON c.ControlId = pcs.ControlId
        JOIN dbo.ControlTypes ct
            ON ct.ID = c.ControlTypeId
        WHERE pcs.PolicyId = ?
        ORDER BY
            pcs.GroupDisplayOrder,
            pcs.ControlDisplayOrder,
            pcs.DisplayOrder,
            pcs.PolicyControlStatementId
        """
    elif has_policy_control_group_label:
        control_statements_query = """
        SELECT
            pcs.PolicyControlStatementId,
            pcs.StatementText,
            pcs.DisplayOrder,
            pcs.ReviewStatus,
            pcs.GroupLabel,
            CAST(0 AS int) AS GroupDisplayOrder,
            CAST(0 AS int) AS ControlDisplayOrder,
            c.ControlCode,
            c.DisplayName AS ControlName,
            ct.CODE AS ControlTypeCode,
            ct.NAME AS ControlTypeName
        FROM dbo.PolicyControlStatements pcs
        JOIN dbo.Controls c
            ON c.ControlId = pcs.ControlId
        JOIN dbo.ControlTypes ct
            ON ct.ID = c.ControlTypeId
        WHERE pcs.PolicyId = ?
        ORDER BY
            COALESCE(pcs.GroupLabel, ''),
            c.DisplayName,
            pcs.DisplayOrder,
            pcs.PolicyControlStatementId
        """
    else:
        control_statements_query = """
        SELECT
            pcs.PolicyControlStatementId,
            pcs.StatementText,
            pcs.DisplayOrder,
            pcs.ReviewStatus,
            CAST('' AS NVARCHAR(200)) AS GroupLabel,
            CAST(0 AS int) AS GroupDisplayOrder,
            CAST(0 AS int) AS ControlDisplayOrder,
            c.ControlCode,
            c.DisplayName AS ControlName,
            ct.CODE AS ControlTypeCode,
            ct.NAME AS ControlTypeName
        FROM dbo.PolicyControlStatements pcs
        JOIN dbo.Controls c
            ON c.ControlId = pcs.ControlId
        JOIN dbo.ControlTypes ct
            ON ct.ID = c.ControlTypeId
        WHERE pcs.PolicyId = ?
        ORDER BY
            c.DisplayName,
            pcs.DisplayOrder,
            pcs.PolicyControlStatementId
        """

    sections = {
        "policy": _normalize_row(policy_row),
        "objectives": [
            _normalize_row(row)
            for row in fetch_all(
                settings,
                """
                SELECT
                    po.PolicyObjectiveId,
                    po.StatementText,
                    po.DisplayOrder,
                    po.ReviewStatus
                FROM dbo.PolicyObjectives po
                JOIN dbo.PolicySections ps
                    ON ps.PolicySectionId = po.PolicySectionId
                WHERE ps.PolicyId = ?
                ORDER BY po.DisplayOrder, po.PolicyObjectiveId
                """,
                [policy_id],
            )
        ],
        "principles": [
            _normalize_row(row)
            for row in fetch_all(
                settings,
                """
                SELECT
                    pp.PolicyPrincipleId,
                    pp.StatementText,
                    pp.DisplayOrder,
                    pp.ReviewStatus,
                    pr.PrincipleCode,
                    pr.Name AS PrincipleName,
                    ppl.UsageMode
                FROM dbo.PolicyPrinciples pp
                JOIN dbo.PolicySections ps
                    ON ps.PolicySectionId = pp.PolicySectionId
                LEFT JOIN dbo.PolicyPrincipleLinks ppl
                    ON ppl.PolicyPrincipleId = pp.PolicyPrincipleId
                LEFT JOIN dbo.Principles pr
                    ON pr.PrincipleId = ppl.PrincipleId
                WHERE ps.PolicyId = ?
                ORDER BY pp.DisplayOrder, pp.PolicyPrincipleId
                """,
                [policy_id],
            )
        ],
        "accountability": [
            _normalize_row(row)
            for row in fetch_all(
                settings,
                """
                SELECT
                    pas.PolicyAccountabilityStatementId,
                    pas.StatementText,
                    pas.DisplayOrder,
                    pas.ReviewStatus
                FROM dbo.PolicyAccountabilityStatements pas
                JOIN dbo.PolicySections ps
                    ON ps.PolicySectionId = pas.PolicySectionId
                WHERE ps.PolicyId = ?
                ORDER BY pas.DisplayOrder, pas.PolicyAccountabilityStatementId
                """,
                [policy_id],
            )
        ],
        "transparency": [
            _normalize_row(row)
            for row in fetch_all(
                settings,
                """
                SELECT
                    pts.PolicyTransparencyStatementId,
                    pts.StatementText,
                    pts.DisplayOrder,
                    pts.ReviewStatus
                FROM dbo.PolicyTransparencyStatements pts
                JOIN dbo.PolicySections ps
                    ON ps.PolicySectionId = pts.PolicySectionId
                WHERE ps.PolicyId = ?
                ORDER BY pts.DisplayOrder, pts.PolicyTransparencyStatementId
                """,
                [policy_id],
            )
        ],
        "strategy": [
            _normalize_row(row)
            for row in fetch_all(
                settings,
                """
                SELECT
                    pss.PolicyStrategyStatementId,
                    pss.StatementText,
                    pss.DisplayOrder,
                    pss.ReviewStatus
                FROM dbo.PolicyStrategyStatements pss
                JOIN dbo.PolicySections ps
                    ON ps.PolicySectionId = pss.PolicySectionId
                WHERE ps.PolicyId = ?
                ORDER BY pss.DisplayOrder, pss.PolicyStrategyStatementId
                """,
                [policy_id],
            )
        ],
        "consequences": [
            _normalize_row(row)
            for row in fetch_all(
                settings,
                """
                SELECT
                    pc.PolicyConsequenceId,
                    pc.StatementText,
                    pc.DisplayOrder,
                    pc.ReviewStatus
                FROM dbo.PolicyConsequences pc
                JOIN dbo.PolicySections ps
                    ON ps.PolicySectionId = pc.PolicySectionId
                WHERE ps.PolicyId = ?
                ORDER BY pc.DisplayOrder, pc.PolicyConsequenceId
                """,
                [policy_id],
            )
        ],
        "controlStatements": [
            _normalize_row(row)
            for row in fetch_all(
                settings,
                control_statements_query,
                [policy_id],
            )
        ],
        "controlExplanations": list_policy_control_explanations(settings, policy_id),
    }
    return sections


def list_policy_control_explanations(settings: Settings, policy_id: str) -> list[dict[str, object]]:
    with get_connection(settings) as conn:
        cursor = conn.cursor()
        _ensure_policy_control_explanations_table(cursor)
        cursor.execute(
            """
            SELECT
                pce.PolicyId,
                c.ControlCode,
                c.DisplayName AS ControlName,
                pce.ExplanationText,
                pce.SourceModelName,
                pce.CreatedAtUtc,
                pce.UpdatedAtUtc
            FROM dbo.PolicyControlExplanations pce
            JOIN dbo.Controls c
                ON c.ControlId = pce.ControlId
            WHERE pce.PolicyId = ?
            ORDER BY c.DisplayName
            """,
            [policy_id],
        )
        rows = [_normalize_row(_row_to_dict(cursor, row)) for row in cursor.fetchall()]
    return rows


def upsert_policy_control_explanation(
    settings: Settings,
    *,
    policy_id: str,
    control_code: str,
    explanation_text: str,
    source_model_name: str | None = None,
) -> dict[str, object]:
    normalized_policy_id = str(policy_id or "").strip()
    normalized_control_code = _slug_code(control_code)
    normalized_explanation = str(explanation_text or "").strip()
    if not normalized_policy_id:
        raise ValueError("PolicyId is required.")
    if not normalized_control_code:
        raise ValueError("ControlCode is required.")
    if not normalized_explanation:
        raise ValueError("Explanation text is required.")

    with get_connection(settings) as conn:
        cursor = conn.cursor()
        _ensure_policy_control_explanations_table(cursor)
        cursor.execute(
            """
            SELECT TOP 1 c.ControlId
            FROM dbo.PolicyControlStatements pcs
            JOIN dbo.Controls c
                ON c.ControlId = pcs.ControlId
            WHERE pcs.PolicyId = ?
              AND c.ControlCode = ?;
            """,
            [
                normalized_policy_id,
                normalized_control_code,
            ],
        )
        control_row = cursor.fetchone()
        if control_row is None:
            raise ValueError("Control is not linked to the specified policy.")

        control_id = str(control_row[0])
        cursor.execute(
            """
            UPDATE dbo.PolicyControlExplanations
            SET
                ExplanationText = ?,
                SourceModelName = ?,
                UpdatedAtUtc = SYSUTCDATETIME()
            WHERE PolicyId = ?
              AND ControlId = ?;
            """,
            [
                normalized_explanation,
                source_model_name,
                normalized_policy_id,
                control_id,
            ],
        )
        if cursor.rowcount == 0:
            cursor.execute(
                """
                INSERT INTO dbo.PolicyControlExplanations (
                    PolicyId,
                    ControlId,
                    ExplanationText,
                    SourceModelName
                )
                VALUES (?, ?, ?, ?);
                """,
                [
                    normalized_policy_id,
                    control_id,
                    normalized_explanation,
                    source_model_name,
                ],
            )

        cursor.execute(
            """
            SELECT
                pce.PolicyId,
                c.ControlCode,
                c.DisplayName AS ControlName,
                pce.ExplanationText,
                pce.SourceModelName,
                pce.CreatedAtUtc,
                pce.UpdatedAtUtc
            FROM dbo.PolicyControlExplanations pce
            JOIN dbo.Controls c
                ON c.ControlId = pce.ControlId
            WHERE pce.PolicyId = ?
              AND c.ControlId = ?;
            """,
            [
                normalized_policy_id,
                control_id,
            ],
        )
        row = cursor.fetchone()
        normalized_row = None if row is None else _normalize_row(_row_to_dict(cursor, row))
        conn.commit()

    if normalized_row is None:
        raise ValueError("Policy control explanation was not saved.")

    return normalized_row


def get_latest_policy_for_root_domain(settings: Settings, root_domain_code: str) -> dict[str, object] | None:
    normalized_root_domain_code = _slug_code(root_domain_code)
    row = fetch_one(
        settings,
        """
        SELECT TOP 1
            p.PolicyId
        FROM dbo.Policies p
        JOIN dbo.Domains d
            ON d.DomainId = p.RootDomainId
        WHERE d.DomainCode = ?
        ORDER BY
            CASE p.Status
                WHEN 'Draft' THEN 0
                WHEN 'Active' THEN 1
                WHEN 'Retired' THEN 2
                WHEN 'Archived' THEN 3
                ELSE 4
            END,
            COALESCE(p.UpdatedAtUtc, p.CreatedAtUtc) DESC,
            p.PolicyTitle
        """,
        [normalized_root_domain_code],
    )
    if not row:
        return None

    return get_policy_presentation_data(settings, str(row.get("PolicyId") or ""))


def delete_policy(settings: Settings, policy_id: str) -> dict[str, object]:
    normalized_policy_id = (policy_id or "").strip()
    if not normalized_policy_id:
        raise ValueError("Policy id is required.")

    with get_connection(settings) as conn:
        cursor = conn.cursor()
        cursor.execute(
            """
            SELECT PolicyId, PolicyCode, PolicyTitle
            FROM dbo.Policies
            WHERE PolicyId = ?
            """,
            [normalized_policy_id],
        )
        policy_row = cursor.fetchone()
        if policy_row is None:
            raise ValueError(f"Policy not found for id '{policy_id}'.")

        cursor.execute(
            """
            DELETE FROM dbo.PolicyPrincipleLinks
            WHERE PolicyPrincipleId IN (
                SELECT pp.PolicyPrincipleId
                FROM dbo.PolicyPrinciples pp
                JOIN dbo.PolicySections ps
                    ON ps.PolicySectionId = pp.PolicySectionId
                WHERE ps.PolicyId = ?
            )
            """,
            [normalized_policy_id],
        )
        cursor.execute("DELETE FROM dbo.PolicyControlStatements WHERE PolicyId = ?", [normalized_policy_id])
        cursor.execute(
            """
            DELETE po
            FROM dbo.PolicyObjectives po
            JOIN dbo.PolicySections ps
                ON ps.PolicySectionId = po.PolicySectionId
            WHERE ps.PolicyId = ?
            """,
            [normalized_policy_id],
        )
        cursor.execute(
            """
            DELETE pp
            FROM dbo.PolicyPrinciples pp
            JOIN dbo.PolicySections ps
                ON ps.PolicySectionId = pp.PolicySectionId
            WHERE ps.PolicyId = ?
            """,
            [normalized_policy_id],
        )
        cursor.execute(
            """
            DELETE pas
            FROM dbo.PolicyAccountabilityStatements pas
            JOIN dbo.PolicySections ps
                ON ps.PolicySectionId = pas.PolicySectionId
            WHERE ps.PolicyId = ?
            """,
            [normalized_policy_id],
        )
        cursor.execute(
            """
            DELETE pts
            FROM dbo.PolicyTransparencyStatements pts
            JOIN dbo.PolicySections ps
                ON ps.PolicySectionId = pts.PolicySectionId
            WHERE ps.PolicyId = ?
            """,
            [normalized_policy_id],
        )
        cursor.execute(
            """
            DELETE pss
            FROM dbo.PolicyStrategyStatements pss
            JOIN dbo.PolicySections ps
                ON ps.PolicySectionId = pss.PolicySectionId
            WHERE ps.PolicyId = ?
            """,
            [normalized_policy_id],
        )
        cursor.execute(
            """
            DELETE pc
            FROM dbo.PolicyConsequences pc
            JOIN dbo.PolicySections ps
                ON ps.PolicySectionId = pc.PolicySectionId
            WHERE ps.PolicyId = ?
            """,
            [normalized_policy_id],
        )
        cursor.execute("DELETE FROM dbo.PolicySections WHERE PolicyId = ?", [normalized_policy_id])
        cursor.execute("DELETE FROM dbo.Policies WHERE PolicyId = ?", [normalized_policy_id])
        conn.commit()

    return {
        "status": "deleted",
        "policyId": normalized_policy_id,
        "policyCode": str(policy_row.PolicyCode),
        "policyTitle": str(policy_row.PolicyTitle),
    }


def reorder_root_domains(
    settings: Settings,
    parent_domain_id: str | None,
    orientation_code: str | None,
    ordered_domain_codes: list[str],
) -> dict[str, object]:
    normalized_parent_domain_id = (parent_domain_id or "").strip() or None
    normalized_orientation_code = (
        _slug_code(orientation_code).replace("-", "_").upper()
        if orientation_code and orientation_code.strip()
        else None
    )
    normalized_codes = [_slug_code(code) for code in ordered_domain_codes if code.strip()]
    if not normalized_codes:
        raise ValueError("At least one root domain code is required to reorder domains.")

    with get_connection(settings) as conn:
        cursor = conn.cursor()
        cursor.execute(
            """
            SELECT
                d.DomainId,
                d.DomainCode,
                d.DomainParentId,
                dor.CODE AS DomainOrientationCode
            FROM dbo.Domains d
            LEFT JOIN dbo.DomainOrientations dor
                ON dor.ID = d.DomainOrientationId
            WHERE d.Status = 'Active'
              AND d.DomainCode IN ({placeholders})
            """.format(placeholders=", ".join("?" for _ in normalized_codes)),
            normalized_codes,
        )
        domain_rows = [_normalize_row(_row_to_dict(cursor, row)) for row in cursor.fetchall()]
        if len(domain_rows) != len(normalized_codes):
            found_codes = {str(row.get("DomainCode") or "") for row in domain_rows}
            missing_codes = [code for code in normalized_codes if code not in found_codes]
            raise ValueError(f"Domains not found for reorder: {', '.join(missing_codes)}.")

        for row in domain_rows:
            row_parent_id = str(row.get("DomainParentId") or "").strip() or None
            if row_parent_id != normalized_parent_domain_id:
                raise ValueError("All reordered domains must belong to the same parent.")
            if normalized_orientation_code and str(row.get("DomainOrientationCode") or "").upper() != normalized_orientation_code:
                raise ValueError("All reordered domains must belong to the same orientation.")

        for index, domain_code in enumerate(normalized_codes, start=1):
            cursor.execute(
                """
                UPDATE dbo.Domains
                SET
                    DisplayOrder = ?,
                    UpdatedAtUtc = SYSUTCDATETIME()
                WHERE DomainCode = ? AND Status = 'Active'
                """,
                [index * 10, domain_code],
            )

        conn.commit()

    return {
        "status": "reordered",
        "parentDomainId": normalized_parent_domain_id,
        "orientationCode": normalized_orientation_code,
        "domainCodes": normalized_codes,
    }


def reorder_domain_types(
    settings: Settings,
    *,
    ordered_type_ids: list[int],
) -> dict[str, object]:
    normalized_ids = [int(type_id) for type_id in ordered_type_ids]
    if not normalized_ids:
        raise ValueError("At least one domain type id is required to reorder domain types.")

    with get_connection(settings) as conn:
        cursor = conn.cursor()
        placeholders = ", ".join("?" for _ in normalized_ids)
        cursor.execute(
            f"""
            SELECT ID
            FROM dbo.DomainTypes
            WHERE ID IN ({placeholders})
            """,
            normalized_ids,
        )
        found_ids = {int(row[0]) for row in cursor.fetchall()}
        missing_ids = [type_id for type_id in normalized_ids if type_id not in found_ids]
        if missing_ids:
            raise ValueError(f"Domain types not found for reorder: {', '.join(str(item) for item in missing_ids)}.")

        for index, type_id in enumerate(normalized_ids, start=1):
            cursor.execute(
                """
                UPDATE dbo.DomainTypes
                SET DISPLAY_ORDER = ?
                WHERE ID = ?
                """,
                [index * 10, type_id],
            )

        conn.commit()

    return {
        "status": "reordered",
        "typeIds": normalized_ids,
    }


def create_domain(
    settings: Settings,
    domain_code: str,
    domain_type_id: int | None,
    domain_orientation_id: int | None,
    display_name: str,
    description: str | None = None,
    domain_parent_id: str | None = None,
) -> dict[str, object]:
    domain_code = _slug_code(domain_code)
    with get_connection(settings) as conn:
        cursor = conn.cursor()
        cursor.execute(
            """
            INSERT INTO dbo.Domains (
                DomainParentId,
                DomainTypeId,
                DomainOrientationId,
                DisplayOrder,
                DomainCode,
                DisplayName,
                Description
            )
            OUTPUT
                inserted.DomainId,
                inserted.DomainParentId,
                inserted.DomainTypeId,
                inserted.DomainOrientationId,
                inserted.DisplayOrder,
                inserted.DomainCode,
                inserted.DisplayName,
                inserted.Description,
                inserted.Status
            VALUES (
                ?,
                ?,
                ?,
                (
                    SELECT COALESCE(MAX(DisplayOrder), 0) + 10
                    FROM dbo.Domains
                    WHERE
                        Status = 'Active'
                        AND (
                            (DomainParentId IS NULL AND ? IS NULL)
                            OR DomainParentId = ?
                        )
                ),
                ?,
                ?,
                ?
            )
            """,
            [
                domain_parent_id,
                domain_type_id,
                domain_orientation_id,
                domain_parent_id,
                domain_parent_id,
                domain_code,
                display_name.strip(),
                description,
            ],
        )
        row = cursor.fetchone()
        conn.commit()
    return _normalize_row(_row_to_dict(cursor, row))


def create_collection(
    settings: Settings,
    domain_code: str,
    collection_code: str,
    display_name: str,
    description: str | None = None,
) -> dict[str, object]:
    collection_code = _slug_code(collection_code)
    with get_connection(settings) as conn:
        cursor = conn.cursor()
        cursor.execute(
            """
            INSERT INTO dbo.Collections (
                DomainId,
                CollectionCode,
                DisplayName,
                Description
            )
            SELECT
                d.DomainId,
                ?,
                ?,
                ?
            FROM dbo.Domains d
            WHERE d.DomainCode = ? AND d.Status = 'Active'
            """,
            [collection_code, display_name.strip(), description, domain_code],
        )
        if cursor.rowcount == 0:
            raise ValueError(f"Active domain not found for code '{domain_code}'.")
        conn.commit()

    rows = list_collections(settings, domain_code)
    for row in rows:
        if row.get("CollectionCode") == collection_code:
            return row
    raise ValueError(f"Collection '{collection_code}' was inserted but could not be reloaded.")


def update_collection(
    settings: Settings,
    collection_code: str,
    display_name: str,
    description: str | None = None,
) -> dict[str, object]:
    with get_connection(settings) as conn:
        cursor = conn.cursor()
        cursor.execute(
            """
            UPDATE dbo.Collections
            SET
                DisplayName = ?,
                Description = ?,
                UpdatedAtUtc = SYSUTCDATETIME()
            WHERE CollectionCode = ? AND Status = 'Active'
            """,
            [display_name.strip(), description, _slug_code(collection_code)],
        )
        if cursor.rowcount == 0:
            raise ValueError(f"Active collection not found for code '{collection_code}'.")
        conn.commit()

    rows = list_collections(settings)
    for row in rows:
        if row.get("CollectionCode") == _slug_code(collection_code):
            return row
    raise ValueError(f"Collection '{collection_code}' was updated but could not be reloaded.")


def update_domain(
    settings: Settings,
    domain_code: str,
    display_name: str,
    description: str | None = None,
    domain_type_id: int | None = None,
    domain_orientation_id: int | None = None,
    parent_domain_id: str | None = None,
) -> dict[str, object]:
    with get_connection(settings) as conn:
        cursor = conn.cursor()
        cursor.execute(
            """
            UPDATE dbo.Domains
            SET
                DisplayName = ?,
                Description = ?,
                DomainTypeId = ?,
                DomainOrientationId = ?,
                DomainParentId = ?,
                UpdatedAtUtc = SYSUTCDATETIME()
            WHERE DomainCode = ? AND Status = 'Active'
            """,
            [display_name.strip(), description, domain_type_id, domain_orientation_id, parent_domain_id, _slug_code(domain_code)],
        )
        if cursor.rowcount == 0:
            raise ValueError(f"Active domain not found for code '{domain_code}'.")
        conn.commit()

    rows = list_domains(settings)
    for row in rows:
        if row.get("DomainCode") == _slug_code(domain_code):
            return row
    raise ValueError(f"Domain '{domain_code}' was updated but could not be reloaded.")


def move_domain(
    settings: Settings,
    *,
    domain_code: str,
    new_parent_domain_code: str | None = None,
    new_domain_type_id: int | None = None,
) -> dict[str, object]:
    normalized_domain_code = _slug_code(domain_code)
    normalized_parent_code = _slug_code(new_parent_domain_code) if new_parent_domain_code and new_parent_domain_code.strip() else None
    if normalized_parent_code and normalized_domain_code == normalized_parent_code:
        raise ValueError("A domain cannot be moved under itself.")

    domains = list_domains(settings)
    domain_by_code = {
        str(row.get("DomainCode") or ""): row
        for row in domains
        if row.get("DomainCode")
    }

    source_domain = domain_by_code.get(normalized_domain_code)
    target_parent = domain_by_code.get(normalized_parent_code) if normalized_parent_code else None
    if source_domain is None:
        raise ValueError(f"Active domain not found for code '{domain_code}'.")
    if normalized_parent_code and target_parent is None:
        raise ValueError(f"Active target parent domain not found for code '{new_parent_domain_code}'.")
    if target_parent is None and new_domain_type_id is None:
        raise ValueError("A target parent domain or target domain type is required.")

    source_domain_id = str(source_domain.get("DomainId") or "").strip()
    target_parent_id = str(target_parent.get("DomainId") or "").strip() if target_parent is not None else ""
    current_parent_id = str(source_domain.get("DomainParentId") or "").strip()
    current_domain_type_id = int(source_domain.get("DomainTypeId") or 0)
    target_domain_type_id = int(new_domain_type_id or target_parent.get("DomainTypeId") or 0)
    if current_parent_id == target_parent_id and current_domain_type_id == target_domain_type_id:
        return source_domain

    child_domains_by_parent_id: dict[str, list[dict[str, object]]] = {}
    for domain in domains:
        parent_id = str(domain.get("DomainParentId") or "").strip()
        if parent_id:
            child_domains_by_parent_id.setdefault(parent_id, []).append(domain)

    def _is_descendant(candidate_id: str, ancestor_id: str) -> bool:
        for child in child_domains_by_parent_id.get(ancestor_id, []):
            child_id = str(child.get("DomainId") or "").strip()
            if child_id == candidate_id or _is_descendant(candidate_id, child_id):
                return True
        return False

    if target_parent is not None and _is_descendant(target_parent_id, source_domain_id):
        raise ValueError("A domain cannot be moved under one of its descendants.")

    with get_connection(settings) as conn:
        cursor = conn.cursor()
        if target_parent is None:
            cursor.execute(
                """
                SELECT COALESCE(MAX(DisplayOrder) + 10, 10)
                FROM dbo.Domains
                WHERE Status = 'Active'
                  AND DomainParentId IS NULL
                  AND DomainTypeId = ?
                """,
                [target_domain_type_id],
            )
        else:
            cursor.execute(
                """
                SELECT COALESCE(MAX(DisplayOrder) + 10, 10)
                FROM dbo.Domains
                WHERE Status = 'Active'
                  AND DomainParentId = ?
                """,
                [target_parent_id],
            )
        next_display_order = int(cursor.fetchone()[0] or 10)

        cursor.execute(
            """
            UPDATE dbo.Domains
            SET
                DomainParentId = ?,
                DomainTypeId = ?,
                DomainOrientationId = ?,
                DisplayOrder = ?,
                UpdatedAtUtc = SYSUTCDATETIME()
            WHERE DomainCode = ? AND Status = 'Active'
            """,
            [
                target_parent_id or None,
                target_domain_type_id,
                target_parent.get("DomainOrientationId") if target_parent is not None else source_domain.get("DomainOrientationId"),
                next_display_order,
                normalized_domain_code,
            ],
        )
        if cursor.rowcount == 0:
            raise ValueError(f"Domain '{domain_code}' could not be moved.")
        conn.commit()

    rows = list_domains(settings)
    for row in rows:
        if row.get("DomainCode") == normalized_domain_code:
            return row
    raise ValueError(f"Domain '{domain_code}' was moved but could not be reloaded.")


def get_domain_delete_preview(
    settings: Settings,
    domain_code: str,
) -> dict[str, object]:
    normalized_domain_code = _slug_code(domain_code)

    with get_connection(settings) as conn:
        cursor = conn.cursor()
        cursor.execute(
            """
            WITH DomainTree AS (
                SELECT
                    DomainId,
                    DomainCode,
                    CAST(0 AS INT) AS Depth
                FROM dbo.Domains
                WHERE DomainCode = ? AND Status = 'Active'

                UNION ALL

                SELECT
                    d.DomainId,
                    d.DomainCode,
                    dt.Depth + 1
                FROM dbo.Domains d
                JOIN DomainTree dt
                    ON d.DomainParentId = dt.DomainId
                WHERE d.Status = 'Active'
            )
            SELECT DomainId, DomainCode, Depth
            FROM DomainTree
            """,
            [normalized_domain_code],
        )
        domain_rows = [_row_to_dict(cursor, row) for row in cursor.fetchall()]
        if not domain_rows:
            raise ValueError(f"Active domain not found for code '{domain_code}'.")

        domain_ids = [str(row["DomainId"]) for row in domain_rows]
        domain_id_placeholders = ", ".join("?" for _ in domain_ids)

        cursor.execute(
            f"""
            SELECT COUNT(*) AS CollectionCount
            FROM dbo.Collections
            WHERE DomainId IN ({domain_id_placeholders})
            """,
            domain_ids,
        )
        collection_count = int(cursor.fetchone()[0] or 0)

        cursor.execute(
            f"""
            SELECT COUNT(*) AS DocumentCount
            FROM dbo.Documents d
            JOIN dbo.Collections c
                ON c.CollectionId = d.CollectionId
            WHERE c.DomainId IN ({domain_id_placeholders})
            """,
            domain_ids,
        )
        document_count = int(cursor.fetchone()[0] or 0)

    return {
        "domainCode": normalized_domain_code,
        "domainCount": len(domain_rows),
        "collectionCount": collection_count,
        "documentCount": document_count,
    }


def get_collection_delete_preview(
    settings: Settings,
    collection_code: str,
) -> dict[str, object]:
    normalized_collection_code = _slug_code(collection_code)

    row = fetch_one(
        settings,
        """
        SELECT
            c.CollectionCode,
            COUNT(d.DocumentId) AS DocumentCount
        FROM dbo.Collections c
        LEFT JOIN dbo.Documents d
            ON d.CollectionId = c.CollectionId
        WHERE c.CollectionCode = ? AND c.Status = 'Active'
        GROUP BY c.CollectionCode
        """,
        [normalized_collection_code],
    )
    if row is None:
        raise ValueError(f"Active collection not found for code '{collection_code}'.")

    normalized_row = _normalize_row(row)
    return {
        "collectionCode": normalized_row.get("CollectionCode") or normalized_collection_code,
        "documentCount": int(normalized_row.get("DocumentCount") or 0),
    }


def delete_domain(
    settings: Settings,
    domain_code: str,
) -> dict[str, object]:
    normalized_domain_code = _slug_code(domain_code)

    with get_connection(settings) as conn:
        cursor = conn.cursor()
        cursor.execute(
            """
            WITH DomainTree AS (
                SELECT
                    DomainId,
                    DomainCode,
                    CAST(0 AS INT) AS Depth
                FROM dbo.Domains
                WHERE DomainCode = ? AND Status = 'Active'

                UNION ALL

                SELECT
                    d.DomainId,
                    d.DomainCode,
                    dt.Depth + 1
                FROM dbo.Domains d
                JOIN DomainTree dt
                    ON d.DomainParentId = dt.DomainId
                WHERE d.Status = 'Active'
            )
            SELECT DomainId, DomainCode, Depth
            FROM DomainTree
            """,
            [normalized_domain_code],
        )
        domain_rows = [_row_to_dict(cursor, row) for row in cursor.fetchall()]
        if not domain_rows:
            raise ValueError(f"Active domain not found for code '{domain_code}'.")

        domain_ids = [str(row["DomainId"]) for row in domain_rows]
        domain_id_placeholders = ", ".join("?" for _ in domain_ids)

        cursor.execute(
            f"""
            SELECT CollectionId, CollectionCode
            FROM dbo.Collections
            WHERE DomainId IN ({domain_id_placeholders})
            """,
            domain_ids,
        )
        collection_rows = [_row_to_dict(cursor, row) for row in cursor.fetchall()]
        collection_ids = [str(row["CollectionId"]) for row in collection_rows]
        has_legacy_chroma_table = _table_exists(cursor, "dbo.LegacyChromaMigrationState")
        has_domain_cluster_members_table = _table_exists(cursor, "dbo.DomainClusterMembers")

        if collection_ids:
            collection_id_placeholders = ", ".join("?" for _ in collection_ids)

            cursor.execute(
                f"""
                DELETE emb
                FROM dbo.ContentUnitEmbeddings768 emb
                JOIN dbo.ContentUnits cu
                    ON cu.ContentUnitId = emb.ContentUnitId
                JOIN dbo.Documents d
                    ON d.DocumentId = cu.DocumentId
                WHERE d.CollectionId IN ({collection_id_placeholders})
                """,
                collection_ids,
            )

            if has_legacy_chroma_table:
                cursor.execute(
                    f"""
                    DELETE FROM dbo.LegacyChromaMigrationState
                    WHERE CollectionId IN ({collection_id_placeholders})
                    """,
                    collection_ids,
                )

            cursor.execute(
                f"""
                DELETE cu
                FROM dbo.ContentUnits cu
                JOIN dbo.Documents d
                    ON d.DocumentId = cu.DocumentId
                WHERE d.CollectionId IN ({collection_id_placeholders})
                """,
                collection_ids,
            )

            cursor.execute(
                f"""
                DELETE FROM dbo.Documents
                WHERE CollectionId IN ({collection_id_placeholders})
                """,
                collection_ids,
            )

            cursor.execute(
                f"""
                DELETE FROM dbo.Collections
                WHERE CollectionId IN ({collection_id_placeholders})
                """,
                collection_ids,
            )

        if has_legacy_chroma_table and _column_exists(cursor, "dbo.LegacyChromaMigrationState", "DomainId"):
            cursor.execute(
                f"""
                DELETE FROM dbo.LegacyChromaMigrationState
                WHERE DomainId IN ({domain_id_placeholders})
                """,
                domain_ids,
            )

        if has_domain_cluster_members_table:
            cursor.execute(
                f"""
                DELETE FROM dbo.DomainClusterMembers
                WHERE DomainId IN ({domain_id_placeholders})
                """,
                domain_ids,
            )

        for domain_row in sorted(domain_rows, key=lambda item: int(item["Depth"]), reverse=True):
            cursor.execute(
                "DELETE FROM dbo.Domains WHERE DomainId = ?",
                [str(domain_row["DomainId"])],
            )

        conn.commit()

    return {
        "status": "deleted",
        "domainCode": normalized_domain_code,
        "deletedDomainCount": len(domain_rows),
        "deletedCollectionCount": len(collection_rows),
    }


def delete_collection(
    settings: Settings,
    collection_code: str,
) -> dict[str, object]:
    normalized_collection_code = _slug_code(collection_code)

    with get_connection(settings) as conn:
        cursor = conn.cursor()
        cursor.execute(
            """
            SELECT CollectionId, CollectionCode
            FROM dbo.Collections
            WHERE CollectionCode = ? AND Status = 'Active'
            """,
            [normalized_collection_code],
        )
        collection_row = cursor.fetchone()
        if collection_row is None:
            raise ValueError(f"Active collection not found for code '{collection_code}'.")

        collection = _row_to_dict(cursor, collection_row)
        collection_id = str(collection["CollectionId"])
        has_legacy_chroma_table = _table_exists(cursor, "dbo.LegacyChromaMigrationState")

        cursor.execute(
            """
            SELECT COUNT(*) AS DocumentCount
            FROM dbo.Documents
            WHERE CollectionId = ?
            """,
            [collection_id],
        )
        document_count = int(cursor.fetchone()[0] or 0)

        cursor.execute(
            """
            DELETE emb
            FROM dbo.ContentUnitEmbeddings768 emb
            JOIN dbo.ContentUnits cu
                ON cu.ContentUnitId = emb.ContentUnitId
            JOIN dbo.Documents d
                ON d.DocumentId = cu.DocumentId
            WHERE d.CollectionId = ?
            """,
            [collection_id],
        )

        if has_legacy_chroma_table:
            cursor.execute(
                """
                DELETE FROM dbo.LegacyChromaMigrationState
                WHERE CollectionId = ?
                """,
                [collection_id],
            )

        cursor.execute(
            """
            DELETE cu
            FROM dbo.ContentUnits cu
            JOIN dbo.Documents d
                ON d.DocumentId = cu.DocumentId
            WHERE d.CollectionId = ?
            """,
            [collection_id],
        )

        cursor.execute(
            """
            DELETE FROM dbo.Documents
            WHERE CollectionId = ?
            """,
            [collection_id],
        )

        cursor.execute(
            """
            DELETE FROM dbo.Collections
            WHERE CollectionId = ?
            """,
            [collection_id],
        )

        conn.commit()

    return {
        "status": "deleted",
        "collectionCode": normalized_collection_code,
        "deletedDocumentCount": document_count,
    }


def create_text_document(
    settings: Settings,
    collection_code: str,
    source_name: str,
    body_text: str,
    source_type: str = "pasted_text",
) -> dict[str, object]:
    text = body_text.strip()
    if not text:
        raise ValueError("Body text is required.")

    chunks = _chunk_text(text)

    with get_connection(settings) as conn:
        cursor = conn.cursor()
        cursor.execute(
            """
            INSERT INTO dbo.Documents (
                CollectionId,
                SourceName,
                SourceType
            )
            OUTPUT
                inserted.DocumentId,
                inserted.CollectionId,
                inserted.SourceName,
                inserted.SourceType,
                inserted.Status
            SELECT
                c.CollectionId,
                ?,
                ?
            FROM dbo.Collections c
            WHERE c.CollectionCode = ? AND c.Status = 'Active'
            """,
            [source_name.strip(), source_type.strip(), collection_code],
        )
        document_row = cursor.fetchone()
        if document_row is None:
            raise ValueError(f"Active collection not found for code '{collection_code}'.")

        document = _row_to_dict(cursor, document_row)
        document_id = document["DocumentId"]

        for index, chunk in enumerate(chunks, start=1):
            cursor.execute(
                """
                INSERT INTO dbo.ContentUnits (
                    DocumentId,
                    UnitType,
                    UnitOrdinal,
                    Heading,
                    BodyText,
                    TokenCount
                )
                VALUES (?, 'Chunk', ?, ?, ?, ?)
                """,
                [
                    document_id,
                    index,
                    source_name.strip(),
                    chunk,
                    _estimate_token_count(chunk),
                ],
            )

        conn.commit()

    return {
        **_normalize_row(document),
        "CollectionCode": collection_code,
        "ChunkCount": len(chunks),
    }


def archive_collection(
    settings: Settings,
    collection_code: str,
) -> None:
    with get_connection(settings) as conn:
        cursor = conn.cursor()
        cursor.execute(
            """
            UPDATE cu
            SET
                cu.Status = 'Archived',
                cu.UpdatedAtUtc = SYSUTCDATETIME()
            FROM dbo.ContentUnits cu
            JOIN dbo.Documents d
                ON d.DocumentId = cu.DocumentId
            JOIN dbo.Collections c
                ON c.CollectionId = d.CollectionId
            WHERE c.CollectionCode = ? AND cu.Status = 'Active'
            """,
            [collection_code],
        )
        cursor.execute(
            """
            UPDATE d
            SET
                d.Status = 'Archived',
                d.UpdatedAtUtc = SYSUTCDATETIME()
            FROM dbo.Documents d
            JOIN dbo.Collections c
                ON c.CollectionId = d.CollectionId
            WHERE c.CollectionCode = ? AND d.Status = 'Active'
            """,
            [collection_code],
        )
        cursor.execute(
            """
            UPDATE dbo.Collections
            SET
                Status = 'Archived',
                UpdatedAtUtc = SYSUTCDATETIME()
            WHERE CollectionCode = ? AND Status = 'Active'
            """,
            [collection_code],
        )
        if cursor.rowcount == 0:
            raise ValueError(f"Active collection not found for code '{collection_code}'.")
        conn.commit()


def archive_document(
    settings: Settings,
    document_id: str,
) -> None:
    with get_connection(settings) as conn:
        cursor = conn.cursor()
        cursor.execute(
            """
            DELETE emb
            FROM dbo.ContentUnitEmbeddings768 emb
            JOIN dbo.ContentUnits cu
                ON cu.ContentUnitId = emb.ContentUnitId
            WHERE cu.DocumentId = ?
            """,
            [document_id],
        )
        cursor.execute(
            """
            DELETE FROM dbo.ContentUnits
            WHERE DocumentId = ?
            """,
            [document_id],
        )
        cursor.execute(
            """
            DELETE FROM dbo.Documents
            WHERE DocumentId = ? AND Status = 'Active'
            """,
            [document_id],
        )
        if cursor.rowcount == 0:
            raise ValueError(f"Active document not found for id '{document_id}'.")
        conn.commit()


def list_collection_documents(
    settings: Settings,
    collection_code: str,
) -> list[dict[str, object]]:
    rows = fetch_all(
        settings,
        """
        SELECT
            d.DocumentId,
            d.SourceName,
            d.SourceType,
            d.Status,
            d.CreatedAtUtc,
            d.UpdatedAtUtc,
            c.CollectionCode,
            COUNT(cu.ContentUnitId) AS ContentUnitCount,
            COUNT(emb.ContentUnitId) AS EmbeddedContentUnitCount
        FROM dbo.Documents d
        JOIN dbo.Collections c
            ON c.CollectionId = d.CollectionId
        LEFT JOIN dbo.ContentUnits cu
            ON cu.DocumentId = d.DocumentId AND cu.Status = 'Active'
        LEFT JOIN dbo.ContentUnitEmbeddings768 emb
            ON emb.ContentUnitId = cu.ContentUnitId
        WHERE c.CollectionCode = ? AND d.Status = 'Active'
        GROUP BY
            d.DocumentId,
            d.SourceName,
            d.SourceType,
            d.Status,
            d.CreatedAtUtc,
            d.UpdatedAtUtc,
            c.CollectionCode
        ORDER BY d.UpdatedAtUtc DESC, d.CreatedAtUtc DESC
        """,
        [collection_code],
    )
    return [_normalize_row(row) for row in rows]


def list_document_chunks(
    settings: Settings,
    document_id: str,
) -> list[dict[str, object]]:
    rows = fetch_all(
        settings,
        """
        SELECT
            cu.ContentUnitId,
            cu.DocumentId,
            cu.UnitOrdinal,
            cu.UnitType,
            cu.TokenCount,
            cu.Status,
            cu.BodyText
        FROM dbo.ContentUnits cu
        JOIN dbo.Documents d
            ON d.DocumentId = cu.DocumentId
        WHERE cu.DocumentId = ? AND cu.Status = 'Active' AND d.Status = 'Active'
        ORDER BY cu.UnitOrdinal
        """,
        [document_id],
    )
    return [_normalize_row(row) for row in rows]


def delete_content_unit(
    settings: Settings,
    content_unit_id: str,
) -> None:
    with get_connection(settings) as conn:
        cursor = conn.cursor()
        cursor.execute(
            """
            DELETE FROM dbo.ContentUnitEmbeddings768
            WHERE ContentUnitId = ?
            """,
            [content_unit_id],
        )
        cursor.execute(
            """
            DELETE FROM dbo.ContentUnits
            WHERE ContentUnitId = ? AND Status = 'Active'
            """,
            [content_unit_id],
        )
        if cursor.rowcount == 0:
            raise ValueError(f"Active content unit not found for id '{content_unit_id}'.")
        conn.commit()


def get_recent_context_units(
    settings: Settings,
    collection_codes: list[str],
    limit: int = 12,
) -> list[dict[str, object]]:
    cleaned_codes = [_slug_code(code) for code in collection_codes if code.strip()]
    if not cleaned_codes:
        return []

    placeholders = ", ".join("?" for _ in cleaned_codes)
    rows = fetch_all(
        settings,
        f"""
        SELECT TOP ({limit})
            c.CollectionCode,
            c.DisplayName AS CollectionDisplayName,
            d.SourceName,
            cu.ContentUnitId,
            cu.UnitType,
            cu.UnitOrdinal,
            cu.BodyText
        FROM dbo.ContentUnits cu
        JOIN dbo.Documents d
            ON d.DocumentId = cu.DocumentId
        JOIN dbo.Collections c
            ON c.CollectionId = d.CollectionId
        WHERE c.CollectionCode IN ({placeholders})
          AND c.Status = 'Active'
          AND d.Status = 'Active'
          AND cu.Status = 'Active'
        ORDER BY d.CreatedAtUtc DESC, cu.UnitOrdinal ASC
        """,
        cleaned_codes,
    )
    return [_normalize_row(row) for row in rows]


def list_context_units_for_collections(
    settings: Settings,
    collection_codes: list[str],
) -> list[dict[str, object]]:
    cleaned_codes = [_slug_code(code) for code in collection_codes if code.strip()]
    if not cleaned_codes:
        return []

    placeholders = ", ".join("?" for _ in cleaned_codes)
    rows = fetch_all(
        settings,
        f"""
        SELECT
            c.CollectionCode,
            c.DisplayName AS CollectionDisplayName,
            d.DocumentId,
            d.SourceName,
            cu.ContentUnitId,
            cu.UnitType,
            cu.UnitOrdinal,
            cu.TokenCount,
            cu.BodyText
        FROM dbo.ContentUnits cu
        JOIN dbo.Documents d
            ON d.DocumentId = cu.DocumentId
        JOIN dbo.Collections c
            ON c.CollectionId = d.CollectionId
        WHERE c.CollectionCode IN ({placeholders})
          AND c.Status = 'Active'
          AND d.Status = 'Active'
          AND cu.Status = 'Active'
        ORDER BY c.DisplayName, d.SourceName, cu.UnitOrdinal ASC
        """,
        cleaned_codes,
    )
    return [_normalize_row(row) for row in rows]


def upsert_app_user(
    settings: Settings,
    windows_user_name: str,
    windows_sid: str | None = None,
    display_name: str | None = None,
) -> dict[str, object]:
    normalized_user_name = windows_user_name.strip()
    normalized_sid = windows_sid.strip() if windows_sid else None
    normalized_display_name = display_name.strip() if display_name else None

    with get_connection(settings) as conn:
        cursor = conn.cursor()
        if normalized_sid:
            cursor.execute(
                """
                SELECT TOP 1 AppUserId
                FROM dbo.AppUsers
                WHERE WindowsSid = ?
                """,
                [normalized_sid],
            )
            row = cursor.fetchone()
            if row is not None:
                cursor.execute(
                    """
                    UPDATE dbo.AppUsers
                    SET
                        WindowsUserName = ?,
                        DisplayName = COALESCE(?, DisplayName),
                        LastSeenAtUtc = SYSUTCDATETIME(),
                        UpdatedAtUtc = SYSUTCDATETIME()
                    WHERE AppUserId = ?
                    """,
                    [normalized_user_name, normalized_display_name, row.AppUserId],
                )
                conn.commit()
                return get_app_user(settings, app_user_id=str(row.AppUserId)) or {}

        cursor.execute(
            """
            SELECT TOP 1 AppUserId
            FROM dbo.AppUsers
            WHERE IdentityProvider = 'Windows' AND WindowsUserName = ?
            """,
            [normalized_user_name],
        )
        row = cursor.fetchone()
        if row is not None:
            cursor.execute(
                """
                UPDATE dbo.AppUsers
                SET
                    WindowsSid = COALESCE(WindowsSid, ?),
                    DisplayName = COALESCE(?, DisplayName),
                    LastSeenAtUtc = SYSUTCDATETIME(),
                    UpdatedAtUtc = SYSUTCDATETIME()
                WHERE AppUserId = ?
                """,
                [normalized_sid, normalized_display_name, row.AppUserId],
            )
            conn.commit()
            return get_app_user(settings, app_user_id=str(row.AppUserId)) or {}

        cursor.execute(
            """
            INSERT INTO dbo.AppUsers (
                WindowsUserName,
                WindowsSid,
                DisplayName
            )
            VALUES (?, ?, ?)
            """,
            [normalized_user_name, normalized_sid, normalized_display_name],
        )
        conn.commit()

    return get_app_user(settings, windows_sid=normalized_sid, windows_user_name=normalized_user_name) or {}


def get_app_user(
    settings: Settings,
    *,
    app_user_id: str | None = None,
    windows_sid: str | None = None,
    windows_user_name: str | None = None,
) -> dict[str, object] | None:
    if app_user_id:
        row = fetch_one(
            settings,
            """
            SELECT
                AppUserId,
                IdentityProvider,
                WindowsUserName,
                WindowsSid,
                DisplayName,
                Status,
                FirstSeenAtUtc,
                LastSeenAtUtc,
                CreatedAtUtc,
                UpdatedAtUtc
            FROM dbo.AppUsers
            WHERE AppUserId = ?
            """,
            [app_user_id],
        )
        return _normalize_row(row) if row else None

    params: list[object] = []
    where_clause = []
    if windows_sid:
        where_clause.append("WindowsSid = ?")
        params.append(windows_sid)
    if windows_user_name:
        where_clause.append("WindowsUserName = ?")
        params.append(windows_user_name)
    if not where_clause:
        return None

    row = fetch_one(
        settings,
        f"""
        SELECT TOP 1
            AppUserId,
            IdentityProvider,
            WindowsUserName,
            WindowsSid,
            DisplayName,
            Status,
            FirstSeenAtUtc,
            LastSeenAtUtc,
            CreatedAtUtc,
            UpdatedAtUtc
        FROM dbo.AppUsers
        WHERE {' OR '.join(where_clause)}
        ORDER BY CreatedAtUtc DESC
        """,
        params,
    )
    return _normalize_row(row) if row else None


def has_user_chat_backup_files(settings: Settings, app_user_id: str) -> bool:
    row = fetch_one(
        settings,
        """
        SELECT TOP 1 1 AS HasBackups
        FROM dbo.UserChatBackupFiles
        WHERE AppUserId = ? AND IsDeleted = 0
        """,
        [app_user_id],
    )
    return row is not None


def list_user_chat_backup_files(
    settings: Settings,
    app_user_id: str,
    *,
    include_payload: bool,
) -> list[dict[str, object]]:
    payload_clause = "FileContentCompressedEncrypted," if include_payload else "CAST(NULL AS VARBINARY(MAX)) AS FileContentCompressedEncrypted,"
    rows = fetch_all(
        settings,
        f"""
        SELECT
            UserChatBackupFileId,
            AppUserId,
            RootCollectionCode,
            RootDisplayName,
            FileName,
            {payload_clause}
            ContentHashSha256,
            CompressionType,
            EncryptionType,
            KeyVersion,
            ClientModifiedUtc,
            BackupCreatedUtc,
            BackupUpdatedUtc,
            LastRestoredUtc,
            ClientMachineName,
            AppVersion,
            IsDeleted
        FROM dbo.UserChatBackupFiles
        WHERE AppUserId = ? AND IsDeleted = 0
        ORDER BY RootDisplayName
        """,
        [app_user_id],
    )
    return [_normalize_row(row) for row in rows]


def upsert_user_chat_backup_file(
    settings: Settings,
    *,
    app_user_id: str,
    root_collection_code: str,
    root_display_name: str,
    file_name: str,
    payload_bytes: bytes,
    content_hash_bytes: bytes,
    compression_type: str,
    encryption_type: str,
    key_version: int,
    client_modified_utc,
    client_machine_name: str | None,
    app_version: str | None,
    is_deleted: bool,
) -> None:
    with get_connection(settings) as conn:
        cursor = conn.cursor()
        cursor.execute(
            """
            SELECT TOP 1 UserChatBackupFileId
            FROM dbo.UserChatBackupFiles
            WHERE AppUserId = ? AND RootCollectionCode = ?
            """,
            [app_user_id, root_collection_code],
        )
        row = cursor.fetchone()
        if row is None:
            cursor.execute(
                """
                INSERT INTO dbo.UserChatBackupFiles (
                    AppUserId,
                    RootCollectionCode,
                    RootDisplayName,
                    FileName,
                    FileContentCompressedEncrypted,
                    ContentHashSha256,
                    CompressionType,
                    EncryptionType,
                    KeyVersion,
                    ClientModifiedUtc,
                    ClientMachineName,
                    AppVersion,
                    IsDeleted
                )
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                """,
                [
                    app_user_id,
                    root_collection_code,
                    root_display_name,
                    file_name,
                    payload_bytes,
                    content_hash_bytes,
                    compression_type,
                    encryption_type,
                    key_version,
                    client_modified_utc,
                    client_machine_name,
                    app_version,
                    1 if is_deleted else 0,
                ],
            )
        else:
            cursor.execute(
                """
                UPDATE dbo.UserChatBackupFiles
                SET
                    RootDisplayName = ?,
                    FileName = ?,
                    FileContentCompressedEncrypted = ?,
                    ContentHashSha256 = ?,
                    CompressionType = ?,
                    EncryptionType = ?,
                    KeyVersion = ?,
                    ClientModifiedUtc = ?,
                    BackupUpdatedUtc = SYSUTCDATETIME(),
                    ClientMachineName = ?,
                    AppVersion = ?,
                    IsDeleted = ?
                WHERE UserChatBackupFileId = ?
                """,
                [
                    root_display_name,
                    file_name,
                    payload_bytes,
                    content_hash_bytes,
                    compression_type,
                    encryption_type,
                    key_version,
                    client_modified_utc,
                    client_machine_name,
                    app_version,
                    1 if is_deleted else 0,
                    row.UserChatBackupFileId,
                ],
            )
        conn.commit()


def mark_user_chat_backup_files_restored(
    settings: Settings,
    app_user_id: str,
) -> None:
    with get_connection(settings) as conn:
        cursor = conn.cursor()
        cursor.execute(
            """
            UPDATE dbo.UserChatBackupFiles
            SET LastRestoredUtc = SYSUTCDATETIME()
            WHERE AppUserId = ? AND IsDeleted = 0
            """,
            [app_user_id],
        )
        conn.commit()


def clear_policy_tables(settings: Settings) -> dict[str, object]:
    table_names = [
        "PolicyControlStatements",
        "PolicyPrincipleLinks",
        "PrincipleRelations",
        "PolicyObjectives",
        "PolicyPrinciples",
        "PolicyAccountabilityStatements",
        "PolicyTransparencyStatements",
        "PolicyStrategyStatements",
        "PolicyConsequences",
        "PolicySections",
        "Policies",
        "Principles",
        "PolicyTemplates",
    ]

    with get_connection(settings) as conn:
        cursor = conn.cursor()
        cursor.execute("DELETE FROM dbo.PolicyControlStatements;")
        cursor.execute("DELETE FROM dbo.PolicyPrincipleLinks;")
        cursor.execute("DELETE FROM dbo.PrincipleRelations;")
        cursor.execute("DELETE FROM dbo.PolicyObjectives;")
        cursor.execute("DELETE FROM dbo.PolicyPrinciples;")
        cursor.execute("DELETE FROM dbo.PolicyAccountabilityStatements;")
        cursor.execute("DELETE FROM dbo.PolicyTransparencyStatements;")
        cursor.execute("DELETE FROM dbo.PolicyStrategyStatements;")
        cursor.execute("DELETE FROM dbo.PolicyConsequences;")
        cursor.execute("DELETE FROM dbo.PolicySections;")
        cursor.execute("DELETE FROM dbo.Policies;")
        cursor.execute("DELETE FROM dbo.Principles;")
        cursor.execute("DELETE FROM dbo.PolicyTemplates;")

        cursor.execute(
            """
            SELECT 'PolicyTemplates' AS TableName, COUNT(*) AS TotalRows FROM dbo.PolicyTemplates
            UNION ALL SELECT 'Policies', COUNT(*) FROM dbo.Policies
            UNION ALL SELECT 'PolicySections', COUNT(*) FROM dbo.PolicySections
            UNION ALL SELECT 'PolicyObjectives', COUNT(*) FROM dbo.PolicyObjectives
            UNION ALL SELECT 'PolicyPrinciples', COUNT(*) FROM dbo.PolicyPrinciples
            UNION ALL SELECT 'PolicyAccountabilityStatements', COUNT(*) FROM dbo.PolicyAccountabilityStatements
            UNION ALL SELECT 'PolicyTransparencyStatements', COUNT(*) FROM dbo.PolicyTransparencyStatements
            UNION ALL SELECT 'PolicyStrategyStatements', COUNT(*) FROM dbo.PolicyStrategyStatements
            UNION ALL SELECT 'PolicyConsequences', COUNT(*) FROM dbo.PolicyConsequences
            UNION ALL SELECT 'Principles', COUNT(*) FROM dbo.Principles
            UNION ALL SELECT 'PolicyPrincipleLinks', COUNT(*) FROM dbo.PolicyPrincipleLinks
            UNION ALL SELECT 'PrincipleRelations', COUNT(*) FROM dbo.PrincipleRelations
            UNION ALL SELECT 'PolicyControlStatements', COUNT(*) FROM dbo.PolicyControlStatements
            ORDER BY TableName
            """
        )
        count_rows = [_normalize_row(_row_to_dict(cursor, row)) for row in cursor.fetchall()]
        conn.commit()

    return {
        "status": "ok",
        "clearedTables": table_names,
        "counts": count_rows,
    }


def upsert_policy_draft(
    settings: Settings,
    *,
    root_domain_code: str,
    policy_code: str,
    policy_title: str,
    version_text: str,
    status: str,
    template_path: str | None,
    template_body: str | None,
    source_model_name: str | None,
    objectives: list[dict[str, object]],
    principles: list[dict[str, object]],
    accountability: list[dict[str, object]],
    transparency: list[dict[str, object]],
    strategy: list[dict[str, object]],
    consequences: list[dict[str, object]],
    control_statements: list[dict[str, object]],
) -> dict[str, object]:
    def _next_version_text(cursor, root_domain_id: object, normalized_title: str) -> str:
        cursor.execute(
            """
            SELECT VersionText
            FROM dbo.Policies
            WHERE RootDomainId = ? AND PolicyTitle = ?
            """,
            [root_domain_id, normalized_title],
        )
        max_minor = 0
        for row in cursor.fetchall():
            raw_text = str(row.VersionText or "").strip()
            match = re.match(r"^(\d+)\.(\d+)", raw_text)
            if not match:
                continue
            if int(match.group(1)) != 0:
                continue
            max_minor = max(max_minor, int(match.group(2)))
        return f"0.{max_minor + 1:02d}"

    normalized_root_domain_code = _slug_code(root_domain_code)
    normalized_policy_code = _slug_code(policy_code)
    normalized_status = status.strip() or "Draft"
    normalized_version_text = version_text.strip() if version_text else ""
    normalized_policy_title = policy_title.strip()
    if not normalized_policy_title:
        raise ValueError("Policy title is required.")

    normalized_template_path = template_path.strip() if template_path else None
    template_code = None
    template_name = None
    if normalized_template_path:
        template_code = _slug_code(Path(normalized_template_path).stem)
        template_name = Path(normalized_template_path).stem.replace("-", " ").replace("_", " ").strip() or template_code

    section_definitions = [
        ("OBJECTIVE", "Objectives", 10),
        ("PRINCIPLE", "Principles", 20),
        ("ACCOUNTABILITY", "Accountability", 30),
        ("TRANSPARENCY", "Transparency", 40),
        ("STRATEGY", "Strategy", 50),
        ("CONTROL_POLICY", "Policy Statements By Control", 60),
        ("CONSEQUENCE", "Consequences", 70),
    ]

    section_payloads: dict[str, list[dict[str, object]]] = {
        "OBJECTIVE": objectives,
        "PRINCIPLE": principles,
        "ACCOUNTABILITY": accountability,
        "TRANSPARENCY": transparency,
        "STRATEGY": strategy,
        "CONSEQUENCE": consequences,
    }

    def _normalize_statement_rows(rows: list[dict[str, object]]) -> list[dict[str, object]]:
        normalized_rows: list[dict[str, object]] = []
        for index, row in enumerate(rows, start=1):
            statement_text = str(row.get("statementText") or "").strip()
            if not statement_text:
                continue
            display_order = int(row.get("displayOrder") or (index * 10))
            review_status = str(row.get("reviewStatus") or "Pending").strip() or "Pending"
            normalized_rows.append(
                {
                    "statementText": statement_text,
                    "displayOrder": display_order,
                    "reviewStatus": review_status,
                }
            )
        return normalized_rows

    normalized_section_payloads = {
        code: _normalize_statement_rows(rows)
        for code, rows in section_payloads.items()
    }
    normalized_control_statements = []
    for index, row in enumerate(control_statements, start=1):
        control_code = str(row.get("controlCode") or "").strip()
        statement_text = str(row.get("statementText") or "").strip()
        if not control_code or not statement_text:
            continue
        normalized_control_statements.append(
            {
                "controlCode": _slug_code(control_code),
                "statementText": statement_text,
                "displayOrder": int(row.get("displayOrder") or (index * 10)),
                "reviewStatus": str(row.get("reviewStatus") or "Pending").strip() or "Pending",
                "groupLabel": str(row.get("groupLabel") or "").strip(),
                "groupDisplayOrder": int(row.get("groupDisplayOrder") or 0),
                "controlDisplayOrder": int(row.get("controlDisplayOrder") or 0),
            }
        )

    with get_connection(settings) as conn:
        cursor = conn.cursor()
        has_policy_control_group_label = _column_exists(cursor, "dbo.PolicyControlStatements", "GroupLabel")
        has_policy_control_ordering = _column_exists(cursor, "dbo.PolicyControlStatements", "GroupDisplayOrder")

        cursor.execute(
            """
            SELECT DomainId, DomainCode, DisplayName
            FROM dbo.Domains
            WHERE DomainCode = ? AND Status = 'Active'
            """,
            [normalized_root_domain_code],
        )
        domain_row = cursor.fetchone()
        if domain_row is None:
            raise ValueError(f"Active root domain not found for code '{root_domain_code}'.")
        root_domain_id = domain_row.DomainId
        root_domain_name = domain_row.DisplayName
        normalized_version_text = _next_version_text(cursor, root_domain_id, normalized_policy_title)

        policy_template_id = None
        if template_code:
            cursor.execute(
                """
                SELECT PolicyTemplateId
                FROM dbo.PolicyTemplates
                WHERE TemplateCode = ?
                """,
                [template_code],
            )
            template_row = cursor.fetchone()
            if template_row is None:
                cursor.execute(
                    """
                    INSERT INTO dbo.PolicyTemplates (
                        TemplateCode,
                        TemplateName,
                        VersionText,
                        TemplateBody,
                        SourcePath,
                        Status
                    )
                    OUTPUT inserted.PolicyTemplateId
                    VALUES (?, ?, ?, ?, ?, 'Draft')
                    """,
                    [
                        template_code,
                        template_name,
                        normalized_version_text,
                        template_body,
                        normalized_template_path,
                    ],
                )
                policy_template_id = cursor.fetchone().PolicyTemplateId
            else:
                policy_template_id = template_row.PolicyTemplateId
                cursor.execute(
                    """
                    UPDATE dbo.PolicyTemplates
                    SET
                        TemplateName = ?,
                        VersionText = ?,
                        TemplateBody = COALESCE(?, TemplateBody),
                        SourcePath = ?,
                        UpdatedAtUtc = SYSUTCDATETIME()
                    WHERE PolicyTemplateId = ?
                    """,
                    [
                        template_name,
                        normalized_version_text,
                        template_body,
                        normalized_template_path,
                        policy_template_id,
                    ],
                )

        cursor.execute(
            """
            INSERT INTO dbo.Policies (
                RootDomainId,
                PolicyTemplateId,
                PolicyCode,
                PolicyTitle,
                VersionText,
                Status,
                TemplatePath,
                SourceModelName
            )
            OUTPUT inserted.PolicyId
            VALUES (?, ?, ?, ?, ?, ?, ?, ?)
            """,
            [
                root_domain_id,
                policy_template_id,
                normalized_policy_code,
                normalized_policy_title,
                normalized_version_text,
                normalized_status,
                normalized_template_path,
                source_model_name,
            ],
        )
        policy_id = cursor.fetchone().PolicyId

        section_ids: dict[str, object] = {}
        for section_code, section_name, display_order in section_definitions:
            cursor.execute(
                """
                SELECT PolicySectionId
                FROM dbo.PolicySections
                WHERE PolicyId = ? AND SectionCode = ?
                """,
                [policy_id, section_code],
            )
            section_row = cursor.fetchone()
            if section_row is None:
                cursor.execute(
                    """
                    INSERT INTO dbo.PolicySections (
                        PolicyId,
                        SectionCode,
                        SectionName,
                        DisplayOrder,
                        Status
                    )
                    OUTPUT inserted.PolicySectionId
                    VALUES (?, ?, ?, ?, ?)
                    """,
                    [policy_id, section_code, section_name, display_order, normalized_status],
                )
                section_ids[section_code] = cursor.fetchone().PolicySectionId
            else:
                section_ids[section_code] = section_row.PolicySectionId
                cursor.execute(
                    """
                    UPDATE dbo.PolicySections
                    SET
                        SectionName = ?,
                        DisplayOrder = ?,
                        Status = ?,
                        UpdatedAtUtc = SYSUTCDATETIME()
                    WHERE PolicySectionId = ?
                    """,
                    [section_name, display_order, normalized_status, section_ids[section_code]],
                )

        cursor.execute(
            """
            DELETE FROM dbo.PolicyPrincipleLinks
            WHERE PolicyPrincipleId IN (
                SELECT PolicyPrincipleId
                FROM dbo.PolicyPrinciples
                WHERE PolicySectionId = ?
            )
            """,
            [section_ids["PRINCIPLE"]],
        )
        cursor.execute("DELETE FROM dbo.PolicyObjectives WHERE PolicySectionId = ?", [section_ids["OBJECTIVE"]])
        cursor.execute("DELETE FROM dbo.PolicyPrinciples WHERE PolicySectionId = ?", [section_ids["PRINCIPLE"]])
        cursor.execute("DELETE FROM dbo.PolicyAccountabilityStatements WHERE PolicySectionId = ?", [section_ids["ACCOUNTABILITY"]])
        cursor.execute("DELETE FROM dbo.PolicyTransparencyStatements WHERE PolicySectionId = ?", [section_ids["TRANSPARENCY"]])
        cursor.execute("DELETE FROM dbo.PolicyStrategyStatements WHERE PolicySectionId = ?", [section_ids["STRATEGY"]])
        cursor.execute("DELETE FROM dbo.PolicyConsequences WHERE PolicySectionId = ?", [section_ids["CONSEQUENCE"]])
        cursor.execute("DELETE FROM dbo.PolicyControlStatements WHERE PolicyId = ?", [policy_id])

        section_insert_map = {
            "OBJECTIVE": "dbo.PolicyObjectives",
            "PRINCIPLE": "dbo.PolicyPrinciples",
            "ACCOUNTABILITY": "dbo.PolicyAccountabilityStatements",
            "TRANSPARENCY": "dbo.PolicyTransparencyStatements",
            "STRATEGY": "dbo.PolicyStrategyStatements",
            "CONSEQUENCE": "dbo.PolicyConsequences",
        }

        inserted_policy_principles: list[tuple[object, int, str]] = []
        for section_code, table_name in section_insert_map.items():
            for index, row in enumerate(normalized_section_payloads.get(section_code, []), start=1):
                if section_code == "PRINCIPLE":
                    cursor.execute(
                        f"""
                        INSERT INTO {table_name} (
                            PolicySectionId,
                            StatementText,
                            DisplayOrder,
                            ReviewStatus
                        )
                        OUTPUT inserted.PolicyPrincipleId
                        VALUES (?, ?, ?, ?)
                        """,
                        [
                            section_ids[section_code],
                            row["statementText"],
                            row["displayOrder"],
                            row["reviewStatus"],
                        ],
                    )
                    policy_principle_id = cursor.fetchone().PolicyPrincipleId
                    inserted_policy_principles.append((policy_principle_id, index, str(row["statementText"])))
                else:
                    cursor.execute(
                        f"""
                        INSERT INTO {table_name} (
                            PolicySectionId,
                            StatementText,
                            DisplayOrder,
                            ReviewStatus
                        )
                        VALUES (?, ?, ?, ?)
                        """,
                        [
                            section_ids[section_code],
                            row["statementText"],
                            row["displayOrder"],
                            row["reviewStatus"],
                        ],
                    )

        for policy_principle_id, index, statement_text in inserted_policy_principles:
            principle_code = f"{normalized_policy_code}-principle-{index:02d}"
            principle_name = f"{normalized_policy_title} Principle {index}"
            cursor.execute(
                """
                SELECT PrincipleId
                FROM dbo.Principles
                WHERE PrincipleCode = ?
                """,
                [principle_code],
            )
            principle_row = cursor.fetchone()
            if principle_row is None:
                cursor.execute(
                    """
                    INSERT INTO dbo.Principles (
                        OriginDomainId,
                        PrincipleCode,
                        Name,
                        StatementText,
                        VisibilityScope,
                        LifecycleStatus
                    )
                    OUTPUT inserted.PrincipleId
                    VALUES (?, ?, ?, ?, 'Private', 'Draft')
                    """,
                    [
                        root_domain_id,
                        principle_code,
                        principle_name,
                        statement_text,
                    ],
                )
                principle_id = cursor.fetchone().PrincipleId
            else:
                principle_id = principle_row.PrincipleId
                cursor.execute(
                    """
                    UPDATE dbo.Principles
                    SET
                        OriginDomainId = ?,
                        Name = ?,
                        StatementText = ?,
                        UpdatedAtUtc = SYSUTCDATETIME(),
                        UpdatedBy = SUSER_SNAME()
                    WHERE PrincipleId = ?
                    """,
                    [
                        root_domain_id,
                        principle_name,
                        statement_text,
                        principle_id,
                    ],
                )

            cursor.execute(
                """
                INSERT INTO dbo.PolicyPrincipleLinks (
                    PolicyPrincipleId,
                    PrincipleId,
                    UsageMode
                )
                VALUES (?, ?, 'Override')
                """,
                [policy_principle_id, principle_id],
            )

        if normalized_control_statements:
            control_codes = [row["controlCode"] for row in normalized_control_statements]
            placeholders = ", ".join("?" for _ in control_codes)
            cursor.execute(
                f"""
                SELECT ControlId, ControlCode
                FROM dbo.Controls
                WHERE ControlCode IN ({placeholders})
                """,
                control_codes,
            )
            control_lookup = {
                str(row.ControlCode): row.ControlId
                for row in cursor.fetchall()
            }
            missing_control_codes = [
                row["controlCode"]
                for row in normalized_control_statements
                if row["controlCode"] not in control_lookup
            ]
            if missing_control_codes:
                raise ValueError(f"Control codes not found for policy control statements: {', '.join(sorted(set(missing_control_codes)))}.")

            for row in normalized_control_statements:
                if has_policy_control_group_label and has_policy_control_ordering:
                    cursor.execute(
                        """
                        INSERT INTO dbo.PolicyControlStatements (
                            PolicySectionId,
                            PolicyId,
                            ControlId,
                            GroupLabel,
                            GroupDisplayOrder,
                            ControlDisplayOrder,
                            StatementText,
                            DisplayOrder,
                            ReviewStatus
                        )
                        VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
                        """,
                        [
                            section_ids["CONTROL_POLICY"],
                            policy_id,
                            control_lookup[row["controlCode"]],
                            row["groupLabel"] or None,
                            row["groupDisplayOrder"],
                            row["controlDisplayOrder"],
                            row["statementText"],
                            row["displayOrder"],
                            row["reviewStatus"],
                        ],
                    )
                elif has_policy_control_group_label:
                    cursor.execute(
                        """
                        INSERT INTO dbo.PolicyControlStatements (
                            PolicySectionId,
                            PolicyId,
                            ControlId,
                            GroupLabel,
                            StatementText,
                            DisplayOrder,
                            ReviewStatus
                        )
                        VALUES (?, ?, ?, ?, ?, ?, ?)
                        """,
                        [
                            section_ids["CONTROL_POLICY"],
                            policy_id,
                            control_lookup[row["controlCode"]],
                            row["groupLabel"] or None,
                            row["statementText"],
                            row["displayOrder"],
                            row["reviewStatus"],
                        ],
                    )
                else:
                    cursor.execute(
                        """
                        INSERT INTO dbo.PolicyControlStatements (
                            PolicySectionId,
                            PolicyId,
                            ControlId,
                            StatementText,
                            DisplayOrder,
                            ReviewStatus
                        )
                        VALUES (?, ?, ?, ?, ?, ?)
                        """,
                        [
                            section_ids["CONTROL_POLICY"],
                            policy_id,
                            control_lookup[row["controlCode"]],
                            row["statementText"],
                            row["displayOrder"],
                            row["reviewStatus"],
                        ],
                    )

        conn.commit()

    return {
        "PolicyId": str(policy_id),
        "PolicyCode": normalized_policy_code,
        "PolicyTitle": normalized_policy_title,
        "VersionText": normalized_version_text,
        "Status": normalized_status,
        "RootDomainCode": normalized_root_domain_code,
        "RootDomainName": root_domain_name,
        "ModelName": source_model_name or "",
    }


def _normalize_row(row: dict[str, object]) -> dict[str, object]:
    return normalize_database_record(row)


def _ensure_policy_control_explanations_table(cursor) -> None:
    cursor.execute(
        """
        IF OBJECT_ID('dbo.PolicyControlExplanations', 'U') IS NULL
        BEGIN
            CREATE TABLE dbo.PolicyControlExplanations (
                PolicyControlExplanationId INT IDENTITY(1, 1) NOT NULL,
                PolicyId INT NOT NULL,
                ControlId INT NOT NULL,
                ExplanationText NVARCHAR(MAX) NOT NULL,
                SourceModelName NVARCHAR(200) NULL,
                CreatedAtUtc DATETIME2(0) NOT NULL
                    CONSTRAINT DF_PolicyControlExplanations_CreatedAt DEFAULT (SYSUTCDATETIME()),
                UpdatedAtUtc DATETIME2(0) NOT NULL
                    CONSTRAINT DF_PolicyControlExplanations_UpdatedAt DEFAULT (SYSUTCDATETIME()),
                CONSTRAINT PK_PolicyControlExplanations PRIMARY KEY (PolicyControlExplanationId),
                CONSTRAINT UQ_PolicyControlExplanations_Policy_Control UNIQUE (PolicyId, ControlId),
                CONSTRAINT FK_PolicyControlExplanations_Policies
                    FOREIGN KEY (PolicyId) REFERENCES dbo.Policies(PolicyId),
                CONSTRAINT FK_PolicyControlExplanations_Controls
                    FOREIGN KEY (ControlId) REFERENCES dbo.Controls(ControlId)
            );

            CREATE INDEX IX_PolicyControlExplanations_PolicyId
                ON dbo.PolicyControlExplanations(PolicyId);
        END;
        """
    )


def _row_to_dict(cursor, row: object) -> dict[str, object]:
    columns = [column[0] for column in cursor.description]
    return dict(zip(columns, row, strict=False))


def _slug_code(value: str) -> str:
    code = re.sub(r"[^a-z0-9]+", "-", value.strip().lower()).strip("-")
    if not code:
        raise ValueError("Code must contain at least one letter or digit.")
    return code[:100]


def _table_exists(cursor, full_table_name: str) -> bool:
    cursor.execute("SELECT OBJECT_ID(?, 'U')", [full_table_name])
    return cursor.fetchone()[0] is not None


def _column_exists(cursor, full_table_name: str, column_name: str) -> bool:
    cursor.execute("SELECT COL_LENGTH(?, ?)", [full_table_name, column_name])
    return cursor.fetchone()[0] is not None


def _chunk_text(text: str, chunk_size: int = 1200, overlap: int = 150) -> list[str]:
    if len(text) <= chunk_size:
        return [text]

    chunks: list[str] = []
    start = 0
    text_length = len(text)
    while start < text_length:
        end = min(start + chunk_size, text_length)
        chunk = text[start:end].strip()
        if chunk:
            chunks.append(chunk)
        if end >= text_length:
            break
        start = max(end - overlap, start + 1)
    return chunks


def _estimate_token_count(text: str) -> int:
    return max(1, len(text.split()))
