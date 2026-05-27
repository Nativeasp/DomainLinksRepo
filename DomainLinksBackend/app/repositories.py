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
            d.DisplayOrder,
            d.DomainCode,
            d.DisplayName,
            d.Description,
            d.Status,
            dt.NAME AS DomainType
        FROM dbo.Domains d
        LEFT JOIN dbo.DomainTypes dt
            ON dt.ID = d.DomainTypeId
        WHERE d.Status = 'Active'
        ORDER BY
            CASE WHEN d.DomainCode = 'projects' THEN 0 ELSE 1 END,
            d.DisplayOrder,
            d.DisplayName
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


def create_domain(
    settings: Settings,
    domain_code: str,
    domain_type_id: int | None,
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
                DisplayOrder,
                DomainCode,
                DisplayName,
                Description
            )
            OUTPUT
                inserted.DomainId,
                inserted.DomainParentId,
                inserted.DomainTypeId,
                inserted.DisplayOrder,
                inserted.DomainCode,
                inserted.DisplayName,
                inserted.Description,
                inserted.Status
            VALUES (
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
                UpdatedAtUtc = SYSUTCDATETIME()
            WHERE DomainCode = ? AND Status = 'Active'
            """,
            [display_name.strip(), description, domain_type_id, _slug_code(domain_code)],
        )
        if cursor.rowcount == 0:
            raise ValueError(f"Active domain not found for code '{domain_code}'.")
        conn.commit()

    rows = list_domains(settings)
    for row in rows:
        if row.get("DomainCode") == _slug_code(domain_code):
            return row
    raise ValueError(f"Domain '{domain_code}' was updated but could not be reloaded.")


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
