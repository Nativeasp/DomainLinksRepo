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

Config check:

```text
http://127.0.0.1:5056/config
```

## Migrations

The first SQL Server schema migration lives in:

```text
migrations/001_initial_schema.sql
```

Apply it with `sqlcmd`:

```powershell
sqlcmd -S RICHARDBASQB378 -E -C -d DomainLinks -i DomainLinksBackend\migrations\001_initial_schema.sql
sqlcmd -S RICHARDBASQB378 -E -C -d DomainLinks -i DomainLinksBackend\migrations\002_seed_initial_scopes.sql
```
