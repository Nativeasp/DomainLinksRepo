"""Convert DomainLinks GUID primary/foreign keys to INT IDENTITY keys.

This is an operational, one-time migration utility. It builds replacement tables,
maps every relationship, validates the result, and swaps the tables atomically.

Run it against a restored validation database first. The production database name
is refused unless ``--allow-production`` is supplied explicitly.
"""

from __future__ import annotations

import argparse
import csv
import sys
from collections import defaultdict
from dataclasses import dataclass
from datetime import UTC, datetime
from pathlib import Path
from typing import Any, Iterable

import pyodbc


MIGRATION_ID = "035_integer_identity_keys"
DEFAULT_SERVER = "RICHARDBASQB378"
DEFAULT_BACKUP_DIRECTORY = Path(r"C:\SQLDatabases\Backups")
PRODUCTION_DATABASE = "DomainLinks"
SHADOW_PREFIX = "__int_"
LEGACY_PREFIX = "__Legacy_"


@dataclass(frozen=True)
class Column:
    name: str
    type_name: str
    ordinal: int
    nullable: bool
    identity: bool
    computed: bool
    generated_always_type: int
    sparse: bool
    filestream: bool
    hidden: bool


@dataclass(frozen=True)
class Table:
    schema: str
    name: str
    object_id: int
    primary_key_column: str
    columns: tuple[Column, ...]

    @property
    def qualified(self) -> str:
        return qualified(self.schema, self.name)

    @property
    def shadow_name(self) -> str:
        return f"{SHADOW_PREFIX}{self.name}"

    @property
    def shadow_qualified(self) -> str:
        return qualified(self.schema, self.shadow_name)

    @property
    def guid_columns(self) -> tuple[Column, ...]:
        return tuple(column for column in self.columns if column.type_name == "uniqueidentifier")


def quote(name: str) -> str:
    return f"[{name.replace(']', ']]')}]"


def qualified(schema: str, name: str) -> str:
    return f"{quote(schema)}.{quote(name)}"


def legacy(column: str) -> str:
    return f"{LEGACY_PREFIX}{column}"


def fetch_dicts(cursor: pyodbc.Cursor, sql: str, params: Iterable[Any] = ()) -> list[dict[str, Any]]:
    cursor.execute(sql, tuple(params))
    names = [description[0] for description in cursor.description]
    return [dict(zip(names, row, strict=True)) for row in cursor.fetchall()]


def scalar(cursor: pyodbc.Cursor, sql: str, params: Iterable[Any] = ()) -> Any:
    cursor.execute(sql, tuple(params))
    row = cursor.fetchone()
    return None if row is None else row[0]


def load_tables(cursor: pyodbc.Cursor) -> dict[str, Table]:
    pk_rows = fetch_dicts(
        cursor,
        """
        SELECT
            s.name AS SchemaName,
            t.name AS TableName,
            t.object_id AS ObjectId,
            c.name AS ColumnName,
            ic.key_ordinal AS KeyOrdinal
        FROM sys.tables AS t
        JOIN sys.schemas AS s ON s.schema_id = t.schema_id
        JOIN sys.key_constraints AS kc
          ON kc.parent_object_id = t.object_id AND kc.type = 'PK'
        JOIN sys.index_columns AS ic
          ON ic.object_id = t.object_id AND ic.index_id = kc.unique_index_id
        JOIN sys.columns AS c
          ON c.object_id = t.object_id AND c.column_id = ic.column_id
        JOIN sys.types AS ty ON ty.user_type_id = c.user_type_id
        WHERE t.is_ms_shipped = 0 AND ty.name = 'uniqueidentifier'
        ORDER BY s.name, t.name, ic.key_ordinal;
        """,
    )
    grouped_pks: dict[tuple[str, str, int], list[str]] = defaultdict(list)
    for row in pk_rows:
        grouped_pks[(row["SchemaName"], row["TableName"], row["ObjectId"])].append(
            row["ColumnName"]
        )

    for key, columns in grouped_pks.items():
        if len(columns) != 1:
            raise RuntimeError(f"{key[0]}.{key[1]} does not have a single-column GUID PK: {columns}")

    object_ids = {key[2] for key in grouped_pks}
    if not object_ids:
        return {}

    column_rows = fetch_dicts(
        cursor,
        """
        SELECT
            c.object_id AS ObjectId,
            c.name AS ColumnName,
            ty.name AS TypeName,
            c.column_id AS Ordinal,
            c.is_nullable AS IsNullable,
            c.is_identity AS IsIdentity,
            c.is_computed AS IsComputed,
            c.generated_always_type AS GeneratedAlwaysType,
            c.is_sparse AS IsSparse,
            c.is_filestream AS IsFileStream,
            c.is_hidden AS IsHidden
        FROM sys.columns AS c
        JOIN sys.types AS ty ON ty.user_type_id = c.user_type_id
        JOIN sys.tables AS t ON t.object_id = c.object_id
        WHERE t.is_ms_shipped = 0
        ORDER BY c.object_id, c.column_id;
        """,
    )
    columns_by_object: dict[int, list[Column]] = defaultdict(list)
    for row in column_rows:
        if row["ObjectId"] not in object_ids:
            continue
        columns_by_object[row["ObjectId"]].append(
            Column(
                name=row["ColumnName"],
                type_name=row["TypeName"].lower(),
                ordinal=int(row["Ordinal"]),
                nullable=bool(row["IsNullable"]),
                identity=bool(row["IsIdentity"]),
                computed=bool(row["IsComputed"]),
                generated_always_type=int(row["GeneratedAlwaysType"]),
                sparse=bool(row["IsSparse"]),
                filestream=bool(row["IsFileStream"]),
                hidden=bool(row["IsHidden"]),
            )
        )

    tables: dict[str, Table] = {}
    for (schema, name, object_id), pk_columns in grouped_pks.items():
        if schema != "dbo":
            raise RuntimeError(f"Only dbo tables are supported; found {schema}.{name}")
        table_columns = tuple(columns_by_object[object_id])
        unsupported = [
            column.name
            for column in table_columns
            if column.computed
            or column.generated_always_type
            or column.sparse
            or column.filestream
            or column.hidden
            or (column.identity and column.name != pk_columns[0])
        ]
        if unsupported:
            raise RuntimeError(f"Unsupported special columns in {schema}.{name}: {unsupported}")
        collisions = [column.name for column in table_columns if column.name.startswith(LEGACY_PREFIX)]
        if collisions:
            raise RuntimeError(f"Reserved legacy column prefix already used in {schema}.{name}: {collisions}")
        tables[name] = Table(
            schema=schema,
            name=name,
            object_id=object_id,
            primary_key_column=pk_columns[0],
            columns=table_columns,
        )
    return dict(sorted(tables.items()))


