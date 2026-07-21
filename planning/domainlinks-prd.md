# DomainLinks PRD

Date: 2026-05-20
Status: Draft

## Product Summary

DomainLinks is a local-first AI knowledge and project-memory workspace. It lets a user query durable organizational knowledge and short-term project context from one desktop application backed by a local Windows Python service and SQL Server.

The product should preserve the useful behavior proven in the current VB WinForms prototype while rebuilding the long-term architecture around SQL Server, C# WPF MVVM, and a Windows-hosted backend.

## Goals

- Provide a modern desktop workspace for local AI-assisted knowledge work.
- Support durable domain knowledge, such as HR policies or governance documents.
- Support short-term project memory, such as an active hiring request or temporary research thread.
- Use SQL Server as the source of truth for content, metadata, embeddings, and retrieval.
- Use SQL Server vector search as the long-term primary retrieval backend.
- Keep Ollama as the default model provider today.
- Leave room for LM Studio as an explicit future provider.

## Non-Goals

- Do not rebuild the whole system inside the current VB WinForms app.
- Do not make Chroma the permanent storage authority.
- Do not create implementation folders during this documentation step.
- Do not require cloud services for core local operation.

## Core Concepts

### Domain

A durable knowledge area, such as:
- HR
- Finance
- Governance
- Strategy

Domains contain collections, documents, content units, and embeddings. They represent knowledge expected to stay useful beyond a single short-term task.

### Project

A project is a short-term memory scope. It acts like a domain from a retrieval perspective, but its purpose is active work rather than durable reference knowledge.

Example:
- `Projects -> Hire request new DBA`

Projects should be represented in SQL Server through a domain/scope type such as `ProjectMemory`.

### Collection

A collection is a retrievable corpus inside a domain or project. Internally, collections use SQL Server integer identity keys plus stable unique collection codes. Friendly names and slugs are mutable UI fields.

Example display names:
- `Hiring Policy`
- `Hire request new DBA`

Example internal identity:
- `CollectionId = 42`
- `CollectionCode = hiring-policy`

### Document

A document is an original source item. It may come from a file, pasted text, generated notes, a transcription, or another source.

### ContentUnit

A content unit is a retrievable piece of a document or project memory.

Supported unit types:
- `Document`
- `Section`
- `Chunk`
- `Summary`

This model allows classic RAG and future larger-context retrieval where a whole document may be loaded directly into context.

### Embedding

An embedding is a vector representation of a content unit. The first implementation should use SQL Server native `vector(768)` storage for the current Ollama/Nomic embedding profile unless the model changes.

### RetrievalProfile

A retrieval profile controls how context is selected for inference.

Initial retrieval modes:
- `NoSearch`
- `WholeDocument`
- `DomainVector`
- `ProjectVector`
- `ClusterVector`
- `Hybrid`

## User Experience Requirements

The new desktop app should be built as a C# WPF MVVM application.

Required app areas:
- domain/project navigation tree
- prompt editor
- streaming response surface
- context/source panel
- document/content management area
- backend/provider status indicator
- side chain prompt (counter)
- backend domain selection (for domain context)

The active retrieval scope must be visible. The user should always know whether they are asking against durable domain knowledge, project memory, or no retrieval scope.

Domain stores and project stores should be presented consistently. The UI should not expose Chroma folders, SQLite files, or backend storage details.

## Backend Requirements

The target backend is a Windows-hosted Python service running on the Win11 VM near SQL Server.

Backend responsibilities:
- manage domains and project scopes
- manage collections
- ingest documents
- create content units
- generate embeddings through Ollama
- store embeddings in SQL Server
- retrieve context using SQL Server vector and whole-document strategies
- stream prompt responses
- expose backend/provider health
- import legacy Chroma data

Provider behavior:
- Ollama is the default provider
- LM Studio may be supported later as an explicit option
- provider selection should never silently fall back to the wrong provider

## Data Architecture

SQL Server owns truth for:
- domains
- projects
- collections
- documents
- content units
- embeddings
- retrieval profiles
- provider settings
- migration state

Recommended schema direction:
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

Use a field such as `DomainType` to distinguish:
- `Knowledge`
- `ProjectMemory`
- `System`

## Chroma Migration Policy

Chroma is a compatibility bridge, not the target database.

During migration:
- existing Chroma stores can be imported
- a single shared Chroma persistent store may be used temporarily
- domain/project boundaries should be represented by collection codes or metadata filters
- one-folder-per-domain should be retired

After migration:
- SQL Server retrieval should become the normal runtime path
- Chroma should be removable without losing truth data

## Project Structure Direction

Future implementation folders:
- `DomainLinksDesktop`
- `DomainLinksBackend`

Current reference systems:
- `DomainLinksAI`: VB WinForms prototype and reference behavior
- current `rag-chroma-gunicorn`: Python/Flask/Chroma prototype and reference backend

The future root repository should include the planning docs, desktop app, backend service, and migration tooling.

## Initial Workflows

### Ask Against A Durable Domain

1. User selects a durable domain such as HR.
2. User selects or defaults to a collection such as Hiring Policy.
3. User enters a prompt.
4. Backend retrieves context using the selected retrieval profile.
5. Response streams back with visible source context.

### Ask Against Project Memory

1. User selects a project such as Hire request new DBA.
2. User asks a question about the active work.
3. Backend retrieves from the project memory scope.
4. Response streams back using only relevant project context unless configured otherwise.

### Whole Document Context

1. User selects a document or project item.
2. Retrieval profile chooses whole-document mode.
3. Backend sends the whole document or selected sections into context.
4. Vector search is skipped or used only as supporting retrieval.

### Import Legacy Chroma Data

1. Backend scans existing Chroma stores.
2. Domains, collections, documents, and content units are created in SQL Server.
3. Embeddings are imported or regenerated.
4. SQL retrieval is validated against known prompts.

## Acceptance Criteria

- PRD distinguishes durable domains from short-term project memory.
- PRD defines C# WPF MVVM as the target desktop direction.
- PRD defines Windows-hosted Python service as the target backend direction.
- PRD defines SQL Server vector retrieval as the long-term target.
- PRD keeps Chroma only as a migration bridge.
- PRD captures the existing VB app and Python backend as references.
- PRD names future folders without creating them yet.

## Assumptions

- SQL Server 2025 vector support is available locally.
- Ollama remains the active provider for current work.
- The first embedding profile is expected to be 768 dimensions.
- Project memory and durable knowledge can share the same core schema.
- The user is the initial primary operator of the system.
