# DomainLinks

DomainLinks is a local-first AI knowledge and project-memory workspace. It is being rebuilt around a C# WPF desktop app, a Windows-hosted Python backend, and SQL Server 2025 vector retrieval.

## Structure

- `DomainLinksDesktop/` - new C# WPF MVVM desktop client.
- `DomainLinksBackend/` - new Python backend service for SQL Server, retrieval, providers, and streaming.
- `planning/` - PRD, SQL migration plan, and DDL notes.
- `DomainLinksAI/` - legacy VB WinForms reference app, intentionally excluded from the new root repo.

## Direction

- SQL Server is the long-term source of truth for domains, project memory, documents, content units, embeddings, and retrieval.
- Projects act as short-term memory scopes.
- Durable domains hold longer-lived organizational knowledge.
- Chroma is only a legacy migration bridge, not the target storage model.
- Ollama is the default provider today; LM Studio remains a future explicit option.

## Current Status

The desktop project is scaffolded. The backend folder contains a minimal FastAPI skeleton ready for SQL Server and provider integration.
