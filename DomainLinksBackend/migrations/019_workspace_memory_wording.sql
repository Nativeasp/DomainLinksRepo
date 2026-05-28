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

IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigrations WHERE MigrationId = '019_workspace_memory_wording')
   AND OBJECT_ID('dbo.Domains', 'U') IS NOT NULL
BEGIN
    UPDATE dbo.Domains
    SET
        DisplayName = 'Workspace Memory',
        Description = 'Workspace Memory for active chats, uploads, and project-level inference.',
        UpdatedAtUtc = SYSUTCDATETIME()
    WHERE DomainCode = 'workspace-memory';
END;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigrations WHERE MigrationId = '019_workspace_memory_wording')
   AND OBJECT_ID('dbo.Collections', 'U') IS NOT NULL
BEGIN
    UPDATE c
    SET
        c.Description = REPLACE(c.Description, 'New short-memory project collection.', 'New Workspace Memory collection.'),
        c.UpdatedAtUtc = SYSUTCDATETIME()
    FROM dbo.Collections c
    JOIN dbo.Domains d
        ON d.DomainId = c.DomainId
    WHERE
        d.DomainCode = 'workspace-memory'
        AND c.Description = 'New short-memory project collection.';

    UPDATE c
    SET
        c.Description = 'Sample Workspace Memory collection for an active hiring request.',
        c.UpdatedAtUtc = SYSUTCDATETIME()
    FROM dbo.Collections c
    JOIN dbo.Domains d
        ON d.DomainId = c.DomainId
    WHERE
        d.DomainCode = 'workspace-memory'
        AND c.Description = 'Sample short-term project memory scope for an active hiring request.';
END;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigrations WHERE MigrationId = '019_workspace_memory_wording')
BEGIN
    INSERT INTO dbo.SchemaMigrations (MigrationId)
    VALUES ('019_workspace_memory_wording');
END;
GO
