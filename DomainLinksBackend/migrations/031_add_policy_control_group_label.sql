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

IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigrations WHERE MigrationId = '031_add_policy_control_group_label')
BEGIN
    BEGIN TRANSACTION;

    BEGIN TRY
        IF OBJECT_ID('dbo.PolicyControlStatements', 'U') IS NOT NULL
           AND COL_LENGTH('dbo.PolicyControlStatements', 'GroupLabel') IS NULL
        BEGIN
            ALTER TABLE dbo.PolicyControlStatements
            ADD GroupLabel NVARCHAR(200) NULL;

            CREATE INDEX IX_PolicyControlStatements_PolicyId_GroupLabel_ControlId
                ON dbo.PolicyControlStatements(PolicyId, GroupLabel, ControlId);
        END;

        INSERT INTO dbo.SchemaMigrations (MigrationId)
        VALUES ('031_add_policy_control_group_label');

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;
GO