def load_foreign_keys(cursor: pyodbc.Cursor) -> list[dict[str, Any]]:
    rows = fetch_dicts(
        cursor,
        """
        SELECT
            fk.object_id AS ForeignKeyId,
            fk.name AS ForeignKeyName,
            ps.name AS ParentSchema,
            pt.name AS ParentTable,
            rs.name AS ReferencedSchema,
            rt.name AS ReferencedTable,
            fkc.constraint_column_id AS ColumnOrdinal,
            pc.name AS ParentColumn,
            rc.name AS ReferencedColumn,
            fk.delete_referential_action_desc AS DeleteAction,
            fk.update_referential_action_desc AS UpdateAction,
            fk.is_not_for_replication AS NotForReplication,
            fk.is_disabled AS IsDisabled,
            fk.is_not_trusted AS IsNotTrusted
        FROM sys.foreign_keys AS fk
        JOIN sys.tables AS pt ON pt.object_id = fk.parent_object_id
        JOIN sys.schemas AS ps ON ps.schema_id = pt.schema_id
        JOIN sys.tables AS rt ON rt.object_id = fk.referenced_object_id
        JOIN sys.schemas AS rs ON rs.schema_id = rt.schema_id
        JOIN sys.foreign_key_columns AS fkc ON fkc.constraint_object_id = fk.object_id
        JOIN sys.columns AS pc
          ON pc.object_id = fk.parent_object_id AND pc.column_id = fkc.parent_column_id
        JOIN sys.columns AS rc
          ON rc.object_id = fk.referenced_object_id AND rc.column_id = fkc.referenced_column_id
        ORDER BY fk.object_id, fkc.constraint_column_id;
        """,
    )
    grouped: dict[int, dict[str, Any]] = {}
    for row in rows:
        fk_id = int(row["ForeignKeyId"])
        if fk_id not in grouped:
            grouped[fk_id] = {
                "id": fk_id,
                "name": row["ForeignKeyName"],
                "parent_schema": row["ParentSchema"],
                "parent_table": row["ParentTable"],
                "referenced_schema": row["ReferencedSchema"],
                "referenced_table": row["ReferencedTable"],
                "delete_action": row["DeleteAction"],
                "update_action": row["UpdateAction"],
                "not_for_replication": bool(row["NotForReplication"]),
                "disabled": bool(row["IsDisabled"]),
                "not_trusted": bool(row["IsNotTrusted"]),
                "columns": [],
            }
        grouped[fk_id]["columns"].append((row["ParentColumn"], row["ReferencedColumn"]))
    return list(grouped.values())


def load_key_constraints(cursor: pyodbc.Cursor, table_names: set[str]) -> list[dict[str, Any]]:
    rows = fetch_dicts(
        cursor,
        """
        SELECT
            kc.object_id AS ConstraintId,
            kc.name AS ConstraintName,
            kc.type AS ConstraintType,
            s.name AS SchemaName,
            t.name AS TableName,
            i.type_desc AS IndexType,
            ic.key_ordinal AS KeyOrdinal,
            c.name AS ColumnName,
            ic.is_descending_key AS IsDescending
        FROM sys.key_constraints AS kc
        JOIN sys.tables AS t ON t.object_id = kc.parent_object_id
        JOIN sys.schemas AS s ON s.schema_id = t.schema_id
        JOIN sys.indexes AS i
          ON i.object_id = kc.parent_object_id AND i.index_id = kc.unique_index_id
        JOIN sys.index_columns AS ic
          ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0
        JOIN sys.columns AS c
          ON c.object_id = ic.object_id AND c.column_id = ic.column_id
        ORDER BY kc.object_id, ic.key_ordinal;
        """,
    )
    grouped: dict[int, dict[str, Any]] = {}
    for row in rows:
        if row["TableName"] not in table_names or row["SchemaName"] != "dbo":
            continue
        constraint_id = int(row["ConstraintId"])
        grouped.setdefault(
            constraint_id,
            {
                "name": row["ConstraintName"],
                "type": row["ConstraintType"],
                "schema": row["SchemaName"],
                "table": row["TableName"],
                "index_type": row["IndexType"],
                "columns": [],
            },
        )["columns"].append((row["ColumnName"], bool(row["IsDescending"])))
    return list(grouped.values())


