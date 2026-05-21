SET NOCOUNT ON;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigrations WHERE MigrationId = '002_seed_initial_scopes')
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'hr')
    BEGIN
        INSERT INTO dbo.Domains (DomainCode, DomainType, DisplayName, Description)
        VALUES ('hr', 'Knowledge', 'HR', 'Durable human resources knowledge and policy.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'finance')
    BEGIN
        INSERT INTO dbo.Domains (DomainCode, DomainType, DisplayName, Description)
        VALUES ('finance', 'Knowledge', 'Finance', 'Durable finance knowledge, controls, and guidance.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'governance')
    BEGIN
        INSERT INTO dbo.Domains (DomainCode, DomainType, DisplayName, Description)
        VALUES ('governance', 'Knowledge', 'Governance', 'Durable governance, board, and policy materials.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'strategy')
    BEGIN
        INSERT INTO dbo.Domains (DomainCode, DomainType, DisplayName, Description)
        VALUES ('strategy', 'Knowledge', 'Strategy', 'Durable strategic planning and operating context.');
    END;

    DECLARE @ProjectsDomainId UNIQUEIDENTIFIER =
        (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'projects');
    DECLARE @HrDomainId UNIQUEIDENTIFIER =
        (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'hr');
    DECLARE @FinanceDomainId UNIQUEIDENTIFIER =
        (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'finance');
    DECLARE @GovernanceDomainId UNIQUEIDENTIFIER =
        (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'governance');
    DECLARE @StrategyDomainId UNIQUEIDENTIFIER =
        (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'strategy');

    IF @ProjectsDomainId IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM dbo.Collections WHERE CollectionCode = 'hire-request-new-dba')
    BEGIN
        INSERT INTO dbo.Collections (DomainId, CollectionCode, DisplayName, Description)
        VALUES (
            @ProjectsDomainId,
            'hire-request-new-dba',
            'Hire Request New DBA',
            'Sample short-term project memory scope for an active hiring request.'
        );
    END;

    IF @HrDomainId IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM dbo.Collections WHERE CollectionCode = 'hiring-policy')
    BEGIN
        INSERT INTO dbo.Collections (DomainId, CollectionCode, DisplayName, Description)
        VALUES (
            @HrDomainId,
            'hiring-policy',
            'Hiring Policy',
            'Core hiring policy, process, and role intake guidance.'
        );
    END;

    IF @FinanceDomainId IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM dbo.Collections WHERE CollectionCode = 'budget-controls')
    BEGIN
        INSERT INTO dbo.Collections (DomainId, CollectionCode, DisplayName, Description)
        VALUES (
            @FinanceDomainId,
            'budget-controls',
            'Budget Controls',
            'Budget rules, approvals, and financial control guidance.'
        );
    END;

    IF @GovernanceDomainId IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM dbo.Collections WHERE CollectionCode = 'board-governance')
    BEGIN
        INSERT INTO dbo.Collections (DomainId, CollectionCode, DisplayName, Description)
        VALUES (
            @GovernanceDomainId,
            'board-governance',
            'Board Governance',
            'Board structure, governance policy, and committee guidance.'
        );
    END;

    IF @StrategyDomainId IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM dbo.Collections WHERE CollectionCode = 'strategic-planning')
    BEGIN
        INSERT INTO dbo.Collections (DomainId, CollectionCode, DisplayName, Description)
        VALUES (
            @StrategyDomainId,
            'strategic-planning',
            'Strategic Planning',
            'Strategy memos, planning context, and priority framing.'
        );
    END;

    INSERT INTO dbo.SchemaMigrations (MigrationId)
    VALUES ('002_seed_initial_scopes');
END;
GO
