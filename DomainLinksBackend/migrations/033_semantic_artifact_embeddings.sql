SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigrations WHERE MigrationId = '033_semantic_artifact_embeddings')
BEGIN
    BEGIN TRANSACTION;
    BEGIN TRY
        CREATE TABLE dbo.SemanticArtifacts (
            SemanticArtifactId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_SemanticArtifacts_Id DEFAULT (NEWSEQUENTIALID()),
            ArtifactType NVARCHAR(50) NOT NULL,
            SourceRecordId UNIQUEIDENTIFIER NOT NULL,
            SourceParentId UNIQUEIDENTIFIER NULL,
            DisplayName NVARCHAR(500) NOT NULL,
            CanonicalText NVARCHAR(MAX) NOT NULL,
            ContentHash VARBINARY(32) NOT NULL,
            EmbeddingStatus NVARCHAR(20) NOT NULL CONSTRAINT DF_SemanticArtifacts_Status DEFAULT ('Pending'),
            RetryCount INT NOT NULL CONSTRAINT DF_SemanticArtifacts_RetryCount DEFAULT (0),
            LastError NVARCHAR(2000) NULL,
            NextAttemptAtUtc DATETIME2(3) NULL,
            ClaimedAtUtc DATETIME2(3) NULL,
            CreatedAtUtc DATETIME2(3) NOT NULL CONSTRAINT DF_SemanticArtifacts_CreatedAtUtc DEFAULT (SYSUTCDATETIME()),
            UpdatedAtUtc DATETIME2(3) NOT NULL CONSTRAINT DF_SemanticArtifacts_UpdatedAtUtc DEFAULT (SYSUTCDATETIME()),
            CONSTRAINT PK_SemanticArtifacts PRIMARY KEY (SemanticArtifactId),
            CONSTRAINT UQ_SemanticArtifacts_Type_Source UNIQUE (ArtifactType, SourceRecordId),
            CONSTRAINT CK_SemanticArtifacts_Status CHECK (EmbeddingStatus IN ('Pending','Processing','Embedded','Failed','Archived'))
        );

        CREATE INDEX IX_SemanticArtifacts_WorkQueue
            ON dbo.SemanticArtifacts(EmbeddingStatus, NextAttemptAtUtc, UpdatedAtUtc)
            INCLUDE (ArtifactType, SourceRecordId, RetryCount);
        CREATE INDEX IX_SemanticArtifacts_Parent ON dbo.SemanticArtifacts(SourceParentId, ArtifactType);

        CREATE TABLE dbo.SemanticArtifactEmbeddings768 (
            SemanticArtifactEmbeddingId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_SemanticArtifactEmbeddings768_Id DEFAULT (NEWSEQUENTIALID()),
            SemanticArtifactId UNIQUEIDENTIFIER NOT NULL,
            EmbeddingProfileId UNIQUEIDENTIFIER NOT NULL,
            EmbeddingVector VECTOR(768) NOT NULL,
            EmbeddingHash VARBINARY(32) NOT NULL,
            CreatedAtUtc DATETIME2(3) NOT NULL CONSTRAINT DF_SemanticArtifactEmbeddings768_CreatedAtUtc DEFAULT (SYSUTCDATETIME()),
            UpdatedAtUtc DATETIME2(3) NOT NULL CONSTRAINT DF_SemanticArtifactEmbeddings768_UpdatedAtUtc DEFAULT (SYSUTCDATETIME()),
            CONSTRAINT PK_SemanticArtifactEmbeddings768 PRIMARY KEY (SemanticArtifactEmbeddingId),
            CONSTRAINT FK_SemanticArtifactEmbeddings768_Artifact FOREIGN KEY (SemanticArtifactId) REFERENCES dbo.SemanticArtifacts(SemanticArtifactId) ON DELETE CASCADE,
            CONSTRAINT FK_SemanticArtifactEmbeddings768_Profile FOREIGN KEY (EmbeddingProfileId) REFERENCES dbo.EmbeddingProfiles(EmbeddingProfileId),
            CONSTRAINT UQ_SemanticArtifactEmbeddings768_Artifact_Profile UNIQUE (SemanticArtifactId, EmbeddingProfileId)
        );
        CREATE INDEX IX_SemanticArtifactEmbeddings768_Profile ON dbo.SemanticArtifactEmbeddings768(EmbeddingProfileId);

        INSERT INTO dbo.SchemaMigrations(MigrationId) VALUES ('033_semantic_artifact_embeddings');
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;
GO
