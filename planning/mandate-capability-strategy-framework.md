# Mandate–Capability–Strategy Framework

## Status

Version 1.0 database foundation implemented by migration `034_mandate_capability_strategy_framework`. The DDL below remains illustrative; the migration is the executable source.

## Purpose

The Mandate–Capability–Strategy Framework is a database-owned organizational framework used during organization design, strategic planning, policy and control development, project evaluation, capability audits, and follow-up reviews.

The database is the only source of truth. UI guidance, AI context, documents, semantic artifacts, and embeddings are derived representations.

## Foundational Model

The two pillars are **Capability** and **Strategy**.

**Capability** defines the organization's state of readiness: its people, systems, processes, tools, controls, resources, and structure.

**Strategy** determines where the organization is going and how that capability will be applied.

**Capability defines the state of readiness. Strategy puts it to work.**

**Mandate** sits above both, giving them purpose, authority, and scope.

The governing principle and the basic relationship between Mandate, Capability, and Strategy are foundational. They are immutable and cannot be silently edited or deleted.

## Versioning and Immutability

- A framework has a stable identity and one or more versions.
- Draft versions are malleable: non-foundational elements may be added, changed, reordered, or removed.
- Publishing makes a framework version immutable.
- Later changes require a new draft version based on the latest published version.
- A foundational principle is append-only and cannot be overwritten.
- A correction or conceptual replacement creates a new principle with an explicit `Supersedes` relationship to the original.
- Historical framework versions and superseded principles remain available for audit and interpretation.
- A framework version cannot remove or redefine its required foundational elements.

## Conceptual ERD

```text
Frameworks
    │
    └──< FrameworkVersions
             │
             ├──< FrameworkElements
             │        │
             │        ├── parent/child hierarchy
             │        ├──< FrameworkElementRelations
             │        ├──< FrameworkPrincipleLinks >── Principles
             │        ├──< FrameworkArtifactLinks >── SemanticArtifacts
             │        └──< FrameworkContextRules
             │
             └── Mandate
                    ├── Capability
                    └── Strategy

Principles ──< PrincipleRelations
                 └── Supersedes / Supports / DerivesFrom
```

`SemanticArtifacts` supports retrieval and contextual linking. It is not the source of framework or principle wording.

## Illustrative SQL Server DDL

