SET NOCOUNT ON;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigrations WHERE MigrationId = '008_domains_hierarchy')
   AND OBJECT_ID('dbo.Domains', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.Domains', 'DomainParentId') IS NULL
    BEGIN
        ALTER TABLE dbo.Domains
        ADD DomainParentId UNIQUEIDENTIFIER NULL;
    END;

    IF COL_LENGTH('dbo.Domains', 'DomainTypeId') IS NULL
    BEGIN
        ALTER TABLE dbo.Domains
        ADD DomainTypeId INT NULL;
    END;
END;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigrations WHERE MigrationId = '008_domains_hierarchy')
   AND OBJECT_ID('dbo.Domains', 'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM sys.foreign_keys
        WHERE name = 'FK_Domains_ParentDomain'
    )
    BEGIN
        ALTER TABLE dbo.Domains
        ADD CONSTRAINT FK_Domains_ParentDomain
            FOREIGN KEY (DomainParentId) REFERENCES dbo.Domains(DomainId);
    END;

    IF NOT EXISTS (
        SELECT 1
        FROM sys.foreign_keys
        WHERE name = 'FK_Domains_DomainTypes'
    )
    BEGIN
        ALTER TABLE dbo.Domains
        ADD CONSTRAINT FK_Domains_DomainTypes
            FOREIGN KEY (DomainTypeId) REFERENCES dbo.DomainTypes(ID);
    END;

    IF NOT EXISTS (
        SELECT 1
        FROM sys.check_constraints
        WHERE name = 'CK_Domains_ParentDomain_NotSelf'
    )
    BEGIN
        ALTER TABLE dbo.Domains
        ADD CONSTRAINT CK_Domains_ParentDomain_NotSelf
            CHECK (DomainParentId IS NULL OR DomainParentId <> DomainId);
    END;
END;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigrations WHERE MigrationId = '008_domains_hierarchy')
   AND OBJECT_ID('dbo.Domains', 'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE name = 'IX_Domains_DomainParentId'
          AND object_id = OBJECT_ID('dbo.Domains')
    )
    BEGIN
        CREATE INDEX IX_Domains_DomainParentId
            ON dbo.Domains(DomainParentId);
    END;

    IF NOT EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE name = 'IX_Domains_DomainTypeId'
          AND object_id = OBJECT_ID('dbo.Domains')
    )
    BEGIN
        CREATE INDEX IX_Domains_DomainTypeId
            ON dbo.Domains(DomainTypeId);
    END;

    INSERT INTO dbo.SchemaMigrations (MigrationId)
    VALUES ('008_domains_hierarchy');
END;
GO