def load_indexes(cursor: pyodbc.Cursor, table_names: set[str]) -> list[dict[str, Any]]:
    rows = fetch_dicts(
        cursor,
        """
        SELECT
            i.object_id AS ObjectId,
            i.index_id AS IndexId,
            s.name AS SchemaName,
            t.name AS TableName,
            i.name AS IndexName,
            i.type_desc AS IndexType,
            i.is_unique AS IsUnique,
            i.has_filter AS HasFilter,
            i.filter_definition AS FilterDefinition,
            i.is_disabled AS IsDisabled,
            ic.index_column_id AS IndexColumnOrdinal,
            ic.key_ordinal AS KeyOrdinal,
            ic.is_included_column AS IsIncluded,
            ic.is_descending_key AS IsDescending,
            c.name AS ColumnName
        FROM sys.indexes AS i
        JOIN sys.tables AS t ON t.object_id = i.object_id
        JOIN sys.schemas AS s ON s.schema_id = t.schema_id
        JOIN sys.index_columns AS ic
          ON ic.object_id = i.object_id AND ic.index_id = i.index_id
        JOIN sys.columns AS c
          ON c.object_id = ic.object_id AND c.column_id = ic.column_id
        WHERE i.index_id > 0
          AND i.is_hypothetical = 0
          AND i.is_primary_key = 0
          AND i.is_unique_constraint = 0
        ORDER BY i.object_id, i.index_id, ic.index_column_id;
        """,
    )
    grouped: dict[tuple[int, int], dict[str, Any]] = {}
    for row in rows:
        if row["TableName"] not in table_names or row["SchemaName"] != "dbo":
            continue
        key = (int(row["ObjectId"]), int(row["IndexId"]))
        index = grouped.setdefault(
            key,
            {
                "schema": row["SchemaName"],
                "table": row["TableName"],
                "name": row["IndexName"],
                "type": row["IndexType"],
                "unique": bool(row["IsUnique"]),
                "filter": row["FilterDefinition"] if row["HasFilter"] else None,
                "disabled": bool(row["IsDisabled"]),
                "keys": [],
                "includes": [],
            },
        )
        target = "includes" if row["IsIncluded"] else "keys"
        index[target].append((row["ColumnName"], bool(row["IsDescending"])))
    for index in grouped.values():
        if index["type"] not in {"CLUSTERED", "NONCLUSTERED"}:
            raise RuntimeError(
                f"Unsupported index type {index['type']} on {index['table']}.{index['name']}"
            )
    return list(grouped.values())


def load_simple_metadata(
    cursor: pyodbc.Cursor, table_names: set[str]
) -> tuple[list[dict[str, Any]], list[dict[str, Any]], list[dict[str, Any]]]:
    defaults = [
        row
        for row in fetch_dicts(
            cursor,
            """
            SELECT s.name AS SchemaName, t.name AS TableName, c.name AS ColumnName,
                   dc.name AS ConstraintName, dc.definition AS Definition,
                   ty.name AS TypeName
            FROM sys.default_constraints AS dc
            JOIN sys.tables AS t ON t.object_id = dc.parent_object_id
            JOIN sys.schemas AS s ON s.schema_id = t.schema_id
            JOIN sys.columns AS c
              ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
            JOIN sys.types AS ty ON ty.user_type_id = c.user_type_id;
            """,
        )
        if row["SchemaName"] == "dbo"
        and row["TableName"] in table_names
        and row["TypeName"].lower() != "uniqueidentifier"
    ]
    checks = [
        row
        for row in fetch_dicts(
            cursor,
            """
            SELECT s.name AS SchemaName, t.name AS TableName, cc.name AS ConstraintName,
                   cc.definition AS Definition, cc.is_disabled AS IsDisabled,
                   cc.is_not_trusted AS IsNotTrusted
            FROM sys.check_constraints AS cc
            JOIN sys.tables AS t ON t.object_id = cc.parent_object_id
            JOIN sys.schemas AS s ON s.schema_id = t.schema_id;
            """,
        )
        if row["SchemaName"] == "dbo" and row["TableName"] in table_names
    ]
    triggers = [
        row
        for row in fetch_dicts(
            cursor,
            """
            SELECT s.name AS SchemaName, t.name AS TableName, tr.name AS TriggerName,
                   OBJECT_DEFINITION(tr.object_id) AS Definition, tr.is_disabled AS IsDisabled
            FROM sys.triggers AS tr
            JOIN sys.tables AS t ON t.object_id = tr.parent_id
            JOIN sys.schemas AS s ON s.schema_id = t.schema_id
            WHERE tr.is_ms_shipped = 0;
            """,
        )
        if row["SchemaName"] == "dbo" and row["TableName"] in table_names
    ]
    if any(not row["Definition"] for row in triggers):
        raise RuntimeError("An encrypted or unreadable trigger depends on a converted table")
    return defaults, checks, triggers


