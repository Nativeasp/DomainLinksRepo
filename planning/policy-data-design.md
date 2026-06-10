# Policy Data Design

## Goal

Store policy content as structured SQL data first, with document output treated as a render or export concern.

## Correction

This note is specific to the DomainLinks project.

- It should be based on the DomainLinks domain and control model.
- It should not assume or reuse the separate `ENTERPRISE_ORIGIN` schema.

## Working Direction

- Use SQL Server as the primary store for policy content.
- Anchor a policy to a DomainLinks domain branch.
- Keep reusable child content, especially principles, as first-class records rather than burying them inside one policy document.
- Support reuse, adoption, and derivation so strategic content can spread through the organization with visible lineage.

## DomainLinks Anchors

The current DomainLinks app already centers policy drafting around:

- `Domains`
- `Controls`
- `DomainControls`

That gives us the right base:

- a root domain for the policy
- child domains in the branch
- controls that drive policy statements

So the policy data model should extend this project’s domain and control structure rather than sit beside it as unrelated text.

## Why SQL Server Fits

- Policy content is structured and relational.
- Policies naturally relate to domains, controls, principles, and section entries.
- Reuse and inheritance need explicit linkage, visibility, lineage, and adoption history.
- Querying for "which policies use this principle" or "which principles are most reused" is much easier in SQL than in flat files.

## Recommended Model

### 1. Policy Templates

Templates should also exist as data, not only as files on disk.

Suggested table:

- `dbo.PolicyTemplates`

Suggested columns:

- `PolicyTemplateId`
- `TemplateCode`
- `TemplateName`
- `VersionText`
- `TemplateBody`
- `SourcePath`
- `Status`
- `CreatedAtUtc`
- `UpdatedAtUtc`

Purpose:

- gives templates stable identity
- lets policies point to a database template record
- keeps room for either file-backed or database-authored templates

### 2. Policy Header

Create one header row per policy.

Suggested table:

- `dbo.Policies`

Suggested columns:

- `PolicyId`
- `RootDomainId`
- `PolicyTemplateId`
- `PolicyCode`
- `PolicyTitle`
- `VersionText`
- `Status`
- `TemplatePath`
- `SummaryText`
- `SourceModelName`
- `CreatedAtUtc`
- `CreatedBy`
- `UpdatedAtUtc`
- `UpdatedBy`
- `PublishedAtUtc`
- `PublishedBy`

Purpose:

- one row per policy
- anchor for versioning, review, and publication
- connects the policy to the main domain branch
- optionally links the policy to a database template record

## Domain Ownership Rule

For v1, a policy belongs to exactly one root domain.

If a policy appears to span two sibling domains:

- create or use a parent domain above them
- attach the policy to that parent domain
- keep the child domains under that parent

This keeps ownership, inheritance, and control coverage clear.

If cross-domain policy attachment is ever needed later, add a secondary linking table then instead of weakening the main ownership model now.

### 3. Policy Section Entries

Do not force every section into its own table at first. Use a generic section-entry model for the drafted lines that appear in the UI.

Suggested table:

- `dbo.PolicySectionEntries`

Suggested columns:

- `PolicySectionEntryId`
- `PolicyId`
- `SectionCode`
- `SectionName`
- `EntryKind`
- `EntryText`
- `SourceKind`
- `SourceId`
- `DisplayOrder`
- `ReviewStatus`
- `IsInherited`
- `IsReused`
- `IsLocalOverride`
- `CreatedAtUtc`
- `CreatedBy`
- `UpdatedAtUtc`
- `UpdatedBy`

Examples:

- `SectionCode = OBJECTIVE`
- `SectionCode = PRINCIPLE`
- `SectionCode = ACCOUNTABILITY`
- `SectionCode = TRANSPARENCY`
- `SectionCode = STRATEGY`
- `SectionCode = CONTROL_POLICY`
- `SectionCode = CONSEQUENCE`

Purpose:

- supports the current WPF section-based editing flow
- lets the app save each reviewed line as data
- gives us a flexible base while the authoring model is still evolving

### 4. Principles As Reusable Strategic Assets

Principles should not only live as policy section text. They should also exist as reusable records with their own identity and lineage.

Suggested table:

- `dbo.Principles`

Suggested columns:

- `PrincipleId`
- `OriginDomainId`
- `PrincipleCode`
- `Name`
- `StatementText`
- `RationaleText`
- `VisibilityScope`
- `LifecycleStatus`
- `OriginPrincipleId`
- `CreatedAtUtc`
- `CreatedBy`
- `UpdatedAtUtc`
- `UpdatedBy`
- `PublishedAtUtc`
- `PublishedBy`

