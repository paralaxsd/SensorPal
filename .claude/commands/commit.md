# Commit Workflow

Follow these steps exactly — do NOT skip ahead:

1. Run `git diff --stat HEAD` to show all changed files.
2. Run `git diff HEAD` to read the full diff.
3. Summarize all changes as a concise bullet list grouped by area (server / mobile / shared / build).
4. Propose a conventional commit message (type: short summary, optional body).
5. **STOP. Wait for explicit user approval** ("commit", "yes", "ok", etc.) before running `git commit`.
6. Only after approval: stage the relevant files and create the commit.
7. **Never push** unless the user explicitly says so.
8. **Never amend** a previous commit unless explicitly asked.
9. **Never use `--no-verify`** or skip hooks.
