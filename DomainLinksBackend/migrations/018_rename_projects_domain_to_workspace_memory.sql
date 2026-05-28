SET NOCOUNT ON;
GO

IF OBJECT_ID('dbo.SchemaMigrations', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.SchemaMigrations (
        MigrationId      NVARCHAR(100) NOT NULL PRIMARY KEY,
        AppliedAtUtc     DATETIME2(3) NOT NULL
            CONSTRAINT DF_SchemaMigrations_AppliedAtUtc DEFAULT (SYSUTCDATETIME())
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigrations WHERE MigrationId = '018_rename_projects_domain_to_workspace_memory')
   AND OBJECT_ID('dbo.Domains', 'U') IS NOT NULL
BEGIN
    IF EXISTS (
        SELECT 1
        FROM dbo.Domains
        WHERE DomainCode = 'projects'
    )
       AND NOT EXISTS (
           SELECT 1
           FROM dbo.Domains
           WHERE DomainCode = 'workspace-memory'
       )
    BEGIN
        UPDATE dbo.Domains
        SET
            DomainCode = 'workspace-memory',
            DisplayName = 'Workspace Memory',
            UpdatedAtUtc = SYSUTCDATETIME()
        WHERE DomainCode = 'projects';
    END;
END;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigrations WHERE MigrationId = '018_rename_projects_domain_to_workspace_memory')
BEGIN
    INSERT INTO dbo.SchemaMigrations (MigrationId)
    VALUES ('018_rename_projects_domain_to_workspace_memory');
END;
GO
