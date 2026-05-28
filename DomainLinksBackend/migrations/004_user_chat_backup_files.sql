SET NOCOUNT ON;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigrations WHERE MigrationId = '004_user_chat_backup_files')
BEGIN
    IF OBJECT_ID('dbo.UserChatBackupFiles', 'U') IS NULL
    BEGIN
        CREATE TABLE dbo.UserChatBackupFiles (
            UserChatBackupFileId         UNIQUEIDENTIFIER NOT NULL
                CONSTRAINT DF_UserChatBackupFiles_Id DEFAULT (NEWSEQUENTIALID()),
            AppUserId                    UNIQUEIDENTIFIER NOT NULL,
            RootCollectionCode           NVARCHAR(100) NOT NULL,
            RootDisplayName              NVARCHAR(255) NOT NULL,
            FileName                     NVARCHAR(400) NOT NULL,
            FileContentCompressedEncrypted VARBINARY(MAX) NOT NULL,
            ContentHashSha256            VARBINARY(32) NOT NULL,
            CompressionType              NVARCHAR(30) NOT NULL
                CONSTRAINT DF_UserChatBackupFiles_CompressionType DEFAULT ('gzip'),
            EncryptionType               NVARCHAR(50) NOT NULL
                CONSTRAINT DF_UserChatBackupFiles_EncryptionType DEFAULT ('aes-gcm-user-key'),
            KeyVersion                   INT NOT NULL
                CONSTRAINT DF_UserChatBackupFiles_KeyVersion DEFAULT (1),
            ClientModifiedUtc            DATETIME2(3) NOT NULL,
            BackupCreatedUtc             DATETIME2(3) NOT NULL
                CONSTRAINT DF_UserChatBackupFiles_BackupCreatedUtc DEFAULT (SYSUTCDATETIME()),
            BackupUpdatedUtc             DATETIME2(3) NOT NULL
                CONSTRAINT DF_UserChatBackupFiles_BackupUpdatedUtc DEFAULT (SYSUTCDATETIME()),
            LastRestoredUtc              DATETIME2(3) NULL,
            ClientMachineName            NVARCHAR(255) NULL,
            AppVersion                   NVARCHAR(50) NULL,
            IsDeleted                    BIT NOT NULL
                CONSTRAINT DF_UserChatBackupFiles_IsDeleted DEFAULT (0),
            CONSTRAINT PK_UserChatBackupFiles PRIMARY KEY (UserChatBackupFileId),
            CONSTRAINT FK_UserChatBackupFiles_AppUsers
                FOREIGN KEY (AppUserId) REFERENCES dbo.AppUsers(AppUserId)
        );

        CREATE UNIQUE INDEX UX_UserChatBackupFiles_User_Root
            ON dbo.UserChatBackupFiles(AppUserId, RootCollectionCode);

        CREATE INDEX IX_UserChatBackupFiles_User_UpdatedUtc
            ON dbo.UserChatBackupFiles(AppUserId, BackupUpdatedUtc DESC);
    END;

    INSERT INTO dbo.SchemaMigrations (MigrationId)
    VALUES ('004_user_chat_backup_files');
END;
GO
