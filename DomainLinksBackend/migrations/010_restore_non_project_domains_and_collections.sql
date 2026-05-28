SET NOCOUNT ON;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigrations WHERE MigrationId = '010_restore_non_project_domains_and_collections')
BEGIN
    DECLARE @StrategicDomainTypeId INT =
        (SELECT ID FROM dbo.DomainTypes WHERE CODE = 'STRATEGIC');

    IF @StrategicDomainTypeId IS NULL
    BEGIN
        THROW 51000, 'Strategic DomainTypes row was not found. Aborting restore.', 1;
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'finance')
    BEGIN
        INSERT INTO dbo.Domains
        (
            DomainId,
            DomainParentId,
            DomainTypeId,
            DomainCode,
            DisplayName,
            Description
        )
        VALUES
        (
            'BF003718-BCBD-4DCC-A08A-098474B5B2BB',
            NULL,
            @StrategicDomainTypeId,
            'finance',
            'Finance',
            'Durable finance knowledge, controls, and guidance.'
        );
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'governance')
    BEGIN
        INSERT INTO dbo.Domains
        (
            DomainId,
            DomainParentId,
            DomainTypeId,
            DomainCode,
            DisplayName,
            Description
        )
        VALUES
        (
            'F024E45F-5A72-4839-9F0C-CF169847ABB7',
            NULL,
            @StrategicDomainTypeId,
            'governance',
            'Governance',
            'Durable governance, board, and policy materials.'
        );
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'hr')
    BEGIN
        INSERT INTO dbo.Domains
        (
            DomainId,
            DomainParentId,
            DomainTypeId,
            DomainCode,
            DisplayName,
            Description
        )
        VALUES
        (
            '9ACD6263-B8E5-446B-B649-DE9967255118',
            NULL,
            @StrategicDomainTypeId,
            'hr',
            'HR',
            'Durable human resources knowledge and policy.'
        );
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Collections WHERE CollectionCode = 'budget-controls')
    BEGIN
        INSERT INTO dbo.Collections
        (
            DomainId,
            CollectionCode,
            DisplayName,
            Description
        )
        VALUES
        (
            'BF003718-BCBD-4DCC-A08A-098474B5B2BB',
            'budget-controls',
            'Budget Controls',
            'Budget rules, approvals, and financial control guidance.'
        );
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Collections WHERE CollectionCode = 'board-governance')
    BEGIN
        INSERT INTO dbo.Collections
        (
            DomainId,
            CollectionCode,
            DisplayName,
            Description
        )
        VALUES
        (
            'F024E45F-5A72-4839-9F0C-CF169847ABB7',
            'board-governance',
            'Board Governance',
            'Board structure, governance policy, and committee guidance.'
        );
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Collections WHERE CollectionCode = 'hiring-policy')
    BEGIN
        INSERT INTO dbo.Collections
        (
            DomainId,
            CollectionCode,
            DisplayName,
            Description
        )
        VALUES
        (
            '9ACD6263-B8E5-446B-B649-DE9967255118',
            'hiring-policy',
            'Hiring Policy',
            'Core hiring policy, process, and role intake guidance.'
        );
    END;

    INSERT INTO dbo.SchemaMigrations (MigrationId)
    VALUES ('010_restore_non_project_domains_and_collections');
END;
GO
