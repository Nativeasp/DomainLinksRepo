SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

BEGIN TRANSACTION;
BEGIN TRY
    DROP TRIGGER IF EXISTS dbo.TR_FrameworkPrincipleLinks_ProtectPublished;
    DROP TRIGGER IF EXISTS dbo.TR_FrameworkElementRelations_ProtectPublished;
    DROP TRIGGER IF EXISTS dbo.TR_FrameworkContextRules_ProtectPublished;
    DROP TRIGGER IF EXISTS dbo.TR_FrameworkElements_ProtectPublished;
    DROP TRIGGER IF EXISTS dbo.TR_FrameworkVersions_ProtectPublished;
    DROP TRIGGER IF EXISTS dbo.TR_Principles_ProtectImmutable;

    DROP TABLE IF EXISTS dbo.FrameworkContextRules;
    DROP TABLE IF EXISTS dbo.FrameworkArtifactLinks;
    DROP TABLE IF EXISTS dbo.FrameworkPrincipleLinks;
    DROP TABLE IF EXISTS dbo.FrameworkElementRelations;
    DROP TABLE IF EXISTS dbo.FrameworkElements;
    DROP TABLE IF EXISTS dbo.FrameworkVersions;
    DROP TABLE IF EXISTS dbo.Frameworks;

    DELETE FROM dbo.PrincipleRelations
    WHERE FromPrincipleId IN (
        SELECT PrincipleId FROM dbo.Principles
        WHERE PrincipleCode IN ('org-capability-readiness', 'org-strategy-applies-capability')
    ) OR ToPrincipleId IN (
        SELECT PrincipleId FROM dbo.Principles
        WHERE PrincipleCode IN ('org-capability-readiness', 'org-strategy-applies-capability')
    );

    DELETE FROM dbo.PolicyPrincipleLinks
    WHERE PrincipleId IN (
        SELECT PrincipleId FROM dbo.Principles
        WHERE PrincipleCode IN ('org-capability-readiness', 'org-strategy-applies-capability')
    );

    DELETE FROM dbo.Principles
    WHERE PrincipleCode IN ('org-capability-readiness', 'org-strategy-applies-capability');

    IF COL_LENGTH('dbo.Principles', 'IsImmutable') IS NOT NULL
    BEGIN
        ALTER TABLE dbo.Principles DROP CONSTRAINT DF_Principles_IsImmutable;
        ALTER TABLE dbo.Principles DROP COLUMN IsImmutable;
    END;

    IF COL_LENGTH('dbo.Principles', 'ShortStatementText') IS NOT NULL
        ALTER TABLE dbo.Principles DROP COLUMN ShortStatementText;

    DELETE FROM dbo.SchemaMigrations
    WHERE MigrationId = '034_mandate_capability_strategy_framework';

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
