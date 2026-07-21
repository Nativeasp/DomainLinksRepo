# Session Summary

## Date

2026-07-21

## Heads up for next chat

- Production `DomainLinks` now uses integer identity keys; the former GUID database is retained read-only for rollback.
- The Mandate–Capability–Strategy Framework v1.0 is published in SQL Server, with Capability and Strategy as immutable foundational principles.
- The biggest open item is proving how those pillars affect a real workflow rather than only confirming storage and prompt plumbing.
- Start with the DomainLinks-versus-Enterprise architecture discussion, then exercise department mandate creation as the first concrete use case.

## Topic

Implement the versioned Mandate–Capability–Strategy Framework, strengthen organizational content generation, and convert all application GUID keys to SQL Server integer identity keys.

## Last stopping point

Production database cutover, API validation, semantic synchronization, and desktop launch are complete. Work paused before exercising Capability and Strategy through an Enterprise organization/department mandate workflow.

## Key Decisions

- SQL Server is the only source of truth for authoritative framework and principle content.
- Mandate sits above Capability and Strategy, providing purpose, authority, and scope.
- Capability and Strategy are immutable foundational principles; framework structure and contextual rules remain versioned and malleable.
- Mandates are part of the implementation traceability path alongside principles, policies, controls, procedures, and evidence.
- Downstream AI work receives applicable framework context based on artifact type, activity, stage, and delivery mode.
- Organizational generation uses `qwen3.5:35b-mlx`; embeddings remain `nomic-embed-text:v1.5`.
- All 33 former GUID entity PKs now use `INT IDENTITY(1,1)`, related FKs use `INT`, and the active application schema has zero `uniqueidentifier` columns.
- Database IDs remain strings at the HTTP/JSON boundary so the existing desktop contract remains compatible.
- `C:\SQLDatabases\Backups` is the standard backup directory, with database name and timestamp in each filename.
- The former production database remains read-only as `DomainLinks_guid_archive_20260721_151121` for rollback.

## Commands Used

- `BACKUP DATABASE`, `RESTORE VERIFYONLY`, `RESTORE DATABASE`, and database-name swap commands through `sqlcmd`.
- `python DomainLinksBackend/scripts/convert_guid_ids_to_int.py --database <clone> --yes`.
- `DBCC CHECKCONSTRAINTS WITH ALL_CONSTRAINTS` and `DBCC CHECKDB WITH NO_INFOMSGS`.
- `python -m ruff check DomainLinksBackend/app DomainLinksBackend/tests DomainLinksBackend/scripts`.
- `python -m pytest DomainLinksBackend/tests -q`.
- `dotnet build DomainLinksDesktop/DomainLinksDesktop/DomainLinksDesktop.csproj -c Release`.
- Launched `DomainLinksDesktop.exe` and confirmed the main window was responding.

## Files Created or Changed

- Framework implementation: `DomainLinksBackend/app/frameworks.py`, `DomainLinksBackend/app/main.py`, `DomainLinksBackend/app/semantic_worker.py`.
- Framework schema: migrations `034` and its rollback script.
- Integer conversion: `DomainLinksBackend/scripts/convert_guid_ids_to_int.py`, migration `035`, and its restore-based rollback marker.
- Database/API compatibility: `DomainLinksBackend/app/db.py`, `DomainLinksBackend/app/repositories.py`.
- Model configuration: backend configuration and desktop content-generation settings/workspaces.
- Tests: framework routes, database ID serialization, and configuration coverage.
- Documentation: current technology stack, backend README, root README, framework design, integer conversion record, PRD, migration plan, and `AGENTS.md` backup convention.
- Backups/audits outside the repository:
  - `C:\SQLDatabases\Backups\DomainLinks_pre_integer_cutover_20260721_151121.bak`
  - `C:\SQLDatabases\Backups\DomainLinks_post_integer_conversion_20260721_151341.bak`
  - `C:\SQLDatabases\Backups\DomainLinks_guid_to_int_20260721_151121.csv`
  - orphan policy-control explanation export with the same timestamp.

## Concerns

- The Capability and Strategy implementation is stored and connected to AI context, but it still needs a real impacted workflow exercise to confirm that the principles influence useful outputs at the correct points.
- This is not yet a confirmed defect; it is an implementation-validation concern.
- The boundary between DomainLinks and a future Enterprise application is undecided. They may remain separate applications or be kept together if shared organizational and mandate workflows justify it.

## Open Questions

- Should DomainLinks and Enterprise remain separate applications, share services/data, or become one product surface?
- At which exact stages should Capability and Strategy be shown to users versus injected only into AI context?
- What acceptance criteria demonstrate that a generated department mandate correctly interprets both pillars and the parent organizational strategy?

## Blocked by

Further Enterprise workflow implementation should wait for the application-boundary discussion and agreement on the first mandate exercise and its acceptance criteria.

## Next Steps

1. Discuss the DomainLinks and Enterprise product/application boundary.
2. Map an Enterprise organization and department mandate creation workflow to the published framework.
3. Generate or assess a department mandate using both Capability and Strategy context.
4. Inspect the output and trace which framework rules, principles, and organizational strategy inputs affected it.
5. Refine contextual delivery rules and UI presentation based on that exercise.

## Question for Richard: What is my next phase or priority?

Begin the next session by deciding how DomainLinks and the Enterprise system should relate. Use creation of an organization and a department mandate as the first practical exercise for pulling and applying the Capability and Strategy pillars, including alignment with the organization’s overall strategy and goals.

## Next Session Priority

Start with the DomainLinks-versus-Enterprise architecture discussion, then exercise department mandate creation as the first real validation of how Capability and Strategy are retrieved, applied, traced, and presented.
