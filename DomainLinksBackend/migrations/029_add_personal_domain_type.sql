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

IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigrations WHERE MigrationId = '029_add_personal_domain_type')
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
        ADD CONSTRAINT CK_DomainTypes_DOMAIN_LEVEL CHECK (DOMAIN_LEVEL BETWEEN 1 AND 4);

        IF NOT EXISTS (SELECT 1 FROM dbo.DomainTypes WHERE CODE = 'PERSONAL')
        BEGIN
            INSERT INTO dbo.DomainTypes (
                CODE,
                NAME,
                DOMAIN_LEVEL,
                PRIMARY_FOCUS,
                KEY_QUESTION,
                DESCRIPTION,
                DISPLAY_ORDER,
                EFFECTIVE_START_DATE
            )
            VALUES (
                'PERSONAL',
                'Personal',
                4,
                'Personal knowledge, planning, and private working context for an individual user.',
                'What personal areas should this user organize for their own work, notes, and development?',
                'Personal domains for individual planning, learning, notes, goals, and private working context.',
                40,
                CAST(SYSDATETIME() AS date)
            );
        END;

        INSERT INTO dbo.SchemaMigrations (MigrationId)
        VALUES ('029_add_personal_domain_type');

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;
GO