def preflight(cursor: pyodbc.Cursor, tables: dict[str, Table]) -> None:
    table_names = set(tables)
    shadows = [
        row[0]
        for row in cursor.execute(
            """
            SELECT t.name
            FROM sys.tables AS t
            JOIN sys.schemas AS s ON s.schema_id = t.schema_id
            WHERE s.name = 'dbo' AND t.name LIKE '__int[_]%';
            """
        ).fetchall()
    ]
    if shadows:
        raise RuntimeError(f"Shadow tables already exist: {', '.join(shadows)}")

    special_tables = fetch_dicts(
        cursor,
        """
        SELECT t.name AS TableName, t.temporal_type AS TemporalType,
               t.is_memory_optimized AS IsMemoryOptimized,
               t.is_filetable AS IsFileTable,
               t.is_tracked_by_cdc AS IsTrackedByCdc
        FROM sys.tables AS t
        JOIN sys.schemas AS s ON s.schema_id = t.schema_id
        WHERE s.name = 'dbo' AND t.is_ms_shipped = 0;
        """,
    )
    unsupported = [
        row["TableName"]
        for row in special_tables
        if row["TableName"] in table_names
        and (
            row["TemporalType"]
            or row["IsMemoryOptimized"]
            or row["IsFileTable"]
            or row["IsTrackedByCdc"]
        )
    ]
    if unsupported:
        raise RuntimeError(f"Unsupported table features are enabled on: {unsupported}")

    explicit_permissions = int(
        scalar(
            cursor,
            """
            SELECT COUNT(*)
            FROM sys.database_permissions AS dp
            JOIN sys.tables AS t ON t.object_id = dp.major_id
            JOIN sys.schemas AS s ON s.schema_id = t.schema_id
            WHERE dp.class = 1 AND s.name = 'dbo'
              AND t.name IN ({})
            """.format(",".join("?" for _ in table_names)),
            sorted(table_names),
        )
    )
    if explicit_permissions:
        raise RuntimeError("Explicit table/column permissions exist and require preservation support")

    extended_properties = int(
        scalar(
            cursor,
            """
            SELECT COUNT(*)
            FROM sys.extended_properties AS ep
            JOIN sys.tables AS t ON t.object_id = ep.major_id
            JOIN sys.schemas AS s ON s.schema_id = t.schema_id
            WHERE ep.class = 1 AND s.name = 'dbo'
              AND t.name IN ({})
            """.format(",".join("?" for _ in table_names)),
            sorted(table_names),
        )
    )
    if extended_properties:
        raise RuntimeError("Extended properties exist and require preservation support")

    dependencies = fetch_dicts(
        cursor,
        """
        SELECT DISTINCT
            OBJECT_SCHEMA_NAME(d.referencing_id) AS ReferencingSchema,
            OBJECT_NAME(d.referencing_id) AS ReferencingObject,
            o.type AS ObjectType
        FROM sys.sql_expression_dependencies AS d
        JOIN sys.objects AS o ON o.object_id = d.referencing_id
        JOIN sys.tables AS t ON t.object_id = d.referenced_id
        JOIN sys.schemas AS s ON s.schema_id = t.schema_id
        WHERE s.name = 'dbo' AND t.name IN ({})
          AND o.type NOT IN ('TR', 'C', 'U');
        """.format(",".join("?" for _ in table_names)),
        sorted(table_names),
    )
    if dependencies:
        formatted = [
            f"{row['ReferencingSchema']}.{row['ReferencingObject']} ({row['ObjectType']})"
            for row in dependencies
        ]
        raise RuntimeError(f"Unsupported SQL dependencies reference converted tables: {formatted}")


def create_shadow_tables(cursor: pyodbc.Cursor, tables: dict[str, Table]) -> None:
    for table in tables.values():
        selections: list[str] = []
        for column in table.columns:
            if column.name == table.primary_key_column:
                selections.append(f"IDENTITY(int, 1, 1) AS {quote(column.name)}")
            elif column.type_name == "uniqueidentifier":
                selections.append(f"CAST(NULL AS int) AS {quote(column.name)}")
            else:
                selections.append(f"src.{quote(column.name)} AS {quote(column.name)}")
        selections.extend(
            f"src.{quote(column.name)} AS {quote(legacy(column.name))}"
            for column in table.guid_columns
        )
        sql = f"""
        SELECT TOP (100) PERCENT
            {', '.join(selections)}
        INTO {table.shadow_qualified}
        FROM {table.qualified} AS src
        ORDER BY src.{quote(table.primary_key_column)};
        """
        cursor.execute(sql)


def export_and_remove_orphan_explanations(
    cursor: pyodbc.Cursor, mapping_path: Path
) -> tuple[int, Path | None]:
    cursor.execute(
        """
        SELECT explanation.*
        FROM dbo.PolicyControlExplanations AS explanation
        LEFT JOIN dbo.Policies AS policy ON policy.PolicyId = explanation.PolicyId
        LEFT JOIN dbo.Controls AS control ON control.ControlId = explanation.ControlId
        WHERE policy.PolicyId IS NULL OR control.ControlId IS NULL
        ORDER BY explanation.CreatedAtUtc, explanation.PolicyControlExplanationId;
        """
    )
    columns = [description[0] for description in cursor.description]
    rows = cursor.fetchall()
    if not rows:
        return 0, None

    output_path = mapping_path.with_name(
        f"{mapping_path.stem}_orphan_policy_control_explanations.csv"
    )
    output_path.parent.mkdir(parents=True, exist_ok=True)
    with output_path.open("w", newline="", encoding="utf-8") as stream:
        writer = csv.writer(stream)
        writer.writerow(columns)
        writer.writerows(rows)

    cursor.execute(
        """
        DELETE explanation
        FROM dbo.PolicyControlExplanations AS explanation
        LEFT JOIN dbo.Policies AS policy ON policy.PolicyId = explanation.PolicyId
        LEFT JOIN dbo.Controls AS control ON control.ControlId = explanation.ControlId
        WHERE policy.PolicyId IS NULL OR control.ControlId IS NULL;
        """
    )
    return len(rows), output_path


