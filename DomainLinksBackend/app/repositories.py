from __future__ import annotations

import re

from .config import Settings
from .db import fetch_all, fetch_one, get_connection


def list_domains(settings: Settings) -> list[dict[str, object]]:
    rows = fetch_all(
        settings,
        """
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
            dor.NAME AS DomainOrientation
        FROM dbo.Domains d
        LEFT JOIN dbo.DomainTypes dt
            ON dt.ID = d.DomainTypeId
        LEFT JOIN dbo.DomainOrientations dor
            ON dor.ID = d.DomainOrientationId
        WHERE d.Status = 'Active'
        ORDER BY
            CASE WHEN d.DomainCode = 'workspace-memory' THEN 0 ELSE 1 END,
            d.DisplayOrder,
            d.DisplayName
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
            DOMAIN_LEVEL,
            DISPLAY_ORDER
        FROM dbo.DomainTypes
        WHERE
            (EFFECTIVE_END_DATE IS NULL OR EFFECTIVE_END_DATE >= CAST(SYSDATETIME() AS date))
        ORDER BY DISPLAY_ORDER, NAME
        """,
    )
    return [_normalize_row(row) for row in rows]


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
    return [_normalize_row(row) for row in rows]


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
            d.DisplayName,
            c.DisplayName
        """,
        [root_domain_code, *domain_codes, root_domain_code],
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

    return {
        "domain": domain,
        "parentPath": " / ".join(path_parts),
        "childDomains": [_normalize_row(row) for row in child_domains],
        "collections": [_normalize_row(row) for row in collections],
        "documents": [_normalize_row(row) for row in documents],
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
                UpdatedAtUtc = SYSUTCDATETIME()
            WHERE DomainCode = ? AND Status = 'Active'
            """,
            [display_name.strip(), description, domain_type_id, domain_orientation_id, _slug_code(domain_code)],
        )
        if cursor.rowcount == 0:
            raise ValueError(f"Active domain not found for code '{domain_code}'.")
        conn.commit()

    rows = list_domains(settings)
    for row in rows:
        if row.get("DomainCode") == _slug_code(domain_code):
            return row
    raise ValueError(f"Domain '{domain_code}' was updated but could not be reloaded.")


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


def _normalize_row(row: dict[str, object]) -> dict[str, object]:
    normalized: dict[str, object] = {}
    for key, value in row.items():
        normalized[key] = str(value) if hasattr(value, "hex") and not isinstance(value, (bytes, bytearray, memoryview)) else value
    return normalized


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
