# PEMS Development Guideline

Status: **v1.1 - 2026-10-26.** Owner: PM. Applies to every developer and every AI agent working in this repository.

## 1. Purpose

One place that says *how we build PEMS*: the workflow around a task, the coding standards, how to use tools and libraries,
how to reuse components, how to research, and how the knowledge gathered while working flows back. It consolidates:

- the standing **Instructions** (rules 1-26) in `plan/sheets/instructions.md`;
- the Jira workflow and the plan/Jira field ownership ([PLAN_TRACKER_WORKFLOW.md](../process/PLAN_TRACKER_WORKFLOW.md));
- the architecture and conventions that were scattered in `README.md`, `.claude/CLAUDE.md` and `docs/design/*`;
- the owner's working preferences (reuse before build, research first, batch similar tasks with approval).

The plan files stay the source of truth for **what** to build; this guideline is the source of truth for **how**.
When they disagree, the Instructions page wins for rules 1-26 and this guideline must be corrected in the same pull request.

## 2. Reading map - what to read for which task

Every task: chapters **0, 1 (section "Definition of done"), 6**. Then add by task type:

| Chapter | Read it when |
|---|---|
| [00 Task lifecycle](00-task-lifecycle.md) | **every task** - **the words we use for grouping work (milestone, sprint, issue, work block, batch, slice, wave, family, part, pattern, step, phase, tier, follow-up: section 0.0)**, Jira workflow, research, batching, closing; **section 0 on every go-ahead**: pull main, audit the whole backlog, batch related tasks, state the plan |
| [01 Engineering standards](01-engineering-standards.md) | **every task** - architecture, code, database, settings, security, tests, done |
| [02 UI and components](02-ui-and-components.md) | any view, page, CSS, JS, form, table, modal, dashboard |
| [03 Libraries, tools and research](03-libraries-tools-and-research.md) | adding/changing a package, tool, script; any new capability; design decisions |
| [04 Platform direction](04-platform-direction.md) | anything near metadata, forms, workflow, licensing, deploy, demo, versions (the commercial/BPMS direction); **section 7: the definition-only model (no code, build or deploy to change a form or page)** |
| [05 Environment and commands](05-environment-and-commands.md) | first time in the repo, build/test/migrate/run, CI, deploy |
| [06 Knowledge and guideline maintenance](06-knowledge-and-guideline-maintenance.md) | **every task, at the end** - what to record where; how to change this guideline |
| [07 QA check in the UI](07-qa-check-in-the-ui.md) | **when a task is merged and moves to In Review**, and whenever you QA a task - the independent check in the browser before Done; **section 7a: the sprint review procedure (first sprint: two identical QA tasks; later sprints: section 7b, Bugs in the active sprint, then Done and sprint complete)** |
| [08 REST API standard](08-rest-api-standard.md) | adding or changing anything under `/api` or a JSON endpoint; designing a designer or integration call |
| [09 AI agents and token efficiency](09-ai-agents-and-token-efficiency.md) | **every agent session** - what costs tokens (Jira calls, tests, skills), how to read, search and report cheaply, the project skills `.claude/skills/pems-*`, recommended tools |
| [Learnings log](learnings-log.md) | skim the last entries at the start; append at the end |

## 3. How this guideline reaches every task (enforcement layers)

No single layer is reliable, so there are four:

1. **`AGENTS.md` / `CLAUDE.md` at the repo root** - loaded automatically by agents at session start; they force "read the guideline first".
2. **The Jira issue** - `tools/plan-sync` writes a *Step 0* line into every generated issue description pointing here.
3. **The pull request template** - the author ticks "guideline read, guideline updated or nothing to add".
4. **CI / hooks (optional, see chapter 6)** - `commit-key-check.yml` fails a PR whose title, branch or commit subjects lack the Jira key (chapter 5, [PLAN_TRACKER_WORKFLOW.md](../process/PLAN_TRACKER_WORKFLOW.md) section 4); a further check can fail a PR whose template boxes are unticked.

If you are an agent and the user's prompt names a Jira issue, do all of chapter 0 before touching code, even if the prompt does not say so.

## 4. Conventions of this guideline

- Language: English. Product UI is Persian-first (rule 5), but code, docs, plan, commits and Jira are English.
- "MUST" = a rule that needs the owner's decision to break; "SHOULD" = default, deviate with a stated reason in the task comment.
- Every rule carries its origin (rule number, decision date or learnings-log entry) so a reader can judge whether it is still valid.
- Small and factual. If a chapter exceeds ~300 lines, split it and update the table above.
