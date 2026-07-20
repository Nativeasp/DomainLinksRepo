# DomainLinks Current Technology Stack

Last reviewed: 2026-07-18

This document describes the technology that is implemented or configured in the current DomainLinks repository. Machine-specific addresses reflect the checked-in development configuration and are not portable defaults.

## Architecture

```mermaid
flowchart LR
    User[User] --> Desktop[.NET 10 WPF desktop]
    Desktop -->|HTTP/JSON and streamed responses| API[Python 3.11+ FastAPI backend]
    Desktop -->|model discovery and selected direct calls| Ollama[Ollama local AI runtime]
    Desktop -->|JSON chat files| ChatFiles[AppData DomainLinks Chats]
    Desktop -->|encrypted compressed backup| API
    API -->|pyodbc / ODBC Driver 18| SQL[(SQL Server 2025<br/>DomainLinks database)]
    API -->|chat, title, and embedding requests| Ollama
    Ollama --> ChatModel[Chat and title models]
    Ollama --> EmbedModel[nomic-embed-text:v1.5]
    Ollama --> OcrModel[glm-ocr:bf16 or selected OCR model]
    API -->|VECTOR(768) similarity search| SQL
    Desktop --> WebView[WebView2 HTML presentation surfaces]
```

The desktop is the user-facing application. It communicates with the backend for domain knowledge, documents, retrieval, controls, policies, chat generation, and backup operations. SQL Server is the structured source of truth. Ollama supplies local inference. Chat history remains local-first as JSON files and may be backed up through the backend.

## Core Technology Inventory

| Area | Technology | Current role |
|---|---|---|
| Desktop | C#, .NET 10, WPF | Windows desktop UI, chat workspace, domain management, controls, policies, document workflows, and local settings |
| Embedded web UI | Microsoft WebView2 | Renders the shared navigation shell, policy presentations, OCR previews, and other HTML surfaces |
| Rich document output | DocumentFormat.OpenXml 3.3.0 | Reads or creates Microsoft Office Open XML documents |
| Markdown | Markdig 0.44.0 | Converts Markdown content for presentation in the desktop application |
| Backend | Python 3.11+, FastAPI 0.115+, Uvicorn 0.30+ | Local HTTP API, request validation, streaming, provider calls, ingestion, retrieval, and persistence orchestration |
| Backend data access | pyodbc 5.1+ | Connects the backend to SQL Server through ODBC |
| Backend HTTP | httpx 0.27+ | Calls Ollama and other HTTP services |
| PDF ingestion | pypdf 5.1+ | Extracts text from text-based PDFs in the backend |
| Configuration | pydantic-settings 2.4+ | Loads backend defaults and `DOMAINLINKS_` environment overrides |
| Main database | SQL Server 2025 | Stores domain knowledge and native vector embeddings |
| AI runtime | Ollama | Hosts local chat, title, embedding, and vision/OCR models |
| Tests and quality | pytest 8.3+, Ruff 0.6+ | Backend tests and Python linting; both are development dependencies |

Package versions use the constraints checked into the project files; they are not necessarily the exact versions installed on every workstation.

## Databases and Storage

### SQL Server: `DomainLinks`

The backend connects to SQL Server `RICHARDBASQB378`, database `DomainLinks`, using `ODBC Driver 18 for SQL Server`, Windows trusted authentication, encryption, and a trusted server certificate. The desktop does not connect to SQL Server directly.

SQL Server stores:

- Domain taxonomy, hierarchy, clusters, collections, orientations, and workspace-memory scopes.
- Documents and extracted content units.
- Embedding profiles and 768-dimensional content embeddings in the native `VECTOR(768)` type.
- Canonical semantic artifacts and 768-dimensional embeddings for domains, controls, policies, and policy statements.
- Retrieval profiles and provider settings.
- Controls, control types, domain-to-control assignments, and ordering metadata.
- Policy templates, policies, structured policy sections and statements, principles, relations, and linked control statements.
- Application users and encrypted chat-backup payload metadata.
- Schema migration history.

Retrieval is implemented in SQL Server. The backend embeds the query through Ollama, then performs vector similarity search against stored content-unit embeddings. Full-document context can also be assembled without semantic retrieval.

### Local JSON chat storage

`LocalChatStore` saves chat history as formatted JSON files under `%APPDATA%\DomainLinks\Chats`. Each root collection has a file named approximately `<display-name>--<collection-code>.json`, containing its threads, messages, supplemental content, timestamps, and response statistics.

This is the primary local chat history store; it is not SQLite. `ChatBackupService` can gzip, encrypt, and send snapshots to the backend for SQL-backed backup and restore. Malformed local files are skipped so one damaged file does not prevent startup.

### Local configuration

- Desktop runtime settings are read from `domainlinks-desktop.settings.json` beside the executable and are updated with window state and the last model/retrieval selections.
- Backend settings are loaded from defaults, `.env`, or `DOMAINLINKS_` environment variables.
- No SQL credentials are stored in the desktop configuration.

## AI Models and Responsibilities

