SET NOCOUNT ON;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigrations WHERE MigrationId = '014_domain_display_order')
   AND OBJECT_ID('dbo.Domains', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.Domains', 'DisplayOrder') IS NULL
    BEGIN
        ALTER TABLE dbo.Domains
        ADD DisplayOrder INT NULL;
    END;
END;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigrations WHERE MigrationId = '014_domain_display_order')
   AND OBJECT_ID('dbo.Domains', 'U') IS NOT NULL
BEGIN
    ;WITH OrderedDomains AS (
        SELECT
            DomainId,
            ROW_NUMBER() OVER (
                PARTITION BY DomainParentId
                ORDER BY CreatedAtUtc, DisplayName, DomainId
            ) * 10 AS NewDisplayOrder
        FROM dbo.Domains
    )
    UPDATE d
    SET DisplayOrder = od.NewDisplayOrder
    FROM dbo.Domains d
    JOIN OrderedDomains od
        ON od.DomainId = d.DomainId
    WHERE d.DisplayOrder IS NULL OR d.DisplayOrder = 0;
END;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigrations WHERE MigrationId = '014_domain_display_order')
   AND OBJECT_ID('dbo.Domains', 'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM sys.default_constraints dc
        JOIN sys.columns c
            ON c.default_object_id = dc.object_id
        JOIN sys.tables t
            ON t.object_id = c.object_id
        JOIN sys.schemas s
            ON s.schema_id = t.schema_id
        WHERE
            s.name = 'dbo'
            AND t.name = 'Domains'
            AND c.name = 'DisplayOrder'
    )
    BEGIN
        ALTER TABLE dbo.Domains
        ADD CONSTRAINT DF_Domains_DisplayOrder DEFAULT (0) FOR DisplayOrder;
    END;

    ALTER TABLE dbo.Domains
    ALTER COLUMN DisplayOrder INT NOT NULL;
END;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigrations WHERE MigrationId = '014_domain_display_order')
   AND OBJECT_ID('dbo.Domains', 'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE
            name = 'IX_Domains_Parent_DisplayOrder'
            AND object_id = OBJECT_ID('dbo.Domains')
    )
    BEGIN
        CREATE INDEX IX_Domains_Parent_DisplayOrder
            ON dbo.Domains(DomainParentId, DisplayOrder, DisplayName);
    END;

    INSERT INTO dbo.SchemaMigrations (MigrationId)
    VALUES ('014_domain_display_order');
END;
GO
