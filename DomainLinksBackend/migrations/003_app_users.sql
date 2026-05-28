SET NOCOUNT ON;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigrations WHERE MigrationId = '003_app_users')
BEGIN
    IF OBJECT_ID('dbo.AppUsers', 'U') IS NULL
    BEGIN
        CREATE TABLE dbo.AppUsers (
            AppUserId           UNIQUEIDENTIFIER NOT NULL
                CONSTRAINT DF_AppUsers_AppUserId DEFAULT (NEWSEQUENTIALID()),
            IdentityProvider    NVARCHAR(30) NOT NULL
                CONSTRAINT DF_AppUsers_IdentityProvider DEFAULT ('Windows'),
            WindowsUserName     NVARCHAR(256) NOT NULL,
            WindowsSid          NVARCHAR(256) NULL,
            DisplayName         NVARCHAR(255) NULL,
            Status              NVARCHAR(30) NOT NULL
                CONSTRAINT DF_AppUsers_Status DEFAULT ('Active'),
            FirstSeenAtUtc      DATETIME2(3) NOT NULL
                CONSTRAINT DF_AppUsers_FirstSeenAtUtc DEFAULT (SYSUTCDATETIME()),
            LastSeenAtUtc       DATETIME2(3) NOT NULL
                CONSTRAINT DF_AppUsers_LastSeenAtUtc DEFAULT (SYSUTCDATETIME()),
            CreatedAtUtc        DATETIME2(3) NOT NULL
                CONSTRAINT DF_AppUsers_CreatedAtUtc DEFAULT (SYSUTCDATETIME()),
            UpdatedAtUtc        DATETIME2(3) NOT NULL
                CONSTRAINT DF_AppUsers_UpdatedAtUtc DEFAULT (SYSUTCDATETIME()),
            CONSTRAINT PK_AppUsers PRIMARY KEY (AppUserId),
            CONSTRAINT CK_AppUsers_Status
                CHECK (Status IN ('Active', 'Disabled'))
        );

        CREATE UNIQUE INDEX UX_AppUsers_WindowsSid
            ON dbo.AppUsers(WindowsSid)
            WHERE WindowsSid IS NOT NULL;

        CREATE UNIQUE INDEX UX_AppUsers_IdentityProvider_UserName
            ON dbo.AppUsers(IdentityProvider, WindowsUserName);
    END;

    INSERT INTO dbo.SchemaMigrations (MigrationId)
    VALUES ('003_app_users');
END;
GO
