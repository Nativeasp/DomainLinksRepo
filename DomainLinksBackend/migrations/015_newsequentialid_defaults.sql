SET NOCOUNT ON;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigrations WHERE MigrationId = '015_newsequentialid_defaults')
BEGIN
    DECLARE @sql nvarchar(max) = N'';

    ;WITH TargetDefaults AS (
        SELECT 'dbo' AS SchemaName, 'Domains' AS TableName, 'DomainId' AS ColumnName, 'DF_Domains_DomainId' AS ConstraintName
        UNION ALL SELECT 'dbo', 'Collections', 'CollectionId', 'DF_Collections_CollectionId'
        UNION ALL SELECT 'dbo', 'Documents', 'DocumentId', 'DF_Documents_DocumentId'
        UNION ALL SELECT 'dbo', 'ContentUnits', 'ContentUnitId', 'DF_ContentUnits_ContentUnitId'
        UNION ALL SELECT 'dbo', 'EmbeddingProfiles', 'EmbeddingProfileId', 'DF_EmbeddingProfiles_EmbeddingProfileId'
        UNION ALL SELECT 'dbo', 'ContentUnitEmbeddings768', 'ContentUnitEmbeddingId', 'DF_ContentUnitEmbeddings768_Id'
        UNION ALL SELECT 'dbo', 'RetrievalProfiles', 'RetrievalProfileId', 'DF_RetrievalProfiles_RetrievalProfileId'
        UNION ALL SELECT 'dbo', 'ProviderSettings', 'ProviderSettingId', 'DF_ProviderSettings_ProviderSettingId'
        UNION ALL SELECT 'dbo', 'AppUsers', 'AppUserId', 'DF_AppUsers_AppUserId'
        UNION ALL SELECT 'dbo', 'UserChatBackupFiles', 'Id', 'DF_UserChatBackupFiles_Id'
    )
    SELECT @sql = STRING_AGG(CAST(CommandText AS nvarchar(max)), CHAR(10) + CHAR(10))
    FROM (
        SELECT
            N'ALTER TABLE ' + QUOTENAME(td.SchemaName) + N'.' + QUOTENAME(td.TableName)
            + N' DROP CONSTRAINT ' + QUOTENAME(dc.name) + N';'
            + CHAR(10)
            + N'ALTER TABLE ' + QUOTENAME(td.SchemaName) + N'.' + QUOTENAME(td.TableName)
            + N' ADD CONSTRAINT ' + QUOTENAME(td.ConstraintName)
            + N' DEFAULT (NEWSEQUENTIALID()) FOR ' + QUOTENAME(td.ColumnName) + N';' AS CommandText
        FROM TargetDefaults td
        JOIN sys.tables t
            ON t.name = td.TableName
        JOIN sys.schemas s
            ON s.schema_id = t.schema_id
           AND s.name = td.SchemaName
        JOIN sys.columns c
            ON c.object_id = t.object_id
           AND c.name = td.ColumnName
        JOIN sys.default_constraints dc
            ON dc.parent_object_id = t.object_id
           AND dc.parent_column_id = c.column_id
        WHERE dc.definition <> '(newsequentialid())'
    ) commands;

    IF @sql IS NOT NULL AND LEN(@sql) > 0
    BEGIN
        EXEC sys.sp_executesql @sql;
    END;

    INSERT INTO dbo.SchemaMigrations (MigrationId)
    VALUES ('015_newsequentialid_defaults');
END;
GO
