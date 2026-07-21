# DomainLinks Backend

Windows-hosted Python backend for DomainLinks.

## Responsibilities

- SQL Server access and schema-backed repositories.
- SQL Server vector retrieval.
- Background semantic embedding for domains, controls, policies, and policy statements.
- Ollama provider integration.
- Future LM Studio provider integration.
- Streaming prompt responses for the desktop client.

## Development

Create a virtual environment, install dependencies, then run:

```powershell
uvicorn app.main:app --reload --host 127.0.0.1 --port 5056
```

If you launch the WPF desktop app with its default settings, it now attempts to auto-start this backend on application load when `http://127.0.0.1:5056/health` is unavailable. That auto-start expects the backend virtual environment at:

```text
DomainLinksBackend\.venv\Scripts\python.exe
```

If you want to disable or change that behavior, update `DomainLinksDesktop/domainlinks-desktop.settings.json`.

Use `127.0.0.1` when the desktop app and backend run inside the same Windows VM. If the backend must be reached from the Mac host or another device, bind to `0.0.0.0` and allow the port through Windows Firewall:

```powershell
uvicorn app.main:app --reload --host 0.0.0.0 --port 5056
```

With Parallels Shared Network, the Mac host is commonly reachable from the Windows VM at `10.211.55.2`; bridged networking uses the router-provided address and can change when Wi-Fi networks change.

Health check:

```text
http://127.0.0.1:5056/health
```

Run the semantic embedding worker continuously:

```powershell
python -m app.semantic_worker --poll-seconds 15 --batch-size 16
```

Use `--once` for a one-time synchronization and backfill. The desktop starts the continuous worker automatically by default; SQL application locking prevents duplicate active workers.

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

Migration 035 is an operational table-rebuild migration. Run its guarded utility against a restored validation database before production cutover:

```powershell
python scripts\convert_guid_ids_to_int.py --database DomainLinks_Int_Validation --yes
```

The standard backup and ID-mapping location is `C:\SQLDatabases\Backups`.