```sql
CREATE TABLE dbo.Frameworks (
    FrameworkId       UNIQUEIDENTIFIER NOT NULL
        CONSTRAINT DF_Frameworks_Id DEFAULT NEWSEQUENTIALID(),
    FrameworkCode     NVARCHAR(100) NOT NULL,
    FrameworkName     NVARCHAR(255) NOT NULL,
    LifecycleStatus   NVARCHAR(30) NOT NULL DEFAULT 'Active',
    CreatedAtUtc      DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAtUtc      DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_Frameworks PRIMARY KEY (FrameworkId),
    CONSTRAINT UQ_Frameworks_Code UNIQUE (FrameworkCode),
    CONSTRAINT CK_Frameworks_Status
        CHECK (LifecycleStatus IN ('Active', 'Retired', 'Archived'))
);

CREATE TABLE dbo.FrameworkVersions (
    FrameworkVersionId UNIQUEIDENTIFIER NOT NULL
        CONSTRAINT DF_FrameworkVersions_Id DEFAULT NEWSEQUENTIALID(),
    FrameworkId        UNIQUEIDENTIFIER NOT NULL,
    VersionText        NVARCHAR(30) NOT NULL,
    SummaryText        NVARCHAR(MAX) NOT NULL,
    VersionStatus      NVARCHAR(30) NOT NULL DEFAULT 'Draft',
    BasedOnVersionId   UNIQUEIDENTIFIER NULL,
    PublishedAtUtc     DATETIME2(3) NULL,
    PublishedBy        NVARCHAR(128) NULL,
    CreatedAtUtc       DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    CreatedBy          NVARCHAR(128) NOT NULL DEFAULT SUSER_SNAME(),
    CONSTRAINT PK_FrameworkVersions PRIMARY KEY (FrameworkVersionId),
    CONSTRAINT FK_FrameworkVersions_Framework
        FOREIGN KEY (FrameworkId) REFERENCES dbo.Frameworks(FrameworkId),
    CONSTRAINT FK_FrameworkVersions_BasedOn
        FOREIGN KEY (BasedOnVersionId)
        REFERENCES dbo.FrameworkVersions(FrameworkVersionId),
    CONSTRAINT UQ_FrameworkVersions_Version
        UNIQUE (FrameworkId, VersionText),
    CONSTRAINT CK_FrameworkVersions_Status
        CHECK (VersionStatus IN ('Draft', 'Published', 'Superseded', 'Archived'))
);

CREATE TABLE dbo.FrameworkElements (
    FrameworkElementId UNIQUEIDENTIFIER NOT NULL
        CONSTRAINT DF_FrameworkElements_Id DEFAULT NEWSEQUENTIALID(),
    FrameworkVersionId UNIQUEIDENTIFIER NOT NULL,
    ParentElementId    UNIQUEIDENTIFIER NULL,
    ElementCode        NVARCHAR(100) NOT NULL,
    ElementType        NVARCHAR(30) NOT NULL,
    ElementName        NVARCHAR(255) NOT NULL,
    StatementText      NVARCHAR(MAX) NULL,
    DefinitionText     NVARCHAR(MAX) NULL,
    IsFoundational     BIT NOT NULL DEFAULT 0,
    DisplayOrder       INT NOT NULL DEFAULT 0,
    CONSTRAINT PK_FrameworkElements PRIMARY KEY (FrameworkElementId),
    CONSTRAINT FK_FrameworkElements_Version
        FOREIGN KEY (FrameworkVersionId)
        REFERENCES dbo.FrameworkVersions(FrameworkVersionId),
    CONSTRAINT FK_FrameworkElements_Parent
        FOREIGN KEY (ParentElementId)
        REFERENCES dbo.FrameworkElements(FrameworkElementId),
    CONSTRAINT UQ_FrameworkElements_Code
        UNIQUE (FrameworkVersionId, ElementCode),
    CONSTRAINT CK_FrameworkElements_Type
        CHECK (ElementType IN
            ('Mandate', 'Pillar', 'Principle', 'Concept', 'Definition'))
);

CREATE TABLE dbo.FrameworkElementRelations (
    FrameworkElementRelationId UNIQUEIDENTIFIER NOT NULL
        CONSTRAINT DF_FrameworkElementRelations_Id DEFAULT NEWSEQUENTIALID(),
    FromElementId UNIQUEIDENTIFIER NOT NULL,
    ToElementId   UNIQUEIDENTIFIER NOT NULL,
    RelationType  NVARCHAR(30) NOT NULL,
    RationaleText NVARCHAR(MAX) NULL,
    CONSTRAINT PK_FrameworkElementRelations
        PRIMARY KEY (FrameworkElementRelationId),
    CONSTRAINT FK_FrameworkElementRelations_From
        FOREIGN KEY (FromElementId)
        REFERENCES dbo.FrameworkElements(FrameworkElementId),
    CONSTRAINT FK_FrameworkElementRelations_To
        FOREIGN KEY (ToElementId)
        REFERENCES dbo.FrameworkElements(FrameworkElementId),
    CONSTRAINT UQ_FrameworkElementRelations
        UNIQUE (FromElementId, ToElementId, RelationType),
    CONSTRAINT CK_FrameworkElementRelations_Type
        CHECK (RelationType IN
            ('Governs', 'Enables', 'Applies', 'Supports', 'Constrains'))
);

CREATE TABLE dbo.FrameworkPrincipleLinks (
    FrameworkPrincipleLinkId UNIQUEIDENTIFIER NOT NULL
        CONSTRAINT DF_FrameworkPrincipleLinks_Id DEFAULT NEWSEQUENTIALID(),
    FrameworkElementId UNIQUEIDENTIFIER NOT NULL,
    PrincipleId        UNIQUEIDENTIFIER NOT NULL,
    RelationshipType   NVARCHAR(30) NOT NULL,
    ApplicabilityText  NVARCHAR(MAX) NULL,
    CONSTRAINT PK_FrameworkPrincipleLinks
        PRIMARY KEY (FrameworkPrincipleLinkId),
    CONSTRAINT FK_FrameworkPrincipleLinks_Element
        FOREIGN KEY (FrameworkElementId)
        REFERENCES dbo.FrameworkElements(FrameworkElementId),
    CONSTRAINT FK_FrameworkPrincipleLinks_Principle
        FOREIGN KEY (PrincipleId) REFERENCES dbo.Principles(PrincipleId),
    CONSTRAINT UQ_FrameworkPrincipleLinks
        UNIQUE (FrameworkElementId, PrincipleId)
);

CREATE TABLE dbo.FrameworkArtifactLinks (
    FrameworkArtifactLinkId UNIQUEIDENTIFIER NOT NULL
        CONSTRAINT DF_FrameworkArtifactLinks_Id DEFAULT NEWSEQUENTIALID(),
    FrameworkElementId  UNIQUEIDENTIFIER NOT NULL,
    SemanticArtifactId UNIQUEIDENTIFIER NOT NULL,
    RelationshipType   NVARCHAR(30) NOT NULL,
    RelevanceWeight    DECIMAL(5,4) NULL,
    CONSTRAINT PK_FrameworkArtifactLinks
        PRIMARY KEY (FrameworkArtifactLinkId),
    CONSTRAINT FK_FrameworkArtifactLinks_Element
        FOREIGN KEY (FrameworkElementId)
        REFERENCES dbo.FrameworkElements(FrameworkElementId),
    CONSTRAINT FK_FrameworkArtifactLinks_Artifact
        FOREIGN KEY (SemanticArtifactId)
        REFERENCES dbo.SemanticArtifacts(SemanticArtifactId),
    CONSTRAINT UQ_FrameworkArtifactLinks
        UNIQUE (FrameworkElementId, SemanticArtifactId, RelationshipType),
    CONSTRAINT CK_FrameworkArtifactLinks_Weight
        CHECK (RelevanceWeight IS NULL
            OR RelevanceWeight BETWEEN 0.0000 AND 1.0000)
);

CREATE TABLE dbo.FrameworkContextRules (
    FrameworkContextRuleId UNIQUEIDENTIFIER NOT NULL
        CONSTRAINT DF_FrameworkContextRules_Id DEFAULT NEWSEQUENTIALID(),
    FrameworkElementId UNIQUEIDENTIFIER NOT NULL,
    ArtifactType       NVARCHAR(50) NULL,
    ActivityType       NVARCHAR(50) NOT NULL,
    TriggerStage       NVARCHAR(50) NOT NULL,
    DeliveryMode       NVARCHAR(20) NOT NULL,
    IsRequired         BIT NOT NULL DEFAULT 0,
    Priority           INT NOT NULL DEFAULT 100,
    InstructionText    NVARCHAR(MAX) NULL,
    CONSTRAINT PK_FrameworkContextRules PRIMARY KEY (FrameworkContextRuleId),
    CONSTRAINT FK_FrameworkContextRules_Element
        FOREIGN KEY (FrameworkElementId)
        REFERENCES dbo.FrameworkElements(FrameworkElementId),
    CONSTRAINT CK_FrameworkContextRules_Delivery
        CHECK (DeliveryMode IN ('AIContext', 'UserGuidance', 'Both'))
);
```

