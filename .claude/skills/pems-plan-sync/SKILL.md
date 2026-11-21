---
name: pems-plan-sync
description: Read, change or add PEMS plan rows (plan/rows/NNNN.md) with tools/plan-sync, keep Jira and the plan in step.
---

# PEMS plan rows (guideline 00 sections 1 and 5d; `python tools/plan-sync/plan_sync.py ...`)

The plan is markdown, one file per row in `plan/rows/NNNN.md`; the row number is the Jira key number (SCRUM-504 is #504). The plan owns the scope text, Jira shows a copy: read the row, not the Jira description (cheaper).

- **Read:** `plan_sync.py show <id>` (front matter + Evidence, Prompt, Requirement), `summary`, `report`.
- **Change a field:** `plan_sync.py set <id> key=value` (status, effort, depends ...) or edit the file; then `plan_sync.py check` (must say 0 findings).
- **Add a row** for a new Jira issue (after searching for an existing one, rule 28): create the Jira issue first (`pems-jira-handover`), then `plan_sync.py new <key number> "Title" area="..." group="..." milestone=M2 priority=P1 effort=M track="Tech Debt" doer=Claude` and fill `## Evidence` (why, with the measured facts), `## Prompt` (what to do, numbered) and `## Requirement` (starts "Done when:"). Run `check`.
- **Jira consistency:** `plan_sync.py tracker-check` verifies the epic parent, the `[#n]` title prefix, the labels `area-*`, `doer-*`, `effort-*`, `plan-n` and `qa-pending` on In Review (needs the `JIRA_*` settings; otherwise you are the check: read the issue back after creating it).
- Commit the row in the same pull request as the work or the next one; stage files by path, never `git add -A`.
