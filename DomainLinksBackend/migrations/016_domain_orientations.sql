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

IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigrations WHERE MigrationId = '016_domain_orientations')
BEGIN
    IF OBJECT_ID('dbo.DomainOrientations', 'U') IS NULL
    BEGIN
        CREATE TABLE dbo.DomainOrientations (
            ID                    INT IDENTITY(1,1) NOT NULL,
            CODE                  NVARCHAR(50) NOT NULL,
            NAME                  NVARCHAR(100) NOT NULL,
            DESCRIPTION           NVARCHAR(500) NULL,
            DISPLAY_ORDER         INT NOT NULL,
            CREATED_DATE          DATETIME2 NOT NULL
                CONSTRAINT DF_DomainOrientations_CREATED_DATE DEFAULT (SYSDATETIME()),
            CREATED_BY            NVARCHAR(128) NOT NULL
                CONSTRAINT DF_DomainOrientations_CREATED_BY DEFAULT (SUSER_SNAME()),
            UPDATED_DATE          DATETIME2 NULL,
            UPDATED_BY            NVARCHAR(128) NULL,
            CONSTRAINT PK_DomainOrientations PRIMARY KEY CLUSTERED (ID),
            CONSTRAINT UQ_DomainOrientations_CODE UNIQUE (CODE)
        );
    END;
END;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigrations WHERE MigrationId = '016_domain_orientations')
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

IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigrations WHERE MigrationId = '016_domain_orientations')
   AND OBJECT_ID('dbo.Domains', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.Domains', 'DomainOrientationId') IS NULL
    BEGIN
        ALTER TABLE dbo.Domains
        ADD DomainOrientationId INT NULL;
    END;
END;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigrations WHERE MigrationId = '016_domain_orientations')
   AND OBJECT_ID('dbo.Domains', 'U') IS NOT NULL
   AND OBJECT_ID('dbo.DomainOrientations', 'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM sys.foreign_keys
        WHERE
            name = 'FK_Domains_DomainOrientations'
            AND parent_object_id = OBJECT_ID('dbo.Domains')
    )
    BEGIN
        ALTER TABLE dbo.Domains
        ADD CONSTRAINT FK_Domains_DomainOrientations
            FOREIGN KEY (DomainOrientationId) REFERENCES dbo.DomainOrientations(ID);
    END;

    IF NOT EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE
            name = 'IX_Domains_DomainOrientationId'
            AND object_id = OBJECT_ID('dbo.Domains')
    )
    BEGIN
        CREATE INDEX IX_Domains_DomainOrientationId
            ON dbo.Domains(DomainOrientationId);
    END;
END;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigrations WHERE MigrationId = '016_domain_orientations')
BEGIN
    INSERT INTO dbo.SchemaMigrations (MigrationId)
    VALUES ('016_domain_orientations');
END;
GO
