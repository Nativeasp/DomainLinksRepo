# DomainLinks Brain Database DDL and Reversal Record

Last reviewed: 2026-07-19

## Purpose

This is the database-removal record for the DomainLinks Brain component. Update it in the same change as any Brain-specific DDL so the component can be rebuilt or removed without reverse-engineering its database footprint.

## Current DDL Inventory

| Migration | Object | Action | Reversal |
|---|---|---|---|
| `033_semantic_artifact_embeddings.sql` | `dbo.SemanticArtifacts` | Adds the canonical semantic-artifact registry and durable embedding work queue. | Drop after the embeddings table. |
| `033_semantic_artifact_embeddings.sql` | `dbo.SemanticArtifactEmbeddings768` | Adds 768-dimensional vectors for domains, controls, policies, and policy statements. | Drop first because it references artifacts and embedding profiles. |
| `033_semantic_artifact_embeddings.sql` | Indexes, constraints, and migration record | Adds queue/source/profile indexes, keys, status validation, and the `033` migration record. | Removed with the tables; delete the `SchemaMigrations` row last. |

The exact paired reversal is `DomainLinksBackend/migrations/rollback_033_semantic_artifact_embeddings.sql`. It deletes Brain-owned semantic vectors and queue state but never modifies the source domain, control, policy, statement, document, or content-unit records.

## Component Removal

To remove the MVP:

1. Remove the `/brain/*` FastAPI routes and the backend graph-query module.
2. Remove the WPF Brain window, launch-context types, WebView assets, menu action, and Domain Store entry points.
3. If semantic vectors must be retained, export the two Brain-owned tables before rollback.
4. Run `rollback_033_semantic_artifact_embeddings.sql`.
5. Do not alter existing knowledge tables; they are shared application data and are not owned by Brain.
6. Run the query below. It should return no rows after removal.

```sql
SELECT
    s.name AS SchemaName,
    o.name AS ObjectName,
    o.type_desc AS ObjectType
FROM sys.objects o
JOIN sys.schemas s ON s.schema_id = o.schema_id
WHERE o.name LIKE 'Brain%'
   OR o.name LIKE 'DomainLinksBrain%'
   OR o.name LIKE 'SemanticArtifact%'
ORDER BY s.name, o.name;
```

## Rule for Future Brain DDL

Any future Brain migration must update this record with:

- Migration filename and deployment order.
- Every created or altered database object.
- Whether the object owns data or derives it from shared DomainLinks data.
- A reverse-order rollback script that removes only Brain-owned objects.
- Data-export requirements before destructive reversal.
- Foreign-key, index, constraint, and `SchemaMigrations` cleanup steps.

The rollback must be written and reviewed in the same change as the forward migration. A Brain migration is incomplete without its paired reversal.
