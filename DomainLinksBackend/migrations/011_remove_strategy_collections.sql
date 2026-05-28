SET NOCOUNT ON;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigrations WHERE MigrationId = '011_remove_strategy_collections')
BEGIN
    DELETE FROM dbo.Collections
    WHERE CollectionCode IN ('ops-priorities', 'strategic-planning');

    INSERT INTO dbo.SchemaMigrations (MigrationId)
    VALUES ('011_remove_strategy_collections');
END;
GO
