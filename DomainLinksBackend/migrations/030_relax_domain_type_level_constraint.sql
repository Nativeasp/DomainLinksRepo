SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
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

IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigrations WHERE MigrationId = '030_relax_domain_type_level_constraint')
BEGIN
    BEGIN TRANSACTION;

    BEGIN TRY
        IF EXISTS (
            SELECT 1
            FROM sys.check_constraints
            WHERE name = 'CK_DomainTypes_DOMAIN_LEVEL'
              AND parent_object_id = OBJECT_ID('dbo.DomainTypes')
        )
        BEGIN
            ALTER TABLE dbo.DomainTypes DROP CONSTRAINT CK_DomainTypes_DOMAIN_LEVEL;
        END;

        ALTER TABLE dbo.DomainTypes
        ADD CONSTRAINT CK_DomainTypes_DOMAIN_LEVEL CHECK (DOMAIN_LEVEL BETWEEN 1 AND 99);

        INSERT INTO dbo.SchemaMigrations (MigrationId)
        VALUES ('030_relax_domain_type_level_constraint');

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;
GO
