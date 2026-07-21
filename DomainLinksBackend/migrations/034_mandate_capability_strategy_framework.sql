SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF NOT EXISTS (
    SELECT 1
    FROM dbo.SchemaMigrations
    WHERE MigrationId = '034_mandate_capability_strategy_framework'
)
BEGIN
    BEGIN TRANSACTION;
    BEGIN TRY
        IF COL_LENGTH('dbo.Principles', 'ShortStatementText') IS NULL
            ALTER TABLE dbo.Principles ADD ShortStatementText NVARCHAR(500) NULL;

        IF COL_LENGTH('dbo.Principles', 'IsImmutable') IS NULL
            ALTER TABLE dbo.Principles ADD IsImmutable BIT NOT NULL
                CONSTRAINT DF_Principles_IsImmutable DEFAULT (0);

        CREATE TABLE dbo.Frameworks (
            FrameworkId       UNIQUEIDENTIFIER NOT NULL
                CONSTRAINT DF_Frameworks_Id DEFAULT NEWSEQUENTIALID(),
            FrameworkCode     NVARCHAR(100) NOT NULL,
            FrameworkName     NVARCHAR(255) NOT NULL,
            LifecycleStatus   NVARCHAR(30) NOT NULL
                CONSTRAINT DF_Frameworks_Status DEFAULT ('Active'),
            CreatedAtUtc      DATETIME2(3) NOT NULL
                CONSTRAINT DF_Frameworks_CreatedAt DEFAULT SYSUTCDATETIME(),
            UpdatedAtUtc      DATETIME2(3) NOT NULL
                CONSTRAINT DF_Frameworks_UpdatedAt DEFAULT SYSUTCDATETIME(),
            CONSTRAINT PK_Frameworks PRIMARY KEY (FrameworkId),
            CONSTRAINT UQ_Frameworks_Code UNIQUE (FrameworkCode),
            CONSTRAINT CK_Frameworks_Status
                CHECK (LifecycleStatus IN ('Active', 'Retired', 'Archived'))
        );

        CREATE TABLE dbo.FrameworkVersions (
            FrameworkVersionId UNIQUEIDENTIFIER NOT NULL
                CONSTRAINT DF_FrameworkVersions_Id DEFAULT NEWSEQUENTIALID(),
            FrameworkId        UNIQUEIDENTIFIER NOT NULL,
            VersionText        NVARCHAR(30) NOT NULL,
            SummaryText        NVARCHAR(MAX) NOT NULL,
            VersionStatus      NVARCHAR(30) NOT NULL
                CONSTRAINT DF_FrameworkVersions_Status DEFAULT ('Draft'),
            BasedOnVersionId   UNIQUEIDENTIFIER NULL,
            PublishedAtUtc     DATETIME2(3) NULL,
            PublishedBy        NVARCHAR(128) NULL,
            CreatedAtUtc       DATETIME2(3) NOT NULL
                CONSTRAINT DF_FrameworkVersions_CreatedAt DEFAULT SYSUTCDATETIME(),
            CreatedBy          NVARCHAR(128) NOT NULL
                CONSTRAINT DF_FrameworkVersions_CreatedBy DEFAULT SUSER_SNAME(),
            CONSTRAINT PK_FrameworkVersions PRIMARY KEY (FrameworkVersionId),
            CONSTRAINT FK_FrameworkVersions_Framework
                FOREIGN KEY (FrameworkId) REFERENCES dbo.Frameworks(FrameworkId),
            CONSTRAINT FK_FrameworkVersions_BasedOn
                FOREIGN KEY (BasedOnVersionId)
                REFERENCES dbo.FrameworkVersions(FrameworkVersionId),
            CONSTRAINT UQ_FrameworkVersions_Version
                UNIQUE (FrameworkId, VersionText),
            CONSTRAINT CK_FrameworkVersions_Status
                CHECK (VersionStatus IN ('Draft', 'Published', 'Superseded', 'Archived')),
            CONSTRAINT CK_FrameworkVersions_Published
                CHECK (
                    (VersionStatus = 'Published' AND PublishedAtUtc IS NOT NULL)
                    OR VersionStatus <> 'Published'
                )
        );

        CREATE INDEX IX_FrameworkVersions_Framework_Status
            ON dbo.FrameworkVersions(FrameworkId, VersionStatus, PublishedAtUtc DESC);

        CREATE TABLE dbo.FrameworkElements (
            FrameworkElementId UNIQUEIDENTIFIER NOT NULL
                CONSTRAINT DF_FrameworkElements_Id DEFAULT NEWSEQUENTIALID(),
            FrameworkVersionId UNIQUEIDENTIFIER NOT NULL,
            ParentElementId    UNIQUEIDENTIFIER NULL,
            ElementCode        NVARCHAR(100) NOT NULL,
            ElementType        NVARCHAR(30) NOT NULL,
            ElementName        NVARCHAR(255) NOT NULL,
            StatementText      NVARCHAR(MAX) NULL,
            DefinitionText     NVARCHAR(MAX) NULL,
            IsFoundational     BIT NOT NULL
                CONSTRAINT DF_FrameworkElements_Foundational DEFAULT (0),
            DisplayOrder       INT NOT NULL
                CONSTRAINT DF_FrameworkElements_Order DEFAULT (0),
            CONSTRAINT PK_FrameworkElements PRIMARY KEY (FrameworkElementId),
            CONSTRAINT FK_FrameworkElements_Version
                FOREIGN KEY (FrameworkVersionId)
                REFERENCES dbo.FrameworkVersions(FrameworkVersionId),
            CONSTRAINT FK_FrameworkElements_Parent
                FOREIGN KEY (ParentElementId)
                REFERENCES dbo.FrameworkElements(FrameworkElementId),
            CONSTRAINT UQ_FrameworkElements_Code
                UNIQUE (FrameworkVersionId, ElementCode),
            CONSTRAINT CK_FrameworkElements_Type
                CHECK (ElementType IN
                    ('Mandate', 'Pillar', 'Principle', 'Concept', 'Definition'))
        );

        CREATE INDEX IX_FrameworkElements_Version_Order
            ON dbo.FrameworkElements(FrameworkVersionId, DisplayOrder);

        CREATE TABLE dbo.FrameworkElementRelations (
            FrameworkElementRelationId UNIQUEIDENTIFIER NOT NULL
                CONSTRAINT DF_FrameworkElementRelations_Id DEFAULT NEWSEQUENTIALID(),
            FromElementId UNIQUEIDENTIFIER NOT NULL,
            ToElementId   UNIQUEIDENTIFIER NOT NULL,
            RelationType  NVARCHAR(30) NOT NULL,
            RationaleText NVARCHAR(MAX) NULL,
            CONSTRAINT PK_FrameworkElementRelations
                PRIMARY KEY (FrameworkElementRelationId),
            CONSTRAINT FK_FrameworkElementRelations_From
                FOREIGN KEY (FromElementId)
                REFERENCES dbo.FrameworkElements(FrameworkElementId),
            CONSTRAINT FK_FrameworkElementRelations_To
                FOREIGN KEY (ToElementId)
                REFERENCES dbo.FrameworkElements(FrameworkElementId),
            CONSTRAINT UQ_FrameworkElementRelations
                UNIQUE (FromElementId, ToElementId, RelationType),
            CONSTRAINT CK_FrameworkElementRelations_Type
                CHECK (RelationType IN
                    ('Governs', 'Enables', 'Applies', 'Supports', 'Constrains')),
            CONSTRAINT CK_FrameworkElementRelations_NotSelf
                CHECK (FromElementId <> ToElementId)
        );

        CREATE TABLE dbo.FrameworkPrincipleLinks (
            FrameworkPrincipleLinkId UNIQUEIDENTIFIER NOT NULL
                CONSTRAINT DF_FrameworkPrincipleLinks_Id DEFAULT NEWSEQUENTIALID(),
            FrameworkElementId UNIQUEIDENTIFIER NOT NULL,
            PrincipleId        UNIQUEIDENTIFIER NOT NULL,
            RelationshipType   NVARCHAR(30) NOT NULL,
            ApplicabilityText  NVARCHAR(MAX) NULL,
            CONSTRAINT PK_FrameworkPrincipleLinks
                PRIMARY KEY (FrameworkPrincipleLinkId),
            CONSTRAINT FK_FrameworkPrincipleLinks_Element
                FOREIGN KEY (FrameworkElementId)
                REFERENCES dbo.FrameworkElements(FrameworkElementId),
            CONSTRAINT FK_FrameworkPrincipleLinks_Principle
                FOREIGN KEY (PrincipleId) REFERENCES dbo.Principles(PrincipleId),
            CONSTRAINT UQ_FrameworkPrincipleLinks
                UNIQUE (FrameworkElementId, PrincipleId),
            CONSTRAINT CK_FrameworkPrincipleLinks_Type
                CHECK (RelationshipType IN
                    ('Defines', 'Composes', 'Applies', 'Supports', 'Constrains'))
        );

        CREATE INDEX IX_FrameworkPrincipleLinks_Principle
            ON dbo.FrameworkPrincipleLinks(PrincipleId);

        CREATE TABLE dbo.FrameworkArtifactLinks (
            FrameworkArtifactLinkId UNIQUEIDENTIFIER NOT NULL
                CONSTRAINT DF_FrameworkArtifactLinks_Id DEFAULT NEWSEQUENTIALID(),
            FrameworkElementId  UNIQUEIDENTIFIER NOT NULL,
            SemanticArtifactId UNIQUEIDENTIFIER NOT NULL,
            RelationshipType   NVARCHAR(30) NOT NULL,
            RelevanceWeight    DECIMAL(5,4) NULL,
            CreatedAtUtc       DATETIME2(3) NOT NULL
                CONSTRAINT DF_FrameworkArtifactLinks_CreatedAt DEFAULT SYSUTCDATETIME(),
            CONSTRAINT PK_FrameworkArtifactLinks
                PRIMARY KEY (FrameworkArtifactLinkId),
            CONSTRAINT FK_FrameworkArtifactLinks_Element
                FOREIGN KEY (FrameworkElementId)
                REFERENCES dbo.FrameworkElements(FrameworkElementId),
            CONSTRAINT FK_FrameworkArtifactLinks_Artifact
                FOREIGN KEY (SemanticArtifactId)
                REFERENCES dbo.SemanticArtifacts(SemanticArtifactId),
            CONSTRAINT UQ_FrameworkArtifactLinks
                UNIQUE (FrameworkElementId, SemanticArtifactId, RelationshipType),
            CONSTRAINT CK_FrameworkArtifactLinks_Type
                CHECK (RelationshipType IN
                    ('Authorizes', 'Implements', 'Applies', 'Supports', 'Constrains', 'Evidences')),
            CONSTRAINT CK_FrameworkArtifactLinks_Weight
                CHECK (RelevanceWeight IS NULL
                    OR RelevanceWeight BETWEEN 0.0000 AND 1.0000)
        );

        CREATE INDEX IX_FrameworkArtifactLinks_Artifact
            ON dbo.FrameworkArtifactLinks(SemanticArtifactId);

        CREATE TABLE dbo.FrameworkContextRules (
            FrameworkContextRuleId UNIQUEIDENTIFIER NOT NULL
                CONSTRAINT DF_FrameworkContextRules_Id DEFAULT NEWSEQUENTIALID(),
            FrameworkElementId UNIQUEIDENTIFIER NOT NULL,
            ArtifactType       NVARCHAR(50) NULL,
            ActivityType       NVARCHAR(50) NOT NULL,
            TriggerStage       NVARCHAR(50) NOT NULL,
            DeliveryMode       NVARCHAR(20) NOT NULL,
            IsRequired         BIT NOT NULL
                CONSTRAINT DF_FrameworkContextRules_Required DEFAULT (0),
            Priority           INT NOT NULL
                CONSTRAINT DF_FrameworkContextRules_Priority DEFAULT (100),
            InstructionText    NVARCHAR(MAX) NULL,
            CONSTRAINT PK_FrameworkContextRules PRIMARY KEY (FrameworkContextRuleId),
            CONSTRAINT FK_FrameworkContextRules_Element
                FOREIGN KEY (FrameworkElementId)
                REFERENCES dbo.FrameworkElements(FrameworkElementId),
            CONSTRAINT UQ_FrameworkContextRules
                UNIQUE (FrameworkElementId, ArtifactType, ActivityType, TriggerStage),
            CONSTRAINT CK_FrameworkContextRules_Delivery
                CHECK (DeliveryMode IN ('AIContext', 'UserGuidance', 'Both')),
            CONSTRAINT CK_FrameworkContextRules_Priority
                CHECK (Priority BETWEEN 0 AND 1000)
        );

        DECLARE @OriginDomainId UNIQUEIDENTIFIER = (
            SELECT TOP (1) DomainId
            FROM dbo.Domains
            WHERE DomainCode = 'strategic-planning-governance'
        );

        IF @OriginDomainId IS NULL
            THROW 51000, 'The strategic-planning-governance domain is required to seed the framework.', 1;

        DECLARE @CapabilityPrincipleId UNIQUEIDENTIFIER = NEWID();
        DECLARE @StrategyPrincipleId UNIQUEIDENTIFIER = NEWID();

        EXEC sys.sp_executesql N'
            INSERT INTO dbo.Principles (
                PrincipleId, OriginDomainId, PrincipleCode, Name, StatementText,
                ShortStatementText, RationaleText, VisibilityScope, LifecycleStatus,
                IsImmutable, PublishedAtUtc, PublishedBy
            )
            VALUES
            (
                @CapabilityPrincipleId,
                @OriginDomainId,
                ''org-capability-readiness'',
                ''Capability Defines Readiness'',
                ''Capability defines the organization''''s state of readiness: its people, systems, processes, tools, controls, resources, and structure.'',
                ''Capability defines the state of readiness.'',
                ''Organizational responsibility must be matched by the capability required to discharge it.'',
                ''Organization'', ''Active'', 1, SYSUTCDATETIME(), SUSER_SNAME()
            ),
            (
                @StrategyPrincipleId,
                @OriginDomainId,
                ''org-strategy-applies-capability'',
                ''Strategy Applies Capability'',
                ''Strategy determines where the organization is going and how that capability will be applied.'',
                ''Strategy puts it to work.'',
                ''Organizational capability produces value when directed toward an agreed purpose and goals.'',
                ''Organization'', ''Active'', 1, SYSUTCDATETIME(), SUSER_SNAME()
            );',
            N'@CapabilityPrincipleId UNIQUEIDENTIFIER, @StrategyPrincipleId UNIQUEIDENTIFIER, @OriginDomainId UNIQUEIDENTIFIER',
            @CapabilityPrincipleId,
            @StrategyPrincipleId,
            @OriginDomainId;

        DECLARE @FrameworkId UNIQUEIDENTIFIER = NEWID();
        DECLARE @FrameworkVersionId UNIQUEIDENTIFIER = NEWID();
        DECLARE @MandateElementId UNIQUEIDENTIFIER = NEWID();
        DECLARE @CapabilityElementId UNIQUEIDENTIFIER = NEWID();
        DECLARE @StrategyElementId UNIQUEIDENTIFIER = NEWID();
        DECLARE @GoverningPrincipleElementId UNIQUEIDENTIFIER = NEWID();

        INSERT INTO dbo.Frameworks (
            FrameworkId, FrameworkCode, FrameworkName, LifecycleStatus
        )
        VALUES (
            @FrameworkId,
            'MANDATE-CAPABILITY-STRATEGY',
            'Mandate-Capability-Strategy Framework',
            'Active'
        );

        INSERT INTO dbo.FrameworkVersions (
            FrameworkVersionId, FrameworkId, VersionText, SummaryText,
            VersionStatus, PublishedAtUtc, PublishedBy
        )
        VALUES (
            @FrameworkVersionId,
            @FrameworkId,
            '1.0',
            'Mandate provides purpose, authority, and scope. Capability establishes readiness. Strategy directs how that capability is applied.',
            'Published', SYSUTCDATETIME(), SUSER_SNAME()
        );

        INSERT INTO dbo.FrameworkElements (
            FrameworkElementId, FrameworkVersionId, ParentElementId,
            ElementCode, ElementType, ElementName, StatementText,
            DefinitionText, IsFoundational, DisplayOrder
        )
        VALUES
        (
            @MandateElementId, @FrameworkVersionId, NULL,
            'MANDATE', 'Mandate', 'Mandate',
            'Mandate sits above Capability and Strategy, giving them purpose, authority, and scope.',
            'The authorizing expression of organizational purpose, authority, responsibility, and scope.',
            1, 10
        ),
        (
            @CapabilityElementId, @FrameworkVersionId, @MandateElementId,
            'CAPABILITY', 'Pillar', 'Capability', NULL,
            NULL, 1, 20
        ),
        (
            @StrategyElementId, @FrameworkVersionId, @MandateElementId,
            'STRATEGY', 'Pillar', 'Strategy', NULL,
            NULL, 1, 30
        ),
        (
            @GoverningPrincipleElementId, @FrameworkVersionId, @MandateElementId,
            'CAPABILITY-STRATEGY-PRINCIPLE', 'Principle',
            'Capability and Strategy Governing Principle', NULL,
            'Capability establishes readiness; Strategy directs its application.',
            1, 40
        );

        INSERT INTO dbo.FrameworkElementRelations (
            FromElementId, ToElementId, RelationType, RationaleText
        )
        VALUES
        (@MandateElementId, @CapabilityElementId, 'Governs', 'Mandate gives Capability its purpose, authority, and scope.'),
        (@MandateElementId, @StrategyElementId, 'Governs', 'Mandate gives Strategy its purpose, authority, and scope.'),
        (@CapabilityElementId, @StrategyElementId, 'Enables', 'Available capability enables execution of strategy.');

        INSERT INTO dbo.FrameworkPrincipleLinks (
            FrameworkElementId, PrincipleId, RelationshipType, ApplicabilityText
        )
        VALUES
        (@CapabilityElementId, @CapabilityPrincipleId, 'Defines', 'Defines the Capability pillar.'),
        (@StrategyElementId, @StrategyPrincipleId, 'Defines', 'Defines the Strategy pillar.'),
        (@GoverningPrincipleElementId, @CapabilityPrincipleId, 'Composes', 'First clause of the governing principle.'),
        (@GoverningPrincipleElementId, @StrategyPrincipleId, 'Composes', 'Second clause of the governing principle.');

        INSERT INTO dbo.FrameworkContextRules (
            FrameworkElementId, ArtifactType, ActivityType, TriggerStage,
            DeliveryMode, IsRequired, Priority, InstructionText
        )
        VALUES
        (
            @MandateElementId, NULL, '*', 'ContextAssembly', 'Both', 1, 10,
            'Interpret the applicable mandate as the source of purpose, authority, responsibility, and scope.'
        ),
        (
            @CapabilityElementId, NULL, '*', 'ContextAssembly', 'Both', 1, 20,
            'Identify, establish, maintain, and assess the people, systems, processes, tools, controls, resources, and structure required to fulfil the mandate.'
        ),
        (
            @StrategyElementId, NULL, '*', 'ContextAssembly', 'Both', 1, 30,
            'Align direction with the organization''s overall strategy and goals, and state how available capability will be applied.'
        );

        EXEC(N'
            CREATE TRIGGER dbo.TR_Principles_ProtectImmutable
            ON dbo.Principles
            AFTER UPDATE, DELETE
            AS
            BEGIN
                SET NOCOUNT ON;
                IF EXISTS (SELECT 1 FROM deleted WHERE IsImmutable = 1)
                    THROW 51001, ''Immutable principles cannot be updated or deleted. Create a replacement principle and record a REPLACES relationship.'', 1;
            END;
        ');

        EXEC(N'
            CREATE TRIGGER dbo.TR_FrameworkVersions_ProtectPublished
            ON dbo.FrameworkVersions
            AFTER UPDATE, DELETE
            AS
            BEGIN
                SET NOCOUNT ON;
                IF EXISTS (SELECT 1 FROM deleted WHERE VersionStatus = ''Published'')
                    THROW 51002, ''Published framework versions cannot be updated or deleted. Create a new draft version.'', 1;
            END;
        ');

        EXEC(N'
            CREATE TRIGGER dbo.TR_FrameworkElements_ProtectPublished
            ON dbo.FrameworkElements
            AFTER INSERT, UPDATE, DELETE
            AS
            BEGIN
                SET NOCOUNT ON;
                IF EXISTS (
                    SELECT 1 FROM inserted i
                    JOIN dbo.FrameworkVersions v ON v.FrameworkVersionId = i.FrameworkVersionId
                    WHERE v.VersionStatus = ''Published''
                ) OR EXISTS (
                    SELECT 1 FROM deleted d
                    JOIN dbo.FrameworkVersions v ON v.FrameworkVersionId = d.FrameworkVersionId
                    WHERE v.VersionStatus = ''Published''
                )
                    THROW 51003, ''Elements of a published framework version cannot be changed.'', 1;
            END;
        ');

        EXEC(N'
            CREATE TRIGGER dbo.TR_FrameworkContextRules_ProtectPublished
            ON dbo.FrameworkContextRules
            AFTER INSERT, UPDATE, DELETE
            AS
            BEGIN
                SET NOCOUNT ON;
                IF EXISTS (
                    SELECT 1 FROM inserted i
                    JOIN dbo.FrameworkElements e ON e.FrameworkElementId = i.FrameworkElementId
                    JOIN dbo.FrameworkVersions v ON v.FrameworkVersionId = e.FrameworkVersionId
                    WHERE v.VersionStatus = ''Published''
                ) OR EXISTS (
                    SELECT 1 FROM deleted d
                    JOIN dbo.FrameworkElements e ON e.FrameworkElementId = d.FrameworkElementId
                    JOIN dbo.FrameworkVersions v ON v.FrameworkVersionId = e.FrameworkVersionId
                    WHERE v.VersionStatus = ''Published''
                )
                    THROW 51004, ''Context rules of a published framework version cannot be changed.'', 1;
            END;
        ');

        EXEC(N'
            CREATE TRIGGER dbo.TR_FrameworkElementRelations_ProtectPublished
            ON dbo.FrameworkElementRelations
            AFTER INSERT, UPDATE, DELETE
            AS
            BEGIN
                SET NOCOUNT ON;
                IF EXISTS (
                    SELECT 1 FROM inserted i
                    JOIN dbo.FrameworkElements e ON e.FrameworkElementId = i.FromElementId
                    JOIN dbo.FrameworkVersions v ON v.FrameworkVersionId = e.FrameworkVersionId
                    WHERE v.VersionStatus = ''Published''
                ) OR EXISTS (
                    SELECT 1 FROM deleted d
                    JOIN dbo.FrameworkElements e ON e.FrameworkElementId = d.FromElementId
                    JOIN dbo.FrameworkVersions v ON v.FrameworkVersionId = e.FrameworkVersionId
                    WHERE v.VersionStatus = ''Published''
                )
                    THROW 51005, ''Relations of a published framework version cannot be changed.'', 1;
            END;
        ');

        EXEC(N'
            CREATE TRIGGER dbo.TR_FrameworkPrincipleLinks_ProtectPublished
            ON dbo.FrameworkPrincipleLinks
            AFTER INSERT, UPDATE, DELETE
            AS
            BEGIN
                SET NOCOUNT ON;
                IF EXISTS (
                    SELECT 1 FROM inserted i
                    JOIN dbo.FrameworkElements e ON e.FrameworkElementId = i.FrameworkElementId
                    JOIN dbo.FrameworkVersions v ON v.FrameworkVersionId = e.FrameworkVersionId
                    WHERE v.VersionStatus = ''Published''
                ) OR EXISTS (
                    SELECT 1 FROM deleted d
                    JOIN dbo.FrameworkElements e ON e.FrameworkElementId = d.FrameworkElementId
                    JOIN dbo.FrameworkVersions v ON v.FrameworkVersionId = e.FrameworkVersionId
                    WHERE v.VersionStatus = ''Published''
                )
                    THROW 51006, ''Principle links of a published framework version cannot be changed.'', 1;
            END;
        ');

        INSERT INTO dbo.SchemaMigrations(MigrationId)
        VALUES ('034_mandate_capability_strategy_framework');

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;
GO
