from __future__ import annotations

from collections.abc import Mapping, Sequence

from .config import Settings

try:
    import pyodbc
except ModuleNotFoundError as exc:  # pragma: no cover - exercised via runtime startup paths.
    pyodbc = None
    PYODBC_IMPORT_ERROR = exc
else:
    PYODBC_IMPORT_ERROR = None


def build_connection_string(settings: Settings) -> str:
    parts = [
        f"DRIVER={{{settings.sql_driver}}}",
        f"SERVER={settings.sql_server}",
        f"DATABASE={settings.sql_database}",
        f"Encrypt={'yes' if settings.sql_encrypt else 'no'}",
        f"TrustServerCertificate={'yes' if settings.sql_trust_server_certificate else 'no'}",
    ]
    if settings.sql_trusted_connection:
        parts.append("Trusted_Connection=yes")
    return ";".join(parts) + ";"


def get_connection(settings: Settings) -> pyodbc.Connection:
    if pyodbc is None:
        raise RuntimeError("pyodbc is not installed") from PYODBC_IMPORT_ERROR
    connection_string = build_connection_string(settings)
    return pyodbc.connect(connection_string, timeout=5)


def fetch_all(
    settings: Settings,
    query: str,
    params: Sequence[object] | None = None,
) -> list[dict[str, object]]:
    with get_connection(settings) as conn:
        cursor = conn.cursor()
        cursor.execute(query, params or [])
        columns = [column[0] for column in cursor.description]
        rows = cursor.fetchall()
        return [dict(zip(columns, row, strict=False)) for row in rows]


def ping_database(settings: Settings) -> Mapping[str, object]:
    with get_connection(settings) as conn:
        cursor = conn.cursor()
        cursor.execute(
            """
            SELECT
                CAST(SERVERPROPERTY('ServerName') AS nvarchar(128)) AS ServerName,
                DB_NAME() AS DatabaseName,
                CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(128)) AS ProductVersion
            """
        )
        row = cursor.fetchone()
        return {
            "reachable": True,
            "server_name": row.ServerName,
            "database_name": row.DatabaseName,
            "product_version": row.ProductVersion,
        }
