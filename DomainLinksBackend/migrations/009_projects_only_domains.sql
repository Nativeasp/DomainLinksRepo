SET NOCOUNT ON;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigrations WHERE MigrationId = '009_projects_only_domains')
BEGIN
    DECLARE @ProjectsDomainId UNIQUEIDENTIFIER =
        (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'workspace-memory');
    DECLARE @StrategicDomainTypeId INT =
        (SELECT ID FROM dbo.DomainTypes WHERE CODE = 'STRATEGIC');

    IF @ProjectsDomainId IS NULL
    BEGIN
        THROW 51000, 'Projects domain was not found. Aborting domain cleanup.', 1;
    END;

    IF @StrategicDomainTypeId IS NULL
    BEGIN
        THROW 51000, 'Strategic DomainTypes row was not found. Aborting domain cleanup.', 1;
    END;

    UPDATE dbo.Domains
    SET
        DomainParentId = NULL,
        DomainTypeId = CASE WHEN DomainId = @ProjectsDomainId THEN @StrategicDomainTypeId ELSE DomainTypeId END,
        UpdatedAtUtc = SYSUTCDATETIME()
    WHERE DomainId = @ProjectsDomainId;

    DELETE e
    FROM dbo.ContentUnitEmbeddings768 e
    INNER JOIN dbo.ContentUnits cu ON cu.ContentUnitId = e.ContentUnitId
    INNER JOIN dbo.Documents d ON d.DocumentId = cu.DocumentId
    INNER JOIN dbo.Collections c ON c.CollectionId = d.CollectionId
    WHERE c.DomainId <> @ProjectsDomainId;

    DELETE cu
    FROM dbo.ContentUnits cu
    INNER JOIN dbo.Documents d ON d.DocumentId = cu.DocumentId
    INNER JOIN dbo.Collections c ON c.CollectionId = d.CollectionId
    WHERE c.DomainId <> @ProjectsDomainId;

    DELETE d
    FROM dbo.Documents d
    INNER JOIN dbo.Collections c ON c.CollectionId = d.CollectionId
    WHERE c.DomainId <> @ProjectsDomainId;

    DELETE FROM dbo.Collections
    WHERE DomainId <> @ProjectsDomainId;

    DELETE FROM dbo.Domains
    WHERE DomainId <> @ProjectsDomainId;

    DELETE FROM dbo.DomainTypes
    WHERE CODE = 'TECHNICAL_EXECUTION';

    IF EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID('dbo.Domains')
          AND name = 'IX_Domains_DomainType_Status'
    )
    BEGIN
        DROP INDEX IX_Domains_DomainType_Status ON dbo.Domains;
    END;

    IF EXISTS (
        SELECT 1
        FROM sys.check_constraints
        WHERE name = 'CK_Domains_DomainType'
    )
    BEGIN
        ALTER TABLE dbo.Domains
        DROP CONSTRAINT CK_Domains_DomainType;
    END;

    IF COL_LENGTH('dbo.Domains', 'DomainType') IS NOT NULL
    BEGIN
        ALTER TABLE dbo.Domains
        DROP COLUMN DomainType;
    END;

    INSERT INTO dbo.SchemaMigrations (MigrationId)
    VALUES ('009_projects_only_domains');
END;
GO
