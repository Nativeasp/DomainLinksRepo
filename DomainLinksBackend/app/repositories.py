from __future__ import annotations

import re

from .config import Settings
from .db import fetch_all, get_connection


def list_domains(settings: Settings) -> list[dict[str, object]]:
    rows = fetch_all(
        settings,
        """
        SELECT
            DomainId,
            DomainCode,
            DomainType,
            DisplayName,
            Description,
            Status
        FROM dbo.Domains
        WHERE Status = 'Active'
        ORDER BY
            CASE DomainType
                WHEN 'ProjectMemory' THEN 0
                WHEN 'Knowledge' THEN 1
                ELSE 2
            END,
            DisplayName
        """,
    )
    return [_normalize_row(row) for row in rows]


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
            d.DomainCode,
            d.DomainType,
            d.DisplayName AS DomainDisplayName
        FROM dbo.Collections c
        JOIN dbo.Domains d
            ON d.DomainId = c.DomainId
        {where_clause}
        ORDER BY d.DisplayName, c.DisplayName
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
    domain_type: str,
    display_name: str,
    description: str | None = None,
) -> dict[str, object]:
    domain_code = _slug_code(domain_code)
    with get_connection(settings) as conn:
        cursor = conn.cursor()
        cursor.execute(
            """
            INSERT INTO dbo.Domains (
                DomainCode,
                DomainType,
                DisplayName,
                Description
            )
            OUTPUT
                inserted.DomainId,
                inserted.DomainCode,
                inserted.DomainType,
                inserted.DisplayName,
                inserted.Description,
                inserted.Status
            VALUES (?, ?, ?, ?)
            """,
            [domain_code, domain_type, display_name.strip(), description],
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
            c.CollectionCode,
            COUNT(cu.ContentUnitId) AS ContentUnitCount
        FROM dbo.Documents d
        JOIN dbo.Collections c
            ON c.CollectionId = d.CollectionId
        LEFT JOIN dbo.ContentUnits cu
            ON cu.DocumentId = d.DocumentId AND cu.Status = 'Active'
        WHERE c.CollectionCode = ? AND d.Status = 'Active'
        GROUP BY
            d.DocumentId,
            d.SourceName,
            d.SourceType,
            d.Status,
            d.CreatedAtUtc,
            c.CollectionCode
        ORDER BY d.CreatedAtUtc DESC
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


def _normalize_row(row: dict[str, object]) -> dict[str, object]:
    normalized: dict[str, object] = {}
    for key, value in row.items():
        normalized[key] = str(value) if hasattr(value, "hex") else value
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
