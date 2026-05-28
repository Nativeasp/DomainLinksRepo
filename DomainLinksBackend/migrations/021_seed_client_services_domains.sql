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

IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigrations WHERE MigrationId = '021_seed_client_services_domains')
BEGIN
    DECLARE @StrategicDomainTypeId INT =
        (SELECT ID FROM dbo.DomainTypes WHERE CODE = 'STRATEGIC');
    DECLARE @TacticalDomainTypeId INT =
        (SELECT ID FROM dbo.DomainTypes WHERE CODE = 'TACTICAL');
    DECLARE @ClientServicesOrientationId INT =
        (SELECT ID FROM dbo.DomainOrientations WHERE CODE = 'CLIENT_SERVICES');

    IF @StrategicDomainTypeId IS NULL OR @TacticalDomainTypeId IS NULL
    BEGIN
        THROW 51000, 'Required DomainTypes rows were not found. Aborting client services seed.', 1;
    END;

    IF @ClientServicesOrientationId IS NULL
    BEGIN
        THROW 51000, 'Client Services DomainOrientations row was not found. Aborting client services seed.', 1;
    END;

    DECLARE @CapitalManagementId UNIQUEIDENTIFIER =
        (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'capital-management');
    DECLARE @PublicWorksCapitalInfrastructureManagementId UNIQUEIDENTIFIER =
        (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'public-works-capital-infrastructure-management');
    DECLARE @SocialDevelopmentAndSupportId UNIQUEIDENTIFIER =
        (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'social-development-and-support');
    DECLARE @EnvironmentProtectionOfficeId UNIQUEIDENTIFIER =
        (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'environment-protection-office');
    DECLARE @HousingManagementId UNIQUEIDENTIFIER =
        (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'housing-management');
    DECLARE @JusticeManagementId UNIQUEIDENTIFIER =
        (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'justice-management');
    DECLARE @LandsManagementId UNIQUEIDENTIFIER =
        (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'lands-management');
    DECLARE @LaborManagementId UNIQUEIDENTIFIER =
        (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'labor-management');
    DECLARE @PublicSafetyCommissionDivisionId UNIQUEIDENTIFIER =
        (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'public-safety-commission-division');
    DECLARE @SportsRecreationManagementId UNIQUEIDENTIFIER =
        (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'sports-recreation-management');

    IF @CapitalManagementId IS NULL
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
            @ClientServicesOrientationId,
            310,
            'capital-management',
            'Capital Management',
            'Capital planning and service delivery for construction, utility, and infrastructure investment.'
        );

        SET @CapitalManagementId = (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'capital-management');
    END;

    IF @PublicWorksCapitalInfrastructureManagementId IS NULL
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
            @ClientServicesOrientationId,
            320,
            'public-works-capital-infrastructure-management',
            'Public Works/Capital Infrastructure Management',
            'Public works services and infrastructure operations that support community function and resilience.'
        );

        SET @PublicWorksCapitalInfrastructureManagementId = (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'public-works-capital-infrastructure-management');
    END;

    IF @SocialDevelopmentAndSupportId IS NULL
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
            @ClientServicesOrientationId,
            330,
            'social-development-and-support',
            'Social Development and Support',
            'Programs and supports that strengthen social wellbeing, inclusion, and community identity.'
        );

        SET @SocialDevelopmentAndSupportId = (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'social-development-and-support');
    END;

    IF @EnvironmentProtectionOfficeId IS NULL
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
            @ClientServicesOrientationId,
            340,
            'environment-protection-office',
            'Environment Protection Office',
            'Environmental stewardship, monitoring, permitting, and advisory services for land and community protection.'
        );

        SET @EnvironmentProtectionOfficeId = (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'environment-protection-office');
    END;

    IF @HousingManagementId IS NULL
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
            @ClientServicesOrientationId,
            350,
            'housing-management',
            'Housing Management',
            'Housing program administration, housing asset support, and external housing coordination.'
        );

        SET @HousingManagementId = (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'housing-management');
    END;

    IF @JusticeManagementId IS NULL
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
            @ClientServicesOrientationId,
            360,
            'justice-management',
            'Justice Management',
            'Justice services, case administration, and community-based justice support.'
        );

        SET @JusticeManagementId = (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'justice-management');
    END;

    IF @LandsManagementId IS NULL
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
            @ClientServicesOrientationId,
            370,
            'lands-management',
            'Lands Management',
            'Land administration, land use oversight, and land registry stewardship.'
        );

        SET @LandsManagementId = (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'lands-management');
    END;

    IF @LaborManagementId IS NULL
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
            @ClientServicesOrientationId,
            380,
            'labor-management',
            'Labor Management',
            'Labor standards, certification, workforce mobility, and worker protection oversight.'
        );

        SET @LaborManagementId = (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'labor-management');
    END;

    IF @PublicSafetyCommissionDivisionId IS NULL
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
            @ClientServicesOrientationId,
            390,
            'public-safety-commission-division',
            'Public Safety Commission/Division',
            'Public safety, enforcement, emergency response, and territory protection services.'
        );

        SET @PublicSafetyCommissionDivisionId = (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'public-safety-commission-division');
    END;

    IF @SportsRecreationManagementId IS NULL
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
            @ClientServicesOrientationId,
            400,
            'sports-recreation-management',
            'Sports & Recreation Management',
            'Community recreation, sport support, and facility access management.'
        );

        SET @SportsRecreationManagementId = (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'sports-recreation-management');
    END;

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @CapitalManagementId, @TacticalDomainTypeId, @ClientServicesOrientationId, 10, 'capital-construction-management', 'Capital Construction Management', 'Planning and delivery oversight for capital construction projects and assets.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'capital-construction-management');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @CapitalManagementId, @TacticalDomainTypeId, @ClientServicesOrientationId, 20, 'water-and-sewers', 'Water and Sewers', 'Water and sewer infrastructure services, planning, and system oversight.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'water-and-sewers');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @PublicWorksCapitalInfrastructureManagementId, @TacticalDomainTypeId, @ClientServicesOrientationId, 10, 'waste-management', 'Waste Management', 'Waste collection, disposal, diversion, and related service management.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'waste-management');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @PublicWorksCapitalInfrastructureManagementId, @TacticalDomainTypeId, @ClientServicesOrientationId, 20, 'roads', 'Roads', 'Road construction, maintenance, and operational coordination.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'roads');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @PublicWorksCapitalInfrastructureManagementId, @TacticalDomainTypeId, @ClientServicesOrientationId, 30, 'transport', 'Transport', 'Transportation services, fleet movement, and mobility coordination.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'transport');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @PublicWorksCapitalInfrastructureManagementId, @TacticalDomainTypeId, @ClientServicesOrientationId, 40, 'emergency-response', 'Emergency Response', 'Operational emergency response coordination for public works and infrastructure services.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'emergency-response');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @PublicWorksCapitalInfrastructureManagementId, @TacticalDomainTypeId, @ClientServicesOrientationId, 50, 'maintenance-management', 'Maintenance Management', 'Maintenance planning, service response, and infrastructure upkeep management.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'maintenance-management');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @SocialDevelopmentAndSupportId, @TacticalDomainTypeId, @ClientServicesOrientationId, 10, 'social-assistance', 'Social Assistance', 'Income, emergency, and social support services for eligible community members.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'social-assistance');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @SocialDevelopmentAndSupportId, @TacticalDomainTypeId, @ClientServicesOrientationId, 20, 'membership-citizenship', 'Membership/Citizenship', 'Membership and citizenship administration, records, and support processes.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'membership-citizenship');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @SocialDevelopmentAndSupportId, @TacticalDomainTypeId, @ClientServicesOrientationId, 30, 'language-and-culture', 'Language and Culture', 'Programs and supports that sustain language, culture, and community identity.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'language-and-culture');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @EnvironmentProtectionOfficeId, @TacticalDomainTypeId, @ClientServicesOrientationId, 10, 'environmental-education', 'Environmental Education', 'Education and awareness activities that support environmental stewardship.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'environmental-education');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @EnvironmentProtectionOfficeId, @TacticalDomainTypeId, @ClientServicesOrientationId, 20, 'environmental-monitoring-and-compliance', 'Environmental Monitoring and Compliance', 'Monitoring, reporting, and compliance oversight for environmental obligations.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'environmental-monitoring-and-compliance');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @EnvironmentProtectionOfficeId, @TacticalDomainTypeId, @ClientServicesOrientationId, 30, 'corporate-advisory-crown-duty-to-consult', 'Corporate Advisory (Crown Duty to Consult)', 'Advisory support on consultation obligations, environmental matters, and related corporate responsibilities.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'corporate-advisory-crown-duty-to-consult');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @EnvironmentProtectionOfficeId, @TacticalDomainTypeId, @ClientServicesOrientationId, 40, 'inspections-and-permits', 'Inspections & Permits', 'Inspection processes and permit administration for regulated activities.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'inspections-and-permits');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @EnvironmentProtectionOfficeId, @TacticalDomainTypeId, @ClientServicesOrientationId, 50, 'geographic-information-system', 'Geographic Information System', 'Spatial information services, mapping, and geospatial analysis support.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'geographic-information-system');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @HousingManagementId, @TacticalDomainTypeId, @ClientServicesOrientationId, 10, 'mortgage-loans', 'Mortgage Loans', 'Administration and support for housing mortgage and loan programs.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'mortgage-loans');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @HousingManagementId, @TacticalDomainTypeId, @ClientServicesOrientationId, 20, 'rental-management', 'Rental Management', 'Rental housing administration, occupancy coordination, and tenant support.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'rental-management');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @HousingManagementId, @TacticalDomainTypeId, @ClientServicesOrientationId, 30, 'renovations', 'Renovations', 'Housing renovation planning, coordination, and improvement tracking.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'renovations');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @HousingManagementId, @TacticalDomainTypeId, @ClientServicesOrientationId, 40, 'contract-management', 'Contract Management', 'Contract oversight for housing services, projects, and vendors.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'contract-management');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @HousingManagementId, @TacticalDomainTypeId, @ClientServicesOrientationId, 50, 'external-governmental-support', 'External Governmental Support', 'Coordination with external governments and funders on housing support programs.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'external-governmental-support');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @JusticeManagementId, @TacticalDomainTypeId, @ClientServicesOrientationId, 10, 'court-service-management', 'Court Service Management', 'Administration and operational support for court-related services.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'court-service-management');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @JusticeManagementId, @TacticalDomainTypeId, @ClientServicesOrientationId, 20, 'tickets-and-case-management', 'Tickets and Case Management', 'Tracking and administration of tickets, cases, and related justice records.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'tickets-and-case-management');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @JusticeManagementId, @TacticalDomainTypeId, @ClientServicesOrientationId, 30, 'alternative-justice-development-and-management', 'Alternative Justice Development and Management', 'Design and management of alternative and community-based justice approaches.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'alternative-justice-development-and-management');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @JusticeManagementId, @TacticalDomainTypeId, @ClientServicesOrientationId, 40, 'victim-service-management', 'Victim Service Management', 'Victim support services, coordination, and case-related assistance.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'victim-service-management');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @LandsManagementId, @TacticalDomainTypeId, @ClientServicesOrientationId, 10, 'land-use-management', 'Land Use Management', 'Planning, review, and administration of land use decisions and activities.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'land-use-management');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @LandsManagementId, @TacticalDomainTypeId, @ClientServicesOrientationId, 20, 'land-registry-management', 'Land Registry Management', 'Land registry administration, records, and transaction tracking.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'land-registry-management');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @LaborManagementId, @TacticalDomainTypeId, @ClientServicesOrientationId, 10, 'certification-and-competency', 'Certification & Competency', 'Certification requirements, competency tracking, and qualification support.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'certification-and-competency');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @LaborManagementId, @TacticalDomainTypeId, @ClientServicesOrientationId, 20, 'fair-wages-and-benefits', 'Fair Wages and Benefits', 'Labor standards related to wages, benefits, and worker entitlements.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'fair-wages-and-benefits');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @LaborManagementId, @TacticalDomainTypeId, @ClientServicesOrientationId, 30, 'occupational-health-safety-labor', 'Occupational Health & Safety', 'Labor-focused occupational health and safety standards, obligations, and oversight.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'occupational-health-safety-labor');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @LaborManagementId, @TacticalDomainTypeId, @ClientServicesOrientationId, 40, 'workforce-mobility', 'Workforce Mobility', 'Mobility requirements, worker movement rules, and labor access coordination.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'workforce-mobility');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @LaborManagementId, @TacticalDomainTypeId, @ClientServicesOrientationId, 50, 'insurance-requirements', 'Insurance Requirements', 'Insurance compliance and coverage requirements tied to labor and service activities.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'insurance-requirements');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @PublicSafetyCommissionDivisionId, @TacticalDomainTypeId, @ClientServicesOrientationId, 10, 'territory-conservation', 'Territory Conservation', 'Protection and stewardship of territory through public safety and conservation activities.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'territory-conservation');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @PublicSafetyCommissionDivisionId, @TacticalDomainTypeId, @ClientServicesOrientationId, 20, 'animal-control', 'Animal Control', 'Animal control response, safety enforcement, and related community protection work.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'animal-control');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @PublicSafetyCommissionDivisionId, @TacticalDomainTypeId, @ClientServicesOrientationId, 30, 'public-safety-inspection', 'Public Safety Inspection', 'Inspection activities that support safety compliance and public protection.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'public-safety-inspection');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @PublicSafetyCommissionDivisionId, @TacticalDomainTypeId, @ClientServicesOrientationId, 40, 'emergency-management', 'Emergency Management', 'Emergency planning, coordination, and readiness for public safety incidents.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'emergency-management');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @PublicSafetyCommissionDivisionId, @TacticalDomainTypeId, @ClientServicesOrientationId, 50, 'search-and-rescue', 'Search and Rescue', 'Search, rescue, and emergency field response coordination.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'search-and-rescue');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @PublicSafetyCommissionDivisionId, @TacticalDomainTypeId, @ClientServicesOrientationId, 60, 'landfill-inspections', 'Landfill Inspections', 'Inspection and oversight of landfill operations and related public safety concerns.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'landfill-inspections');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @PublicSafetyCommissionDivisionId, @TacticalDomainTypeId, @ClientServicesOrientationId, 70, 'policing-and-community-surveillance', 'Policing and Community Surveillance', 'Community policing, surveillance, and public safety monitoring services.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'policing-and-community-surveillance');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @SportsRecreationManagementId, @TacticalDomainTypeId, @ClientServicesOrientationId, 10, 'minor-sports-support', 'Minor Sports Support', 'Support for minor sports programming, participation, and coordination.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'minor-sports-support');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @SportsRecreationManagementId, @TacticalDomainTypeId, @ClientServicesOrientationId, 20, 'recreation-management', 'Recreation Management', 'Planning and management of recreation programs, schedules, and services.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'recreation-management');

    INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description)
    SELECT @SportsRecreationManagementId, @TacticalDomainTypeId, @ClientServicesOrientationId, 30, 'facility-rental-and-maintenance', 'Facility Rental and Maintenance', 'Rental coordination and upkeep of recreation and sport facilities.'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'facility-rental-and-maintenance');
END;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigrations WHERE MigrationId = '021_seed_client_services_domains')
BEGIN
    INSERT INTO dbo.SchemaMigrations (MigrationId)
    VALUES ('021_seed_client_services_domains');
END;
GO
