SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

BEGIN TRANSACTION;
BEGIN TRY
    IF OBJECT_ID('dbo.SemanticArtifactEmbeddings768', 'U') IS NOT NULL
        DROP TABLE dbo.SemanticArtifactEmbeddings768;
    IF OBJECT_ID('dbo.SemanticArtifacts', 'U') IS NOT NULL
        DROP TABLE dbo.SemanticArtifacts;
    DELETE FROM dbo.SchemaMigrations WHERE MigrationId = '033_semantic_artifact_embeddings';
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
