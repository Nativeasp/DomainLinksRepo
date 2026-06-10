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

IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigrations WHERE MigrationId = '027_add_policy_schema')
BEGIN
    BEGIN TRANSACTION;

    BEGIN TRY
        IF OBJECT_ID('dbo.PolicyTemplates', 'U') IS NULL
        BEGIN
            CREATE TABLE dbo.PolicyTemplates (
                PolicyTemplateId      UNIQUEIDENTIFIER NOT NULL
                    CONSTRAINT DF_PolicyTemplates_PolicyTemplateId DEFAULT (NEWSEQUENTIALID()),
                TemplateCode          NVARCHAR(100) NOT NULL,
                TemplateName          NVARCHAR(255) NOT NULL,
                VersionText           NVARCHAR(50) NOT NULL,
                TemplateBody          NVARCHAR(MAX) NULL,
                SourcePath            NVARCHAR(260) NULL,
                Status                NVARCHAR(30) NOT NULL
                    CONSTRAINT DF_PolicyTemplates_Status DEFAULT ('Draft'),
                CreatedAtUtc          DATETIME2(3) NOT NULL
                    CONSTRAINT DF_PolicyTemplates_CreatedAtUtc DEFAULT (SYSUTCDATETIME()),
                UpdatedAtUtc          DATETIME2(3) NOT NULL
                    CONSTRAINT DF_PolicyTemplates_UpdatedAtUtc DEFAULT (SYSUTCDATETIME()),
                CONSTRAINT PK_PolicyTemplates PRIMARY KEY (PolicyTemplateId),
                CONSTRAINT UQ_PolicyTemplates_TemplateCode UNIQUE (TemplateCode),
                CONSTRAINT CK_PolicyTemplates_Status
                    CHECK (Status IN ('Draft', 'Active', 'Retired', 'Archived'))
            );

            CREATE INDEX IX_PolicyTemplates_Status_TemplateName
                ON dbo.PolicyTemplates(Status, TemplateName);
        END;

        IF OBJECT_ID('dbo.Policies', 'U') IS NULL
        BEGIN
            CREATE TABLE dbo.Policies (
                PolicyId              UNIQUEIDENTIFIER NOT NULL
                    CONSTRAINT DF_Policies_PolicyId DEFAULT (NEWSEQUENTIALID()),
                RootDomainId          UNIQUEIDENTIFIER NOT NULL,
                PolicyTemplateId      UNIQUEIDENTIFIER NULL,
                PolicyCode            NVARCHAR(100) NOT NULL,
                PolicyTitle           NVARCHAR(255) NOT NULL,
                VersionText           NVARCHAR(50) NOT NULL,
                Status                NVARCHAR(30) NOT NULL
                    CONSTRAINT DF_Policies_Status DEFAULT ('Draft'),
                TemplatePath          NVARCHAR(260) NULL,
                SummaryText           NVARCHAR(MAX) NULL,
                SourceModelName       NVARCHAR(100) NULL,
                PublishedAtUtc        DATETIME2(3) NULL,
                PublishedBy           NVARCHAR(128) NULL,
                CreatedAtUtc          DATETIME2(3) NOT NULL
                    CONSTRAINT DF_Policies_CreatedAtUtc DEFAULT (SYSUTCDATETIME()),
                CreatedBy             NVARCHAR(128) NOT NULL
                    CONSTRAINT DF_Policies_CreatedBy DEFAULT (SUSER_SNAME()),
                UpdatedAtUtc          DATETIME2(3) NOT NULL
                    CONSTRAINT DF_Policies_UpdatedAtUtc DEFAULT (SYSUTCDATETIME()),
                UpdatedBy             NVARCHAR(128) NULL,
                CONSTRAINT PK_Policies PRIMARY KEY (PolicyId),
                CONSTRAINT UQ_Policies_PolicyCode UNIQUE (PolicyCode),
                CONSTRAINT FK_Policies_Domains
                    FOREIGN KEY (RootDomainId) REFERENCES dbo.Domains(DomainId),
                CONSTRAINT FK_Policies_PolicyTemplates
                    FOREIGN KEY (PolicyTemplateId) REFERENCES dbo.PolicyTemplates(PolicyTemplateId),
                CONSTRAINT CK_Policies_Status
                    CHECK (Status IN ('Draft', 'Active', 'Retired', 'Archived'))
            );

            CREATE INDEX IX_Policies_RootDomainId_Status
                ON dbo.Policies(RootDomainId, Status);
            CREATE INDEX IX_Policies_Status_PolicyTitle
                ON dbo.Policies(Status, PolicyTitle);
        END;

        IF OBJECT_ID('dbo.PolicySections', 'U') IS NULL
        BEGIN
            CREATE TABLE dbo.PolicySections (
                PolicySectionId       UNIQUEIDENTIFIER NOT NULL
                    CONSTRAINT DF_PolicySections_PolicySectionId DEFAULT (NEWSEQUENTIALID()),
                PolicyId              UNIQUEIDENTIFIER NOT NULL,
                SectionCode           NVARCHAR(50) NOT NULL,
                SectionName           NVARCHAR(100) NOT NULL,
                DisplayOrder          INT NOT NULL
                    CONSTRAINT DF_PolicySections_DisplayOrder DEFAULT (0),
                Status                NVARCHAR(30) NOT NULL
                    CONSTRAINT DF_PolicySections_Status DEFAULT ('Draft'),
                CreatedAtUtc          DATETIME2(3) NOT NULL
                    CONSTRAINT DF_PolicySections_CreatedAtUtc DEFAULT (SYSUTCDATETIME()),
                UpdatedAtUtc          DATETIME2(3) NOT NULL
                    CONSTRAINT DF_PolicySections_UpdatedAtUtc DEFAULT (SYSUTCDATETIME()),
                CONSTRAINT PK_PolicySections PRIMARY KEY (PolicySectionId),
                CONSTRAINT UQ_PolicySections_Policy_SectionCode UNIQUE (PolicyId, SectionCode),
                CONSTRAINT UQ_PolicySections_PolicySection_Policy UNIQUE (PolicySectionId, PolicyId),
                CONSTRAINT FK_PolicySections_Policies
                    FOREIGN KEY (PolicyId) REFERENCES dbo.Policies(PolicyId),
                CONSTRAINT CK_PolicySections_SectionCode
                    CHECK (SectionCode IN ('OBJECTIVE', 'PRINCIPLE', 'ACCOUNTABILITY', 'TRANSPARENCY', 'STRATEGY', 'CONTROL_POLICY', 'CONSEQUENCE')),
                CONSTRAINT CK_PolicySections_Status
                    CHECK (Status IN ('Draft', 'Active', 'Retired', 'Archived'))
            );

            CREATE INDEX IX_PolicySections_PolicyId_DisplayOrder
                ON dbo.PolicySections(PolicyId, DisplayOrder);
        END;

        IF OBJECT_ID('dbo.PolicyObjectives', 'U') IS NULL
        BEGIN
            CREATE TABLE dbo.PolicyObjectives (
                PolicyObjectiveId     UNIQUEIDENTIFIER NOT NULL
                    CONSTRAINT DF_PolicyObjectives_PolicyObjectiveId DEFAULT (NEWSEQUENTIALID()),
                PolicySectionId       UNIQUEIDENTIFIER NOT NULL,
                StatementText         NVARCHAR(MAX) NOT NULL,
                DisplayOrder          INT NOT NULL
                    CONSTRAINT DF_PolicyObjectives_DisplayOrder DEFAULT (0),
                ReviewStatus          NVARCHAR(30) NOT NULL
                    CONSTRAINT DF_PolicyObjectives_ReviewStatus DEFAULT ('Pending'),
                CreatedAtUtc          DATETIME2(3) NOT NULL
                    CONSTRAINT DF_PolicyObjectives_CreatedAtUtc DEFAULT (SYSUTCDATETIME()),
                UpdatedAtUtc          DATETIME2(3) NOT NULL
                    CONSTRAINT DF_PolicyObjectives_UpdatedAtUtc DEFAULT (SYSUTCDATETIME()),
                CONSTRAINT PK_PolicyObjectives PRIMARY KEY (PolicyObjectiveId),
                CONSTRAINT FK_PolicyObjectives_PolicySections
                    FOREIGN KEY (PolicySectionId) REFERENCES dbo.PolicySections(PolicySectionId),
                CONSTRAINT CK_PolicyObjectives_ReviewStatus
                    CHECK (ReviewStatus IN ('Pending', 'Accepted', 'Rejected', 'Revised'))
            );

            CREATE INDEX IX_PolicyObjectives_PolicySectionId_DisplayOrder
                ON dbo.PolicyObjectives(PolicySectionId, DisplayOrder);
        END;

        IF OBJECT_ID('dbo.PolicyPrinciples', 'U') IS NULL
        BEGIN
            CREATE TABLE dbo.PolicyPrinciples (
                PolicyPrincipleId     UNIQUEIDENTIFIER NOT NULL
                    CONSTRAINT DF_PolicyPrinciples_PolicyPrincipleId DEFAULT (NEWSEQUENTIALID()),
                PolicySectionId       UNIQUEIDENTIFIER NOT NULL,
                StatementText         NVARCHAR(MAX) NOT NULL,
                DisplayOrder          INT NOT NULL
                    CONSTRAINT DF_PolicyPrinciples_DisplayOrder DEFAULT (0),
                ReviewStatus          NVARCHAR(30) NOT NULL
                    CONSTRAINT DF_PolicyPrinciples_ReviewStatus DEFAULT ('Pending'),
                CreatedAtUtc          DATETIME2(3) NOT NULL
                    CONSTRAINT DF_PolicyPrinciples_CreatedAtUtc DEFAULT (SYSUTCDATETIME()),
                UpdatedAtUtc          DATETIME2(3) NOT NULL
                    CONSTRAINT DF_PolicyPrinciples_UpdatedAtUtc DEFAULT (SYSUTCDATETIME()),
                CONSTRAINT PK_PolicyPrinciples PRIMARY KEY (PolicyPrincipleId),
                CONSTRAINT FK_PolicyPrinciples_PolicySections
                    FOREIGN KEY (PolicySectionId) REFERENCES dbo.PolicySections(PolicySectionId),
                CONSTRAINT CK_PolicyPrinciples_ReviewStatus
                    CHECK (ReviewStatus IN ('Pending', 'Accepted', 'Rejected', 'Revised'))
            );

            CREATE INDEX IX_PolicyPrinciples_PolicySectionId_DisplayOrder
                ON dbo.PolicyPrinciples(PolicySectionId, DisplayOrder);
        END;

        IF OBJECT_ID('dbo.PolicyAccountabilityStatements', 'U') IS NULL
        BEGIN
            CREATE TABLE dbo.PolicyAccountabilityStatements (
                PolicyAccountabilityStatementId UNIQUEIDENTIFIER NOT NULL
                    CONSTRAINT DF_PolicyAccountabilityStatements_Id DEFAULT (NEWSEQUENTIALID()),
                PolicySectionId       UNIQUEIDENTIFIER NOT NULL,
                StatementText         NVARCHAR(MAX) NOT NULL,
                DisplayOrder          INT NOT NULL
                    CONSTRAINT DF_PolicyAccountabilityStatements_DisplayOrder DEFAULT (0),
                ReviewStatus          NVARCHAR(30) NOT NULL
                    CONSTRAINT DF_PolicyAccountabilityStatements_ReviewStatus DEFAULT ('Pending'),
                CreatedAtUtc          DATETIME2(3) NOT NULL
                    CONSTRAINT DF_PolicyAccountabilityStatements_CreatedAtUtc DEFAULT (SYSUTCDATETIME()),
                UpdatedAtUtc          DATETIME2(3) NOT NULL
                    CONSTRAINT DF_PolicyAccountabilityStatements_UpdatedAtUtc DEFAULT (SYSUTCDATETIME()),
                CONSTRAINT PK_PolicyAccountabilityStatements PRIMARY KEY (PolicyAccountabilityStatementId),
                CONSTRAINT FK_PolicyAccountabilityStatements_PolicySections
                    FOREIGN KEY (PolicySectionId) REFERENCES dbo.PolicySections(PolicySectionId),
                CONSTRAINT CK_PolicyAccountabilityStatements_ReviewStatus
                    CHECK (ReviewStatus IN ('Pending', 'Accepted', 'Rejected', 'Revised'))
            );

            CREATE INDEX IX_PolicyAccountabilityStatements_PolicySectionId_DisplayOrder
                ON dbo.PolicyAccountabilityStatements(PolicySectionId, DisplayOrder);
        END;

        IF OBJECT_ID('dbo.PolicyTransparencyStatements', 'U') IS NULL
        BEGIN
            CREATE TABLE dbo.PolicyTransparencyStatements (
                PolicyTransparencyStatementId UNIQUEIDENTIFIER NOT NULL
                    CONSTRAINT DF_PolicyTransparencyStatements_Id DEFAULT (NEWSEQUENTIALID()),
                PolicySectionId       UNIQUEIDENTIFIER NOT NULL,
                StatementText         NVARCHAR(MAX) NOT NULL,
                DisplayOrder          INT NOT NULL
                    CONSTRAINT DF_PolicyTransparencyStatements_DisplayOrder DEFAULT (0),
                ReviewStatus          NVARCHAR(30) NOT NULL
                    CONSTRAINT DF_PolicyTransparencyStatements_ReviewStatus DEFAULT ('Pending'),
                CreatedAtUtc          DATETIME2(3) NOT NULL
                    CONSTRAINT DF_PolicyTransparencyStatements_CreatedAtUtc DEFAULT (SYSUTCDATETIME()),
                UpdatedAtUtc          DATETIME2(3) NOT NULL
                    CONSTRAINT DF_PolicyTransparencyStatements_UpdatedAtUtc DEFAULT (SYSUTCDATETIME()),
                CONSTRAINT PK_PolicyTransparencyStatements PRIMARY KEY (PolicyTransparencyStatementId),
                CONSTRAINT FK_PolicyTransparencyStatements_PolicySections
                    FOREIGN KEY (PolicySectionId) REFERENCES dbo.PolicySections(PolicySectionId),
                CONSTRAINT CK_PolicyTransparencyStatements_ReviewStatus
                    CHECK (ReviewStatus IN ('Pending', 'Accepted', 'Rejected', 'Revised'))
            );

            CREATE INDEX IX_PolicyTransparencyStatements_PolicySectionId_DisplayOrder
                ON dbo.PolicyTransparencyStatements(PolicySectionId, DisplayOrder);
        END;

        IF OBJECT_ID('dbo.PolicyStrategyStatements', 'U') IS NULL
        BEGIN
            CREATE TABLE dbo.PolicyStrategyStatements (
                PolicyStrategyStatementId UNIQUEIDENTIFIER NOT NULL
                    CONSTRAINT DF_PolicyStrategyStatements_Id DEFAULT (NEWSEQUENTIALID()),
                PolicySectionId       UNIQUEIDENTIFIER NOT NULL,
                StatementText         NVARCHAR(MAX) NOT NULL,
                DisplayOrder          INT NOT NULL
                    CONSTRAINT DF_PolicyStrategyStatements_DisplayOrder DEFAULT (0),
                ReviewStatus          NVARCHAR(30) NOT NULL
                    CONSTRAINT DF_PolicyStrategyStatements_ReviewStatus DEFAULT ('Pending'),
                CreatedAtUtc          DATETIME2(3) NOT NULL
                    CONSTRAINT DF_PolicyStrategyStatements_CreatedAtUtc DEFAULT (SYSUTCDATETIME()),
                UpdatedAtUtc          DATETIME2(3) NOT NULL
                    CONSTRAINT DF_PolicyStrategyStatements_UpdatedAtUtc DEFAULT (SYSUTCDATETIME()),
                CONSTRAINT PK_PolicyStrategyStatements PRIMARY KEY (PolicyStrategyStatementId),
                CONSTRAINT FK_PolicyStrategyStatements_PolicySections
                    FOREIGN KEY (PolicySectionId) REFERENCES dbo.PolicySections(PolicySectionId),
                CONSTRAINT CK_PolicyStrategyStatements_ReviewStatus
                    CHECK (ReviewStatus IN ('Pending', 'Accepted', 'Rejected', 'Revised'))
            );

            CREATE INDEX IX_PolicyStrategyStatements_PolicySectionId_DisplayOrder
                ON dbo.PolicyStrategyStatements(PolicySectionId, DisplayOrder);
        END;

        IF OBJECT_ID('dbo.PolicyConsequences', 'U') IS NULL
        BEGIN
            CREATE TABLE dbo.PolicyConsequences (
                PolicyConsequenceId   UNIQUEIDENTIFIER NOT NULL
                    CONSTRAINT DF_PolicyConsequences_PolicyConsequenceId DEFAULT (NEWSEQUENTIALID()),
                PolicySectionId       UNIQUEIDENTIFIER NOT NULL,
                StatementText         NVARCHAR(MAX) NOT NULL,
                DisplayOrder          INT NOT NULL
                    CONSTRAINT DF_PolicyConsequences_DisplayOrder DEFAULT (0),
                ReviewStatus          NVARCHAR(30) NOT NULL
                    CONSTRAINT DF_PolicyConsequences_ReviewStatus DEFAULT ('Pending'),
                CreatedAtUtc          DATETIME2(3) NOT NULL
                    CONSTRAINT DF_PolicyConsequences_CreatedAtUtc DEFAULT (SYSUTCDATETIME()),
                UpdatedAtUtc          DATETIME2(3) NOT NULL
                    CONSTRAINT DF_PolicyConsequences_UpdatedAtUtc DEFAULT (SYSUTCDATETIME()),
                CONSTRAINT PK_PolicyConsequences PRIMARY KEY (PolicyConsequenceId),
                CONSTRAINT FK_PolicyConsequences_PolicySections
                    FOREIGN KEY (PolicySectionId) REFERENCES dbo.PolicySections(PolicySectionId),
                CONSTRAINT CK_PolicyConsequences_ReviewStatus
                    CHECK (ReviewStatus IN ('Pending', 'Accepted', 'Rejected', 'Revised'))
            );

            CREATE INDEX IX_PolicyConsequences_PolicySectionId_DisplayOrder
                ON dbo.PolicyConsequences(PolicySectionId, DisplayOrder);
        END;

        IF OBJECT_ID('dbo.Principles', 'U') IS NULL
        BEGIN
            CREATE TABLE dbo.Principles (
                PrincipleId           UNIQUEIDENTIFIER NOT NULL
                    CONSTRAINT DF_Principles_PrincipleId DEFAULT (NEWSEQUENTIALID()),
                OriginDomainId        UNIQUEIDENTIFIER NOT NULL,
                PrincipleCode         NVARCHAR(100) NOT NULL,
                Name                  NVARCHAR(200) NOT NULL,
                StatementText         NVARCHAR(MAX) NOT NULL,
                RationaleText         NVARCHAR(MAX) NULL,
                VisibilityScope       NVARCHAR(30) NOT NULL
                    CONSTRAINT DF_Principles_VisibilityScope DEFAULT ('Private'),
                LifecycleStatus       NVARCHAR(30) NOT NULL
                    CONSTRAINT DF_Principles_LifecycleStatus DEFAULT ('Draft'),
                OriginPrincipleId     UNIQUEIDENTIFIER NULL,
                PublishedAtUtc        DATETIME2(3) NULL,
                PublishedBy           NVARCHAR(128) NULL,
                CreatedAtUtc          DATETIME2(3) NOT NULL
                    CONSTRAINT DF_Principles_CreatedAtUtc DEFAULT (SYSUTCDATETIME()),
                CreatedBy             NVARCHAR(128) NOT NULL
                    CONSTRAINT DF_Principles_CreatedBy DEFAULT (SUSER_SNAME()),
                UpdatedAtUtc          DATETIME2(3) NOT NULL
                    CONSTRAINT DF_Principles_UpdatedAtUtc DEFAULT (SYSUTCDATETIME()),
                UpdatedBy             NVARCHAR(128) NULL,
                CONSTRAINT PK_Principles PRIMARY KEY (PrincipleId),
                CONSTRAINT UQ_Principles_PrincipleCode UNIQUE (PrincipleCode),
                CONSTRAINT FK_Principles_Domains
                    FOREIGN KEY (OriginDomainId) REFERENCES dbo.Domains(DomainId),
                CONSTRAINT FK_Principles_OriginPrinciples
                    FOREIGN KEY (OriginPrincipleId) REFERENCES dbo.Principles(PrincipleId),
                CONSTRAINT CK_Principles_VisibilityScope
                    CHECK (VisibilityScope IN ('Private', 'Organization')),
                CONSTRAINT CK_Principles_LifecycleStatus
                    CHECK (LifecycleStatus IN ('Draft', 'Active', 'Retired', 'Archived'))
            );

            CREATE INDEX IX_Principles_OriginDomainId
                ON dbo.Principles(OriginDomainId);
            CREATE INDEX IX_Principles_VisibilityScope_LifecycleStatus
                ON dbo.Principles(VisibilityScope, LifecycleStatus);
        END;

        IF OBJECT_ID('dbo.PolicyPrincipleLinks', 'U') IS NULL
        BEGIN
            CREATE TABLE dbo.PolicyPrincipleLinks (
                PolicyPrincipleLinkId UNIQUEIDENTIFIER NOT NULL
                    CONSTRAINT DF_PolicyPrincipleLinks_PolicyPrincipleLinkId DEFAULT (NEWSEQUENTIALID()),
                PolicyPrincipleId     UNIQUEIDENTIFIER NOT NULL,
                PrincipleId           UNIQUEIDENTIFIER NOT NULL,
                UsageMode             NVARCHAR(30) NOT NULL,
                AdoptedFromPrincipleId UNIQUEIDENTIFIER NULL,
                CreatedAtUtc          DATETIME2(3) NOT NULL
                    CONSTRAINT DF_PolicyPrincipleLinks_CreatedAtUtc DEFAULT (SYSUTCDATETIME()),
                UpdatedAtUtc          DATETIME2(3) NOT NULL
                    CONSTRAINT DF_PolicyPrincipleLinks_UpdatedAtUtc DEFAULT (SYSUTCDATETIME()),
                CONSTRAINT PK_PolicyPrincipleLinks PRIMARY KEY (PolicyPrincipleLinkId),
                CONSTRAINT UQ_PolicyPrincipleLinks_PolicyPrinciple_Principle UNIQUE (PolicyPrincipleId, PrincipleId),
                CONSTRAINT FK_PolicyPrincipleLinks_PolicyPrinciples
                    FOREIGN KEY (PolicyPrincipleId) REFERENCES dbo.PolicyPrinciples(PolicyPrincipleId),
                CONSTRAINT FK_PolicyPrincipleLinks_Principles
                    FOREIGN KEY (PrincipleId) REFERENCES dbo.Principles(PrincipleId),
                CONSTRAINT FK_PolicyPrincipleLinks_AdoptedFromPrinciples
                    FOREIGN KEY (AdoptedFromPrincipleId) REFERENCES dbo.Principles(PrincipleId),
                CONSTRAINT CK_PolicyPrincipleLinks_UsageMode
                    CHECK (UsageMode IN ('Reuse', 'Adopt', 'Derive', 'Override'))
            );

            CREATE INDEX IX_PolicyPrincipleLinks_PrincipleId
                ON dbo.PolicyPrincipleLinks(PrincipleId);
        END;

        IF OBJECT_ID('dbo.PrincipleRelations', 'U') IS NULL
        BEGIN
            CREATE TABLE dbo.PrincipleRelations (
                PrincipleRelationId   UNIQUEIDENTIFIER NOT NULL
                    CONSTRAINT DF_PrincipleRelations_PrincipleRelationId DEFAULT (NEWSEQUENTIALID()),
                FromPrincipleId       UNIQUEIDENTIFIER NOT NULL,
                ToPrincipleId         UNIQUEIDENTIFIER NOT NULL,
                RelationType          NVARCHAR(30) NOT NULL,
                Notes                 NVARCHAR(1000) NULL,
                CreatedAtUtc          DATETIME2(3) NOT NULL
                    CONSTRAINT DF_PrincipleRelations_CreatedAtUtc DEFAULT (SYSUTCDATETIME()),
                CreatedBy             NVARCHAR(128) NOT NULL
                    CONSTRAINT DF_PrincipleRelations_CreatedBy DEFAULT (SUSER_SNAME()),
                CONSTRAINT PK_PrincipleRelations PRIMARY KEY (PrincipleRelationId),
                CONSTRAINT UQ_PrincipleRelations_From_To_Type UNIQUE (FromPrincipleId, ToPrincipleId, RelationType),
                CONSTRAINT FK_PrincipleRelations_FromPrinciples
                    FOREIGN KEY (FromPrincipleId) REFERENCES dbo.Principles(PrincipleId),
                CONSTRAINT FK_PrincipleRelations_ToPrinciples
                    FOREIGN KEY (ToPrincipleId) REFERENCES dbo.Principles(PrincipleId),
                CONSTRAINT CK_PrincipleRelations_RelationType
                    CHECK (RelationType IN ('DERIVED_FROM', 'ADOPTED_FROM', 'INSPIRED_BY', 'REPLACES')),
                CONSTRAINT CK_PrincipleRelations_NotSelf
                    CHECK (FromPrincipleId <> ToPrincipleId)
            );

            CREATE INDEX IX_PrincipleRelations_ToPrincipleId
                ON dbo.PrincipleRelations(ToPrincipleId);
        END;

        IF OBJECT_ID('dbo.PolicyControlStatements', 'U') IS NULL
        BEGIN
            CREATE TABLE dbo.PolicyControlStatements (
                PolicyControlStatementId UNIQUEIDENTIFIER NOT NULL
                    CONSTRAINT DF_PolicyControlStatements_PolicyControlStatementId DEFAULT (NEWSEQUENTIALID()),
                PolicySectionId       UNIQUEIDENTIFIER NOT NULL,
                PolicyId              UNIQUEIDENTIFIER NOT NULL,
                ControlId             UNIQUEIDENTIFIER NOT NULL,
                StatementText         NVARCHAR(MAX) NOT NULL,
                DisplayOrder          INT NOT NULL
                    CONSTRAINT DF_PolicyControlStatements_DisplayOrder DEFAULT (0),
                ReviewStatus          NVARCHAR(30) NOT NULL
                    CONSTRAINT DF_PolicyControlStatements_ReviewStatus DEFAULT ('Pending'),
                CreatedAtUtc          DATETIME2(3) NOT NULL
                    CONSTRAINT DF_PolicyControlStatements_CreatedAtUtc DEFAULT (SYSUTCDATETIME()),
                UpdatedAtUtc          DATETIME2(3) NOT NULL
                    CONSTRAINT DF_PolicyControlStatements_UpdatedAtUtc DEFAULT (SYSUTCDATETIME()),
                CONSTRAINT PK_PolicyControlStatements PRIMARY KEY (PolicyControlStatementId),
                CONSTRAINT FK_PolicyControlStatements_PolicySections
                    FOREIGN KEY (PolicySectionId) REFERENCES dbo.PolicySections(PolicySectionId),
                CONSTRAINT FK_PolicyControlStatements_Policies
                    FOREIGN KEY (PolicyId) REFERENCES dbo.Policies(PolicyId),
                CONSTRAINT FK_PolicyControlStatements_Controls
                    FOREIGN KEY (ControlId) REFERENCES dbo.Controls(ControlId),
                CONSTRAINT FK_PolicyControlStatements_PolicySections_Policies
                    FOREIGN KEY (PolicySectionId, PolicyId) REFERENCES dbo.PolicySections(PolicySectionId, PolicyId),
                CONSTRAINT CK_PolicyControlStatements_ReviewStatus
                    CHECK (ReviewStatus IN ('Pending', 'Accepted', 'Rejected', 'Revised'))
            );

            CREATE INDEX IX_PolicyControlStatements_PolicyId_ControlId_DisplayOrder
                ON dbo.PolicyControlStatements(PolicyId, ControlId, DisplayOrder);
            CREATE INDEX IX_PolicyControlStatements_ControlId_PolicyId
                ON dbo.PolicyControlStatements(ControlId, PolicyId);
        END;

        INSERT INTO dbo.SchemaMigrations (MigrationId)
        VALUES ('027_add_policy_schema');

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