def build_fk_mapping(
    tables: dict[str, Table], foreign_keys: list[dict[str, Any]]
) -> dict[tuple[str, str], tuple[str, str]]:
    candidates: dict[tuple[str, str], list[tuple[str, str]]] = defaultdict(list)
    for foreign_key in foreign_keys:
        child = foreign_key["parent_table"]
        parent = foreign_key["referenced_table"]
        if child not in tables or parent not in tables:
            continue
        child_guid_names = {column.name for column in tables[child].guid_columns}
        for child_column, parent_column in foreign_key["columns"]:
            if child_column in child_guid_names:
                candidates[(child, child_column)].append((parent, parent_column))

    selected: dict[tuple[str, str], tuple[str, str]] = {}
    for key, targets in candidates.items():
        pk_targets = [
            target
            for target in targets
            if target[1] == tables[target[0]].primary_key_column
        ]
        if not pk_targets:
            raise RuntimeError(f"No primary-key mapping target found for {key}: {targets}")
        selected[key] = sorted(pk_targets)[0]
    return selected


def map_standard_foreign_keys(
    cursor: pyodbc.Cursor,
    tables: dict[str, Table],
    mappings: dict[tuple[str, str], tuple[str, str]],
) -> None:
    for (child_name, child_column), (parent_name, parent_column) in sorted(mappings.items()):
        child = tables[child_name]
        parent = tables[parent_name]
        cursor.execute(
            f"""
            UPDATE child
            SET child.{quote(child_column)} = parent.{quote(parent_column)}
            FROM {child.shadow_qualified} AS child
            JOIN {parent.shadow_qualified} AS parent
              ON parent.{quote(legacy(parent_column))}
               = child.{quote(legacy(child_column))};
            """
        )


def map_logical_relationships(cursor: pyodbc.Cursor, tables: dict[str, Table]) -> None:
    logical_foreign_keys = {
        ("PolicyControlExplanations", "PolicyId"): ("Policies", "PolicyId"),
        ("PolicyControlExplanations", "ControlId"): ("Controls", "ControlId"),
    }
    for (child_name, child_column), (parent_name, parent_column) in logical_foreign_keys.items():
        child = tables[child_name]
        parent = tables[parent_name]
        cursor.execute(
            f"""
            UPDATE child
            SET child.{quote(child_column)} = parent.{quote(parent_column)}
            FROM {child.shadow_qualified} AS child
            JOIN {parent.shadow_qualified} AS parent
              ON parent.{quote(legacy(parent_column))}
               = child.{quote(legacy(child_column))};
            """
        )

    source_record_targets = {
        "Control": "Controls",
        "Domain": "Domains",
        "FrameworkElement": "FrameworkElements",
        "Policy": "Policies",
        "PolicyAccountability": "PolicyAccountabilityStatements",
        "PolicyConsequence": "PolicyConsequences",
        "PolicyControlStatement": "PolicyControlStatements",
        "PolicyObjective": "PolicyObjectives",
        "PolicyPrinciple": "PolicyPrinciples",
        "PolicyStrategy": "PolicyStrategyStatements",
        "PolicyTransparency": "PolicyTransparencyStatements",
        "Principle": "Principles",
    }
    source_parent_targets = {
        "Domain": "Domains",
        "FrameworkElement": "FrameworkVersions",
        "Policy": "Domains",
        "PolicyAccountability": "Policies",
        "PolicyConsequence": "Policies",
        "PolicyControlStatement": "Policies",
        "PolicyObjective": "Policies",
        "PolicyPrinciple": "Policies",
        "PolicyStrategy": "Policies",
        "PolicyTransparency": "Policies",
        "Principle": "Domains",
    }
    artifacts = tables["SemanticArtifacts"]
    for artifact_type, target_name in source_record_targets.items():
        target = tables[target_name]
        cursor.execute(
            f"""
            UPDATE artifact
            SET artifact.[SourceRecordId] = target.{quote(target.primary_key_column)}
            FROM {artifacts.shadow_qualified} AS artifact
            JOIN {target.shadow_qualified} AS target
              ON target.{quote(legacy(target.primary_key_column))}
               = artifact.{quote(legacy('SourceRecordId'))}
            WHERE artifact.[ArtifactType] = ?;
            """,
            artifact_type,
        )
    for artifact_type, target_name in source_parent_targets.items():
        target = tables[target_name]
        cursor.execute(
            f"""
            UPDATE artifact
            SET artifact.[SourceParentId] = target.{quote(target.primary_key_column)}
            FROM {artifacts.shadow_qualified} AS artifact
            JOIN {target.shadow_qualified} AS target
              ON target.{quote(legacy(target.primary_key_column))}
               = artifact.{quote(legacy('SourceParentId'))}
            WHERE artifact.[ArtifactType] = ?;
            """,
            artifact_type,
        )


