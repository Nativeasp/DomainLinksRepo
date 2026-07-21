SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

-- Operational migration marker.
--
-- The data conversion is performed by:
--   DomainLinksBackend/scripts/convert_guid_ids_to_int.py
--
-- That utility rebuilds all GUID-key tables, maps polymorphic semantic IDs,
-- exports the old-to-new ID map, recreates constraints/indexes/triggers, and
-- records this migration. This SQL file deliberately refuses to mark an
-- unconverted database as complete.

IF EXISTS (
    SELECT 1
    FROM sys.columns AS c
    JOIN sys.types AS ty ON ty.user_type_id = c.user_type_id
    JOIN sys.tables AS t ON t.object_id = c.object_id
    JOIN sys.schemas AS s ON s.schema_id = t.schema_id
    WHERE t.is_ms_shipped = 0
      AND s.name = 'dbo'
      AND ty.name = 'uniqueidentifier'
)
BEGIN
    THROW 51035,
        'Migration 035 requires scripts/convert_guid_ids_to_int.py and a verified backup.',
        1;
END;
GO

IF NOT EXISTS (
    SELECT 1 FROM dbo.SchemaMigrations WHERE MigrationId = '035_integer_identity_keys'
)
BEGIN
    INSERT INTO dbo.SchemaMigrations (MigrationId, AppliedAtUtc)
    VALUES ('035_integer_identity_keys', SYSUTCDATETIME());
END;
GO
