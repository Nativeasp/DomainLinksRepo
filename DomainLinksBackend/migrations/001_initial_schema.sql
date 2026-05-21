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

IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigrations WHERE MigrationId = '001_initial_schema')
BEGIN
    IF OBJECT_ID('dbo.Domains', 'U') IS NULL
    BEGIN
        CREATE TABLE dbo.Domains (
            DomainId           UNIQUEIDENTIFIER NOT NULL
                CONSTRAINT DF_Domains_DomainId DEFAULT (NEWID()),
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
                CONSTRAINT DF_DomainClusters_DomainClusterId DEFAULT (NEWID()),
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
            CONSTRAINT PK_DomainClusterMembers PRIMARY KEY (DomainClusterId, DomainId),
            CONSTRAINT FK_DomainClusterMembers_DomainClusters
                FOREIGN KEY (DomainClusterId) REFERENCES dbo.DomainClusters(DomainClusterId),
            CONSTRAINT FK_DomainClusterMembers_Domains
                FOREIGN KEY (DomainId) REFERENCES dbo.Domains(DomainId)
        );
    END;

    IF OBJECT_ID('dbo.Collections', 'U') IS NULL
    BEGIN
        CREATE TABLE dbo.Collections (
            CollectionId       UNIQUEIDENTIFIER NOT NULL
                CONSTRAINT DF_Collections_CollectionId DEFAULT (NEWID()),
            DomainId           UNIQUEIDENTIFIER NOT NULL,
            CollectionCode     NVARCHAR(100) NOT NULL,
            DisplayName        NVARCHAR(255) NOT NULL,
            Description        NVARCHAR(MAX) NULL,
            Status             NVARCHAR(30) NOT NULL
                CONSTRAINT DF_Collections_Status DEFAULT ('Active'),
            CreatedAtUtc       DATETIME2(3) NOT NULL
                CONSTRAINT DF_Collections_CreatedAtUtc DEFAULT (SYSUTCDATETIME()),
            UpdatedAtUtc       DATETIME2(3) NOT NULL
                CONSTRAINT DF_Collections_UpdatedAtUtc DEFAULT (SYSUTCDATETIME()),
            CONSTRAINT PK_Collections PRIMARY KEY (CollectionId),
            CONSTRAINT FK_Collections_Domains
                FOREIGN KEY (DomainId) REFERENCES dbo.Domains(DomainId),
            CONSTRAINT UQ_Collections_CollectionCode UNIQUE (CollectionCode)
        );
        CREATE INDEX IX_Collections_Domain_Status
            ON dbo.Collections(DomainId, Status);
    END;

    IF OBJECT_ID('dbo.Documents', 'U') IS NULL
    BEGIN
        CREATE TABLE dbo.Documents (
            DocumentId         UNIQUEIDENTIFIER NOT NULL
                CONSTRAINT DF_Documents_DocumentId DEFAULT (NEWID()),
            CollectionId       UNIQUEIDENTIFIER NOT NULL,
            SourceName         NVARCHAR(500) NOT NULL,
            SourcePath         NVARCHAR(1000) NULL,
            SourceType         NVARCHAR(50) NULL,
            ExternalDocumentId NVARCHAR(255) NULL,
            ContentHash        VARBINARY(32) NULL,
            Status             NVARCHAR(30) NOT NULL
                CONSTRAINT DF_Documents_Status DEFAULT ('Active'),
            CreatedAtUtc       DATETIME2(3) NOT NULL
                CONSTRAINT DF_Documents_CreatedAtUtc DEFAULT (SYSUTCDATETIME()),
            UpdatedAtUtc       DATETIME2(3) NOT NULL
                CONSTRAINT DF_Documents_UpdatedAtUtc DEFAULT (SYSUTCDATETIME()),
            CONSTRAINT PK_Documents PRIMARY KEY (DocumentId),
            CONSTRAINT FK_Documents_Collections
                FOREIGN KEY (CollectionId) REFERENCES dbo.Collections(CollectionId)
        );
        CREATE INDEX IX_Documents_Collection_Status
            ON dbo.Documents(CollectionId, Status);
        CREATE INDEX IX_Documents_ExternalDocumentId
            ON dbo.Documents(ExternalDocumentId);
    END;

    IF OBJECT_ID('dbo.ContentUnits', 'U') IS NULL
    BEGIN
        CREATE TABLE dbo.ContentUnits (
            ContentUnitId      UNIQUEIDENTIFIER NOT NULL
                CONSTRAINT DF_ContentUnits_ContentUnitId DEFAULT (NEWID()),
            DocumentId         UNIQUEIDENTIFIER NOT NULL,
            UnitType           NVARCHAR(40) NOT NULL,
            UnitOrdinal        INT NOT NULL,
            Heading            NVARCHAR(500) NULL,
            BodyText           NVARCHAR(MAX) NOT NULL,
            TokenCount         INT NULL,
            ContentHash        VARBINARY(32) NULL,
            Status             NVARCHAR(30) NOT NULL
                CONSTRAINT DF_ContentUnits_Status DEFAULT ('Active'),
            CreatedAtUtc       DATETIME2(3) NOT NULL
                CONSTRAINT DF_ContentUnits_CreatedAtUtc DEFAULT (SYSUTCDATETIME()),
            UpdatedAtUtc       DATETIME2(3) NOT NULL
                CONSTRAINT DF_ContentUnits_UpdatedAtUtc DEFAULT (SYSUTCDATETIME()),
            CONSTRAINT PK_ContentUnits PRIMARY KEY (ContentUnitId),
            CONSTRAINT FK_ContentUnits_Documents
                FOREIGN KEY (DocumentId) REFERENCES dbo.Documents(DocumentId),
            CONSTRAINT UQ_ContentUnits_Document_Ordinal
                UNIQUE (DocumentId, UnitOrdinal),
            CONSTRAINT CK_ContentUnits_UnitType
                CHECK (UnitType IN ('Document', 'Section', 'Chunk', 'Summary'))
        );
        CREATE INDEX IX_ContentUnits_Document_Status
            ON dbo.ContentUnits(DocumentId, Status);
        CREATE INDEX IX_ContentUnits_UnitType_Status
            ON dbo.ContentUnits(UnitType, Status);
    END;

    IF OBJECT_ID('dbo.EmbeddingProfiles', 'U') IS NULL
    BEGIN
        CREATE TABLE dbo.EmbeddingProfiles (
            EmbeddingProfileId UNIQUEIDENTIFIER NOT NULL
                CONSTRAINT DF_EmbeddingProfiles_EmbeddingProfileId DEFAULT (NEWID()),
            ProfileCode        NVARCHAR(100) NOT NULL,
            Provider           NVARCHAR(50) NOT NULL,
            ModelName          NVARCHAR(200) NOT NULL,
            VectorDimension    INT NOT NULL,
            DistanceMetric     NVARCHAR(30) NOT NULL
                CONSTRAINT DF_EmbeddingProfiles_DistanceMetric DEFAULT ('cosine'),
            IsDefault          BIT NOT NULL
                CONSTRAINT DF_EmbeddingProfiles_IsDefault DEFAULT (0),
            Status             NVARCHAR(30) NOT NULL
                CONSTRAINT DF_EmbeddingProfiles_Status DEFAULT ('Active'),
            CreatedAtUtc       DATETIME2(3) NOT NULL
                CONSTRAINT DF_EmbeddingProfiles_CreatedAtUtc DEFAULT (SYSUTCDATETIME()),
            UpdatedAtUtc       DATETIME2(3) NOT NULL
                CONSTRAINT DF_EmbeddingProfiles_UpdatedAtUtc DEFAULT (SYSUTCDATETIME()),
            CONSTRAINT PK_EmbeddingProfiles PRIMARY KEY (EmbeddingProfileId),
            CONSTRAINT UQ_EmbeddingProfiles_ProfileCode UNIQUE (ProfileCode),
            CONSTRAINT CK_EmbeddingProfiles_DistanceMetric
                CHECK (DistanceMetric IN ('cosine', 'euclidean', 'dot'))
        );
    END;

    IF OBJECT_ID('dbo.ContentUnitEmbeddings768', 'U') IS NULL
    BEGIN
        CREATE TABLE dbo.ContentUnitEmbeddings768 (
            ContentUnitEmbeddingId UNIQUEIDENTIFIER NOT NULL
                CONSTRAINT DF_ContentUnitEmbeddings768_Id DEFAULT (NEWID()),
            ContentUnitId          UNIQUEIDENTIFIER NOT NULL,
            EmbeddingProfileId     UNIQUEIDENTIFIER NOT NULL,
            EmbeddingVector        VECTOR(768) NOT NULL,
            EmbeddingHash          VARBINARY(32) NULL,
            CreatedAtUtc           DATETIME2(3) NOT NULL
                CONSTRAINT DF_ContentUnitEmbeddings768_CreatedAtUtc DEFAULT (SYSUTCDATETIME()),
            CONSTRAINT PK_ContentUnitEmbeddings768 PRIMARY KEY (ContentUnitEmbeddingId),
            CONSTRAINT FK_ContentUnitEmbeddings768_ContentUnits
                FOREIGN KEY (ContentUnitId) REFERENCES dbo.ContentUnits(ContentUnitId),
            CONSTRAINT FK_ContentUnitEmbeddings768_EmbeddingProfiles
                FOREIGN KEY (EmbeddingProfileId) REFERENCES dbo.EmbeddingProfiles(EmbeddingProfileId),
            CONSTRAINT UQ_ContentUnitEmbeddings768_Unit_Profile
                UNIQUE (ContentUnitId, EmbeddingProfileId)
        );
        CREATE INDEX IX_ContentUnitEmbeddings768_Profile
            ON dbo.ContentUnitEmbeddings768(EmbeddingProfileId);
    END;

    IF OBJECT_ID('dbo.RetrievalProfiles', 'U') IS NULL
    BEGIN
        CREATE TABLE dbo.RetrievalProfiles (
            RetrievalProfileId UNIQUEIDENTIFIER NOT NULL
                CONSTRAINT DF_RetrievalProfiles_RetrievalProfileId DEFAULT (NEWID()),
            ProfileCode        NVARCHAR(100) NOT NULL,
            DisplayName        NVARCHAR(255) NOT NULL,
            RetrievalMode      NVARCHAR(40) NOT NULL,
            TopK               INT NOT NULL
                CONSTRAINT DF_RetrievalProfiles_TopK DEFAULT (8),
            MaxContextTokens   INT NULL,
            IncludeSummaries   BIT NOT NULL
                CONSTRAINT DF_RetrievalProfiles_IncludeSummaries DEFAULT (1),
            IncludeChunks      BIT NOT NULL
                CONSTRAINT DF_RetrievalProfiles_IncludeChunks DEFAULT (1),
            IncludeWholeDocs   BIT NOT NULL
                CONSTRAINT DF_RetrievalProfiles_IncludeWholeDocs DEFAULT (0),
            Status             NVARCHAR(30) NOT NULL
                CONSTRAINT DF_RetrievalProfiles_Status DEFAULT ('Active'),
            CreatedAtUtc       DATETIME2(3) NOT NULL
                CONSTRAINT DF_RetrievalProfiles_CreatedAtUtc DEFAULT (SYSUTCDATETIME()),
            UpdatedAtUtc       DATETIME2(3) NOT NULL
                CONSTRAINT DF_RetrievalProfiles_UpdatedAtUtc DEFAULT (SYSUTCDATETIME()),
            CONSTRAINT PK_RetrievalProfiles PRIMARY KEY (RetrievalProfileId),
            CONSTRAINT UQ_RetrievalProfiles_ProfileCode UNIQUE (ProfileCode),
            CONSTRAINT CK_RetrievalProfiles_RetrievalMode
                CHECK (RetrievalMode IN ('NoSearch', 'WholeDocument', 'DomainVector', 'ProjectVector', 'ClusterVector', 'Hybrid'))
        );
    END;

    IF OBJECT_ID('dbo.ProviderSettings', 'U') IS NULL
    BEGIN
        CREATE TABLE dbo.ProviderSettings (
            ProviderSettingId  UNIQUEIDENTIFIER NOT NULL
                CONSTRAINT DF_ProviderSettings_ProviderSettingId DEFAULT (NEWID()),
            Provider           NVARCHAR(50) NOT NULL,
            SettingKey         NVARCHAR(200) NOT NULL,
            SettingValue       NVARCHAR(MAX) NOT NULL,
            IsSecret           BIT NOT NULL
                CONSTRAINT DF_ProviderSettings_IsSecret DEFAULT (0),
            UpdatedAtUtc       DATETIME2(3) NOT NULL
                CONSTRAINT DF_ProviderSettings_UpdatedAtUtc DEFAULT (SYSUTCDATETIME()),
            CONSTRAINT PK_ProviderSettings PRIMARY KEY (ProviderSettingId),
            CONSTRAINT UQ_ProviderSettings_Provider_Key UNIQUE (Provider, SettingKey)
        );
    END;

    IF OBJECT_ID('dbo.LegacyChromaMigrationState', 'U') IS NULL
    BEGIN
        CREATE TABLE dbo.LegacyChromaMigrationState (
            LegacyChromaMigrationStateId UNIQUEIDENTIFIER NOT NULL
                CONSTRAINT DF_LegacyChromaMigrationState_Id DEFAULT (NEWID()),
            LegacyStoreName      NVARCHAR(255) NULL,
            LegacyCollectionName NVARCHAR(255) NULL,
            TargetDomainId       UNIQUEIDENTIFIER NULL,
            TargetCollectionId   UNIQUEIDENTIFIER NULL,
            MigrationStatus      NVARCHAR(30) NOT NULL
                CONSTRAINT DF_LegacyChromaMigrationState_Status DEFAULT ('Pending'),
            SourcePath           NVARCHAR(1000) NULL,
            LastMigratedAtUtc    DATETIME2(3) NULL,
            LastError            NVARCHAR(MAX) NULL,
            CreatedAtUtc         DATETIME2(3) NOT NULL
                CONSTRAINT DF_LegacyChromaMigrationState_CreatedAtUtc DEFAULT (SYSUTCDATETIME()),
            UpdatedAtUtc         DATETIME2(3) NOT NULL
                CONSTRAINT DF_LegacyChromaMigrationState_UpdatedAtUtc DEFAULT (SYSUTCDATETIME()),
            CONSTRAINT PK_LegacyChromaMigrationState PRIMARY KEY (LegacyChromaMigrationStateId),
            CONSTRAINT FK_LegacyChromaMigrationState_Domains
                FOREIGN KEY (TargetDomainId) REFERENCES dbo.Domains(DomainId),
            CONSTRAINT FK_LegacyChromaMigrationState_Collections
                FOREIGN KEY (TargetCollectionId) REFERENCES dbo.Collections(CollectionId),
            CONSTRAINT CK_LegacyChromaMigrationState_Status
                CHECK (MigrationStatus IN ('Pending', 'Migrated', 'Skipped', 'Failed', 'Validated'))
        );
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'projects')
    BEGIN
        INSERT INTO dbo.Domains (DomainCode, DomainType, DisplayName, Description)
        VALUES ('projects', 'ProjectMemory', 'Projects', 'Short-term working memory scopes.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'system')
    BEGIN
        INSERT INTO dbo.Domains (DomainCode, DomainType, DisplayName, Description)
        VALUES ('system', 'System', 'System', 'System-owned metadata and defaults.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.EmbeddingProfiles WHERE ProfileCode = 'ollama-nomic-embed-text-v1-5-768')
    BEGIN
        INSERT INTO dbo.EmbeddingProfiles (
            ProfileCode,
            Provider,
            ModelName,
            VectorDimension,
            DistanceMetric,
            IsDefault
        )
        VALUES (
            'ollama-nomic-embed-text-v1-5-768',
            'ollama',
            'nomic-embed-text:v1.5',
            768,
            'cosine',
            1
        );
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.RetrievalProfiles WHERE ProfileCode = 'project-hybrid-default')
    BEGIN
        INSERT INTO dbo.RetrievalProfiles (
            ProfileCode,
            DisplayName,
            RetrievalMode,
            TopK,
            MaxContextTokens,
            IncludeSummaries,
            IncludeChunks,
            IncludeWholeDocs
        )
        VALUES
            ('project-hybrid-default', 'Project Hybrid Default', 'Hybrid', 8, NULL, 1, 1, 0),
            ('domain-vector-default', 'Domain Vector Default', 'DomainVector', 8, NULL, 1, 1, 0),
            ('whole-document-default', 'Whole Document Default', 'WholeDocument', 3, NULL, 0, 0, 1),
            ('no-search', 'No Search', 'NoSearch', 0, NULL, 0, 0, 0);
    END;

    INSERT INTO dbo.SchemaMigrations (MigrationId)
    VALUES ('001_initial_schema');
END;
GO
