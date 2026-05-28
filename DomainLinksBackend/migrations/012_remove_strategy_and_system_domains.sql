SET NOCOUNT ON;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigrations WHERE MigrationId = '012_remove_strategy_and_system_domains')
BEGIN
    DELETE FROM dbo.Domains
    WHERE DomainId IN
    (
        '6A6B3161-4609-4406-9F62-D351C427D2ED',
        'D38E3EDB-3120-4736-BE2F-E91114E7868F'
    );

    INSERT INTO dbo.SchemaMigrations (MigrationId)
    VALUES ('012_remove_strategy_and_system_domains');
END;
GO