Purpose:

- reusable principle library
- supports organization-level sharing
- supports derivation and popularity tracking

## Principle Reuse And Inheritance

This is the key rule to preserve:

- A principle can originate in one purpose or domain.
- If marked public to the organization, other parts of the org can:
  - reuse it directly
  - adopt it as-is
  - create a new principle derived from it
- The linkage must remain visible.

This matters because:

- strategic direction becomes evidence-based
- reusable principles can become popular because teams actually adopt them
- influence is not hidden inside one leader's document
- the organization can see which principles are driving policy across multiple areas

## Recommended Principle Link Tables

### A. Domain principle usage

Suggested table:

- `dbo.DomainPrinciples`

Suggested columns:

- `DomainPrincipleId`
- `DomainId`
- `PrincipleId`
- `UsageMode`
- `LocalName`
- `LocalStatementText`
- `LocalRationaleText`
- `DisplayOrder`
- `IsPrimary`
- `AdoptedFromPrincipleId`
- `CreatedAtUtc`
- `CreatedBy`
- `UpdatedAtUtc`
- `UpdatedBy`

Purpose:

- separates the reusable principle asset from its use inside a specific domain
- allows adoption without blind duplication
- allows local overrides while keeping lineage

### B. Optional principle relation table

If lineage becomes richer than one parent-child link, add:

- `dbo.PrincipleRelations`

Suggested columns:

- `PrincipleRelationId`
- `FromPrincipleId`
- `ToPrincipleId`
- `RelationType`
- `Notes`
- `CreatedAtUtc`
- `CreatedBy`

Example relation types:

- `DERIVED_FROM`
- `ADOPTED_FROM`
- `INSPIRED_BY`
- `REPLACES`

Purpose:

- supports many-to-many lineage
- avoids overloading one self-reference field

## Controls And Policy Statements

Policy statements tied to controls should stay linked to controls as data.

Recommended approach:

- keep controls as their own records
- store control-specific policy lines in the policy section entry table
- link each line back to the control

For control-backed entries:

- `SectionCode = CONTROL_POLICY`
- `SourceKind = CONTROL`
- `SourceId = <ControlId>`

This keeps the authored line reusable for:

- policy rendering
- control dashboards
- audits
- gap analysis
- future AI redrafting

## Inheritance Semantics

We should be explicit about the difference between inheritance modes:

- `REUSE`
  - same content
  - same originating principle
  - no local edits
- `ADOPT`
  - same principle concept
  - local domain formally chooses to use it
  - may allow local ordering or commentary
- `DERIVE`
  - new local principle based on an earlier one
  - separate principle record
  - lineage remains intact
- `OVERRIDE`
  - local use changes the wording for a specific context
  - should be visible as a divergence, not silent copy-paste

## Popularity And Strategic Signal

Because reuse matters, the model should support metrics.

Useful derived measures:

- number of domains using a principle
- number of policies using a principle
- number of direct adoptions
- number of derived principles
- most reused principles by domain family

This gives a practical strategic signal based on actual adoption data.

## Recommended Save Flow For The New UI

When the WPF policy drafting tab is ready to save:

1. Save or update a `PolicyTemplates` row when the template is managed in the database.
2. Save or update a `Policies` header row.
3. Save each reviewed line into `PolicySectionEntries`.
4. For principle lines:
   - either link to an existing `Principles` row
   - or create a new `Principles` row
   - then link its use through `DomainPrinciples`
5. For control policy statements:
   - save as section entries linked to the control source
6. Keep approval and publication as a later workflow layer.

## Recommendation

Best current direction:

1. Add a new `Policies` header table.
2. Add a `PolicyTemplates` table so templates have database identity.
3. Add a flexible `PolicySectionEntries` table for line-based authored content.
4. Add a reusable `Principles` table with visibility and origin tracking.
5. Add `DomainPrinciples` for reuse, adoption, and inheritance.
6. Keep control-linked policy statements tied back to existing control rows.

This gives us:

- structured policy authoring
- reusable strategic content
- lineage and inheritance
- future analytics on what principles actually spread through the organization

## Follow-Up Design Questions

- Should a policy belong to exactly one root domain, or support multiple domain associations?
- Should principles be reusable only within the org, or eventually across clients too?
- Should local edits to an adopted principle create a derived principle automatically?
- Do we want approval and publication at the policy level only, or also at the reusable principle level?
