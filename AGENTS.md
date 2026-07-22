# Project instructions

Use concise, practical output.

At the beginning of every new chat, before working on the user's request, find and read the most recently modified Markdown file in `chats/`. Treat it as continuity context, while prioritizing the user's current request if it conflicts with the summary.

Use `C:\SQLDatabases\Backups` as the standard destination for all SQL Server database backups. Include the database name and a date-time stamp in each backup filename.

Use the repo skill at `.codex/skills/git-commit-push` when the user asks to commit changes, write a commit message, push to GitHub, publish a branch, or sync the repo with its remote. Do not push unless the user explicitly asks.

When asked to save a session summary, write the file to `chats/`.
Include a short `Heads up for next chat` section near the top with 2-4 concise bullets that give fast startup context: current state, the main decision, the biggest open item, and the immediate next step.
Also include `Last stopping point` with the exact file, feature, or task where work paused, `Blocked by` if anything is waiting on a decision or dependency, and end with a short question asking: `What is my next phase or priority?`

Preferred summary format:
- Date
- Heads up for next chat
- Topic
- Last stopping point
- Key decisions
- Commands used
- Files created or changed
- Open questions
- Blocked by
- Next steps
- Question for Richard: What is my next phase or priority?

Use Markdown for summary files.

When creating filenames for session summaries, use:
`YYYY-MM-DD-short-topic.md`

If a requested summary depends on chat content that is not available in the current thread, say so clearly and save only what is available.

At the end of the summary process, ask the user:

“What should be treated as the next priority or phase when this project is resumed?”

Wait for the user’s answer before writing the final summary file.

Add the answer as the final section of the markdown file under:

## Next Session Priority
