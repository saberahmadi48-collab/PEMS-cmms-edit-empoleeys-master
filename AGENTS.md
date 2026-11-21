# AGENTS.md - read this before any task

This file is the entry point for every AI agent (Claude Code, Codex, Cursor, Copilot, ...) and every developer working in PEMS.
It is short on purpose. The rules live in [docs/guidelines/](docs/guidelines/README.md).

## Mandatory order for every task

0. **START OF A WORK BLOCK** - on any go-ahead ("continue", "next", "yes", a task): (a) pull `main`; (b) audit the whole open backlog against the current code, one verdict per issue (go, modify, split, merge, cancel with the owner's yes, blocked); (c) group the rest into related batches, a blocker travels with the task it blocks, and do each batch together; (d) state the plan in your first message, the go-ahead is its approval, and **claim the batch at once: move every issue of it (10 to 30 related issues, `plan/sheets/sprint-batches.md`) to In Progress, assigned, before any code**, so no other agent picks an untouched issue ([chapter 0, section 0](docs/guidelines/00-task-lifecycle.md)).
   **Words**: milestone, sprint, issue, work block, batch, slice, wave, family, part, pattern, step, phase, tier and follow-up each have one meaning ([chapter 0, section 0.0](docs/guidelines/00-task-lifecycle.md)); a piece of an issue is a **slice** (never "part", "phase" or "P1"), a **wave** is only a set of pages.
   **Efficiency**: tokens, project skills and tools are in [chapter 9](docs/guidelines/09-ai-agents-and-token-efficiency.md); the PEMS procedures are skills in `.claude/skills/pems-*` (`pems-work-block`, `pems-test-gates`, `pems-jira-handover`, `pems-list-page`, `pems-errors`, `pems-localization`, `pems-plan-sync`, `pems-shared-modal`, `pems-migration-guard`; role skills `pems-owner`, `pems-product-manager`, `pems-scrum-master`, `pems-qa`, `pems-developer`, `pems-definition-engineer`); `python tools/test-gates/gates.py` picks the tests of your change. **The sprint goal is the definition-only model** (change a form or page by database only, everything generic: [chapter 4 section 7](docs/guidelines/04-platform-direction.md)); inventory with `python tools/definition-audit/audit.py` before planning a migration.
1. **READ** - open [docs/guidelines/README.md](docs/guidelines/README.md) and the chapters it marks as required for your kind of task.
   Then read the Jira issue (and its parent, dependencies and similar open issues). Say in your first message that you did both.
2. **PLAN** - the standard way ([chapter 0, section 1, 5e](docs/guidelines/00-task-lifecycle.md)): search Jira and the plan for the same or a neighbouring task and modify it before creating one, research best practice on the internet and write the sources down, then create or start the issue by the checklist; look for existing code/components to reuse, check for similar tasks to batch
   (the plan is markdown: one file per row in `plan/rows/NNNN.md`, pages in `plan/sheets`; edit the file or use `python tools/plan-sync/plan_sync.py set|report|new|show|summary`, then `check`; new plan rows get `#` = the Jira key's number, e.g. SCRUM-423 is #423; old rows keep their numbers; the user approves a batch before you start; an approved batch goes into the current sprint (the sprint field is set explicitly: In Progress alone does not do it, verify with `sprint in openSprints()`), assigned to you). A **reported bug is an issue of type Bug** (never a Task) and goes the normal way: Jira issue and plan row first, regression test, PR, QA ([chapter 0, section 3a](docs/guidelines/00-task-lifecycle.md)). Never start or edit an issue that is assigned to someone else or already started without that person's confirmation - no collisions. Move the issue to In Progress and comment your plan.
3. **WORK** - on **one branch per task or approved batch** (`feature/SCRUM-123-short-name`, from `main`); follow the guideline while you code. Reuse and modify what exists; do not duplicate.
4. **CLOSE** - after merging, **delete the working branch** (local and remote); tests (if the session can run them) -> PR -> **merge into `main`** -> Jira **In Review** (conditional done, label `qa-pending`) -> QA check in the UI, whose line 1 is the local test run ([chapter 7 of the guideline](docs/guidelines/07-qa-check-in-the-ui.md); every test or check you could not run is an unchecked line of the Jira QA checklist, not just a sentence in chat; **browser checks are always lines of that checklist**: page, action, expected result, section "Browser checks" in chapter 7) -> **Done**; six artefacts updated (rule 22), Jira comment (kept current while you work, rule 27), Jira status. Then **update the guideline** if you learned
   something durable ([chapter 6](docs/guidelines/06-knowledge-and-guideline-maintenance.md)). Propose next tasks and wait.

## Creating a Jira issue (every creator, every time)

Create it with its **parent Epic** (the milestone epic of the plan row), the labels `area-*`, `doer-*`, `effort-*`, `plan-<key number>`, and the title `[#<key number>] ...` (edit the title right after creation, the number is the Jira key's), after searching for an existing task (rule 28). Then add the plan row with `plan_sync.py new` and run `check`. Full checklist: [chapter 0, section 5d](docs/guidelines/00-task-lifecycle.md). `plan_sync.py tracker-check` fails on a missing epic, title prefix or label.

Until the nightly check runs (SCRUM-475 open), **you are the check**: read the issue back right after creating it, and sweep the open issues from SCRUM-400 on (no parent, title, labels, `qa-pending`) at the start of a session and before any In Review; fix metadata findings in the same step and report the rest. The exact queries are in chapter 0, section 5d.

## Five rules you must never forget

- Jira project `SCRUM` (https://atapems.atlassian.net). Never write code before the issue is In Progress, and never leave a started issue without an assignee (yourself, or Saber if it is not yours). Never mark Done before the tests are green and the QA check passed; never mark In Review before the merge into `main`, and when you could not run the tests say exactly which and why (tests and review are one step for now).
- No new static module; **no hand-written UI markup, ever: search `PEMS.UI` (registry and `TagHelpers`) first, call the `pems-*` component (every component is created, used, modified and removed through the registry: `UiKeys`, `[PemsFacade]`, guideline 02 section 7), and if it almost fits modify it safely (default keeps today's output, tests, ratchet, registry row), if it is missing add it to the library first** ([protocol](docs/guidelines/02-ui-and-components.md#1-the-core-rule-call-and-modify-dont-re-write-r26)); no logic in controllers.
- Offline product: no CDN, no external call, no telemetry. Settings only in `env/*.env`, secrets only in `*.secrets.env`.
- Schema only through new migrations (never edit an applied one). Stage specific files only (`git add <path>`, never `-A`).
- Research before building, and a library before hand-written code (licence recorded).

## Who did the work (shared GitHub account)

Branches, commits and pull requests made through the shared GitHub account `atapems-tech` do not show which person asked for the work.
When the GitHub user is `atapems-tech`, name the real user - **Saber** or **Hamid** - at the end of:

- the **branch name**: `feature/SCRUM-123-short-name-hamid`, `fix/SCRUM-456-short-name-saber`;
- the **commit subject**: `SCRUM-123: short summary (hamid)`.

Find the real user from the Jira account the session works for or from the chat; if it is not clear, ask once before the first commit. Another GitHub user (their own account) needs no suffix.
Owner decision 2026-10-27.

If a rule here conflicts with what the user says in chat, the user wins - then tell them which rule you are overriding and offer to
record the decision in the guideline.