def validate_and_finalize_shadow_columns(cursor: pyodbc.Cursor, tables: dict[str, Table]) -> None:
    for table in tables.values():
        source_count = int(scalar(cursor, f"SELECT COUNT_BIG(*) FROM {table.qualified};"))
        shadow_count = int(scalar(cursor, f"SELECT COUNT_BIG(*) FROM {table.shadow_qualified};"))
        if source_count != shadow_count:
            raise RuntimeError(
                f"Row count mismatch for {table.name}: source={source_count}, shadow={shadow_count}"
            )
        distinct_legacy = int(
            scalar(
                cursor,
                f"SELECT COUNT_BIG(DISTINCT {quote(legacy(table.primary_key_column))}) "
                f"FROM {table.shadow_qualified};",
            )
        )
        if distinct_legacy != source_count:
            raise RuntimeError(f"Legacy primary-key mapping is not unique for {table.name}")

        for column in table.guid_columns:
            if column.name == table.primary_key_column:
                continue
            missed = int(
                scalar(
                    cursor,
                    f"""
                    SELECT COUNT_BIG(*)
                    FROM {table.shadow_qualified}
                    WHERE {quote(legacy(column.name))} IS NOT NULL
                      AND {quote(column.name)} IS NULL;
                    """,
                )
            )
            invented = int(
                scalar(
                    cursor,
                    f"""
                    SELECT COUNT_BIG(*)
                    FROM {table.shadow_qualified}
                    WHERE {quote(legacy(column.name))} IS NULL
                      AND {quote(column.name)} IS NOT NULL;
                    """,
                )
            )
            if missed or invented:
                details = ""
                if table.name == "SemanticArtifacts":
                    details = "; artifact types=" + ", ".join(
                        str(row[0])
                        for row in cursor.execute(
                            f"""
                            SELECT DISTINCT [ArtifactType]
                            FROM {table.shadow_qualified}
                            WHERE ({quote(legacy(column.name))} IS NOT NULL
                                   AND {quote(column.name)} IS NULL)
                               OR ({quote(legacy(column.name))} IS NULL
                                   AND {quote(column.name)} IS NOT NULL);
                            """
                        ).fetchall()
                    )
                raise RuntimeError(
                    f"Mapping mismatch for {table.name}.{column.name}: "
                    f"missed={missed}, invented={invented}{details}"
                )
            nullability = "NULL" if column.nullable else "NOT NULL"
            cursor.execute(
                f"ALTER TABLE {table.shadow_qualified} "
                f"ALTER COLUMN {quote(column.name)} int {nullability};"
            )


def export_mappings(
    cursor: pyodbc.Cursor, tables: dict[str, Table], output_path: Path
) -> int:
    output_path.parent.mkdir(parents=True, exist_ok=True)
    row_count = 0
    with output_path.open("w", newline="", encoding="utf-8") as stream:
        writer = csv.writer(stream)
        writer.writerow(("TableName", "LegacyGuid", "NewId"))
        for table in tables.values():
            cursor.execute(
                f"""
                SELECT {quote(legacy(table.primary_key_column))},
                       {quote(table.primary_key_column)}
                FROM {table.shadow_qualified}
                ORDER BY {quote(table.primary_key_column)};
                """
            )
            for old_id, new_id in cursor.fetchall():
                writer.writerow((table.name, str(old_id), int(new_id)))
                row_count += 1
    return row_count


def referential_action_sql(action: str) -> str:
    return action.replace("_", " ")


