SET NOCOUNT ON;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigrations WHERE MigrationId = '006_restore_domains')
BEGIN
    IF OBJECT_ID('dbo.Domains', 'U') IS NULL
    BEGIN
        CREATE TABLE dbo.Domains (
            DomainId           UNIQUEIDENTIFIER NOT NULL
                CONSTRAINT DF_Domains_DomainId DEFAULT (NEWSEQUENTIALID()),
            DomainCode         NVARCHAR(100) NOT NULL,
            DomainType         NVARCHAR(40) NOT NULL,
            DisplayName        NVARCHAR(255) NOT NULL,
            Description        NVARCHAR(MAX) NULL,
            Status             NVARCHAR(30) NOT NULL
                CONSTRAINT DF_Domains_Status DEFAULT ('Active'),
            CreatedAtUtc       DATETIME2(3) NOT NULL
                CONSTRAINT DF_Domains_CreatedAtUtc DEFAULT (SYSUTCDATETIME()),
            UpdatedAtUtc       DATETIME2(3) NOT NULL
                CONSTRAINT DF_Domains_UpdatedAtUtc DEFAULT (SYSUTCDATETIME()),
            CONSTRAINT PK_Domains PRIMARY KEY (DomainId),
            CONSTRAINT UQ_Domains_DomainCode UNIQUE (DomainCode),
            CONSTRAINT CK_Domains_DomainType
                CHECK (DomainType IN ('Knowledge', 'ProjectMemory', 'System'))
        );
        CREATE INDEX IX_Domains_DomainType_Status
            ON dbo.Domains(DomainType, Status);
    END;

    IF OBJECT_ID('dbo.DomainClusters', 'U') IS NULL
    BEGIN
        CREATE TABLE dbo.DomainClusters (
            DomainClusterId    UNIQUEIDENTIFIER NOT NULL
                CONSTRAINT DF_DomainClusters_DomainClusterId DEFAULT (NEWSEQUENTIALID()),
            ClusterCode        NVARCHAR(100) NOT NULL,
            DisplayName        NVARCHAR(255) NOT NULL,
            Description        NVARCHAR(MAX) NULL,
            Status             NVARCHAR(30) NOT NULL
                CONSTRAINT DF_DomainClusters_Status DEFAULT ('Active'),
            CreatedAtUtc       DATETIME2(3) NOT NULL
                CONSTRAINT DF_DomainClusters_CreatedAtUtc DEFAULT (SYSUTCDATETIME()),
            UpdatedAtUtc       DATETIME2(3) NOT NULL
                CONSTRAINT DF_DomainClusters_UpdatedAtUtc DEFAULT (SYSUTCDATETIME()),
            CONSTRAINT PK_DomainClusters PRIMARY KEY (DomainClusterId),
            CONSTRAINT UQ_DomainClusters_ClusterCode UNIQUE (ClusterCode)
        );
    END;

    IF OBJECT_ID('dbo.DomainClusterMembers', 'U') IS NULL
    BEGIN
        CREATE TABLE dbo.DomainClusterMembers (
            DomainClusterId    UNIQUEIDENTIFIER NOT NULL,
            DomainId           UNIQUEIDENTIFIER NOT NULL,
            CreatedAtUtc       DATETIME2(3) NOT NULL
                CONSTRAINT DF_DomainClusterMembers_CreatedAtUtc DEFAULT (SYSUTCDATETIME()),
            CONSTRAINT PK_DomainClusterMembers PRIMARY KEY (DomainClusterId, DomainId)
        );
    END;

    IF OBJECT_ID('dbo.DomainTypes', 'U') IS NULL
    BEGIN
        CREATE TABLE dbo.DomainTypes (
            ID                    int IDENTITY(1,1) NOT NULL,
            CODE                  nvarchar(50) NOT NULL,
            NAME                  nvarchar(100) NOT NULL,
            DOMAIN_LEVEL          int NOT NULL,
            PRIMARY_FOCUS         nvarchar(500) NOT NULL,
            KEY_QUESTION          nvarchar(200) NOT NULL,
            DESCRIPTION           nvarchar(500) NULL,
            DISPLAY_ORDER         int NOT NULL,
            EFFECTIVE_START_DATE  date NULL,
            EFFECTIVE_END_DATE    date NULL,
            CREATED_DATE          datetime2 NOT NULL
                CONSTRAINT DF_DomainTypes_CREATED_DATE DEFAULT (SYSDATETIME()),
            CREATED_BY            nvarchar(128) NOT NULL
                CONSTRAINT DF_DomainTypes_CREATED_BY DEFAULT (SUSER_SNAME()),
            UPDATED_DATE          datetime2 NULL,
            UPDATED_BY            nvarchar(128) NULL,
            CONSTRAINT PK_DomainTypes PRIMARY KEY CLUSTERED (ID),
            CONSTRAINT UQ_DomainTypes_CODE UNIQUE (CODE),
            CONSTRAINT UQ_DomainTypes_DOMAIN_LEVEL UNIQUE (DOMAIN_LEVEL),
            CONSTRAINT CK_DomainTypes_DOMAIN_LEVEL CHECK (DOMAIN_LEVEL BETWEEN 1 AND 4)
        );
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.DomainTypes WHERE CODE = 'STRATEGIC')
    BEGIN
        INSERT INTO dbo.DomainTypes
        (
            CODE, NAME, DOMAIN_LEVEL, PRIMARY_FOCUS, KEY_QUESTION, DESCRIPTION, DISPLAY_ORDER, EFFECTIVE_START_DATE
        )
        VALUES
        ('STRATEGIC', 'Strategic', 1, 'Long-term vision, market positioning, and core objectives.', 'Why are we doing this?', 'Long-term vision, market positioning, and core objectives.', 10, CAST(GETDATE() AS date)),
        ('TACTICAL', 'Tactical', 2, 'Mid-term planning, resource allocation, and initiative coordination.', 'What are we doing?', 'Mid-term planning, resource allocation, and initiative coordination.', 20, CAST(GETDATE() AS date)),
        ('OPERATIONAL', 'Operational', 3, 'Day-to-day processes, workflows, and management of business units.', 'How do we do it?', 'Day-to-day processes, workflows, and management of business units.', 30, CAST(GETDATE() AS date)),
        ('TECHNICAL_EXECUTION', 'Technical/Execution', 4, 'Specific implementation, infrastructure, and granular tasks.', 'What tools/code do we use?', 'Specific implementation, infrastructure, and granular tasks.', 40, CAST(GETDATE() AS date));
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'finance')
    BEGIN
        INSERT INTO dbo.Domains (DomainId, DomainCode, DomainType, DisplayName, Description)
        VALUES ('BF003718-BCBD-4DCC-A08A-098474B5B2BB', 'finance', 'Knowledge', 'Finance', 'Durable finance knowledge, controls, and guidance.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'governance')
    BEGIN
        INSERT INTO dbo.Domains (DomainId, DomainCode, DomainType, DisplayName, Description)
        VALUES ('F024E45F-5A72-4839-9F0C-CF169847ABB7', 'governance', 'Knowledge', 'Governance', 'Durable governance, board, and policy materials.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'hr')
    BEGIN
        INSERT INTO dbo.Domains (DomainId, DomainCode, DomainType, DisplayName, Description)
        VALUES ('9ACD6263-B8E5-446B-B649-DE9967255118', 'hr', 'Knowledge', 'HR', 'Durable human resources knowledge and policy.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'workspace-memory')
    BEGIN
        INSERT INTO dbo.Domains (DomainId, DomainCode, DomainType, DisplayName, Description)
        VALUES ('E76E1C76-B509-4B63-9B67-ECD92E99A469', 'workspace-memory', 'ProjectMemory', 'Workspace Memory', 'Workspace Memory for active chats, uploads, and project-level inference.');
    END;

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Collections_Domains')
    BEGIN
        ALTER TABLE dbo.Collections
        ADD CONSTRAINT FK_Collections_Domains
            FOREIGN KEY (DomainId) REFERENCES dbo.Domains(DomainId);
    END;

    IF OBJECT_ID('dbo.LegacyChromaMigrationState', 'U') IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_LegacyChromaMigrationState_Domains')
    BEGIN
        ALTER TABLE dbo.LegacyChromaMigrationState
        ADD CONSTRAINT FK_LegacyChromaMigrationState_Domains
            FOREIGN KEY (TargetDomainId) REFERENCES dbo.Domains(DomainId);
    END;

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_DomainClusterMembers_DomainClusters')
    BEGIN
        ALTER TABLE dbo.DomainClusterMembers
        ADD CONSTRAINT FK_DomainClusterMembers_DomainClusters
            FOREIGN KEY (DomainClusterId) REFERENCES dbo.DomainClusters(DomainClusterId);
    END;

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_DomainClusterMembers_Domains')
    BEGIN
        ALTER TABLE dbo.DomainClusterMembers
        ADD CONSTRAINT FK_DomainClusterMembers_Domains
            FOREIGN KEY (DomainId) REFERENCES dbo.Domains(DomainId);
    END;

    INSERT INTO dbo.SchemaMigrations (MigrationId)
    VALUES ('006_restore_domains');
END;
GO