The implementation extends `dbo.Principles` with `ShortStatementText` and `IsImmutable` rather than creating a competing principle store. Capability and Strategy are canonical organization-visible principle records. Framework elements reference those records instead of owning duplicate principle wording.

## Reuse and Context Rules

1. **Store meaning once.** Instruments reference canonical framework elements and principles instead of copying their text.
2. **Preserve intentional differences.** Local wording is stored only for an explicit override, derivation, or historical snapshot.
3. **Link principles to pillars.** A principle may support Capability, Strategy, Mandate, or more than one element.
4. **Keep relationships explicit.** Each link states whether an instrument supports, applies, constrains, evidences, or derives from an element.
5. **Use rules for delivery.** Context rules determine the activity, stage, artifact type, channel, priority, and whether inclusion is required.
6. **Keep AI retrieval derived.** Framework and principle text is copied into semantic artifacts only for indexing and embeddings.
7. **Preserve published history.** A policy or other published instrument may retain an immutable wording snapshot while continuing to reference its canonical source.

## Principle Implementation and Traceability

Principles move from organizational intent to operational evidence through an explicit chain:

```text
Organizational Principle
        ↓
Mandate interpretation and authorization
        ↓
Policy commitment
        ↓
Control objective
        ↓
Procedure or work instruction
        ↓
Evidence and measurement
```