def recreate_schema(
    cursor: pyodbc.Cursor,
    tables: dict[str, Table],
    foreign_keys: list[dict[str, Any]],
    keys: list[dict[str, Any]],
    indexes: list[dict[str, Any]],
    defaults: list[dict[str, Any]],
    checks: list[dict[str, Any]],
    triggers: list[dict[str, Any]],
) -> None:
    converted_names = set(tables)
    involved_foreign_keys = [
        foreign_key
        for foreign_key in foreign_keys
        if foreign_key["parent_table"] in converted_names
        or foreign_key["referenced_table"] in converted_names
    ]
    for foreign_key in involved_foreign_keys:
        cursor.execute(
            f"ALTER TABLE {qualified(foreign_key['parent_schema'], foreign_key['parent_table'])} "
            f"DROP CONSTRAINT {quote(foreign_key['name'])};"
        )

    for table in tables.values():
        cursor.execute(f"DROP TABLE {table.qualified};")
    for table in tables.values():
        cursor.execute(
            "EXEC sys.sp_rename ?, ?, 'OBJECT';",
            f"{table.schema}.{table.shadow_name}",
            table.name,
        )
        legacy_columns = ", ".join(quote(legacy(column.name)) for column in table.guid_columns)
        cursor.execute(f"ALTER TABLE {table.qualified} DROP COLUMN {legacy_columns};")

    for default in defaults:
        cursor.execute(
            f"ALTER TABLE {qualified(default['SchemaName'], default['TableName'])} "
            f"ADD CONSTRAINT {quote(default['ConstraintName'])} "
            f"DEFAULT {default['Definition']} FOR {quote(default['ColumnName'])};"
        )
    for check in checks:
        with_mode = "NOCHECK" if check["IsNotTrusted"] else "CHECK"
        table_name = qualified(check["SchemaName"], check["TableName"])
        cursor.execute(
            f"ALTER TABLE {table_name} WITH {with_mode} "
            f"ADD CONSTRAINT {quote(check['ConstraintName'])} CHECK {check['Definition']};"
        )
        if check["IsDisabled"]:
            cursor.execute(
                f"ALTER TABLE {table_name} NOCHECK CONSTRAINT {quote(check['ConstraintName'])};"
            )

    for key in keys:
        constraint_kind = "PRIMARY KEY" if key["type"] == "PK" else "UNIQUE"
        key_columns = ", ".join(
            f"{quote(name)} {'DESC' if descending else 'ASC'}"
            for name, descending in key["columns"]
        )
        cursor.execute(
            f"ALTER TABLE {qualified(key['schema'], key['table'])} "
            f"ADD CONSTRAINT {quote(key['name'])} {constraint_kind} "
            f"{key['index_type']} ({key_columns});"
        )

    for index in indexes:
        key_columns = ", ".join(
            f"{quote(name)} {'DESC' if descending else 'ASC'}"
            for name, descending in index["keys"]
        )
        include_sql = ""
        if index["includes"]:
            include_sql = " INCLUDE (" + ", ".join(
                quote(name) for name, _ in index["includes"]
            ) + ")"
        filter_sql = f" WHERE {index['filter']}" if index["filter"] else ""
        unique_sql = "UNIQUE " if index["unique"] else ""
        table_name = qualified(index["schema"], index["table"])
        cursor.execute(
            f"CREATE {unique_sql}{index['type']} INDEX {quote(index['name'])} "
            f"ON {table_name} ({key_columns}){include_sql}{filter_sql};"
        )
        if index["disabled"]:
            cursor.execute(f"ALTER INDEX {quote(index['name'])} ON {table_name} DISABLE;")

    for foreign_key in involved_foreign_keys:
        child_columns = ", ".join(quote(pair[0]) for pair in foreign_key["columns"])
        parent_columns = ", ".join(quote(pair[1]) for pair in foreign_key["columns"])
        with_mode = "NOCHECK" if foreign_key["not_trusted"] else "CHECK"
        replication = " NOT FOR REPLICATION" if foreign_key["not_for_replication"] else ""
        delete_sql = (
            ""
            if foreign_key["delete_action"] == "NO_ACTION"
            else f" ON DELETE {referential_action_sql(foreign_key['delete_action'])}"
        )
        update_sql = (
            ""
            if foreign_key["update_action"] == "NO_ACTION"
            else f" ON UPDATE {referential_action_sql(foreign_key['update_action'])}"
        )
        child_table = qualified(foreign_key["parent_schema"], foreign_key["parent_table"])
        cursor.execute(
            f"ALTER TABLE {child_table} WITH {with_mode} "
            f"ADD CONSTRAINT {quote(foreign_key['name'])} "
            f"FOREIGN KEY{replication} ({child_columns}) "
            f"REFERENCES {qualified(foreign_key['referenced_schema'], foreign_key['referenced_table'])} "
            f"({parent_columns}){delete_sql}{update_sql};"
        )
        if foreign_key["disabled"]:
            cursor.execute(
                f"ALTER TABLE {child_table} NOCHECK CONSTRAINT {quote(foreign_key['name'])};"
            )

    cursor.execute(
        """
        ALTER TABLE dbo.PolicyControlExplanations WITH CHECK
        ADD CONSTRAINT FK_PolicyControlExplanations_Policies
            FOREIGN KEY (PolicyId) REFERENCES dbo.Policies(PolicyId);
        ALTER TABLE dbo.PolicyControlExplanations WITH CHECK
        ADD CONSTRAINT FK_PolicyControlExplanations_Controls
            FOREIGN KEY (ControlId) REFERENCES dbo.Controls(ControlId);
        """
    )

    for trigger in triggers:
        cursor.execute(trigger["Definition"])
        if trigger["IsDisabled"]:
            cursor.execute(
                f"DISABLE TRIGGER {quote(trigger['TriggerName'])} "
                f"ON {qualified(trigger['SchemaName'], trigger['TableName'])};"
            )

    cursor.execute(
        """
        IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigrations WHERE MigrationId = ?)
            INSERT INTO dbo.SchemaMigrations (MigrationId, AppliedAtUtc)
            VALUES (?, SYSUTCDATETIME());
        """,
        MIGRATION_ID,
        MIGRATION_ID,
    )


def post_commit_validation(cursor: pyodbc.Cursor, converted_table_names: set[str]) -> None:
    guid_columns = int(
        scalar(
            cursor,
            """
            SELECT COUNT(*)
            FROM sys.columns AS c
            JOIN sys.types AS ty ON ty.user_type_id = c.user_type_id
            JOIN sys.tables AS t ON t.object_id = c.object_id
            JOIN sys.schemas AS s ON s.schema_id = t.schema_id
            WHERE t.is_ms_shipped = 0 AND s.name = 'dbo'
              AND ty.name = 'uniqueidentifier';
            """,
        )
    )
    identity_pks = int(
        scalar(
            cursor,
            """
            SELECT COUNT(DISTINCT t.object_id)
            FROM sys.tables AS t
            JOIN sys.schemas AS s ON s.schema_id = t.schema_id
            JOIN sys.key_constraints AS kc
              ON kc.parent_object_id = t.object_id AND kc.type = 'PK'
            JOIN sys.index_columns AS ic
              ON ic.object_id = t.object_id AND ic.index_id = kc.unique_index_id
            JOIN sys.columns AS c
              ON c.object_id = t.object_id AND c.column_id = ic.column_id
            JOIN sys.types AS ty ON ty.user_type_id = c.user_type_id
            WHERE s.name = 'dbo' AND t.is_ms_shipped = 0
              AND ty.name = 'int' AND c.is_identity = 1
              AND t.name IN ({});
            """.format(",".join("?" for _ in converted_table_names)),
            sorted(converted_table_names),
        )
    )
    if guid_columns:
        raise RuntimeError(f"Post-commit validation found {guid_columns} remaining GUID columns")
    if identity_pks != len(converted_table_names):
        raise RuntimeError(
            f"Expected {len(converted_table_names)} converted identity PKs; found {identity_pks}"
        )
    cursor.execute("DBCC CHECKCONSTRAINTS WITH ALL_CONSTRAINTS;")
    violations: list[Any] = []
    while True:
        if cursor.description is not None:
            violations.extend(cursor.fetchall())
        if not cursor.nextset():
            break
    if violations:
        raise RuntimeError(f"DBCC CHECKCONSTRAINTS reported {len(violations)} violations")
    cursor.execute("DBCC CHECKDB WITH NO_INFOMSGS;")
    while True:
        if cursor.description is not None:
            messages = cursor.fetchall()
            if messages:
                raise RuntimeError(f"DBCC CHECKDB reported {len(messages)} messages")
        if not cursor.nextset():
            break


