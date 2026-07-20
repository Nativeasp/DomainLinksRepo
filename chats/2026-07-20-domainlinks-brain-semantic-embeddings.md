# Session Summary

## Date
2026-07-20

## Heads up for next chat
- DomainLinks Brain Explorer MVP is implemented as a directly launchable WebView2 form with contextual entry points.
- SQL Server now holds 346 semantic-artifact embeddings for domains, controls, policies, and policy statements; the separate worker maintains them automatically.
- Brain opens Information Management around its central domain framework and expands related governance knowledge outward.
- Next priority is implementing the Capability and Strategy pillars under Mandate.

## Topic
Current technology-stack documentation, DomainLinks Brain, and background semantic embedding.

## Last stopping point
Radial/force-layout correction in `DomainLinksDesktop/DomainLinksDesktop/WebShell/Brain/brain.js`, followed by a successful rebuild and relaunch.

## Key Decisions
- Replaced obsolete Chroma references in current-facing documentation; SQL Server remains the vector and knowledge source.
- Built Brain as a new WPF window with WebView2 and a self-contained local HTML/CSS/JavaScript graph canvas.
- The parameterless constructor defaults to `information-management`; a typed context supports domain, collection, document, policy, and control launches.
- Brain uses explicit relational edges plus semantic similarity and deterministic evidence-gap indicators.
- Added a shared semantic-artifact layer rather than vector columns on individual source tables.
- Chose a hybrid embedding process: automatic separate worker plus manual pending, retry, and rebuild commands.
- Rebuild-all remains an explicit command; normal processing embeds only missing or changed canonical content.
- Added paired forward/rollback DDL and a database reversal record.
- Defined the next conceptual architecture as Mandate above the two pillars Capability and Strategy.

## Commands Used
- `rg -n ...`
- `git status --short --branch`
- `git diff --stat`
- `python -m py_compile ...`
- `dotnet build DomainLinksDesktop/DomainLinksDesktop/DomainLinksDesktop.csproj -o artifacts/...`
- `sqlcmd -S RICHARDBASQB378 -E -C -d DomainLinks -i DomainLinksBackend/migrations/033_semantic_artifact_embeddings.sql`
- `python -m app.semantic_worker --once --batch-size 32`
- `Invoke-RestMethod http://127.0.0.1:5056/...`
- `Start-Process ... DomainLinksDesktop.exe --brain`

## Files Created or Changed
- `Docs/current-tech-stack.md`
- `Docs/PRD_DomainLinks_Brain_v2_2026-07-18.md`
- `README.md`
- `DomainLinksBackend/app/brain.py`
- `DomainLinksBackend/app/semantic_worker.py`
- `DomainLinksBackend/app/main.py`
- `DomainLinksBackend/migrations/033_semantic_artifact_embeddings.sql`
- `DomainLinksBackend/migrations/rollback_033_semantic_artifact_embeddings.sql`
- `DomainLinksBackend/tests/test_brain_routes.py`
- `DomainLinksDesktop/DomainLinksDesktop/DomainLinksBrainWindow.xaml`
- `DomainLinksDesktop/DomainLinksDesktop/DomainLinksBrainWindow.xaml.cs`
- `DomainLinksDesktop/DomainLinksDesktop/BrainLaunchContext.cs`
- `DomainLinksDesktop/DomainLinksDesktop/WebShell/Brain/`
- Desktop startup, settings, menu, Domain Store, controls, and policy-workspace integration files.
- `planning/domainlinks-brain-ddl-reversal.md`

## Concerns
No additional concerns were requested for this session.

## Open Questions
- How Mandate, Capability, and Strategy should be represented in the domain model, UI, and semantic graph.
- Which readiness dimensions and measures should roll up into Capability.
- How strategic direction should consume, constrain, or prioritize Capability.

## Blocked by
Nothing currently blocked. The next phase needs product/data-model design before implementation.

## Next Steps
- Define Mandate as the purpose, authority, and scope above Capability and Strategy.
- Model Capability as organizational readiness across people, systems, processes, tools, controls, resources, and structure.
- Model Strategy as direction and the application of available capability.
- Decide how the two pillars appear in DomainLinks navigation, data relationships, Brain, and policy/control workflows.

## Question for Richard
What is my next phase or priority?

## Next Session Priority
Implement the two pillars: **Capability** and **Strategy**. Capability defines the organization’s state of readiness—its people, systems, processes, tools, controls, resources, and structure. Strategy determines where the organization is going and how that capability will be applied. **Capability defines the state of readiness. Strategy puts it to work.** Mandate sits above both, giving them purpose, authority, and scope.