| Function | Current default/configuration | How it is used |
|---|---|---|
| General chat and generation | `llama3.1:8b` backend default | Main chat, policy/control assistance, explanations, and other generated text; the desktop can select another installed Ollama model |
| Chat-title generation | `llama3.1:8b` | Produces concise titles for new chats |
| Embeddings | `nomic-embed-text:v1.5` | Generates 768-dimensional embeddings for content units and retrieval queries |
| OCR/vision | `glm-ocr:bf16` desktop default | Extracts text from PDFs and supported images through Ollama's generation API |
| Alternate OCR | `deepseek-ocr:3b` | Offered by the OCR viewer as another suggested installed model |

The desktop discovers locally installed models through Ollama's `/api/tags` endpoint and remembers the last selected chat model. The backend uses Ollama's `/api/generate`, `/api/embed`, and legacy `/api/embeddings` endpoints as appropriate. Model values are configuration, not hard platform dependencies, except that stored embeddings must match the configured embedding profile and vector dimension.

DomainLinks also uses Windows Runtime PDF rendering and Windows OCR in PowerShell document-extraction helpers. This path is separate from the standalone Ollama OCR viewer.

## Major Components and Data Flows

### Desktop application

- **Main chat workspace:** manages domain/context selection, chat threads, model selection, full-context and vector-RAG modes, streaming responses, and local chat persistence.
- **Domain Store:** manages the domain hierarchy, collections, documents, domain orientation, controls, and policy workspaces.
- **Policy and control workspaces:** create, review, order, present, and connect policies, structured policy content, and controls.
- **Document tools:** upload documents, inspect extracted text, and render PDF content.
- **OCR viewer:** previews local PDFs/images and submits page images to a selected Ollama OCR model; it does not automatically import the result into DomainLinks.
- **Backend auto-starter and endpoint resolver:** starts the local Python service when needed and tries configured fallback addresses for the backend and Ollama.

### Backend service

- **FastAPI routes:** expose health/configuration, domains, collections, documents, embeddings, retrieval, controls, policies, AI-generation, debug, and chat-backup operations.
- **Repository layer:** contains SQL statements and maps database rows to API response shapes.
- **Document ingestion:** validates uploads, extracts PDF text, splits content into units, and coordinates persistence and embedding generation.
- **Retrieval and prompt assembly:** selects full document/domain context and/or vector matches, applies prompt budgets, and calls Ollama.
- **Streaming and diagnostics:** streams model output and records request traces used by local debug pages.
- **Semantic embedding worker:** runs as a separate Python process, detects new or changed governance records by content hash, and incrementally maintains their vectors without blocking API requests.

The worker uses SQL-backed `Pending`, `Processing`, `Embedded`, `Failed`, and `Archived` states as its durable queue. It retries failures with backoff and uses a SQL application lock to ensure only one active worker processes the queue. Brain also exposes manual pending, retry, and rebuild commands.

### Chat and retrieval flow

1. The desktop sends the prompt, selected model, conversation history, selected domains/collections, and context-mode choices to FastAPI.
2. The backend loads the requested domain, policy, control, and document context from SQL Server.
3. For vector RAG, the backend embeds the query with `nomic-embed-text:v1.5` and searches SQL Server's stored vectors.
4. The backend compiles the prompt and streams the selected Ollama model's response to the desktop.
5. The desktop adds the response to its in-memory thread and persists the root collection's chat JSON file.

### Document ingestion flow

1. The desktop uploads a document to the backend.
2. The backend extracts available text, creates document and content-unit records, and requests embeddings from Ollama.
3. SQL Server stores the document metadata, content text, embedding profile, and native vectors for later retrieval.

### Backup flow

1. The desktop serializes a root collection's chat history to JSON.
2. `ChatBackupService` compresses and encrypts the content using a key derived from the current Windows user identity.
3. The backend stores or restores the backup payload and metadata through SQL Server.

## Current Development Configuration

| Service | Checked-in value |
|---|---|
| Backend primary URL | `http://127.0.0.1:5056` |
| Backend fallbacks | `http://localhost:5056`, `http://10.211.55.2:5056` |
| Backend startup | `.venv\Scripts\python.exe -m uvicorn app.main:app --reload --host 127.0.0.1 --port 5056` |
| Ollama primary URL | `http://10.211.55.2:11434` |
| Ollama fallbacks | `http://127.0.0.1:11434`, `http://localhost:11434` |
| SQL Server | `RICHARDBASQB378` |
| SQL database | `DomainLinks` |
| SQL driver | `ODBC Driver 18 for SQL Server` |

The `10.211.55.2` address reflects the current Windows/Parallels development network. Environment-specific values should be overridden rather than assumed on another machine.

## Roadmap and Legacy Technology

- **LM Studio:** a base URL (`http://127.0.0.1:1234`) exists in backend configuration, but Ollama is the implemented default provider and LM Studio integration remains future work.
- **Legacy VB WinForms client:** referenced historically as `DomainLinksAI` but intentionally excluded from this rebuilt root repository.
- **SQL Server direction:** remains the long-term source of truth for structured knowledge, content, native embeddings, and retrieval.

## Keeping This Document Current

Review this page whenever dependencies, persistence boundaries, models, or service topology change. The primary sources of truth are:

- `DomainLinksDesktop/DomainLinksDesktop/DomainLinksDesktop.csproj`
- `DomainLinksDesktop/DomainLinksDesktop/domainlinks-desktop.settings.json`
- `DomainLinksBackend/pyproject.toml`
- `DomainLinksBackend/app/config.py`
- `DomainLinksBackend/migrations/`
