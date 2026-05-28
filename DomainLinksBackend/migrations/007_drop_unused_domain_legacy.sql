SET NOCOUNT ON;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigrations WHERE MigrationId = '007_drop_unused_domain_legacy')
BEGIN
    IF OBJECT_ID('dbo.LegacyChromaMigrationState', 'U') IS NOT NULL
    BEGIN
        DROP TABLE dbo.LegacyChromaMigrationState;
    END;

    IF OBJECT_ID('dbo.DomainClusterMembers', 'U') IS NOT NULL
    BEGIN
        DROP TABLE dbo.DomainClusterMembers;
    END;

    IF OBJECT_ID('dbo.DomainClusters', 'U') IS NOT NULL
    BEGIN
        DROP TABLE dbo.DomainClusters;
    END;

    INSERT INTO dbo.SchemaMigrations (MigrationId)
    VALUES ('007_drop_unused_domain_legacy');
END;
GO
