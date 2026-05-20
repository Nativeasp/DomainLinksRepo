# DomainLinks Backend

Windows-hosted Python backend for DomainLinks.

## Responsibilities

- SQL Server access and schema-backed repositories.
- SQL Server vector retrieval.
- Ollama provider integration.
- Future LM Studio provider integration.
- Legacy Chroma import and validation tooling.
- Streaming prompt responses for the desktop client.

## Development

Create a virtual environment, install dependencies, then run:

```powershell
uvicorn app.main:app --reload --host 127.0.0.1 --port 5056
```

Health check:

```text
http://127.0.0.1:5056/health
```
