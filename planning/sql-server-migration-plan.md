# SQL Server Vector Migration Plan

Date: 2026-05-20
Topic: Move DomainLinks from Chroma-as-store to SQL Server as source of truth and retrieval engine

## Summary

DomainLinks is moving toward SQL Server as the long-term authority for metadata, content, embeddings, and vector retrieval. Chroma should be treated as a temporary compatibility bridge for the existing app and data, not as the permanent storage model.

The important model change is that domains and projects are both retrieval scopes. A domain represents durable organizational knowledge. A project represents short-term working memory.

Examples:
- Durable knowledge: `HR -> Hiring Policy`
- Project memory: `Projects -> Hire request new DBA`

## Target Architecture

SQL Server owns the source of truth:
- domains and project scopes
- collections
- documents
- content units
- embeddings
- retrieval profiles
- provider settings
- migration state

SQL Server 2025 native vector support should be the preferred retrieval path. The first embedding profile should use native `vector(768)` storage for the current Ollama/Nomic embedding model unless the model changes.

Chroma remains only for migration and compatibility:
- existing Chroma stores can be read during import
- a shared Chroma persistent store may be used temporarily
- there should not be one Chroma folder per domain in the target design
- Chroma can be removed after SQL search, whole-document retrieval, and prompt flows pass acceptance tests

## Domains, Projects, And Collections

`Domain` is the general retrieval scope concept. Use a `DomainType` or equivalent field to distinguish durable knowledge from project memory.

Recommended domain types:
- `Knowledge`
- `ProjectMemory`
- `System`
- future scope types as needed

A project acts like a domain but represents short-term memory. In SQL Server, project memories should live under a `Projects` domain/scope type rather than being modeled as unrelated folders.

Collections should use SQL Server integer identity keys and stable unique codes. Friendly names and slugs are for UI display only. This avoids brittle naming such as `projects_hire-new-dba-indexcodeorsomething` becoming part of the core identity.

During Chroma transition, SQL/VB stores should map through an abstraction layer:
- SQL domain/project scope maps to a Chroma collection or metadata filter
- SQL collection code maps to a stable Chroma collection name or metadata value
- VB tree nodes never need to know Chroma filesystem paths

## Chroma Transition Strategy

Current Chroma problem:
- the existing backend uses one `PersistentClient` folder per store
- each store folder contains its own `chroma.sqlite3` and vector index files
- this creates operational fragmentation as domains/projects grow

Transition recommendation:
- consolidate temporary Chroma use into one shared persistent store
- represent domain/project/collection boundaries through collection names or metadata filters
- keep SQL Server as the only authoritative model
- treat Chroma indexes as disposable and rebuildable

This means 20 domains should not require 20 Chroma SQLite files in the migration model. If Chroma is still present, it should sit behind a backend abstraction and not leak into the desktop app.

## SQL Server Retrieval Strategy

Retrieval should support more than classic chunk-only RAG.

Retrieval modes:
- `NoSearch`: answer without retrieval
- `WholeDocument`: load one or more complete documents into context
- `DomainVector`: vector search within one durable domain
- `ProjectVector`: vector search within one project-memory scope
- `ClusterVector`: vector search across a controlled group of domains/projects
- `Hybrid`: combine whole-document, summary, and vector-ranked content units

Use `ContentUnits` instead of a narrow `Chunks` table. A content unit can be:
- `Document`
- `Section`
- `Chunk`
- `Summary`

This supports both current RAG and future larger-context strategies where a whole document may be more useful than many small chunks.

## Recommended Schema Direction

Core SQL Server entities:
- `Domains`
- `DomainClusters`
- `DomainClusterMembers`
- `Collections`
- `Documents`
- `ContentUnits`
- `EmbeddingProfiles`
- `ContentUnitEmbeddings768`
- `RetrievalProfiles`
- `ProviderSettings`
- `LegacyChromaMigrationState`

Important defaults:
- SQL Server owns identity and metadata
- collection identifiers are GUID/code based
- display names are mutable
- SQL vector retrieval is primary
- Chroma migration state is separate from runtime retrieval state

## Backend Migration Direction

The current Mac-hosted Python backend should become a reference implementation. The target backend should be a Windows-hosted Python service running close to SQL Server in the Win11 VM.

Backend responsibilities:
- expose domain/project/collection/document APIs
- embed documents and content units
- write SQL Server first
- run SQL vector and whole-document retrieval
- stream prompt responses from Ollama
- keep LM Studio available as a future provider option
- import legacy Chroma data

Ollama remains the default provider today. LM Studio should remain supported as an explicit future provider, but it should not be the silent default.

## Desktop Migration Direction

The existing VB WinForms app remains a working reference. The target desktop app should be a new C# WPF MVVM project.

Desktop responsibilities:
- show domains and projects consistently
- make active retrieval scope visible
- support project tree navigation
- support prompt editing and streaming responses
- display backend/provider status
- call the new backend APIs instead of encoding Chroma assumptions

## Phases

### Phase 1: Documentation And Structure
- finalize migration plan and PRD
- create new root repository structure later
- keep existing VB and Python apps as references

### Phase 2: SQL Foundation
- create SQL Server schema
- add repository layer in backend
- add import tooling for existing Chroma stores
- validate vector type and `VECTOR_DISTANCE`

### Phase 3: Backend Replacement
- implement Windows-hosted Python backend
- support SQL retrieval modes
- support streaming prompts
- compare SQL retrieval against legacy Chroma results

### Phase 4: Desktop Replacement
- build C# WPF MVVM client
- model domains/projects as retrieval scopes
- preserve useful flows from the VB prototype
- remove direct Chroma assumptions from the UI

### Phase 5: Chroma Retirement
- run SQL retrieval as default
- keep Chroma only for rollback until confidence is high
- remove Chroma from normal runtime after acceptance tests pass

## Acceptance Criteria

- SQL Server is the only authoritative source for domains, projects, collections, documents, content units, and embeddings.
- Project memory and durable domain knowledge are represented in one coherent model.
- Chroma is consolidated if used during migration and is not one folder per domain.
- Prompt flows work from SQL retrieval without requiring Chroma folders.
- Whole-document retrieval and vector retrieval both work.
- Existing Chroma data can be imported without duplicate domains, collections, or content units.
- The new desktop and backend project responsibilities are documented before implementation folders are created.

## Open Follow-Up

The DDL should be updated next to match this plan. The current HTML DDL still reflects the older `Chunks` and `Embeddings.VectorJson` approach and should be revised before SQL migration execution.
