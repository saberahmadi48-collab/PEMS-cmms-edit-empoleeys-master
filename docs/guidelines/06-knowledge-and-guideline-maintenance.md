# 06 - Knowledge and guideline maintenance (read at the end of every task)

## 1. Where knowledge goes

Agent "memory" is per person and per machine - the team never sees it. Anything the team needs lives in the repository:

| What you learned | Where it goes |
|---|---|
| A rule every task must follow | this guideline (right chapter); if it is a standing rule, also `plan/sheets/instructions.md` (owner decides) |
| A durable fact, pitfall or pattern from a task | [learnings-log.md](learnings-log.md) (append), and the plan page *Lessons* if it is cross-cutting |
| What is now supported / limited | plan page *Supportability Matrix* (`plan/sheets/supportability-matrix.md`) [R21] |
| Scope, evidence, report of the task | plan row file `plan/rows/NNNN.md` (Evidence/Report, e.g. `plan_sync.py report <id>`) via PR; status stays in Jira |
| A design decision | ADR under [plan/adr](../../plan/adr/README.md) (spec under `docs/`) + Decision row in the plan |
| What changed for users/devs | `CHANGELOG.md` |
| Personal preference of one developer | your own agent memory, not the repo |

## 2. End-of-task update of the guideline

At the end of each task ask: *did I discover something the next person would want before starting?* If yes, add it **in the same PR**:

1. New lesson / gotcha / decision -> append to `learnings-log.md`: `YYYY-MM-DD | SCRUM-n | area | lesson | what to do`. Two or three lines.
2. A lesson that repeats or became a rule -> promote it into the right chapter and replace the log entry with a pointer.
3. A new shared helper, component, tool or command -> add to chapter 02 (components), 03 (tools) or 05 (commands).
4. A rule in the guideline turned out wrong or outdated -> fix it, say why in the PR; if it contradicts the Instructions page, raise it with the owner instead of silently diverging.
5. Nothing to add -> say "guideline: nothing to add" in the closing comment (the PR checkbox covers this).

Keep entries factual and short; no task diaries. Delete or merge entries that are stale. Never record secrets or personal data.

## 3. Changing the guideline

- Normal improvements: any contributor, by pull request, reviewed like code.
- Changes to rules marked as owner decisions (chapter 04 table, Instructions-derived rules): only with the owner's explicit decision; cite the date in the text.
- Bump the version/date line in README on structural changes. Keep each chapter under ~300 lines; split rather than grow.

## 4. Keeping enforcement alive

Four layers (README section 3). Maintainers check periodically:

- `AGENTS.md` / `CLAUDE.md` still point to the guideline and the first step is "read".
- `tools/plan-sync` still writes the *Step 0* line into issue descriptions.
- PR template still carries the guideline checkboxes.
- Optional hard gate: a CI step that fails PRs whose description lacks ticked "guideline read" / "guideline updated or nothing to add" boxes; optional Claude Code hook (`UserPromptSubmit` or `SessionStart`) in `.claude/settings.json` that prints the reminder. Add them when the team agrees; they are intentionally not on by default because they cost every prompt.

## 5. Decisions recorded on 2026-10-25 (owner)

1. Issue key prefix is `SCRUM` everywhere (docs, templates, branches, commits).
2. Commit messages and PR titles start with the Jira key (`SCRUM-123 feat(scope): ...`), following Instructions rule 25. Rule 7's `vX.Y.Z:` wording in the Instructions page is superseded; update `plan/sheets/instructions.md` at the next plan PR.
3. Two levels of done (superseded in part on 2026-10-26, see section 5a item 6): **In Review** = conditional done; **Done** = finished. The SCRUM workflow today has the statuses To Do, In Progress, In Review, Done, Bugs.
4. `.claude/CLAUDE.md` is a pointer to `AGENTS.md`; the old Instrument Passport directive moved to `docs/cmms/INSTRUMENT_PASSPORT_DIRECTIVE.md`.
5. `.claude/settings.json` calls `graphify` from PATH instead of a per-user absolute path.

## 5a. Decisions recorded on 2026-10-26 (owner)

1. Instructions rule 27: the Jira report of a task is kept current during the work and at the end (chapter 00, section 3).
2. A held task has plan Status `Hold` and, in Jira, the status `On Hold` (added to the SCRUM workflow by the owner on 2026-10-26) plus the label `on-hold` (kept for the plan sync), with the reason and date in its Report.
3. Instructions rule 28: study existing tasks before adding one; modify a fitting task instead (chapter 00, step 5b). Held tasks count as not open.
4. Repository setting "Automatically delete head branches" is on (owner, 2026-10-26): merged pull-request branches disappear without anyone deleting them.
5. Secrets-audit follow-ups: history purge (#374) and FTPS certificate verification (#375) are held; test review (#376), test-maintenance procedure (#377) and review of Claude's roles and setup (#378) are open.
6. **Merge before In Review, QA before Done** (owner, 2026-10-26): the PR is merged into `main` right before the issue moves to In Review (label `qa-pending`); In Review means "merged, waiting for the QA check"; the QA check is done fully in the UI by a person other than the author ([chapter 07](07-qa-check-in-the-ui.md)); Done means the QA check passed. A failed check returns the issue to **To Do** (label `qa-failed`, defects in a comment, assignee and QA comments kept) and the fix goes through a new PR (fix forward) after someone moves it to In Progress.
7. **Tests and review are one step for now** (owner, 2026-10-26): the local test run is line 1 of every QA checklist; In Review is allowed when the tests could not be run in the session if the comment says which and why; Done needs the tests green and the QA check passed. Revisit when CI gates every merge (#7).

## 6. Open points

- **Jira does not enforce the creation checklist (owner decision 2026-11-09, SCRUM-557):** the team-managed project cannot make *Parent* required and has no *Plan ID* field; the `plan-<n>` label and the `[#n]` title are the Plan ID and `tracker-check` enforces Parent (chapter 0, section 5d). What is left is to finish SCRUM-475 (the `JIRA_*` secrets) so the nightly `plan-sync.yml` runs `tracker-check` against the live project. Both steps are written into SCRUM-475. Until they are done the agents do the check themselves (read back after creating, sweep at session start and before In Review; chapter 0, section 5d); when SCRUM-475 is done, shorten that fallback to the nightly job.

- **Jira workflow enforcement (owner action):** the SCRUM workflow still allows In Progress -> Done, and SCRUM-353, -367, -368 and -371 were closed that way (found 2026-10-27). In Space settings > Work types > Task > Edit workflow remove the transitions To Do -> Done and In Progress -> Done so that only In Review -> Done exists; keep Done -> In Review (reopen) and In Review -> To Do (failed QA). Until then the rule in chapter 00 section 4a is checked by the weekly audit.
- `PLAN_TRACKER_WORKFLOW.md` maps statuses On Hold, Backlog and Won't Do. SCRUM now has On Hold (2026-10-26; the 13 held issues were moved into it); Backlog and Won't Do are still missing (project statuses: To Do, On Hold, In Progress, In Review, Done, Bugs). Add them in Jira or change the mapping.
