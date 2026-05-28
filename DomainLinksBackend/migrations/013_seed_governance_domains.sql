SET NOCOUNT ON;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigrations WHERE MigrationId = '013_seed_governance_domains')
BEGIN
    DECLARE @StrategicDomainTypeId INT =
        (SELECT ID FROM dbo.DomainTypes WHERE CODE = 'STRATEGIC');
    DECLARE @TacticalDomainTypeId INT =
        (SELECT ID FROM dbo.DomainTypes WHERE CODE = 'TACTICAL');
    DECLARE @OperationalDomainTypeId INT =
        (SELECT ID FROM dbo.DomainTypes WHERE CODE = 'OPERATIONAL');

    IF @StrategicDomainTypeId IS NULL OR @TacticalDomainTypeId IS NULL OR @OperationalDomainTypeId IS NULL
    BEGIN
        THROW 51000, 'Required DomainTypes rows were not found. Aborting governance seed.', 1;
    END;

    DECLARE @GovernanceId UNIQUEIDENTIFIER =
        (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'governance');
    DECLARE @StrategicPlanningId UNIQUEIDENTIFIER =
        (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'strategic-planning-root');
    DECLARE @CouncilGovernanceId UNIQUEIDENTIFIER =
        (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'council-governance');
    DECLARE @AdministrativeGovernanceId UNIQUEIDENTIFIER =
        (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'administrative-governance');
    DECLARE @PolicyGovernanceId UNIQUEIDENTIFIER =
        (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'policy-governance');
    DECLARE @AccountabilityEthicsId UNIQUEIDENTIFIER =
        (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'accountability-and-ethics');
    DECLARE @CorporatePlanningId UNIQUEIDENTIFIER =
        (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'corporate-planning');
    DECLARE @PerformanceManagementId UNIQUEIDENTIFIER =
        (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'performance-management');
    DECLARE @OrganizationalDevelopmentId UNIQUEIDENTIFIER =
        (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'organizational-development');
    DECLARE @CorporateRiskPlanningId UNIQUEIDENTIFIER =
        (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'corporate-risk-planning');
    DECLARE @IntergovernmentalRelationsId UNIQUEIDENTIFIER =
        (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'intergovernmental-relations');
    DECLARE @FederalRelationsId UNIQUEIDENTIFIER =
        (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'federal-relations');
    DECLARE @ProvincialRelationsId UNIQUEIDENTIFIER =
        (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'provincial-relations');
    DECLARE @RegionalMunicipalRelationsId UNIQUEIDENTIFIER =
        (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'regional-and-municipal-relations');
    DECLARE @IndigenousGovernmentRelationsId UNIQUEIDENTIFIER =
        (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'indigenous-government-relations');
    DECLARE @RightsTitleJurisdictionId UNIQUEIDENTIFIER =
        (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'rights-title-and-jurisdiction');
    DECLARE @JurisdictionDevelopmentId UNIQUEIDENTIFIER =
        (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'jurisdiction-development');
    DECLARE @RightsProtectionId UNIQUEIDENTIFIER =
        (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'rights-protection');
    DECLARE @LandsTerritoryId UNIQUEIDENTIFIER =
        (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'lands-and-territory');
    DECLARE @LegalConstitutionalAffairsId UNIQUEIDENTIFIER =
        (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'legal-and-constitutional-affairs');
    DECLARE @CommunityDevelopmentId UNIQUEIDENTIFIER =
        (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'community-development');
    DECLARE @SocialDevelopmentId UNIQUEIDENTIFIER =
        (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'social-development');
    DECLARE @EconomicDevelopmentId UNIQUEIDENTIFIER =
        (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'economic-development');
    DECLARE @HousingDevelopmentId UNIQUEIDENTIFIER =
        (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'housing-development');
    DECLARE @HealthWellnessId UNIQUEIDENTIFIER =
        (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'health-and-wellness');
    DECLARE @EducationTrainingId UNIQUEIDENTIFIER =
        (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'education-and-training');
    DECLARE @CultureLanguageRecreationId UNIQUEIDENTIFIER =
        (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'culture-language-and-recreation');
    DECLARE @FinancialSustainabilityId UNIQUEIDENTIFIER =
        (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'financial-sustainability');
    DECLARE @FinancialPlanningId UNIQUEIDENTIFIER =
        (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'financial-planning');
    DECLARE @RevenueManagementId UNIQUEIDENTIFIER =
        (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'revenue-management');
    DECLARE @FinancialOperationsId UNIQUEIDENTIFIER =
        (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'financial-operations');
    DECLARE @ProcurementContractingId UNIQUEIDENTIFIER =
        (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'procurement-and-contracting');
    DECLARE @AuditControlsId UNIQUEIDENTIFIER =
        (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'audit-and-controls');
    DECLARE @RiskResilienceId UNIQUEIDENTIFIER =
        (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'risk-and-resilience');
    DECLARE @EnterpriseRiskManagementId UNIQUEIDENTIFIER =
        (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'enterprise-risk-management');
    DECLARE @EmergencyManagementId UNIQUEIDENTIFIER =
        (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'emergency-management');
    DECLARE @BusinessContinuityId UNIQUEIDENTIFIER =
        (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'business-continuity');
    DECLARE @InformationSecurityId UNIQUEIDENTIFIER =
        (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'information-security');
    DECLARE @InfrastructureResilienceId UNIQUEIDENTIFIER =
        (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'infrastructure-resilience');

    IF @GovernanceId IS NULL
    BEGIN
        INSERT INTO dbo.Domains
        (
            DomainParentId,
            DomainTypeId,
            DomainCode,
            DisplayName,
            Description
        )
        VALUES
        (
            NULL,
            @StrategicDomainTypeId,
            'governance',
            'Governance',
            'Strategic direction, authority, accountability, and policy stewardship for the organization.'
        );

        SET @GovernanceId = (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'governance');
    END;

    IF @StrategicPlanningId IS NULL
    BEGIN
        INSERT INTO dbo.Domains
        (
            DomainParentId,
            DomainTypeId,
            DomainCode,
            DisplayName,
            Description
        )
        VALUES
        (
            NULL,
            @StrategicDomainTypeId,
            'strategic-planning-root',
            'Strategic Planning',
            'Strategic direction-setting, planning alignment, organizational improvement, and risk-informed corporate planning.'
        );

        SET @StrategicPlanningId = (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'strategic-planning-root');
    END;

    IF @IntergovernmentalRelationsId IS NULL
    BEGIN
        INSERT INTO dbo.Domains
        (
            DomainParentId,
            DomainTypeId,
            DomainCode,
            DisplayName,
            Description
        )
        VALUES
        (
            NULL,
            @StrategicDomainTypeId,
            'intergovernmental-relations',
            'Intergovernmental Relations',
            'Strategic relationships, agreements, coordination, and advocacy across federal, provincial, regional, and Indigenous governments.'
        );

        SET @IntergovernmentalRelationsId = (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'intergovernmental-relations');
    END;

    IF @RightsTitleJurisdictionId IS NULL
    BEGIN
        INSERT INTO dbo.Domains
        (
            DomainParentId,
            DomainTypeId,
            DomainCode,
            DisplayName,
            Description
        )
        VALUES
        (
            NULL,
            @StrategicDomainTypeId,
            'rights-title-and-jurisdiction',
            'Rights, Title, and Jurisdiction',
            'Strategic advancement and protection of rights, title, jurisdiction, lands, and legal-constitutional interests.'
        );

        SET @RightsTitleJurisdictionId = (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'rights-title-and-jurisdiction');
    END;

    IF @CommunityDevelopmentId IS NULL
    BEGIN
        INSERT INTO dbo.Domains
        (
            DomainParentId,
            DomainTypeId,
            DomainCode,
            DisplayName,
            Description
        )
        VALUES
        (
            NULL,
            @StrategicDomainTypeId,
            'community-development',
            'Community Development',
            'Strategic advancement of social, economic, housing, health, education, and cultural well-being in the community.'
        );

        SET @CommunityDevelopmentId = (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'community-development');
    END;

    IF @FinancialSustainabilityId IS NULL
    BEGIN
        INSERT INTO dbo.Domains
        (
            DomainParentId,
            DomainTypeId,
            DomainCode,
            DisplayName,
            Description
        )
        VALUES
        (
            NULL,
            @StrategicDomainTypeId,
            'financial-sustainability',
            'Financial Sustainability',
            'Strategic stewardship of long-term financial planning, revenue strength, operational finance, procurement, and audit control.'
        );

        SET @FinancialSustainabilityId = (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'financial-sustainability');
    END;

    IF @RiskResilienceId IS NULL
    BEGIN
        INSERT INTO dbo.Domains
        (
            DomainParentId,
            DomainTypeId,
            DomainCode,
            DisplayName,
            Description
        )
        VALUES
        (
            NULL,
            @StrategicDomainTypeId,
            'risk-and-resilience',
            'Risk and Resilience',
            'Strategic management of organizational risk, emergency readiness, continuity, security, and infrastructure resilience.'
        );

        SET @RiskResilienceId = (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'risk-and-resilience');
    END;

    IF @CouncilGovernanceId IS NULL
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES
        (
            @GovernanceId,
            @TacticalDomainTypeId,
            'council-governance',
            'Council Governance',
            'Tactical coordination of council oversight, records, and decision support.'
        );

        SET @CouncilGovernanceId = (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'council-governance');
    END;

    IF @AdministrativeGovernanceId IS NULL
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES
        (
            @GovernanceId,
            @TacticalDomainTypeId,
            'administrative-governance',
            'Administrative Governance',
            'Tactical governance of executive authority, management structure, and internal decision control.'
        );

        SET @AdministrativeGovernanceId = (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'administrative-governance');
    END;

    IF @PolicyGovernanceId IS NULL
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES
        (
            @GovernanceId,
            @TacticalDomainTypeId,
            'policy-governance',
            'Policy Governance',
            'Tactical control of policy lifecycle management, approvals, and compliance oversight.'
        );

        SET @PolicyGovernanceId = (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'policy-governance');
    END;

    IF @AccountabilityEthicsId IS NULL
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES
        (
            @GovernanceId,
            @TacticalDomainTypeId,
            'accountability-and-ethics',
            'Accountability and Ethics',
            'Tactical governance of ethical conduct, complaints, transparency, and follow-up accountability.'
        );

        SET @AccountabilityEthicsId = (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'accountability-and-ethics');
    END;

    IF @CorporatePlanningId IS NULL
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES
        (
            @StrategicPlanningId,
            @TacticalDomainTypeId,
            'corporate-planning',
            'Corporate Planning',
            'Tactical planning of organization-wide priorities, annual plans, and coordinated work planning.'
        );

        SET @CorporatePlanningId = (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'corporate-planning');
    END;

    IF @PerformanceManagementId IS NULL
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES
        (
            @StrategicPlanningId,
            @TacticalDomainTypeId,
            'performance-management',
            'Performance Management',
            'Tactical oversight of measures, reporting, standards, and performance review practices.'
        );

        SET @PerformanceManagementId = (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'performance-management');
    END;

    IF @OrganizationalDevelopmentId IS NULL
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES
        (
            @StrategicPlanningId,
            @TacticalDomainTypeId,
            'organizational-development',
            'Organizational Development',
            'Tactical design and improvement of mandate, service structure, processes, and management systems.'
        );

        SET @OrganizationalDevelopmentId = (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'organizational-development');
    END;

    IF @CorporateRiskPlanningId IS NULL
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES
        (
            @StrategicPlanningId,
            @TacticalDomainTypeId,
            'corporate-risk-planning',
            'Corporate Risk Planning',
            'Tactical planning and oversight of organizational risk, continuity, controls, and mitigation strategy.'
        );

        SET @CorporateRiskPlanningId = (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'corporate-risk-planning');
    END;

    IF @FederalRelationsId IS NULL
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES
        (
            @IntergovernmentalRelationsId,
            @TacticalDomainTypeId,
            'federal-relations',
            'Federal Relations',
            'Tactical management of federal agreements, negotiations, reporting, and correspondence.'
        );

        SET @FederalRelationsId = (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'federal-relations');
    END;

    IF @ProvincialRelationsId IS NULL
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES
        (
            @IntergovernmentalRelationsId,
            @TacticalDomainTypeId,
            'provincial-relations',
            'Provincial Relations',
            'Tactical coordination of provincial agreements, reporting, regulation, and joint initiatives.'
        );

        SET @ProvincialRelationsId = (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'provincial-relations');
    END;

    IF @RegionalMunicipalRelationsId IS NULL
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES
        (
            @IntergovernmentalRelationsId,
            @TacticalDomainTypeId,
            'regional-and-municipal-relations',
            'Regional and Municipal Relations',
            'Tactical coordination with regional and municipal partners on shared services, planning, and emergency matters.'
        );

        SET @RegionalMunicipalRelationsId = (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'regional-and-municipal-relations');
    END;

    IF @IndigenousGovernmentRelationsId IS NULL
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES
        (
            @IntergovernmentalRelationsId,
            @TacticalDomainTypeId,
            'indigenous-government-relations',
            'Indigenous Government Relations',
            'Tactical support for nation-to-nation engagement, advocacy, coalition work, and intergovernmental briefings.'
        );

        SET @IndigenousGovernmentRelationsId = (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'indigenous-government-relations');
    END;

    IF @JurisdictionDevelopmentId IS NULL
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES
        (
            @RightsTitleJurisdictionId,
            @TacticalDomainTypeId,
            'jurisdiction-development',
            'Jurisdiction Development',
            'Tactical development of laws, agreements, and implementation pathways for Indigenous jurisdiction.'
        );

        SET @JurisdictionDevelopmentId = (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'jurisdiction-development');
    END;

    IF @RightsProtectionId IS NULL
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES
        (
            @RightsTitleJurisdictionId,
            @TacticalDomainTypeId,
            'rights-protection',
            'Rights Protection',
            'Tactical protection of rights through review, consultation tracking, and coordinated position development.'
        );

        SET @RightsProtectionId = (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'rights-protection');
    END;

    IF @LandsTerritoryId IS NULL
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES
        (
            @RightsTitleJurisdictionId,
            @TacticalDomainTypeId,
            'lands-and-territory',
            'Lands and Territory',
            'Tactical stewardship of land claims, land use, mapping, and territorial records.'
        );

        SET @LandsTerritoryId = (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'lands-and-territory');
    END;

    IF @LegalConstitutionalAffairsId IS NULL
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES
        (
            @RightsTitleJurisdictionId,
            @TacticalDomainTypeId,
            'legal-and-constitutional-affairs',
            'Legal and Constitutional Affairs',
            'Tactical legal support for constitutional matters, agreement review, litigation, and legislative monitoring.'
        );

        SET @LegalConstitutionalAffairsId = (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'legal-and-constitutional-affairs');
    END;

    IF @SocialDevelopmentId IS NULL
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES
        (
            @CommunityDevelopmentId,
            @TacticalDomainTypeId,
            'social-development',
            'Social Development',
            'Tactical coordination of social supports, wellness initiatives, and service records for community members.'
        );

        SET @SocialDevelopmentId = (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'social-development');
    END;

    IF @EconomicDevelopmentId IS NULL
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES
        (
            @CommunityDevelopmentId,
            @TacticalDomainTypeId,
            'economic-development',
            'Economic Development',
            'Tactical support for business growth, employment, investment, partnerships, and development projects.'
        );

        SET @EconomicDevelopmentId = (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'economic-development');
    END;

    IF @HousingDevelopmentId IS NULL
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES
        (
            @CommunityDevelopmentId,
            @TacticalDomainTypeId,
            'housing-development',
            'Housing Development',
            'Tactical administration of housing planning, allocation, maintenance, and tenant-related services.'
        );

        SET @HousingDevelopmentId = (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'housing-development');
    END;

    IF @HealthWellnessId IS NULL
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES
        (
            @CommunityDevelopmentId,
            @TacticalDomainTypeId,
            'health-and-wellness',
            'Health and Wellness',
            'Tactical coordination of community health programming, prevention, referrals, and wellness activities.'
        );

        SET @HealthWellnessId = (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'health-and-wellness');
    END;

    IF @EducationTrainingId IS NULL
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES
        (
            @CommunityDevelopmentId,
            @TacticalDomainTypeId,
            'education-and-training',
            'Education and Training',
            'Tactical support for learners, training programs, school liaison, and education-related records.'
        );

        SET @EducationTrainingId = (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'education-and-training');
    END;

    IF @CultureLanguageRecreationId IS NULL
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES
        (
            @CommunityDevelopmentId,
            @TacticalDomainTypeId,
            'culture-language-and-recreation',
            'Culture, Language, and Recreation',
            'Tactical support for language revitalization, cultural programming, recreation, and related community events.'
        );

        SET @CultureLanguageRecreationId = (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'culture-language-and-recreation');
    END;

    IF @FinancialPlanningId IS NULL
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES
        (
            @FinancialSustainabilityId,
            @TacticalDomainTypeId,
            'financial-planning',
            'Financial Planning',
            'Tactical planning of budgets, forecasts, reserves, and long-range financial requirements.'
        );

        SET @FinancialPlanningId = (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'financial-planning');
    END;

    IF @RevenueManagementId IS NULL
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES
        (
            @FinancialSustainabilityId,
            @TacticalDomainTypeId,
            'revenue-management',
            'Revenue Management',
            'Tactical coordination of own-source revenue, grants, fees, and funding submissions.'
        );

        SET @RevenueManagementId = (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'revenue-management');
    END;

    IF @FinancialOperationsId IS NULL
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES
        (
            @FinancialSustainabilityId,
            @TacticalDomainTypeId,
            'financial-operations',
            'Financial Operations',
            'Tactical management of day-to-day financial processing, purchasing, payroll, and reporting.'
        );

        SET @FinancialOperationsId = (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'financial-operations');
    END;

    IF @ProcurementContractingId IS NULL
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES
        (
            @FinancialSustainabilityId,
            @TacticalDomainTypeId,
            'procurement-and-contracting',
            'Procurement and Contracting',
            'Tactical oversight of tendering, vendors, contracts, purchase orders, and procurement records.'
        );

        SET @ProcurementContractingId = (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'procurement-and-contracting');
    END;

    IF @AuditControlsId IS NULL
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES
        (
            @FinancialSustainabilityId,
            @TacticalDomainTypeId,
            'audit-and-controls',
            'Audit and Controls',
            'Tactical coordination of audits, financial controls, compliance review, and control records.'
        );

        SET @AuditControlsId = (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'audit-and-controls');
    END;

    IF @EnterpriseRiskManagementId IS NULL
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES
        (
            @RiskResilienceId,
            @TacticalDomainTypeId,
            'enterprise-risk-management',
            'Enterprise Risk Management',
            'Tactical coordination of enterprise risk identification, assessment, mitigation, and reporting.'
        );

        SET @EnterpriseRiskManagementId = (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'enterprise-risk-management');
    END;

    IF @EmergencyManagementId IS NULL
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES
        (
            @RiskResilienceId,
            @TacticalDomainTypeId,
            'emergency-management',
            'Emergency Management',
            'Tactical coordination of preparedness, response, recovery, and emergency communication activities.'
        );

        SET @EmergencyManagementId = (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'emergency-management');
    END;

    IF @BusinessContinuityId IS NULL
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES
        (
            @RiskResilienceId,
            @TacticalDomainTypeId,
            'business-continuity',
            'Business Continuity',
            'Tactical planning and maintenance of continuity capabilities for critical services and operations.'
        );

        SET @BusinessContinuityId = (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'business-continuity');
    END;

    IF @InformationSecurityId IS NULL
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES
        (
            @RiskResilienceId,
            @TacticalDomainTypeId,
            'information-security',
            'Information Security',
            'Tactical protection of information systems through security operations, access control, and incident handling.'
        );

        SET @InformationSecurityId = (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'information-security');
    END;

    IF @InfrastructureResilienceId IS NULL
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES
        (
            @RiskResilienceId,
            @TacticalDomainTypeId,
            'infrastructure-resilience',
            'Infrastructure Resilience',
            'Tactical support for resilient facilities, assets, inspections, and adaptation planning.'
        );

        SET @InfrastructureResilienceId = (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'infrastructure-resilience');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'council-meetings')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@CouncilGovernanceId, @OperationalDomainTypeId, 'council-meetings', 'Council Meetings', 'Operational planning, preparation, and administration of formal council meetings.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'resolutions')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@CouncilGovernanceId, @OperationalDomainTypeId, 'resolutions', 'Resolutions', 'Operational control of drafting, approving, indexing, and maintaining council resolutions.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'agendas-and-minutes')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@CouncilGovernanceId, @OperationalDomainTypeId, 'agendas-and-minutes', 'Agendas and Minutes', 'Operational preparation and retention of meeting agendas, minutes, and supporting notes.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'committee-support')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@CouncilGovernanceId, @OperationalDomainTypeId, 'committee-support', 'Committee Support', 'Operational support for committee scheduling, records, and action follow-up.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'council-records')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@CouncilGovernanceId, @OperationalDomainTypeId, 'council-records', 'Council Records', 'Operational management of official council records, archives, and retrieval.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'delegation-of-authority')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@AdministrativeGovernanceId, @OperationalDomainTypeId, 'delegation-of-authority', 'Delegation of Authority', 'Operational administration of delegated approval rights, limits, and responsibilities.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'organizational-structure')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@AdministrativeGovernanceId, @OperationalDomainTypeId, 'organizational-structure', 'Organizational Structure', 'Operational maintenance of reporting structures, units, and governance roles.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'executive-directives')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@AdministrativeGovernanceId, @OperationalDomainTypeId, 'executive-directives', 'Executive Directives', 'Operational handling of executive instructions, decisions, and formal directives.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'management-committees')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@AdministrativeGovernanceId, @OperationalDomainTypeId, 'management-committees', 'Management Committees', 'Operational support for internal management committee coordination and records.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'decision-tracking')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@AdministrativeGovernanceId, @OperationalDomainTypeId, 'decision-tracking', 'Decision Tracking', 'Operational monitoring of governance decisions, actions, and completion status.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'policy-development')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@PolicyGovernanceId, @OperationalDomainTypeId, 'policy-development', 'Policy Development', 'Operational drafting and coordination of new or revised policy instruments.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'policy-review')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@PolicyGovernanceId, @OperationalDomainTypeId, 'policy-review', 'Policy Review', 'Operational review of policy quality, relevance, and required updates.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'policy-approval')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@PolicyGovernanceId, @OperationalDomainTypeId, 'policy-approval', 'Policy Approval', 'Operational processing of policy endorsement, sign-off, and release steps.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'policy-register')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@PolicyGovernanceId, @OperationalDomainTypeId, 'policy-register', 'Policy Register', 'Operational maintenance of the official policy inventory and version record.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'policy-compliance-monitoring')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@PolicyGovernanceId, @OperationalDomainTypeId, 'policy-compliance-monitoring', 'Policy Compliance Monitoring', 'Operational monitoring of policy adherence, gaps, and corrective action needs.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'conflict-of-interest')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@AccountabilityEthicsId, @OperationalDomainTypeId, 'conflict-of-interest', 'Conflict of Interest', 'Operational handling of conflict disclosures, reviews, and mitigation actions.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'code-of-conduct')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@AccountabilityEthicsId, @OperationalDomainTypeId, 'code-of-conduct', 'Code of Conduct', 'Operational administration of expected conduct standards and related guidance.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'complaint-handling')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@AccountabilityEthicsId, @OperationalDomainTypeId, 'complaint-handling', 'Complaint Handling', 'Operational intake, review, and resolution tracking for complaints and concerns.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'transparency-reporting')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@AccountabilityEthicsId, @OperationalDomainTypeId, 'transparency-reporting', 'Transparency Reporting', 'Operational reporting of governance information, disclosures, and public accountability materials.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'audit-follow-up')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@AccountabilityEthicsId, @OperationalDomainTypeId, 'audit-follow-up', 'Audit Follow-up', 'Operational tracking of audit findings, responses, and remediation progress.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'strategic-plan-management')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@CorporatePlanningId, @OperationalDomainTypeId, 'strategic-plan-management', 'Strategic Plan Management', 'Operational coordination, maintenance, and update of the strategic plan.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'annual-planning')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@CorporatePlanningId, @OperationalDomainTypeId, 'annual-planning', 'Annual Planning', 'Operational preparation and coordination of annual planning cycles and deliverables.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'departmental-workplans')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@CorporatePlanningId, @OperationalDomainTypeId, 'departmental-workplans', 'Departmental Workplans', 'Operational management of department-level workplans and aligned activities.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'priority-tracking')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@CorporatePlanningId, @OperationalDomainTypeId, 'priority-tracking', 'Priority Tracking', 'Operational tracking of strategic and corporate priorities across work areas.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'progress-reporting')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@CorporatePlanningId, @OperationalDomainTypeId, 'progress-reporting', 'Progress Reporting', 'Operational reporting of plan progress, status updates, and delivery outcomes.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'indicators-and-measures')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@PerformanceManagementId, @OperationalDomainTypeId, 'indicators-and-measures', 'Indicators and Measures', 'Operational definition and maintenance of performance indicators and measurement logic.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'departmental-reporting')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@PerformanceManagementId, @OperationalDomainTypeId, 'departmental-reporting', 'Departmental Reporting', 'Operational collection and preparation of department-level performance reporting.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'service-standards')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@PerformanceManagementId, @OperationalDomainTypeId, 'service-standards', 'Service Standards', 'Operational administration of service standards, targets, and expected delivery levels.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'evaluation')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@PerformanceManagementId, @OperationalDomainTypeId, 'evaluation', 'Evaluation', 'Operational evaluation of program effectiveness, results, and improvement opportunities.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'dashboard-management')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@PerformanceManagementId, @OperationalDomainTypeId, 'dashboard-management', 'Dashboard Management', 'Operational design, update, and maintenance of reporting dashboards and views.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'mandate-review')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@OrganizationalDevelopmentId, @OperationalDomainTypeId, 'mandate-review', 'Mandate Review', 'Operational review of organizational mandates, fit, and required updates.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'service-structure-design')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@OrganizationalDevelopmentId, @OperationalDomainTypeId, 'service-structure-design', 'Service Structure Design', 'Operational design of service organization, functions, and delivery structures.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'process-improvement')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@OrganizationalDevelopmentId, @OperationalDomainTypeId, 'process-improvement', 'Process Improvement', 'Operational improvement of workflows, efficiency, and process performance.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'change-management')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@OrganizationalDevelopmentId, @OperationalDomainTypeId, 'change-management', 'Change Management', 'Operational planning and support for organizational change adoption and transition.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'management-system-design')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@OrganizationalDevelopmentId, @OperationalDomainTypeId, 'management-system-design', 'Management System Design', 'Operational design of management systems, controls, and coordination structures.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'risk-register')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@CorporateRiskPlanningId, @OperationalDomainTypeId, 'risk-register', 'Risk Register', 'Operational maintenance of the corporate risk register and risk record updates.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'mitigation-planning')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@CorporateRiskPlanningId, @OperationalDomainTypeId, 'mitigation-planning', 'Mitigation Planning', 'Operational planning of risk responses, mitigation actions, and ownership.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'business-continuity-planning')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@CorporateRiskPlanningId, @OperationalDomainTypeId, 'business-continuity-planning', 'Business Continuity Planning', 'Operational continuity planning for disruption response, recovery, and resilience.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'internal-controls-review')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@CorporateRiskPlanningId, @OperationalDomainTypeId, 'internal-controls-review', 'Internal Controls Review', 'Operational review of internal controls, control gaps, and improvement actions.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'risk-reporting')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@CorporateRiskPlanningId, @OperationalDomainTypeId, 'risk-reporting', 'Risk Reporting', 'Operational reporting of risk status, trends, and mitigation progress.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'funding-agreements')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@FederalRelationsId, @OperationalDomainTypeId, 'funding-agreements', 'Funding Agreements', 'Operational administration of federal funding agreements, terms, and renewals.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'federal-reporting')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@FederalRelationsId, @OperationalDomainTypeId, 'federal-reporting', 'Federal Reporting', 'Operational preparation and submission of required federal reports and updates.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'program-negotiations')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@FederalRelationsId, @OperationalDomainTypeId, 'program-negotiations', 'Program Negotiations', 'Operational support for negotiation of federal programs, terms, and implementation details.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'compliance-submissions')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@FederalRelationsId, @OperationalDomainTypeId, 'compliance-submissions', 'Compliance Submissions', 'Operational preparation of compliance filings, attestations, and supporting submissions.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'correspondence-management')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@FederalRelationsId, @OperationalDomainTypeId, 'correspondence-management', 'Correspondence Management', 'Operational tracking and management of formal intergovernmental correspondence.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'provincial-agreements')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@ProvincialRelationsId, @OperationalDomainTypeId, 'provincial-agreements', 'Provincial Agreements', 'Operational administration of provincial agreements, commitments, and renewals.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'regulatory-coordination')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@ProvincialRelationsId, @OperationalDomainTypeId, 'regulatory-coordination', 'Regulatory Coordination', 'Operational coordination of provincial regulatory matters, responses, and follow-up.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'joint-initiatives')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@ProvincialRelationsId, @OperationalDomainTypeId, 'joint-initiatives', 'Joint Initiatives', 'Operational coordination of shared initiatives, working groups, and collaborative actions.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'provincial-reporting')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@ProvincialRelationsId, @OperationalDomainTypeId, 'provincial-reporting', 'Provincial Reporting', 'Operational preparation and submission of provincial reports and accountability materials.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'issue-tracking')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@ProvincialRelationsId, @OperationalDomainTypeId, 'issue-tracking', 'Issue Tracking', 'Operational logging, monitoring, and follow-up of intergovernmental issues and files.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'service-agreements')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@RegionalMunicipalRelationsId, @OperationalDomainTypeId, 'service-agreements', 'Service Agreements', 'Operational management of local service agreements and shared service arrangements.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'shared-infrastructure-coordination')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@RegionalMunicipalRelationsId, @OperationalDomainTypeId, 'shared-infrastructure-coordination', 'Shared Infrastructure Coordination', 'Operational coordination of shared infrastructure planning, use, and partner alignment.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'emergency-coordination')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@RegionalMunicipalRelationsId, @OperationalDomainTypeId, 'emergency-coordination', 'Emergency Coordination', 'Operational intergovernmental coordination for emergency preparedness, response, and recovery.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'regional-planning')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@RegionalMunicipalRelationsId, @OperationalDomainTypeId, 'regional-planning', 'Regional Planning', 'Operational participation in regional planning, land use, and joint planning processes.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'meeting-records')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@RegionalMunicipalRelationsId, @OperationalDomainTypeId, 'meeting-records', 'Meeting Records', 'Operational management of intergovernmental meeting notes, records, and follow-up items.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'nation-to-nation-relations')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@IndigenousGovernmentRelationsId, @OperationalDomainTypeId, 'nation-to-nation-relations', 'Nation-to-Nation Relations', 'Operational support for government-to-government relationship building and related engagements.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'political-advocacy')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@IndigenousGovernmentRelationsId, @OperationalDomainTypeId, 'political-advocacy', 'Political Advocacy', 'Operational development and coordination of advocacy positions, messages, and outreach.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'assembly-participation')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@IndigenousGovernmentRelationsId, @OperationalDomainTypeId, 'assembly-participation', 'Assembly Participation', 'Operational preparation and support for participation in assemblies and political forums.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'coalition-work')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@IndigenousGovernmentRelationsId, @OperationalDomainTypeId, 'coalition-work', 'Coalition Work', 'Operational collaboration with coalitions, alliances, and partner groups on shared issues.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'intergovernmental-briefings')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@IndigenousGovernmentRelationsId, @OperationalDomainTypeId, 'intergovernmental-briefings', 'Intergovernmental Briefings', 'Operational preparation of briefings, notes, and summaries for intergovernmental engagement.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'law-making-initiatives')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@JurisdictionDevelopmentId, @OperationalDomainTypeId, 'law-making-initiatives', 'Law-Making Initiatives', 'Operational development and coordination of Indigenous law-making initiatives and related drafting work.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'governance-agreements')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@JurisdictionDevelopmentId, @OperationalDomainTypeId, 'governance-agreements', 'Governance Agreements', 'Operational negotiation and management of agreements supporting governance authority and jurisdiction.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'jurisdictional-research')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@JurisdictionDevelopmentId, @OperationalDomainTypeId, 'jurisdictional-research', 'Jurisdictional Research', 'Operational research on jurisdictional models, authorities, and precedent.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'authority-mapping')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@JurisdictionDevelopmentId, @OperationalDomainTypeId, 'authority-mapping', 'Authority Mapping', 'Operational mapping of authorities, responsibilities, and decision rights across governance areas.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'implementation-tracking')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@JurisdictionDevelopmentId, @OperationalDomainTypeId, 'implementation-tracking', 'Implementation Tracking', 'Operational tracking of jurisdiction implementation actions, milestones, and readiness.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'rights-based-review')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@RightsProtectionId, @OperationalDomainTypeId, 'rights-based-review', 'Rights-Based Review', 'Operational review of proposals and decisions through a rights-based lens.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'consultation-tracking')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@RightsProtectionId, @OperationalDomainTypeId, 'consultation-tracking', 'Consultation Tracking', 'Operational tracking of consultations, engagement obligations, and follow-up actions.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'impact-assessment-review')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@RightsProtectionId, @OperationalDomainTypeId, 'impact-assessment-review', 'Impact Assessment Review', 'Operational review of impact assessments for rights, title, and jurisdiction implications.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'legal-coordination')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@RightsProtectionId, @OperationalDomainTypeId, 'legal-coordination', 'Legal Coordination', 'Operational coordination with legal advisors on rights-related matters and responses.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'position-papers')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@RightsProtectionId, @OperationalDomainTypeId, 'position-papers', 'Position Papers', 'Operational preparation of formal positions, briefing notes, and rights-based arguments.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'land-claims')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@LandsTerritoryId, @OperationalDomainTypeId, 'land-claims', 'Land Claims', 'Operational management of land claim files, submissions, and supporting materials.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'land-use-planning')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@LandsTerritoryId, @OperationalDomainTypeId, 'land-use-planning', 'Land Use Planning', 'Operational planning for land use, development considerations, and territorial priorities.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'mapping-and-gis')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@LandsTerritoryId, @OperationalDomainTypeId, 'mapping-and-gis', 'Mapping and GIS', 'Operational mapping, spatial analysis, and GIS support for lands and territory work.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'environmental-review')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@LandsTerritoryId, @OperationalDomainTypeId, 'environmental-review', 'Environmental Review', 'Operational review of environmental matters affecting lands, territory, and rights interests.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'territory-records')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@LandsTerritoryId, @OperationalDomainTypeId, 'territory-records', 'Territory Records', 'Operational management of territorial records, historical files, and land-related documentation.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'legal-opinions')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@LegalConstitutionalAffairsId, @OperationalDomainTypeId, 'legal-opinions', 'Legal Opinions', 'Operational management of legal opinions and advisory memoranda.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'litigation-support')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@LegalConstitutionalAffairsId, @OperationalDomainTypeId, 'litigation-support', 'Litigation Support', 'Operational support for litigation preparation, coordination, and record handling.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'agreement-review')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@LegalConstitutionalAffairsId, @OperationalDomainTypeId, 'agreement-review', 'Agreement Review', 'Operational legal and constitutional review of agreements and formal instruments.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'legislative-monitoring')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@LegalConstitutionalAffairsId, @OperationalDomainTypeId, 'legislative-monitoring', 'Legislative Monitoring', 'Operational monitoring of legislation, amendments, and related legal developments.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'legal-records')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@LegalConstitutionalAffairsId, @OperationalDomainTypeId, 'legal-records', 'Legal Records', 'Operational management of legal files, records, and supporting documentation.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'social-programs')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@SocialDevelopmentId, @OperationalDomainTypeId, 'social-programs', 'Social Programs', 'Operational administration of social support programs and related services.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'family-support-services')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@SocialDevelopmentId, @OperationalDomainTypeId, 'family-support-services', 'Family Support Services', 'Operational coordination of family supports, referrals, and assistance services.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'poverty-reduction-initiatives')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@SocialDevelopmentId, @OperationalDomainTypeId, 'poverty-reduction-initiatives', 'Poverty Reduction Initiatives', 'Operational support for poverty reduction actions, resources, and community initiatives.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'community-wellness-programs')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@SocialDevelopmentId, @OperationalDomainTypeId, 'community-wellness-programs', 'Community Wellness Programs', 'Operational delivery and coordination of community wellness activities and supports.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'client-service-records')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@SocialDevelopmentId, @OperationalDomainTypeId, 'client-service-records', 'Client Service Records', 'Operational management of client-facing service records and support documentation.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'business-support')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@EconomicDevelopmentId, @OperationalDomainTypeId, 'business-support', 'Business Support', 'Operational support for local businesses, entrepreneurs, and enterprise development.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'employment-initiatives')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@EconomicDevelopmentId, @OperationalDomainTypeId, 'employment-initiatives', 'Employment Initiatives', 'Operational coordination of employment programs, job supports, and labour participation efforts.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'partnership-development')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@EconomicDevelopmentId, @OperationalDomainTypeId, 'partnership-development', 'Partnership Development', 'Operational development of partnerships that support economic opportunities and shared initiatives.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'investment-planning')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@EconomicDevelopmentId, @OperationalDomainTypeId, 'investment-planning', 'Investment Planning', 'Operational planning for investment attraction, readiness, and project opportunity development.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'project-files')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@EconomicDevelopmentId, @OperationalDomainTypeId, 'project-files', 'Project Files', 'Operational management of economic development project files and supporting records.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'housing-program-planning')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@HousingDevelopmentId, @OperationalDomainTypeId, 'housing-program-planning', 'Housing Program Planning', 'Operational planning and coordination of housing programs and related activities.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'housing-applications')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@HousingDevelopmentId, @OperationalDomainTypeId, 'housing-applications', 'Housing Applications', 'Operational intake and processing of housing applications and related documentation.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'allocation-management')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@HousingDevelopmentId, @OperationalDomainTypeId, 'allocation-management', 'Allocation Management', 'Operational management of housing allocation decisions, waitlists, and placement tracking.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'maintenance-coordination')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@HousingDevelopmentId, @OperationalDomainTypeId, 'maintenance-coordination', 'Maintenance Coordination', 'Operational coordination of housing repairs, maintenance requests, and follow-up work.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'tenant-records')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@HousingDevelopmentId, @OperationalDomainTypeId, 'tenant-records', 'Tenant Records', 'Operational management of tenant records, occupancy files, and housing documentation.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'community-health-programs')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@HealthWellnessId, @OperationalDomainTypeId, 'community-health-programs', 'Community Health Programs', 'Operational support for health promotion and community-based health programming.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'prevention-initiatives')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@HealthWellnessId, @OperationalDomainTypeId, 'prevention-initiatives', 'Prevention Initiatives', 'Operational planning and delivery of prevention-focused health and wellness initiatives.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'referral-coordination')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@HealthWellnessId, @OperationalDomainTypeId, 'referral-coordination', 'Referral Coordination', 'Operational coordination of referrals, intake, and navigation to health and wellness services.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'wellness-events')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@HealthWellnessId, @OperationalDomainTypeId, 'wellness-events', 'Wellness Events', 'Operational planning and support for wellness events, campaigns, and gatherings.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'health-program-records')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@HealthWellnessId, @OperationalDomainTypeId, 'health-program-records', 'Health Program Records', 'Operational management of health program records and related documentation.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'student-support')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@EducationTrainingId, @OperationalDomainTypeId, 'student-support', 'Student Support', 'Operational support for learners, student services, and education-related assistance.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'post-secondary-support')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@EducationTrainingId, @OperationalDomainTypeId, 'post-secondary-support', 'Post-Secondary Support', 'Operational administration of post-secondary funding, guidance, and learner support.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'training-programs')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@EducationTrainingId, @OperationalDomainTypeId, 'training-programs', 'Training Programs', 'Operational coordination of skills development and community training programs.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'school-liaison')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@EducationTrainingId, @OperationalDomainTypeId, 'school-liaison', 'School Liaison', 'Operational liaison with schools, educators, and education partners.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'education-records')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@EducationTrainingId, @OperationalDomainTypeId, 'education-records', 'Education Records', 'Operational management of education support records and learner documentation.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'language-programs')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@CultureLanguageRecreationId, @OperationalDomainTypeId, 'language-programs', 'Language Programs', 'Operational delivery and support of language revitalization and learning programs.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'cultural-programming')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@CultureLanguageRecreationId, @OperationalDomainTypeId, 'cultural-programming', 'Cultural Programming', 'Operational planning and delivery of cultural activities, teachings, and gatherings.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'youth-recreation')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@CultureLanguageRecreationId, @OperationalDomainTypeId, 'youth-recreation', 'Youth Recreation', 'Operational coordination of youth recreation programs and related activities.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'elders-programming')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@CultureLanguageRecreationId, @OperationalDomainTypeId, 'elders-programming', 'Elders Programming', 'Operational support for Elders-focused cultural, social, and wellness programming.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'event-records')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@CultureLanguageRecreationId, @OperationalDomainTypeId, 'event-records', 'Event Records', 'Operational management of records for community cultural and recreation events.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'annual-budgeting')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@FinancialPlanningId, @OperationalDomainTypeId, 'annual-budgeting', 'Annual Budgeting', 'Operational preparation and coordination of annual budgets and budget submissions.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'multi-year-forecasting')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@FinancialPlanningId, @OperationalDomainTypeId, 'multi-year-forecasting', 'Multi-Year Forecasting', 'Operational forecasting of revenue, expenses, and multi-year financial outlooks.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'capital-planning')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@FinancialPlanningId, @OperationalDomainTypeId, 'capital-planning', 'Capital Planning', 'Operational planning for capital investments, replacement needs, and infrastructure funding.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'reserve-planning')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@FinancialPlanningId, @OperationalDomainTypeId, 'reserve-planning', 'Reserve Planning', 'Operational planning for reserve use, replenishment, and financial resilience.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'budget-records')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@FinancialPlanningId, @OperationalDomainTypeId, 'budget-records', 'Budget Records', 'Operational management of budget records, worksheets, and approved planning files.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'own-source-revenue')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@RevenueManagementId, @OperationalDomainTypeId, 'own-source-revenue', 'Own-Source Revenue', 'Operational administration of revenue generated from local sources and enterprise activity.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'grants-and-contributions')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@RevenueManagementId, @OperationalDomainTypeId, 'grants-and-contributions', 'Grants and Contributions', 'Operational management of grant and contribution funding opportunities and awards.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'fee-structures')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@RevenueManagementId, @OperationalDomainTypeId, 'fee-structures', 'Fee Structures', 'Operational development and maintenance of fees, rates, and charging structures.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'funding-applications')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@RevenueManagementId, @OperationalDomainTypeId, 'funding-applications', 'Funding Applications', 'Operational preparation of funding applications and supporting submission materials.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'revenue-reporting')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@RevenueManagementId, @OperationalDomainTypeId, 'revenue-reporting', 'Revenue Reporting', 'Operational reporting of revenue performance, trends, and funding status.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'accounts-payable')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@FinancialOperationsId, @OperationalDomainTypeId, 'accounts-payable', 'Accounts Payable', 'Operational processing of invoices, payments, and payable-related transactions.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'accounts-receivable')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@FinancialOperationsId, @OperationalDomainTypeId, 'accounts-receivable', 'Accounts Receivable', 'Operational invoicing, receivable tracking, and collection follow-up.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'payroll')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@FinancialOperationsId, @OperationalDomainTypeId, 'payroll', 'Payroll', 'Operational administration of payroll processing, deductions, and payroll records.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'purchasing')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@FinancialOperationsId, @OperationalDomainTypeId, 'purchasing', 'Purchasing', 'Operational purchasing support for goods, services, and internal procurement requests.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'financial-reporting')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@FinancialOperationsId, @OperationalDomainTypeId, 'financial-reporting', 'Financial Reporting', 'Operational preparation of financial statements, schedules, and internal finance reports.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'tendering')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@ProcurementContractingId, @OperationalDomainTypeId, 'tendering', 'Tendering', 'Operational administration of tendering processes, bid management, and related documentation.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'vendor-management')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@ProcurementContractingId, @OperationalDomainTypeId, 'vendor-management', 'Vendor Management', 'Operational maintenance of vendor relationships, records, and performance information.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'contract-administration')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@ProcurementContractingId, @OperationalDomainTypeId, 'contract-administration', 'Contract Administration', 'Operational administration of contracts, terms, deliverables, and renewals.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'purchase-orders')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@ProcurementContractingId, @OperationalDomainTypeId, 'purchase-orders', 'Purchase Orders', 'Operational issuance, tracking, and management of purchase orders.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'procurement-records')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@ProcurementContractingId, @OperationalDomainTypeId, 'procurement-records', 'Procurement Records', 'Operational management of procurement files, records, and sourcing documentation.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'internal-controls')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@AuditControlsId, @OperationalDomainTypeId, 'internal-controls', 'Internal Controls', 'Operational documentation and review of internal financial controls and safeguards.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'external-audit')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@AuditControlsId, @OperationalDomainTypeId, 'external-audit', 'External Audit', 'Operational coordination of external audit requirements, evidence, and responses.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'compliance-review')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@AuditControlsId, @OperationalDomainTypeId, 'compliance-review', 'Compliance Review', 'Operational review of financial compliance obligations, controls, and corrective needs.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'financial-policies')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@AuditControlsId, @OperationalDomainTypeId, 'financial-policies', 'Financial Policies', 'Operational management of financial policies, standards, and supporting guidance.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'audit-records')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@AuditControlsId, @OperationalDomainTypeId, 'audit-records', 'Audit Records', 'Operational management of audit files, working papers, and supporting records.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'risk-identification')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@EnterpriseRiskManagementId, @OperationalDomainTypeId, 'risk-identification', 'Risk Identification', 'Operational identification and capture of risks affecting organizational objectives and services.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'risk-assessment')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@EnterpriseRiskManagementId, @OperationalDomainTypeId, 'risk-assessment', 'Risk Assessment', 'Operational assessment of risk likelihood, impact, and priority for action.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'risk-mitigation')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@EnterpriseRiskManagementId, @OperationalDomainTypeId, 'risk-mitigation', 'Risk Mitigation', 'Operational planning and implementation of actions to reduce or manage risk.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'risk-register-maintenance')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@EnterpriseRiskManagementId, @OperationalDomainTypeId, 'risk-register-maintenance', 'Risk Register Maintenance', 'Operational maintenance of the risk register, entries, and status updates.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'risk-reporting')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@EnterpriseRiskManagementId, @OperationalDomainTypeId, 'risk-reporting', 'Risk Reporting', 'Operational reporting of risk status, trends, and mitigation progress.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'emergency-preparedness')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@EmergencyManagementId, @OperationalDomainTypeId, 'emergency-preparedness', 'Emergency Preparedness', 'Operational planning and readiness activities for emergency situations.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'emergency-response')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@EmergencyManagementId, @OperationalDomainTypeId, 'emergency-response', 'Emergency Response', 'Operational coordination of active emergency response actions and support activities.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'recovery-planning')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@EmergencyManagementId, @OperationalDomainTypeId, 'recovery-planning', 'Recovery Planning', 'Operational planning for post-incident recovery, restoration, and stabilization.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'emergency-communications')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@EmergencyManagementId, @OperationalDomainTypeId, 'emergency-communications', 'Emergency Communications', 'Operational management of emergency messaging, alerts, and communication coordination.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'incident-records')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@EmergencyManagementId, @OperationalDomainTypeId, 'incident-records', 'Incident Records', 'Operational management of incident files, logs, and emergency documentation.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'continuity-plans')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@BusinessContinuityId, @OperationalDomainTypeId, 'continuity-plans', 'Continuity Plans', 'Operational preparation and maintenance of business continuity plans.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'critical-services-identification')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@BusinessContinuityId, @OperationalDomainTypeId, 'critical-services-identification', 'Critical Services Identification', 'Operational identification and documentation of critical services and dependencies.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'backup-procedures')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@BusinessContinuityId, @OperationalDomainTypeId, 'backup-procedures', 'Backup Procedures', 'Operational maintenance of backup procedures for continuity and recovery support.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'continuity-testing')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@BusinessContinuityId, @OperationalDomainTypeId, 'continuity-testing', 'Continuity Testing', 'Operational testing and validation of continuity plans, procedures, and readiness.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'continuity-records')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@BusinessContinuityId, @OperationalDomainTypeId, 'continuity-records', 'Continuity Records', 'Operational management of continuity files, tests, and supporting records.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'cybersecurity-operations')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@InformationSecurityId, @OperationalDomainTypeId, 'cybersecurity-operations', 'Cybersecurity Operations', 'Operational monitoring and protection of systems against cyber threats and vulnerabilities.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'access-control')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@InformationSecurityId, @OperationalDomainTypeId, 'access-control', 'Access Control', 'Operational administration of access rights, permissions, and identity safeguards.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'incident-response')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@InformationSecurityId, @OperationalDomainTypeId, 'incident-response', 'Incident Response', 'Operational handling of security incidents, investigation, and response coordination.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'security-awareness')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@InformationSecurityId, @OperationalDomainTypeId, 'security-awareness', 'Security Awareness', 'Operational support for awareness training, guidance, and security culture activities.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'security-logs')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@InformationSecurityId, @OperationalDomainTypeId, 'security-logs', 'Security Logs', 'Operational management of security logs, monitoring records, and related evidence.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'asset-condition-monitoring')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@InfrastructureResilienceId, @OperationalDomainTypeId, 'asset-condition-monitoring', 'Asset Condition Monitoring', 'Operational monitoring of asset condition, deterioration, and resilience risks.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'preventive-maintenance')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@InfrastructureResilienceId, @OperationalDomainTypeId, 'preventive-maintenance', 'Preventive Maintenance', 'Operational planning and tracking of preventive maintenance for resilient operations.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'climate-adaptation')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@InfrastructureResilienceId, @OperationalDomainTypeId, 'climate-adaptation', 'Climate Adaptation', 'Operational planning for climate-related resilience and adaptation measures.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'facility-safety')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@InfrastructureResilienceId, @OperationalDomainTypeId, 'facility-safety', 'Facility Safety', 'Operational maintenance of facility safety practices, follow-up, and risk reduction actions.');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Domains WHERE DomainCode = 'inspection-records')
    BEGIN
        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainCode, DisplayName, Description)
        VALUES (@InfrastructureResilienceId, @OperationalDomainTypeId, 'inspection-records', 'Inspection Records', 'Operational management of inspection reports, findings, and resilience-related records.');
    END;

    INSERT INTO dbo.SchemaMigrations (MigrationId)
    VALUES ('013_seed_governance_domains');
END;
GO
