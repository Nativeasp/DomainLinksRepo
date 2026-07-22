# Session Summary

## Date

2026-07-21

## Heads up for next chat

- Capability and Strategy now shape domain, child-domain, control, and policy prompts through concrete business meaning rather than framework meta-language.
- Domain assist now includes child domains, collections, documents, controls, and policies as downstream context.
- No current concerns were identified; focused tests are passing and the work is looking strong.
- Continue testing, then begin designing a strong Mandate panel or tab.

## Topic

Validate and strengthen practical use of the Mandate–Capability–Strategy Framework in domain and downstream artifact generation.

## Last stopping point

Prompt guidance, downstream inventories, a positive domain-description example, and regression tests are complete. Work paused before designing the Mandate panel or tab and exercising mandate creation as a full business workflow.

## Key Decisions

- Use an organizational-unit or department mandate as the first end-to-end business object for proving the framework.
- Treat domain descriptions as durable business scope for child domains, collections, controls, policies, procedures, evidence, and mandates.
- Express Capability and Strategy through responsibilities, readiness, direction, and outcomes rather than phrases such as “Content addresses”.
- Include the Land and Natural Resources description as a style example, with instructions not to copy its subject matter.
- Load framework context directly from SQL for authoritative prompt use; semantic embeddings remain available for Brain relationships but are not yet part of ordinary document/chat RAG retrieval.
- Automatically read the newest Markdown file in `chats/` at the beginning of future repository chats.

## Commands Used

- `rg` searches across framework migrations, backend prompt builders, semantic code, and tests.
- `sqlcmd` read-only queries against `DomainLinks` to inspect Community Infrastructure domains and semantic embedding status.
- `.venv\Scripts\python.exe -m ruff check ...`
- `.venv\Scripts\python.exe -m pytest ... -q`
- `git status --short --branch`, `git diff --stat`, `git diff`, `git branch --show-current`, and `git remote -v`.

## Files Created or Changed

- `AGENTS.md` — added automatic newest-chat-summary loading and removed a stray backtick.
- `DomainLinksBackend/app/main.py` — strengthened domain, child-domain, control, and policy framework guidance; added a positive style example and downstream control/policy prompt inventory.
- `DomainLinksBackend/app/repositories.py` — added domain-linked control and rooted-policy data to domain-assist context.
- `DomainLinksBackend/tests/test_domain_framework_prompts.py` — added regression tests for durable scope, substantive framework application, downstream inventories, and the style example.
- `chats/2026-07-21-capability-strategy-domain-prompts.md` — this summary.

## Concerns

- None identified at session close.

## Open Questions

- What information architecture and interactions should the Mandate panel or tab provide?
- Should the Mandate experience live in DomainLinks, a future Enterprise surface, or a shared service presented in both?
- What acceptance criteria will prove that mandate output correctly constrains Capability and Strategy?
- When should semantic artifacts be added to ordinary chat RAG retrieval rather than used only for the Brain and direct framework injection?

## Blocked by

Nothing currently blocked. The Mandate panel/tab needs product and workflow design before implementation.

## Next Steps

1. Continue testing revised domain descriptions and downstream control and policy generation.
2. Define the Mandate panel/tab user workflow and required fields.
3. Create an organizational-unit mandate test case with parent goals, authority, responsibilities, capability needs, gaps, and strategic commitments.
4. Add framework traceability showing the framework version, elements, principles, and context rules applied.
5. Compare framework-assisted mandate output with a no-framework baseline.

## Question for Richard: What is my next phase or priority?

Continue testing the Mandate–Capability–Strategy behavior in the morning. The first next move should be designing and building a great Mandate panel or tab.

## Next Session Priority

Continue practical framework testing, then design a strong Mandate panel or tab as the first full business workflow for creating, applying, and tracing organizational mandates.
