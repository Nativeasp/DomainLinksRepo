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

IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigrations WHERE MigrationId = '023_hard_reset_domain_taxonomy')
BEGIN
    BEGIN TRANSACTION;

    BEGIN TRY
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Collections_Domains')
        BEGIN
            ALTER TABLE dbo.Collections DROP CONSTRAINT FK_Collections_Domains;
        END;

        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Domains_ParentDomain')
        BEGIN
            ALTER TABLE dbo.Domains DROP CONSTRAINT FK_Domains_ParentDomain;
        END;

        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Domains_DomainTypes')
        BEGIN
            ALTER TABLE dbo.Domains DROP CONSTRAINT FK_Domains_DomainTypes;
        END;

        DELETE FROM dbo.Domains;
        DELETE FROM dbo.DomainTypes;

        DBCC CHECKIDENT ('dbo.DomainTypes', RESEED, 0) WITH NO_INFOMSGS;

        INSERT INTO dbo.DomainTypes (
            CODE,
            NAME,
            DOMAIN_LEVEL,
            PRIMARY_FOCUS,
            KEY_QUESTION,
            DESCRIPTION,
            DISPLAY_ORDER,
            EFFECTIVE_START_DATE
        )
        VALUES
            ('EXECUTIVE', 'Executive', 1, 'Enterprise direction, governance, accountability, and executive oversight.', 'How is the organization directed, governed, and held accountable?', 'Executive domains for administration, governance, legislation, risk, compliance, and audit.', 10, CAST(SYSDATETIME() AS date)),
            ('CORPORATE', 'Corporate', 2, 'Internal corporate capabilities that enable the organization to operate.', 'What internal capabilities support the organization?', 'Corporate domains for finance, people, information, technology, procurement, communications, and assets.', 20, CAST(SYSDATETIME() AS date)),
            ('SERVICE', 'Service', 3, 'External-facing programs and services delivered to community members, citizens, and clients.', 'What services does the organization deliver?', 'Service domains for infrastructure, social development, health, education, economic development, safety, and citizen services.', 30, CAST(SYSDATETIME() AS date));

        DECLARE @ExecutiveTypeId INT = (SELECT ID FROM dbo.DomainTypes WHERE CODE = 'EXECUTIVE');
        DECLARE @CorporateTypeId INT = (SELECT ID FROM dbo.DomainTypes WHERE CODE = 'CORPORATE');
        DECLARE @ServiceTypeId INT = (SELECT ID FROM dbo.DomainTypes WHERE CODE = 'SERVICE');

        IF @ExecutiveTypeId IS NULL OR @CorporateTypeId IS NULL OR @ServiceTypeId IS NULL
        BEGIN
            THROW 51000, 'Required Executive, Corporate, and Service domain types were not found.', 1;
        END;

        CREATE TABLE #DomainSeed (
            DomainCode       NVARCHAR(100) NOT NULL PRIMARY KEY,
            ParentCode       NVARCHAR(100) NULL,
            DomainTypeCode   NVARCHAR(50) NOT NULL,
            DisplayOrder     INT NOT NULL,
            DisplayName      NVARCHAR(255) NOT NULL,
            Description      NVARCHAR(MAX) NULL
        );

        INSERT INTO #DomainSeed (DomainCode, ParentCode, DomainTypeCode, DisplayOrder, DisplayName, Description)
        VALUES
            ('workspace-memory', NULL, 'CORPORATE', 5, 'Workspace Memory', 'System workspace for active chats, uploads, and project-level inference. Kept as an application storage anchor.'),
            ('executive-administration', NULL, 'EXECUTIVE', 110, 'Executive Administration', 'Executive administration capabilities for strategic planning, organizational management, and performance management.'),
            ('strategic-planning', 'executive-administration', 'EXECUTIVE', 111, 'Strategic Planning', 'Strategic planning direction, priorities, and organizational alignment.'),
            ('organizational-management', 'executive-administration', 'EXECUTIVE', 112, 'Organizational Management', 'Executive oversight of organizational structure, management systems, and operating alignment.'),
            ('performance-management', 'executive-administration', 'EXECUTIVE', 113, 'Performance Management', 'Enterprise performance planning, monitoring, reporting, and improvement.'),
            ('governance-legislative-affairs', NULL, 'EXECUTIVE', 120, 'Governance & Legislative Affairs', 'Governance and legislative affairs capabilities for councils, boards, laws, bylaws, and policy.'),
            ('council-board-administration', 'governance-legislative-affairs', 'EXECUTIVE', 121, 'Council / Board Administration', 'Council and board meeting administration, decision records, and governance support.'),
            ('law-bylaw-development', 'governance-legislative-affairs', 'EXECUTIVE', 122, 'Law / Bylaw Development', 'Development, review, and stewardship of laws, bylaws, and legislative instruments.'),
            ('policy-management', 'governance-legislative-affairs', 'EXECUTIVE', 123, 'Policy Management', 'Policy lifecycle management, governance, review, and publication.'),
            ('risk-compliance-audit', NULL, 'EXECUTIVE', 130, 'Risk, Compliance & Audit', 'Enterprise risk, compliance, and audit capabilities.'),
            ('enterprise-risk-management', 'risk-compliance-audit', 'EXECUTIVE', 131, 'Enterprise Risk Management', 'Risk identification, assessment, mitigation, monitoring, and reporting.'),
            ('compliance-management', 'risk-compliance-audit', 'EXECUTIVE', 132, 'Compliance Management', 'Compliance obligations, controls, evidence, and reporting.'),
            ('internal-audit', 'risk-compliance-audit', 'EXECUTIVE', 133, 'Internal Audit', 'Internal audit planning, fieldwork, findings, and follow-up.'),
            ('financial-management', NULL, 'CORPORATE', 210, 'Financial Management', 'Financial management capabilities for budgeting, accounting, and reporting.'),
            ('budgeting', 'financial-management', 'CORPORATE', 211, 'Budgeting', 'Budget planning, development, allocation, monitoring, and variance management.'),
            ('accounting', 'financial-management', 'CORPORATE', 212, 'Accounting', 'Accounting operations, financial records, payables, receivables, and controls.'),
            ('financial-reporting', 'financial-management', 'CORPORATE', 213, 'Financial Reporting', 'Financial statements, management reporting, funder reporting, and financial analysis.'),
            ('human-resource-management', NULL, 'CORPORATE', 220, 'Human Resource Management', 'Human resource management capabilities for staffing, labour relations, and learning.'),
            ('staffing', 'human-resource-management', 'CORPORATE', 221, 'Staffing', 'Workforce staffing, recruitment, selection, onboarding, and position support.'),
            ('labour-relations', 'human-resource-management', 'CORPORATE', 222, 'Labour Relations', 'Labour relations, collective agreement support, and formal workplace processes.'),
            ('learning-development', 'human-resource-management', 'CORPORATE', 223, 'Learning & Development', 'Training, learning plans, professional development, and capability growth.'),
            ('information-management', NULL, 'CORPORATE', 230, 'Information Management', 'Information management capabilities for records, data, and privacy.'),
            ('records-management', 'information-management', 'CORPORATE', 231, 'Records Management', 'Records lifecycle, filing, retention, disposition, and information custody.'),
            ('data-management', 'information-management', 'CORPORATE', 232, 'Data Management', 'Data stewardship, quality, definitions, lineage, and operational data practices.'),
            ('privacy-management', 'information-management', 'CORPORATE', 233, 'Privacy Management', 'Privacy obligations, controls, access, consent, and incident handling.'),
            ('information-technology-management', NULL, 'CORPORATE', 240, 'Information Technology Management', 'Technology capabilities for infrastructure, cybersecurity, software, and systems.'),
            ('infrastructure-management', 'information-technology-management', 'CORPORATE', 241, 'Infrastructure Management', 'Networks, endpoints, servers, cloud, platforms, and technical operations.'),
            ('cybersecurity', 'information-technology-management', 'CORPORATE', 242, 'Cybersecurity', 'Security controls, threat management, vulnerability management, and cyber resilience.'),
            ('software-systems-management', 'information-technology-management', 'CORPORATE', 243, 'Software & Systems Management', 'Software platforms, business systems, integrations, administration, and lifecycle support.'),
            ('procurement-contract-management', NULL, 'CORPORATE', 250, 'Procurement & Contract Management', 'Procurement and contract capabilities for purchasing, vendors, and contract administration.'),
            ('purchasing', 'procurement-contract-management', 'CORPORATE', 251, 'Purchasing', 'Purchasing requests, sourcing, approvals, and purchase execution.'),
            ('vendor-management', 'procurement-contract-management', 'CORPORATE', 252, 'Vendor Management', 'Vendor onboarding, performance, relationships, and vendor risk.'),
            ('contract-administration', 'procurement-contract-management', 'CORPORATE', 253, 'Contract Administration', 'Contract records, obligations, renewals, amendments, and administration.'),
            ('communications', NULL, 'CORPORATE', 260, 'Communications', 'Communication capabilities for internal, public, and digital messaging.'),
            ('internal-communications', 'communications', 'CORPORATE', 261, 'Internal Communications', 'Employee and internal stakeholder communications.'),
            ('public-communications', 'communications', 'CORPORATE', 262, 'Public Communications', 'Public-facing communications, announcements, notices, and media support.'),
            ('digital-communications', 'communications', 'CORPORATE', 263, 'Digital Communications', 'Web, social, digital channel, and online communication management.'),
            ('asset-facilities-management', NULL, 'CORPORATE', 270, 'Asset & Facilities Management', 'Asset and facilities capabilities for facilities, fleet, equipment, and maintenance.'),
            ('facilities-management', 'asset-facilities-management', 'CORPORATE', 271, 'Facilities Management', 'Facility operations, planning, access, use, and service coordination.'),
            ('fleet-equipment-management', 'asset-facilities-management', 'CORPORATE', 272, 'Fleet & Equipment Management', 'Fleet and equipment inventory, use, lifecycle, and support.'),
            ('asset-maintenance', 'asset-facilities-management', 'CORPORATE', 273, 'Asset Maintenance', 'Asset maintenance planning, preventive work, corrective work, and reliability.'),
            ('community-infrastructure', NULL, 'SERVICE', 310, 'Community Infrastructure', 'Community infrastructure services for capital works, public works, land, and environment.'),
            ('capital-works', 'community-infrastructure', 'SERVICE', 311, 'Capital Works', 'Capital project planning, delivery, oversight, and reporting.'),
            ('public-works', 'community-infrastructure', 'SERVICE', 312, 'Public Works', 'Public works operations, maintenance, roads, utilities, and service response.'),
            ('land-environment', 'community-infrastructure', 'SERVICE', 313, 'Land & Environment', 'Land stewardship, environmental protection, permitting, and monitoring.'),
            ('social-development', NULL, 'SERVICE', 320, 'Social Development', 'Social development services for housing, recreation, and social assistance.'),
            ('housing', 'social-development', 'SERVICE', 321, 'Housing', 'Housing programs, applications, tenancy, maintenance coordination, and housing supports.'),
            ('recreation', 'social-development', 'SERVICE', 322, 'Recreation', 'Recreation programs, sport support, events, and facility access.'),
            ('social-assistance', 'social-development', 'SERVICE', 323, 'Social Assistance', 'Income, emergency, and social support services.'),
            ('community-health', NULL, 'SERVICE', 330, 'Community Health', 'Community health services for healthcare, social services, and public health.'),
            ('healthcare-services', 'community-health', 'SERVICE', 331, 'Healthcare Services', 'Healthcare service access, coordination, and program support.'),
            ('social-services', 'community-health', 'SERVICE', 332, 'Social Services', 'Social service programs, case supports, and community wellbeing services.'),
            ('public-health', 'community-health', 'SERVICE', 333, 'Public Health', 'Public health planning, prevention, education, and response.'),
            ('education', NULL, 'SERVICE', 340, 'Education', 'Education services across early learning, school systems, and lifelong learning.'),
            ('early-learning', 'education', 'SERVICE', 341, 'Early Learning', 'Early learning programs, childcare supports, and readiness services.'),
            ('primary-secondary-education', 'education', 'SERVICE', 342, 'Primary & Secondary Education', 'Primary and secondary education support, coordination, and administration.'),
            ('post-secondary-lifelong-learning', 'education', 'SERVICE', 343, 'Post-Secondary & Lifelong Learning', 'Post-secondary access, adult learning, lifelong learning, and training support.'),
            ('economic-development', NULL, 'SERVICE', 350, 'Economic Development', 'Economic development services for business, licensing, permitting, and employment.'),
            ('business-development', 'economic-development', 'SERVICE', 351, 'Business Development', 'Business attraction, retention, entrepreneurship, and economic initiatives.'),
            ('licensing-permitting', 'economic-development', 'SERVICE', 352, 'Licensing & Permitting', 'Licensing, permitting, approvals, inspections, and related service administration.'),
            ('employment-development', 'economic-development', 'SERVICE', 353, 'Employment Development', 'Employment programs, workforce development, placements, and job readiness.'),
            ('public-safety-emergency-management', NULL, 'SERVICE', 360, 'Public Safety & Emergency Management', 'Public safety and emergency management services.'),
            ('emergency-management', 'public-safety-emergency-management', 'SERVICE', 361, 'Emergency Management', 'Emergency planning, readiness, coordination, response, and recovery.'),
            ('protective-services', 'public-safety-emergency-management', 'SERVICE', 362, 'Protective Services', 'Protective services, enforcement, inspections, and community safety support.'),
            ('fire-rescue', 'public-safety-emergency-management', 'SERVICE', 363, 'Fire & Rescue', 'Fire prevention, fire response, rescue operations, and public safety education.'),
            ('membership-citizen-services', NULL, 'SERVICE', 370, 'Membership / Citizen Services', 'Membership and citizen services for administration, registries, and public access.'),
            ('membership-administration', 'membership-citizen-services', 'SERVICE', 371, 'Membership Administration', 'Membership intake, eligibility, updates, records, and service coordination.'),
            ('registry-services', 'membership-citizen-services', 'SERVICE', 372, 'Registry Services', 'Registry records, certificates, status information, and registry transactions.'),
            ('public-service-access', 'membership-citizen-services', 'SERVICE', 373, 'Public Service Access', 'Front-door service access, intake, navigation, and public service support.');

        INSERT INTO dbo.Domains (DomainParentId, DomainTypeId, DomainOrientationId, DisplayOrder, DomainCode, DisplayName, Description, Status)
        SELECT
            NULL,
            CASE s.DomainTypeCode
                WHEN 'EXECUTIVE' THEN @ExecutiveTypeId
                WHEN 'CORPORATE' THEN @CorporateTypeId
                WHEN 'SERVICE' THEN @ServiceTypeId
            END,
            NULL,
            s.DisplayOrder,
            s.DomainCode,
            s.DisplayName,
            s.Description,
            'Active'
        FROM #DomainSeed s;

        UPDATE child
        SET
            DomainParentId = parent.DomainId,
            UpdatedAtUtc = SYSUTCDATETIME()
        FROM dbo.Domains child
        JOIN #DomainSeed childSeed
            ON childSeed.DomainCode = child.DomainCode
        JOIN dbo.Domains parent
            ON parent.DomainCode = childSeed.ParentCode
        WHERE childSeed.ParentCode IS NOT NULL;

        DECLARE @WorkspaceDomainId UNIQUEIDENTIFIER =
            (SELECT DomainId FROM dbo.Domains WHERE DomainCode = 'workspace-memory');

        IF @WorkspaceDomainId IS NULL
        BEGIN
            THROW 51001, 'Workspace Memory domain was not seeded.', 1;
        END;

        UPDATE dbo.Collections
        SET
            DomainId = @WorkspaceDomainId,
            UpdatedAtUtc = SYSUTCDATETIME();

        DROP TABLE #DomainSeed;

        ALTER TABLE dbo.Domains WITH CHECK
        ADD CONSTRAINT FK_Domains_ParentDomain
            FOREIGN KEY (DomainParentId) REFERENCES dbo.Domains(DomainId);

        ALTER TABLE dbo.Domains CHECK CONSTRAINT FK_Domains_ParentDomain;

        ALTER TABLE dbo.Domains WITH CHECK
        ADD CONSTRAINT FK_Domains_DomainTypes
            FOREIGN KEY (DomainTypeId) REFERENCES dbo.DomainTypes(ID);

        ALTER TABLE dbo.Domains CHECK CONSTRAINT FK_Domains_DomainTypes;

        ALTER TABLE dbo.Collections WITH CHECK
        ADD CONSTRAINT FK_Collections_Domains
            FOREIGN KEY (DomainId) REFERENCES dbo.Domains(DomainId);

        ALTER TABLE dbo.Collections CHECK CONSTRAINT FK_Collections_Domains;

        INSERT INTO dbo.SchemaMigrations (MigrationId)
        VALUES ('023_hard_reset_domain_taxonomy');

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
        BEGIN
            ROLLBACK TRANSACTION;
        END;

        THROW;
    END CATCH;
END;
GO
