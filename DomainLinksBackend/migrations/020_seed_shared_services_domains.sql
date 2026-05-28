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

IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigrations WHERE MigrationId = '020_seed_shared_services_domains')
BEGIN
    DECLARE @StrategicDomainTypeId INT =
        (SELECT ID FROM dbo.DomainTypes WHERE CODE = 'STRATEGIC');
    DECLARE @TacticalDomainTypeId INT =
        (SELECT ID FROM dbo.DomainTypes WHERE CODE = 'TACTICAL');
    DECLARE @SharedServicesOrientationId INT =
        (SELECT ID FROM dbo.DomainOrientations WHERE CODE = 'SHARED_SERVICES');

    IF @StrategicDomainTypeId IS NULL OR @TacticalDomainTypeId IS NULL
    BEGIN
        THROW 51000, 'Required DomainTypes rows were not found. Aborting shared services seed.', 1;
    END;

    IF @SharedServicesOrientationId IS NULL
    BEGIN
        THROW 51000, 'Shared Services DomainOrientations row was not found. Aborting shared services seed.', 1;
    END;

    DECLARE @CorporatePoliticalManagementId UNIQUEIDENTIFIER =
        (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'corporate-political-management');
    DECLARE @ExecutiveOfficeId UNIQUEIDENTIFIER =
        (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'executive-office');
    DECLARE @AdministrationUnitId UNIQUEIDENTIFIER =
        (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'administration-unit');
    DECLARE @AssetManagementId UNIQUEIDENTIFIER =
        (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'asset-management');
    DECLARE @FinanceUnitId UNIQUEIDENTIFIER =
        (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'finance-unit');
    DECLARE @LegalServicesUnitId UNIQUEIDENTIFIER =
        (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'legal-services-unit');
    DECLARE @PublicRelationsManagementId UNIQUEIDENTIFIER =
        (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'public-relations-management');
    DECLARE @OrganizationalDevelopmentUnitId UNIQUEIDENTIFIER =
        (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'organizational-development-unit');
    DECLARE @InformationManagementId UNIQUEIDENTIFIER =
        (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'information-management');
    DECLARE @HumanResourcesId UNIQUEIDENTIFIER =
        (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'human-resources');

    IF @CorporatePoliticalManagementId IS NULL
    BEGIN
        INSERT INTO dbo.Domains (
            DomainParentId,
            DomainTypeId,
            DomainOrientationId,
            DisplayOrder,
            DomainCode,
            DisplayName,
            Description
        )
        VALUES (
            NULL,
            @StrategicDomainTypeId,
            @SharedServicesOrientationId,
            110,
            'corporate-political-management',
            'Corporate/Political Management',
            'Enterprise leadership, governance, and policy direction that aligns corporate and political authority.'
        );

        SET @CorporatePoliticalManagementId = (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'corporate-political-management');
    END;

    IF @ExecutiveOfficeId IS NULL
    BEGIN
        INSERT INTO dbo.Domains (
            DomainParentId,
            DomainTypeId,
            DomainOrientationId,
            DisplayOrder,
            DomainCode,
            DisplayName,
            Description
        )
        VALUES (
            NULL,
            @StrategicDomainTypeId,
            @SharedServicesOrientationId,
            120,
            'executive-office',
            'Executive Office',
            'Executive coordination, governance oversight, and compliance support for organizational leadership.'
        );

        SET @ExecutiveOfficeId = (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'executive-office');
    END;

    IF @AdministrationUnitId IS NULL
    BEGIN
        INSERT INTO dbo.Domains (
            DomainParentId,
            DomainTypeId,
            DomainOrientationId,
            DisplayOrder,
            DomainCode,
            DisplayName,
            Description
        )
        VALUES (
            NULL,
            @StrategicDomainTypeId,
            @SharedServicesOrientationId,
            130,
            'administration-unit',
            'Administration Unit',
            'Daily office administration, front-desk coordination, and workplace support services.'
        );

        SET @AdministrationUnitId = (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'administration-unit');
    END;

    IF @AssetManagementId IS NULL
    BEGIN
        INSERT INTO dbo.Domains (
            DomainParentId,
            DomainTypeId,
            DomainOrientationId,
            DisplayOrder,
            DomainCode,
            DisplayName,
            Description
        )
        VALUES (
            NULL,
            @StrategicDomainTypeId,
            @SharedServicesOrientationId,
            140,
            'asset-management',
            'Asset Management',
            'Protection, upkeep, and stewardship of facilities, equipment, and physical assets.'
        );

        SET @AssetManagementId = (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'asset-management');
    END;

    IF @FinanceUnitId IS NULL
    BEGIN
        INSERT INTO dbo.Domains (
            DomainParentId,
            DomainTypeId,
            DomainOrientationId,
            DisplayOrder,
            DomainCode,
            DisplayName,
            Description
        )
        VALUES (
            NULL,
            @StrategicDomainTypeId,
            @SharedServicesOrientationId,
            150,
            'finance-unit',
            'Finance Unit',
            'Financial control, planning, reporting, and payroll services that support organizational operations.'
        );

        SET @FinanceUnitId = (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'finance-unit');
    END;

    IF @LegalServicesUnitId IS NULL
    BEGIN
        INSERT INTO dbo.Domains (
            DomainParentId,
            DomainTypeId,
            DomainOrientationId,
            DisplayOrder,
            DomainCode,
            DisplayName,
            Description
        )
        VALUES (
            NULL,
            @StrategicDomainTypeId,
            @SharedServicesOrientationId,
            160,
            'legal-services-unit',
            'Legal Services Unit',
            'Legal advice, dispute management, and corporate legal support across the organization.'
        );

        SET @LegalServicesUnitId = (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'legal-services-unit');
    END;

    IF @PublicRelationsManagementId IS NULL
    BEGIN
        INSERT INTO dbo.Domains (
            DomainParentId,
            DomainTypeId,
            DomainOrientationId,
            DisplayOrder,
            DomainCode,
            DisplayName,
            Description
        )
        VALUES (
            NULL,
            @StrategicDomainTypeId,
            @SharedServicesOrientationId,
            170,
            'public-relations-management',
            'Public Relations Management',
            'Communication strategy, reputation management, and internal-external messaging support.'
        );

        SET @PublicRelationsManagementId = (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'public-relations-management');
    END;

    IF @OrganizationalDevelopmentUnitId IS NULL
    BEGIN
        INSERT INTO dbo.Domains (
            DomainParentId,
            DomainTypeId,
            DomainOrientationId,
            DisplayOrder,
            DomainCode,
            DisplayName,
            Description
        )
        VALUES (
            NULL,
            @StrategicDomainTypeId,
            @SharedServicesOrientationId,
            180,
            'organizational-development-unit',
            'Organizational Development Unit',
            'Organizational improvement, policy coordination, and capability-building for enterprise alignment.'
        );

        SET @OrganizationalDevelopmentUnitId = (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'organizational-development-unit');
    END;

    IF @InformationManagementId IS NULL
    BEGIN
        INSERT INTO dbo.Domains (
            DomainParentId,
            DomainTypeId,
            DomainOrientationId,
            DisplayOrder,
            DomainCode,
            DisplayName,
            Description
        )
        VALUES (
            NULL,
            @StrategicDomainTypeId,
            @SharedServicesOrientationId,
            190,
            'information-management',
            'Information Management',
            'Information systems, records, data governance, and technology operations that support the organization.'
        );

        SET @InformationManagementId = (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'information-management');
    END;

    IF @HumanResourcesId IS NULL
    BEGIN
        INSERT INTO dbo.Domains (
            DomainParentId,
            DomainTypeId,
            DomainOrientationId,
            DisplayOrder,
            DomainCode,
            DisplayName,
            Description
        )
        VALUES (
            NULL,
            @StrategicDomainTypeId,
            @SharedServicesOrientationId,
            200,
            'human-resources',
            'Human Resources',
            'People strategy, workforce support, and employee lifecycle services across the organization.'
        );

        SET @HumanResourcesId = (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'human-resources');
    END;

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @CorporatePoliticalManagementId, @TacticalDomainTypeId, @SharedServicesOrientationId, 10, 'corporate-governance', 'Corporate Governance', 'Governance structures, decision rights, and accountability mechanisms for the corporation.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'corporate-governance');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @CorporatePoliticalManagementId, @TacticalDomainTypeId, @SharedServicesOrientationId, 20, 'executive-management', 'Executive Management', 'Executive leadership direction, coordination, and management oversight.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'executive-management');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @CorporatePoliticalManagementId, @TacticalDomainTypeId, @SharedServicesOrientationId, 30, 'political-governance', 'Political Governance', 'Political leadership processes, mandates, and governance coordination.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'political-governance');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @CorporatePoliticalManagementId, @TacticalDomainTypeId, @SharedServicesOrientationId, 40, 'corporate-political-policy', 'Corporate/Political Policy', 'Policies that guide corporate and political governance decisions and conduct.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'corporate-political-policy');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @ExecutiveOfficeId, @TacticalDomainTypeId, @SharedServicesOrientationId, 10, 'strategic-governance', 'Strategic Governance', 'Governance practices that align executive decisions with long-term strategy.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'strategic-governance');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @ExecutiveOfficeId, @TacticalDomainTypeId, @SharedServicesOrientationId, 20, 'administrative-governance', 'Administrative Governance', 'Administrative controls, routines, and governance support for executive operations.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'administrative-governance');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @ExecutiveOfficeId, @TacticalDomainTypeId, @SharedServicesOrientationId, 30, 'quality-governance', 'Quality Governance', 'Quality oversight, standards, and assurance practices across executive functions.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'quality-governance');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @ExecutiveOfficeId, @TacticalDomainTypeId, @SharedServicesOrientationId, 40, 'financial-governance', 'Financial Governance', 'Executive oversight of financial stewardship, controls, and accountability.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'financial-governance');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @ExecutiveOfficeId, @TacticalDomainTypeId, @SharedServicesOrientationId, 50, 'operational-governance', 'Operational Governance', 'Executive governance of day-to-day operations, performance, and service delivery.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'operational-governance');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @ExecutiveOfficeId, @TacticalDomainTypeId, @SharedServicesOrientationId, 60, 'regulatory-compliance', 'Regulatory Compliance', 'Compliance obligations, monitoring, and governance response to regulatory requirements.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'regulatory-compliance');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @AdministrationUnitId, @TacticalDomainTypeId, @SharedServicesOrientationId, 10, 'reception', 'Reception', 'Front-desk coordination, visitor intake, and reception services.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'reception');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @AdministrationUnitId, @TacticalDomainTypeId, @SharedServicesOrientationId, 20, 'mail', 'Mail', 'Incoming and outgoing mail handling and delivery coordination.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'mail');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @AdministrationUnitId, @TacticalDomainTypeId, @SharedServicesOrientationId, 30, 'office-supplies', 'Office Supplies', 'Procurement, tracking, and distribution of routine office supplies.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'office-supplies');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @AdministrationUnitId, @TacticalDomainTypeId, @SharedServicesOrientationId, 40, 'furniture-and-movable-equipment', 'Furniture and Movable Equipment', 'Management of office furniture and movable workplace equipment.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'furniture-and-movable-equipment');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @AssetManagementId, @TacticalDomainTypeId, @SharedServicesOrientationId, 10, 'security', 'Security', 'Physical security services, controls, and incident response for organizational assets.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'security');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @AssetManagementId, @TacticalDomainTypeId, @SharedServicesOrientationId, 20, 'maintenance', 'Maintenance', 'Preventive and corrective maintenance for facilities and asset reliability.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'maintenance');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @AssetManagementId, @TacticalDomainTypeId, @SharedServicesOrientationId, 30, 'custodial-services', 'Custodial Services', 'Cleaning, sanitation, and custodial support for facilities.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'custodial-services');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @FinanceUnitId, @TacticalDomainTypeId, @SharedServicesOrientationId, 10, 'accounting-management', 'Accounting Management', 'Accounting operations including receivables, payables, and financial record control.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'accounting-management');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @FinanceUnitId, @TacticalDomainTypeId, @SharedServicesOrientationId, 20, 'audits', 'Audits', 'Internal and external audit coordination, evidence management, and response tracking.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'audits');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @FinanceUnitId, @TacticalDomainTypeId, @SharedServicesOrientationId, 30, 'budgeting', 'Budgeting', 'Budget development, allocation, review, and variance management.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'budgeting');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @FinanceUnitId, @TacticalDomainTypeId, @SharedServicesOrientationId, 40, 'financial-planning', 'Financial Planning', 'Financial instruments, cash management, and forward planning for organizational resources.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'financial-planning');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @FinanceUnitId, @TacticalDomainTypeId, @SharedServicesOrientationId, 50, 'payroll', 'Payroll', 'Payroll administration, remittance, and employee pay processing.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'payroll');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @LegalServicesUnitId, @TacticalDomainTypeId, @SharedServicesOrientationId, 10, 'litigation', 'Litigation', 'Dispute management, claims, and litigation support.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'litigation');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @LegalServicesUnitId, @TacticalDomainTypeId, @SharedServicesOrientationId, 20, 'political-advisory', 'Political Advisory', 'Legal and procedural advice for political leadership and governance matters.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'political-advisory');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @LegalServicesUnitId, @TacticalDomainTypeId, @SharedServicesOrientationId, 30, 'operational-advisory', 'Operational Advisory', 'Legal advice that supports operational decisions, controls, and service delivery.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'operational-advisory');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @LegalServicesUnitId, @TacticalDomainTypeId, @SharedServicesOrientationId, 40, 'corporate-commercial', 'Corporate/Commercial', 'Corporate legal support including contracts, commercial matters, and intellectual property.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'corporate-commercial');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @PublicRelationsManagementId, @TacticalDomainTypeId, @SharedServicesOrientationId, 10, 'public-communications', 'Public Communications', 'Public-facing messaging, announcements, and communications planning.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'public-communications');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @PublicRelationsManagementId, @TacticalDomainTypeId, @SharedServicesOrientationId, 20, 'internal-communications', 'Internal Communications', 'Internal messaging, staff communications, and organizational updates.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'internal-communications');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @PublicRelationsManagementId, @TacticalDomainTypeId, @SharedServicesOrientationId, 30, 'media-relations', 'Media Relations', 'Media engagement, response coordination, and press relationship management.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'media-relations');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @OrganizationalDevelopmentUnitId, @TacticalDomainTypeId, @SharedServicesOrientationId, 10, 'organizational-standards-governance-and-alignment', 'Organizational Standards, Governance, and Alignment', 'Standards and governance practices that keep organizational structure and execution aligned.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'organizational-standards-governance-and-alignment');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @OrganizationalDevelopmentUnitId, @TacticalDomainTypeId, @SharedServicesOrientationId, 20, 'policy-management', 'Policy Management', 'Development, maintenance, and lifecycle management of organizational policies.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'policy-management');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @OrganizationalDevelopmentUnitId, @TacticalDomainTypeId, @SharedServicesOrientationId, 30, 'strategic-planning', 'Strategic Planning', 'Planning processes that define priorities, objectives, and coordinated execution.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'strategic-planning');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @OrganizationalDevelopmentUnitId, @TacticalDomainTypeId, @SharedServicesOrientationId, 40, 'project-management', 'Project Management', 'Project planning, delivery coordination, and execution oversight.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'project-management');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @OrganizationalDevelopmentUnitId, @TacticalDomainTypeId, @SharedServicesOrientationId, 50, 'capacity-building', 'Capacity Building', 'Capability development, learning support, and organizational readiness improvement.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'capacity-building');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @InformationManagementId, @TacticalDomainTypeId, @SharedServicesOrientationId, 10, 'software-and-database-development-and-management', 'Software and Database Development and Management', 'Development and stewardship of software solutions and supporting databases.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'software-and-database-development-and-management');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @InformationManagementId, @TacticalDomainTypeId, @SharedServicesOrientationId, 20, 'software-systems-integration', 'Software Systems Integration', 'Integration of software platforms, services, and data flows across systems.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'software-systems-integration');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @InformationManagementId, @TacticalDomainTypeId, @SharedServicesOrientationId, 30, 'records-management', 'Records Management', 'Management of records, retention, filing, and information lifecycle controls.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'records-management');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @InformationManagementId, @TacticalDomainTypeId, @SharedServicesOrientationId, 40, 'data-governance', 'Data Governance', 'Standards, ownership, quality, and control of organizational data assets.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'data-governance');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @InformationManagementId, @TacticalDomainTypeId, @SharedServicesOrientationId, 50, 'technology-infrastructure', 'Technology Infrastructure', 'Core infrastructure services including networks, devices, hosting, and foundational platforms.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'technology-infrastructure');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @InformationManagementId, @TacticalDomainTypeId, @SharedServicesOrientationId, 60, 'technology-systems-management', 'Technology Systems Management', 'Administration, monitoring, and operational support for technology systems.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'technology-systems-management');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @HumanResourcesId, @TacticalDomainTypeId, @SharedServicesOrientationId, 10, 'workforce-planning', 'Workforce Planning', 'Planning for staffing capacity, capability needs, and workforce structure.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'workforce-planning');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @HumanResourcesId, @TacticalDomainTypeId, @SharedServicesOrientationId, 20, 'recruitment-and-staffing', 'Recruitment and Staffing', 'Recruitment processes, hiring workflows, and staffing coordination.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'recruitment-and-staffing');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @HumanResourcesId, @TacticalDomainTypeId, @SharedServicesOrientationId, 30, 'employee-relations', 'Employee Relations', 'Employee support, workplace issues, and day-to-day relationship management.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'employee-relations');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @HumanResourcesId, @TacticalDomainTypeId, @SharedServicesOrientationId, 40, 'labor-relations', 'Labor Relations', 'Union relations, collective agreement matters, and formal labor processes.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'labor-relations');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @HumanResourcesId, @TacticalDomainTypeId, @SharedServicesOrientationId, 50, 'compensation-and-benefits', 'Compensation and Benefits', 'Compensation design, benefit programs, and pay-related policy support.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'compensation-and-benefits');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @HumanResourcesId, @TacticalDomainTypeId, @SharedServicesOrientationId, 60, 'performance-management', 'Performance Management', 'Performance planning, review processes, and employee accountability support.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'performance-management');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @HumanResourcesId, @TacticalDomainTypeId, @SharedServicesOrientationId, 70, 'learning-and-development', 'Learning and Development', 'Training, development planning, and capability growth for staff.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'learning-and-development');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @HumanResourcesId, @TacticalDomainTypeId, @SharedServicesOrientationId, 80, 'occupational-health-and-safety', 'Occupational Health and Safety', 'Health, safety, prevention, and workplace wellbeing responsibilities.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'occupational-health-and-safety');
END;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigrations WHERE MigrationId = '020_seed_shared_services_domains')
BEGIN
    INSERT INTO dbo.SchemaMigrations (MigrationId)
    VALUES ('020_seed_shared_services_domains');
END;
GO
