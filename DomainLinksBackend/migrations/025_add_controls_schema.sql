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

IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigrations WHERE MigrationId = '025_add_controls_schema')
BEGIN
    BEGIN TRANSACTION;

    BEGIN TRY
        IF OBJECT_ID('dbo.ControlTypes', 'U') IS NULL
        BEGIN
            CREATE TABLE dbo.ControlTypes (
                ID                    INT IDENTITY(1,1) NOT NULL,
                CODE                  NVARCHAR(50) NOT NULL,
                NAME                  NVARCHAR(100) NOT NULL,
                DESCRIPTION           NVARCHAR(500) NULL,
                DISPLAY_ORDER         INT NOT NULL,
                EFFECTIVE_START_DATE  DATE NULL,
                EFFECTIVE_END_DATE    DATE NULL,
                CREATED_DATE          DATETIME2(3) NOT NULL
                    CONSTRAINT DF_ControlTypes_CREATED_DATE DEFAULT (SYSUTCDATETIME()),
                CREATED_BY            NVARCHAR(128) NOT NULL
                    CONSTRAINT DF_ControlTypes_CREATED_BY DEFAULT (SUSER_SNAME()),
                UPDATED_DATE          DATETIME2(3) NULL,
                UPDATED_BY            NVARCHAR(128) NULL,
                CONSTRAINT PK_ControlTypes PRIMARY KEY CLUSTERED (ID),
                CONSTRAINT UQ_ControlTypes_CODE UNIQUE (CODE)
            );
        END;

        MERGE dbo.ControlTypes AS target
        USING (VALUES
            ('PREVENTIVE', 'Preventive Control', 'Prevents an undesirable event from occurring.', 10),
            ('DETECTIVE', 'Detective Control', 'Identifies an undesirable event that has occurred.', 20),
            ('CORRECTIVE', 'Corrective Control', 'Restores or corrects conditions after an issue is detected.', 30),
            ('DIRECTIVE', 'Directive Control', 'Establishes required behaviour, expectations, or rules.', 40),
            ('COMPENSATING', 'Compensating Control', 'Alternative control used when the preferred control cannot be implemented.', 50),
            ('DETERRENT', 'Deterrent Control', 'Discourages undesirable behaviour through awareness or visibility.', 60)
        ) AS source (CODE, NAME, DESCRIPTION, DISPLAY_ORDER)
            ON target.CODE = source.CODE
        WHEN MATCHED THEN
            UPDATE SET
                NAME = source.NAME,
                DESCRIPTION = source.DESCRIPTION,
                DISPLAY_ORDER = source.DISPLAY_ORDER,
                UPDATED_DATE = SYSUTCDATETIME(),
                UPDATED_BY = SUSER_SNAME()
        WHEN NOT MATCHED THEN
            INSERT (
                CODE,
                NAME,
                DESCRIPTION,
                DISPLAY_ORDER,
                EFFECTIVE_START_DATE
            )
            VALUES (
                source.CODE,
                source.NAME,
                source.DESCRIPTION,
                source.DISPLAY_ORDER,
                CAST(SYSUTCDATETIME() AS DATE)
            );

        IF OBJECT_ID('dbo.Controls', 'U') IS NULL
        BEGIN
            CREATE TABLE dbo.Controls (
                ControlId             UNIQUEIDENTIFIER NOT NULL
                    CONSTRAINT DF_Controls_ControlId DEFAULT (NEWSEQUENTIALID()),
                ControlTypeId         INT NOT NULL,
                ControlCode           NVARCHAR(100) NOT NULL,
                DisplayName           NVARCHAR(255) NOT NULL,
                Description           NVARCHAR(MAX) NULL,
                ControlObjective      NVARCHAR(MAX) NULL,
                Owner                 NVARCHAR(255) NULL,
                EvidenceExpectation   NVARCHAR(MAX) NULL,
                Status                NVARCHAR(30) NOT NULL
                    CONSTRAINT DF_Controls_Status DEFAULT ('Draft'),
                CreatedAtUtc          DATETIME2(3) NOT NULL
                    CONSTRAINT DF_Controls_CreatedAtUtc DEFAULT (SYSUTCDATETIME()),
                UpdatedAtUtc          DATETIME2(3) NOT NULL
                    CONSTRAINT DF_Controls_UpdatedAtUtc DEFAULT (SYSUTCDATETIME()),
                CONSTRAINT PK_Controls PRIMARY KEY (ControlId),
                CONSTRAINT UQ_Controls_ControlCode UNIQUE (ControlCode),
                CONSTRAINT FK_Controls_ControlTypes
                    FOREIGN KEY (ControlTypeId) REFERENCES dbo.ControlTypes(ID),
                CONSTRAINT CK_Controls_Status
                    CHECK (Status IN ('Draft', 'Active', 'Retired', 'Archived'))
            );

            CREATE INDEX IX_Controls_ControlTypeId
                ON dbo.Controls(ControlTypeId);
            CREATE INDEX IX_Controls_Status_DisplayName
                ON dbo.Controls(Status, DisplayName);
        END;

        IF OBJECT_ID('dbo.DomainControls', 'U') IS NULL
        BEGIN
            CREATE TABLE dbo.DomainControls (
                DomainControlId       UNIQUEIDENTIFIER NOT NULL
                    CONSTRAINT DF_DomainControls_DomainControlId DEFAULT (NEWSEQUENTIALID()),
                DomainId              UNIQUEIDENTIFIER NOT NULL,
                ControlId             UNIQUEIDENTIFIER NOT NULL,
                RelationshipType      NVARCHAR(40) NOT NULL
                    CONSTRAINT DF_DomainControls_RelationshipType DEFAULT ('Primary'),
                IsPrimary             BIT NOT NULL
                    CONSTRAINT DF_DomainControls_IsPrimary DEFAULT (0),
                DisplayOrder          INT NOT NULL
                    CONSTRAINT DF_DomainControls_DisplayOrder DEFAULT (0),
                CreatedAtUtc          DATETIME2(3) NOT NULL
                    CONSTRAINT DF_DomainControls_CreatedAtUtc DEFAULT (SYSUTCDATETIME()),
                UpdatedAtUtc          DATETIME2(3) NOT NULL
                    CONSTRAINT DF_DomainControls_UpdatedAtUtc DEFAULT (SYSUTCDATETIME()),
                CONSTRAINT PK_DomainControls PRIMARY KEY (DomainControlId),
                CONSTRAINT UQ_DomainControls_Domain_Control UNIQUE (DomainId, ControlId),
                CONSTRAINT FK_DomainControls_Domains
                    FOREIGN KEY (DomainId) REFERENCES dbo.Domains(DomainId),
                CONSTRAINT FK_DomainControls_Controls
                    FOREIGN KEY (ControlId) REFERENCES dbo.Controls(ControlId),
                CONSTRAINT CK_DomainControls_RelationshipType
                    CHECK (RelationshipType IN ('Primary', 'Supports', 'AppliesTo', 'EvidenceFor', 'InheritedFrom', 'Related'))
            );

            CREATE INDEX IX_DomainControls_DomainId_DisplayOrder
                ON dbo.DomainControls(DomainId, DisplayOrder);
            CREATE INDEX IX_DomainControls_ControlId
                ON dbo.DomainControls(ControlId);
            CREATE UNIQUE INDEX UX_DomainControls_Control_Primary
                ON dbo.DomainControls(ControlId)
                WHERE IsPrimary = 1;
        END;

        CREATE TABLE #ControlSeed (
            DomainCode            NVARCHAR(100) NOT NULL,
            ControlTypeCode       NVARCHAR(50) NOT NULL,
            ControlCode           NVARCHAR(100) NOT NULL,
            DisplayName           NVARCHAR(255) NOT NULL,
            Description           NVARCHAR(MAX) NULL,
            ControlObjective      NVARCHAR(MAX) NULL,
            EvidenceExpectation   NVARCHAR(MAX) NULL,
            DisplayOrder          INT NOT NULL
        );

        INSERT INTO #ControlSeed (
            DomainCode,
            ControlTypeCode,
            ControlCode,
            DisplayName,
            Description,
            ControlObjective,
            EvidenceExpectation,
            DisplayOrder
        )
        VALUES
            ('information-technology-management', 'PREVENTIVE', 'it-mfa', 'MFA', 'Multi-factor authentication requirement for user access.', 'Prevent unauthorized access by requiring more than one authentication factor.', 'MFA policy, configuration screenshots, conditional access rules, or user enrollment reports.', 10),
            ('information-technology-management', 'DETECTIVE', 'it-security-monitoring', 'Security Monitoring', 'Monitoring of security events, alerts, and suspicious activity.', 'Identify security events that have occurred so they can be triaged and responded to.', 'Security alert logs, monitoring dashboards, incident tickets, or review records.', 20),
            ('information-technology-management', 'CORRECTIVE', 'it-backup-recovery', 'Backup Recovery', 'Backup and recovery process for restoring systems or data after disruption.', 'Restore systems, files, or data after an issue, loss, or outage is detected.', 'Backup job logs, restore test records, recovery procedures, or incident recovery notes.', 30),
            ('human-resource-management', 'DIRECTIVE', 'hr-code-of-conduct', 'Code of Conduct', 'Documented conduct expectations for employees and representatives.', 'Establish required behaviour, expectations, and rules for workplace conduct.', 'Approved code of conduct, employee acknowledgements, or communication records.', 10),
            ('human-resource-management', 'DETECTIVE', 'hr-performance-reviews', 'Performance Reviews', 'Formal performance review process for employee feedback and accountability.', 'Identify performance issues, gaps, or achievements through scheduled review activity.', 'Completed performance reviews, review schedules, manager sign-offs, or follow-up notes.', 20),
            ('human-resource-management', 'CORRECTIVE', 'hr-training-plans', 'Training Plans', 'Training plans used to address role, performance, or competency gaps.', 'Correct identified skill or performance gaps through targeted training and development.', 'Training plans, attendance records, completion certificates, or competency updates.', 30),
            ('financial-management', 'PREVENTIVE', 'fin-spending-limits', 'Spending Limits', 'Defined spending limits and delegated financial authorities.', 'Prevent unauthorized or excessive spending before commitments are made.', 'Delegation matrix, approval limits, purchase approvals, or system rule configuration.', 10),
            ('financial-management', 'DETECTIVE', 'fin-financial-reconciliation', 'Financial Reconciliation', 'Reconciliation of financial records, accounts, or transactions.', 'Identify errors, omissions, or irregularities after financial activity has occurred.', 'Reconciliation workpapers, variance notes, reviewer sign-off, or exception logs.', 20),
            ('financial-management', 'CORRECTIVE', 'fin-budget-adjustments', 'Budget Adjustments', 'Budget adjustment process used to correct approved allocations or forecasts.', 'Correct budget positions after variances, errors, or changed conditions are identified.', 'Budget adjustment forms, approval records, revised budgets, or variance explanations.', 30),
            ('records-management', 'DIRECTIVE', 'rec-retention-schedule', 'Retention Schedule', 'Approved records retention and disposition schedule.', 'Establish required retention, disposition, and custody rules for records.', 'Retention schedule, approval record, classification guidance, or disposition authority.', 10),
            ('records-management', 'DETECTIVE', 'rec-records-audit', 'Records Audit', 'Audit or review of records filing, retention, completeness, or custody.', 'Identify records issues, gaps, misclassification, or non-compliance after they occur.', 'Audit checklist, findings report, sample results, or corrective action log.', 20),
            ('records-management', 'CORRECTIVE', 'rec-file-reconstruction', 'File Reconstruction', 'Reconstruction of missing, incomplete, damaged, or inaccessible files.', 'Restore records after gaps, loss, or quality issues are identified.', 'Reconstructed file index, recovery notes, source evidence, or approval of reconstructed records.', 30);

        MERGE dbo.Controls AS target
        USING (
            SELECT
                ct.ID AS ControlTypeId,
                seed.ControlCode,
                seed.DisplayName,
                seed.Description,
                seed.ControlObjective,
                seed.EvidenceExpectation
            FROM #ControlSeed seed
            INNER JOIN dbo.ControlTypes ct
                ON ct.CODE = seed.ControlTypeCode
        ) AS source
            ON target.ControlCode = source.ControlCode
        WHEN MATCHED THEN
            UPDATE SET
                ControlTypeId = source.ControlTypeId,
                DisplayName = source.DisplayName,
                Description = source.Description,
                ControlObjective = source.ControlObjective,
                EvidenceExpectation = source.EvidenceExpectation,
                Status = 'Active',
                UpdatedAtUtc = SYSUTCDATETIME()
        WHEN NOT MATCHED THEN
            INSERT (
                ControlTypeId,
                ControlCode,
                DisplayName,
                Description,
                ControlObjective,
                EvidenceExpectation,
                Status
            )
            VALUES (
                source.ControlTypeId,
                source.ControlCode,
                source.DisplayName,
                source.Description,
                source.ControlObjective,
                source.EvidenceExpectation,
                'Active'
            );

        INSERT INTO dbo.DomainControls (
            DomainId,
            ControlId,
            RelationshipType,
            IsPrimary,
            DisplayOrder
        )
        SELECT
            domain.DomainId,
            control.ControlId,
            'Primary',
            1,
            seed.DisplayOrder
        FROM #ControlSeed seed
        INNER JOIN dbo.Domains domain
            ON domain.DomainCode = seed.DomainCode
        INNER JOIN dbo.Controls control
            ON control.ControlCode = seed.ControlCode
        WHERE NOT EXISTS (
            SELECT 1
            FROM dbo.DomainControls existing
            WHERE existing.DomainId = domain.DomainId
              AND existing.ControlId = control.ControlId
        );

        INSERT INTO dbo.SchemaMigrations (MigrationId)
        VALUES ('025_add_controls_schema');

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