def connection_string(server: str, database: str) -> str:
    return (
        "DRIVER={ODBC Driver 18 for SQL Server};"
        f"SERVER={server};DATABASE={database};Trusted_Connection=yes;"
        "Encrypt=yes;TrustServerCertificate=yes;"
    )


def default_mapping_path(database: str) -> Path:
    timestamp = datetime.now(UTC).strftime("%Y%m%d_%H%M%S")
    safe_database = "".join(character if character.isalnum() or character in "-_" else "_" for character in database)
    return DEFAULT_BACKUP_DIRECTORY / f"{safe_database}_guid_to_int_{timestamp}.csv"


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--server", default=DEFAULT_SERVER)
    parser.add_argument("--database", required=True)
    parser.add_argument("--mapping-output", type=Path)
    parser.add_argument(
        "--allow-production",
        action="store_true",
        help="Allow conversion when --database is exactly DomainLinks.",
    )
    parser.add_argument(
        "--yes",
        action="store_true",
        help="Required confirmation that the target is a disposable/restorable database.",
    )
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    if not args.yes:
        print("Refusing to run without --yes.", file=sys.stderr)
        return 2
    if args.database.casefold() == PRODUCTION_DATABASE.casefold() and not args.allow_production:
        print("Refusing the production database without --allow-production.", file=sys.stderr)
        return 2

    mapping_path = args.mapping_output or default_mapping_path(args.database)
    print(f"Connecting to {args.server}/{args.database}")
    connection = pyodbc.connect(connection_string(args.server, args.database), timeout=10)
    connection.autocommit = False
    cursor = connection.cursor()
    wrote_mapping = False
    orphan_output_path: Path | None = None
    conversion_committed = False
    try:
        actual_database = str(scalar(cursor, "SELECT DB_NAME();"))
        if actual_database.casefold() != args.database.casefold():
            raise RuntimeError(
                f"Connected database mismatch: requested {args.database}, connected {actual_database}"
            )
        already_applied = int(
            scalar(
                cursor,
                "SELECT COUNT(*) FROM dbo.SchemaMigrations WHERE MigrationId = ?;",
                (MIGRATION_ID,),
            )
        )
        if already_applied:
            raise RuntimeError(f"Migration {MIGRATION_ID} is already applied")

        tables = load_tables(cursor)
        if not tables:
            raise RuntimeError("No dbo tables with GUID primary keys were found")
        print(f"Preflight: {len(tables)} GUID-key tables")
        preflight(cursor, tables)
        foreign_keys = load_foreign_keys(cursor)
        keys = load_key_constraints(cursor, set(tables))
        indexes = load_indexes(cursor, set(tables))
        defaults, checks, triggers = load_simple_metadata(cursor, set(tables))
        connection.commit()

        cursor.execute("SET XACT_ABORT ON;")
        orphan_count, orphan_output_path = export_and_remove_orphan_explanations(
            cursor, mapping_path
        )
        if orphan_count:
            print(
                f"Exported and removed {orphan_count} orphan policy-control explanations "
                f"to {orphan_output_path}"
            )
        print("Building shadow tables")
        create_shadow_tables(cursor, tables)
        mappings = build_fk_mapping(tables, foreign_keys)
        map_standard_foreign_keys(cursor, tables, mappings)
        map_logical_relationships(cursor, tables)
        validate_and_finalize_shadow_columns(cursor, tables)

        mapping_rows = export_mappings(cursor, tables, mapping_path)
        wrote_mapping = True
        print(f"Exported {mapping_rows} ID mappings to {mapping_path}")

        print("Swapping tables and rebuilding constraints")
        recreate_schema(
            cursor,
            tables,
            foreign_keys,
            keys,
            indexes,
            defaults,
            checks,
            triggers,
        )
        connection.commit()
        conversion_committed = True
        print("Committed database conversion")

        cursor = connection.cursor()
        post_commit_validation(cursor, set(tables))
        connection.commit()
        print("Validation passed: zero user GUID columns; constraints and DBCC are clean")
        return 0
    except Exception as exc:
        connection.rollback()
        if not conversion_committed:
            if wrote_mapping and mapping_path.exists():
                mapping_path.unlink()
            if orphan_output_path is not None and orphan_output_path.exists():
                orphan_output_path.unlink()
            print(f"Conversion failed and was rolled back: {exc}", file=sys.stderr)
        else:
            print(
                f"Conversion committed, but post-commit validation failed: {exc}",
                file=sys.stderr,
            )
        return 1
    finally:
        connection.close()


if __name__ == "__main__":
    raise SystemExit(main())
