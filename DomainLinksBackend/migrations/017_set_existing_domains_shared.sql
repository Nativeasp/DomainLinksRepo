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

IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigrations WHERE MigrationId = '017_set_existing_domains_shared')
   AND OBJECT_ID('dbo.DomainOrientations', 'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.DomainOrientations WHERE CODE = 'SHARED_SERVICES')
    BEGIN
        INSERT INTO dbo.DomainOrientations (CODE, NAME, DESCRIPTION, DISPLAY_ORDER)
        VALUES (
            'SHARED_SERVICES',
            'Shared Services',
            'Domains that provide internal organizational support capabilities across the enterprise.',
            10
        );
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.DomainOrientations WHERE CODE = 'CLIENT_SERVICES')
    BEGIN
        INSERT INTO dbo.DomainOrientations (CODE, NAME, DESCRIPTION, DISPLAY_ORDER)
        VALUES (
            'CLIENT_SERVICES',
            'Client Services',
            'Domains that primarily deliver services, programs, or direct support to clients, communities, or external recipients.',
            20
        );
    END;
END;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigrations WHERE MigrationId = '017_set_existing_domains_shared')
   AND OBJECT_ID('dbo.Domains', 'U') IS NOT NULL
   AND COL_LENGTH('dbo.Domains', 'DomainOrientationId') IS NOT NULL
BEGIN
    DECLARE @SharedServicesOrientationId INT =
        (SELECT TOP 1 ID FROM dbo.DomainOrientations WHERE CODE = 'SHARED_SERVICES');

    IF @SharedServicesOrientationId IS NULL
    BEGIN
        THROW 51000, 'Shared Services DomainOrientations row was not found. Aborting orientation backfill.', 1;
    END;

    UPDATE dbo.Domains
    SET
        DomainOrientationId = @SharedServicesOrientationId,
        UpdatedAtUtc = SYSUTCDATETIME()
    WHERE DomainOrientationId IS NULL;
END;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigrations WHERE MigrationId = '017_set_existing_domains_shared')
BEGIN
    INSERT INTO dbo.SchemaMigrations (MigrationId)
    VALUES ('017_set_existing_domains_shared');
END;
GO