The mandate is the first authorizing instrument. Every mandate must interpret both foundational pillars within its own purpose, authority, and scope. The interpretation must be applicable to the mandate concerned rather than attempting to prescribe one organization-wide operating model.

### Capability Mandate Requirement

Every mandate must assign responsibility for capability. A suitable generic statement is:

> The accountable officer is responsible for identifying, establishing, maintaining, and assessing the capabilities required to fulfil this mandate, including the necessary people, systems, processes, tools, controls, resources, and organizational structure.

The particular capabilities and measures are defined downstream according to the mandate's scope.

### Strategy Mandate Requirement

Every mandate must assign responsibility for strategic direction. A suitable generic statement is:

> The accountable officer is responsible for developing, maintaining, and executing a strategy for fulfilling this mandate that aligns with the organization's overall strategy and goals and directs how available capability will be applied.

The particular strategy is defined by the responsible organizational area and must remain consistent with its mandate and the organization's overall direction.

### Traceability Example

| Principle | Mandate | Policy | Control | Procedure | Evidence |
|---|---|---|---|---|---|
| Capability defines readiness | Accountable officer must identify and maintain required capability | Changes proceed only when required capability is ready | Assess readiness before major change approval | Complete the readiness review and assign gap owners | Approved assessment, training records, test results, and gap register |
| Strategy puts capability to work | Accountable officer must maintain an aligned strategy | Initiatives must align with approved strategic direction | Confirm strategic alignment before investment approval | Complete the strategy-alignment assessment | Approved strategy, decision record, objectives, and review results |

Each downstream relationship records:

- the principle and mandate from which it receives authority
- how the instrument interprets or implements them
- whether it authorizes, applies, supports, constrains, or evidences them
- the resulting obligation or expected behaviour
- the evidence required to demonstrate implementation

### Programmatic Resolution

Context should be assembled in this order:

```text
Foundational Capability and Strategy principles
        +
Applicable mandate and its interpretations
        +
Explicit links for the current policy, control, procedure, or project
        +
Applicable domain and framework rules
        +
Optional semantic suggestions
```

Foundational and explicitly linked content is authoritative. Semantically discovered content is advisory until a user creates an explicit relationship.

The UI should show why each principle is present, its authoritative wording and origin, the applicable mandate, its relationship to the current instrument, and the implementation evidence expected.

## Contextual Application

### Capability

Supply Capability context during:

- capability and readiness assessments
- control design and control audits
- resource, system, process, and organizational-structure planning
- gap analysis and remediation follow-up
- project feasibility and delivery-readiness reviews

### Strategy

Supply Strategy context during:

- strategic planning
- objective and initiative prioritization
- project selection and portfolio review
- performance and outcome reviews
- decisions about how available capability will be applied

### Mandate

Supply Mandate context whenever purpose, authority, scope, ownership, or organizational boundaries are established or challenged.

- Every mandate interprets both Capability and Strategy.
- The Capability interpretation assigns accountability for required organizational readiness.
- The Strategy interpretation assigns accountability for aligned strategic direction and application of capability.
- Downstream policies, controls, procedures, projects, and evidence retain traceability to the applicable mandate.

### Policies and Controls

- Policies should reference the principles they adopt or apply.
- Policies and controls should identify the mandate from which they receive authority.
- Controls should link to the Capability or Strategy elements they support or evidence.
- A policy-specific copy of a principle should exist only when publication history or an intentional local variation requires it.
- Reuse, adoption, derivation, and override must remain distinguishable and auditable.

### Projects and Other Instruments

Projects are not currently represented by a dedicated database table. When introduced, they should be registered as semantic artifacts and linked through the same framework mechanism rather than requiring a parallel framework model.

## Initial Version

- Framework code: `MANDATE-CAPABILITY-STRATEGY`
- Framework name: `Mandate–Capability–Strategy Framework`
- Initial version: `1.0`
- Required foundational elements: `MANDATE`, `CAPABILITY`, `STRATEGY`, and `CAPABILITY-STRATEGY-PRINCIPLE`
- Initial lifecycle: published and protected from in-place modification

## Implementation Boundary

The first implementation includes versioned framework storage, immutable foundational principles, contextual retrieval APIs, prompt inclusion for domain/control/policy generation, and semantic indexing. A later phase can add a framework editor, mandate records, draft-version management, and explicit user-managed instrument links.
