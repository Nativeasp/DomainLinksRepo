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

IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigrations WHERE MigrationId = '024_add_capital_planning_investment_analysis')
BEGIN
    BEGIN TRANSACTION;

    BEGIN TRY
        DECLARE @CorporateTypeId INT =
            (SELECT ID FROM dbo.DomainTypes WHERE CODE = 'CORPORATE');
        DECLARE @FinancialManagementId UNIQUEIDENTIFIER =
            (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'financial-management');
        DECLARE @CapitalPlanningId UNIQUEIDENTIFIER =
            (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'capital-planning');

        IF @CorporateTypeId IS NULL
        BEGIN
            THROW 51000, 'Corporate domain type was not found.', 1;
        END;

        IF @FinancialManagementId IS NULL
        BEGIN
            THROW 51001, 'Financial Management domain was not found.', 1;
        END;

        IF @CapitalPlanningId IS NULL
        BEGIN
            INSERT INTO dbo.Domains (
                DomainParentId,
                DomainTypeId,
                DomainOrientationId,
                DisplayOrder,
                DomainCode,
                DisplayName,
                Description,
                Status
            )
            VALUES (
                @FinancialManagementId,
                @CorporateTypeId,
                NULL,
                214,
                'capital-planning',
                'Capital Planning',
                'Planning and prioritization of capital investments, projects, and funding decisions.',
                'Active'
            );

            SET @CapitalPlanningId =
                (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'capital-planning');
        END;

        IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'investment-analysis')
        BEGIN
            INSERT INTO dbo.Domains (
                DomainParentId,
                DomainTypeId,
                DomainOrientationId,
                DisplayOrder,
                DomainCode,
                DisplayName,
                Description,
                Status
            )
            VALUES (
                @CapitalPlanningId,
                @CorporateTypeId,
                NULL,
                215,
                'investment-analysis',
                'Investment Analysis',
                'Analysis of capital investment options and financial returns, including ROI, NPV, IRR, and Payback Period.',
                'Active'
            );
        END
        ELSE
        BEGIN
            UPDATE dbo.Domains
            SET
                DomainParentId = @CapitalPlanningId,
                DomainTypeId = @CorporateTypeId,
                DisplayOrder = 215,
                DisplayName = 'Investment Analysis',
                Description = 'Analysis of capital investment options and financial returns, including ROI, NPV, IRR, and Payback Period.',
                Status = 'Active',
                UpdatedAtUtc = SYSUTCDATETIME()
            WHERE DomainCode = 'investment-analysis';
        END;

        INSERT INTO dbo.SchemaMigrations (MigrationId)
        VALUES ('024_add_capital_planning_investment_analysis');

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
        BEGIN
            ROLLBACK TRANSACTION;
        END;

        THROW;
    END CATCH;
END;
GO
