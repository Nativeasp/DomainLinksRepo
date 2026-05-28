SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigrations WHERE MigrationId = '005_domains_cleanup')
BEGIN
    IF OBJECT_ID('dbo.Collections', 'U') IS NOT NULL
       AND EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Collections_Domains')
    BEGIN
        ALTER TABLE dbo.Collections DROP CONSTRAINT FK_Collections_Domains;
    END;

    IF OBJECT_ID('dbo.LegacyChromaMigrationState', 'U') IS NOT NULL
       AND EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_LegacyChromaMigrationState_Domains')
    BEGIN
        ALTER TABLE dbo.LegacyChromaMigrationState DROP CONSTRAINT FK_LegacyChromaMigrationState_Domains;
    END;

    IF OBJECT_ID('dbo.DomainClusterMembers', 'U') IS NOT NULL
       AND EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_DomainClusterMembers_DomainClusters')
    BEGIN
        ALTER TABLE dbo.DomainClusterMembers DROP CONSTRAINT FK_DomainClusterMembers_DomainClusters;
    END;

    IF OBJECT_ID('dbo.DomainClusterMembers', 'U') IS NOT NULL
       AND EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_DomainClusterMembers_Domains')
    BEGIN
        ALTER TABLE dbo.DomainClusterMembers DROP CONSTRAINT FK_DomainClusterMembers_Domains;
    END;

    IF OBJECT_ID('dbo.DomainClusterMembers', 'U') IS NOT NULL
    BEGIN
        DROP TABLE dbo.DomainClusterMembers;
    END;

    IF OBJECT_ID('dbo.DomainClusters', 'U') IS NOT NULL
    BEGIN
        DROP TABLE dbo.DomainClusters;
    END;

    IF OBJECT_ID('dbo.Domains', 'U') IS NOT NULL
    BEGIN
        DROP TABLE dbo.Domains;
    END;

    IF OBJECT_ID('dbo.DomainTypes', 'U') IS NOT NULL
    BEGIN
        DROP TABLE dbo.DomainTypes;
    END;

    INSERT INTO dbo.SchemaMigrations (MigrationId)
    VALUES ('005_domains_cleanup');
END;
GO
