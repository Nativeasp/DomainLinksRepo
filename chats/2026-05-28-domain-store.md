# Session Summary

## Date
2026-05-28

## Heads up for next chat
- Domain Store is working again with Shared Services and Client Services split trees, child creation fixed, and AI assist now using `llama3.1:8b`.
- Main decision: keep pushing the Domain Store as the active RAG curation surface and continue refining hierarchy editing instead of backing into a separate tool.
- Biggest open item: keep tightening domain-tree editing UX and taxonomy structure as real content gets loaded and moved around.
- Immediate next step: continue with Domain Store behavior, especially reorder/edit workflows and any remaining taxonomy cleanup.

## Topic
Domain Store buildout, taxonomy seeding, delete/reorder behavior, and local AI assist stabilization.

## Last stopping point
`DomainLinksDesktop/DomainLinksDesktop/DomainStoreWindow.xaml.cs` while expanding domain movement from roots-only to sibling reordering at all levels.

## Key decisions
- Restored the database to the pre-governance-heavy backup, then preserved `Projects` behavior by renaming the system domain to `Workspace Memory` with code `workspace-memory`.
- Added `DomainOrientations` with `Shared Services` and `Client Services`, then split the Domain Store tree into those two sections.
- Seeded new two-level shared-services and client-services domain taxonomies via SQL migrations instead of continuing with the earlier governance-heavy structure.
- Kept Domain Store as the main RAG curation surface with domain descriptions as retrieval-shaping context text.
- Added AI writing assist inside Domain Store and switched its live Ollama model from missing `gemma3:1b` to working `llama3.1:8b`.
- Implemented hard-delete flows for domains and collections with warnings when documents exist.
- Extended domain reordering from roots-only to sibling movement for all levels while keeping subtree movement together and blocking cross-parent moves.

## Commands used
- `dotnet build DomainLinksDesktop\DomainLinksDesktop.slnx`
- `Invoke-RestMethod http://127.0.0.1:5056/health`
- `Invoke-RestMethod http://127.0.0.1:5056/config`
- `Invoke-RestMethod http://10.211.55.2:11434/api/tags`
- `python -m uvicorn app.main:app --host 127.0.0.1 --port 5056`
- PowerShell `Stop-Process` / `Start-Process` to restart the backend
- direct `Invoke-RestMethod` calls to `/domains`, `/domains/assist`, `/domain-sibling-order`, and delete-preview/delete endpoints for validation
- SQL backup/restore work against local SQL Server, including `.bak` backups before risky changes

## Files created or changed
- `DomainLinksBackend/app/config.py`
- `DomainLinksBackend/app/main.py`
- `DomainLinksBackend/app/repositories.py`
- `DomainLinksBackend/.env`
- `DomainLinksBackend/.env.example`
- `DomainLinksBackend/migrations/016_domain_orientations.sql`
- `DomainLinksBackend/migrations/017_set_existing_domains_shared.sql`
- `DomainLinksBackend/migrations/018_rename_projects_domain_to_workspace_memory.sql`
- `DomainLinksBackend/migrations/019_workspace_memory_wording.sql`
- `DomainLinksBackend/migrations/020_seed_shared_services_domains.sql`
- `DomainLinksBackend/migrations/021_seed_client_services_domains.sql`
- `DomainLinksDesktop/DomainLinksDesktop/DomainStoreWindow.xaml`
- `DomainLinksDesktop/DomainLinksDesktop/DomainStoreWindow.xaml.cs`
- `DomainLinksDesktop/DomainLinksDesktop/TextPromptWindow.xaml`
- `DomainLinksDesktop/DomainLinksDesktop/MainWindow.xaml.cs`
- `DomainLinksDesktop/DomainLinksDesktop/WebShell/menu-host.html`
- `DomainLinksDesktop/DomainLinksDesktop/Models.cs`

## Open questions
- Should domain promote/demote be added next, or should drag/drop stay reorder-only for now?
- Should `Workspace Memory` remain hidden from the Domain Store tree, or become a visible system section later?
- Should domain orientation be editable in the UI, or stay implicit from root section and inheritance?
- How much more taxonomy cleanup is needed before broader document ingestion starts?

## Blocked by
- No hard blocker. Remaining work is mostly product/design decisions around taxonomy shape and editing behavior.

## Next steps
- Continue refining Domain Store behavior and layout.
- Test sibling reorder thoroughly at multiple hierarchy levels in the running app.
- Decide whether promote/demote should be added next.
- Continue rebuilding the domain taxonomy and descriptions with the new shared/client structure.

## Question for Richard: What is my next phase or priority?
Continue with domain store

## Next Session Priority
Continue with domain store
