# 09 - Working with AI agents: tokens, skills, tools (owner decision 2026-11-04, SCRUM-504)

Every agent (Claude Code, Codex, Cursor, Copilot, ...) pays for what it reads, prints and loads. This chapter is how PEMS work stays cheap without losing safety. It is loaded by `AGENTS.md`; the PEMS procedures themselves live as small **project skills** in `.claude/skills/pems-*` so an agent pulls them in only when it needs them.

## 1. What costs tokens here (measured 2026-11-04)

| Cause | Measured | Do instead |
|---|---|---|
| A Jira write (`createJiraIssue`, `editJiraIssue`, `transitionJiraIssue`) | each call echoes the whole issue, about 3k tokens | put every change of one issue into **one** call: summary, labels, assignee, sprint in one `editJiraIssue`; a transition carries its comment |
| A Jira search that asks for `parent` | about 10k tokens for 15 rows (the epic is expanded for each) | `fields: ["summary","status","labels"]` only; never `parent`, `description` or `comment` in a list; `maxResults` as small as the question |
| A free-text JQL (`text ~ "agent"`) | matches half the backlog | search by `labels`, `key`, `summary ~`, or read the plan row |
| Reading an issue description | 1k to 3k tokens | scope lives in `plan/rows/NNNN.md` (`python tools/plan-sync/plan_sync.py show <id>`), cheaper and the same text |
| The whole `PEMS.Tests.Web` namespace | 7 to 11 minutes of waiting and a long log | `python tools/test-gates/gates.py` (chapter 05 "Test budget") |
| A word test filter such as `~Letter` | pulls in the SQL families, each case waits 15 s | exact class names, `FullyQualifiedName~.Class.` |
| Skills and MCP tools in every session | every skill description and every tool schema is loaded each session | few skills, one-line descriptions, nothing unrelated to PEMS (section 4) |
| Unrelated instructions in `CLAUDE.md` / `AGENTS.md` | loaded every session | only what every session needs (section 5) |
| A hook that adds text to every tool call (the removed `graphify hook-guard`) | the same 330 to 450 characters (80 to 110 tokens) on every Read, Glob and Grep: 25 000 to 35 000 tokens in a session of a few hundred calls | no per-call hooks in `.claude/settings.json`; one rule in `CLAUDE.md` costs about 100 tokens once |
| A `graphify query` | 1 600 to 3 400 tokens per answer (budget 2 000, never drops edges), with some noise nodes | use it for orientation only (section 2) |

## 2. Rules for any agent

**Read narrowly.** When you do not know where something lives or how two things relate (orientation), ask the knowledge graph once (`graphify query "..."`, `explain`, `path`; about 2 000 tokens replace 5 to 15 file reads). When you already know the file or the symbol, do not: search for it and read only the lines you need. Search before you read: a content search with a small `head_limit`, then read only the lines you need (offset and limit). Do not re-read a file you just edited or wrote; the tool already confirmed it. Never read `bin/`, `obj/`, `node_modules`, generated manifests or snapshots to "understand" them; read the code that makes them.

**Batch.** Independent tool calls go in one message. A mechanical change over many files is one small script in the scratchpad (with a round-trip or a count that proves it), not fifty edits. A fix and its test are one commit.

**Check once, in the right place.** Run `python tools/test-gates/gates.py` after your change and execute what it prints; do not run the whole suite to see. The very large suites are the end-of-sprint QA's line on the task (chapter 07 section 2b). Run long commands in the background only when they are on the budget table, never to "see".

**Keep Jira cheap and complete.** One write per issue per step (section 1). Read an issue once at the start of a block and keep what you need in the plan comment. The Jira comment, not the chat, is the memory between sessions.

**Write less.** Answers say what changed, what was verified and what was not; they do not repeat tool output, paste diffs or narrate options not taken. Link files as `[name](path:line)`. A report is the PRs, the test counts and the open lines.

**One task, one context.** A new unrelated task starts a new session (or a fork) rather than growing this one; a go-ahead opens a work block ([chapter 00](00-task-lifecycle.md) section 0) so the audit is done once. Compact at a natural boundary (after a merge), not in the middle of a change.

**Subagents only for breadth.** A broad search across many files, or an independent review, may go to a subagent; a fix that needs this conversation's context stays here. Do not start sessions or subagents for what two tool calls answer.

**Match effort to the work.** Mechanical conversions, test runs and Jira hand-overs do not need the deepest reasoning; design decisions, migrations and security do. Say so in the plan when you choose a lower effort.

## 3. The project skills (`.claude/skills/pems-*`)

Skills are committed, so a `git pull` of `main` gives every agent the same ones. Each is one page: a one-line description (that line is the only cost until the skill is used) and the procedure.

| Skill | Use it when |
|---|---|
| `pems-work-block` | any go-ahead: pull main, audit the backlog, batch, state the plan (chapter 00 section 0) |
| `pems-test-gates` | before a merge: pick the tests of your change, the budget, the QA lines for what you skipped |
| `pems-jira-handover` | starting an issue, moving it to In Review, writing the QA checklist, cancelling |
| `pems-list-page` | converting or adding a record list view to the `pems-list-*` components |
| `pems-errors` | throwing an error, calling the server from a script, showing a failure to the user |
| `pems-localization` | any user-visible text: English and Persian resource keys, component texts, the key coverage test |
| `pems-plan-sync` | reading, changing or adding a plan row (`plan/rows`), keeping Jira and the plan in step |
| `pems-shared-modal` | a list or page that opens the same modal form for many rows (`data-fill-*`) |
| `pems-migration-guard` | before touching the schema, a query, a transaction or a database service: migration rules, drift gate, when to stop without SQL Server |

Rules for skills (also for the agent that adds one): (1) a procedure that a guideline chapter already states is **linked, not copied**, so the chapter stays the single source; (2) the description is one line and says when to use it; (3) a skill that is not used for a month, or that does not serve PEMS (a generic design generator, a different CSS framework), is removed, not hidden; (4) a lesson that is a procedure becomes or updates a skill in the same pull request as the learnings-log entry (chapter 06).

Third-party skills kept: `graphify` (knowledge graph queries, the project instruction files point to it) and `ui-ux-pro-max` (design ideas; PEMS pages are still built only from `pems-*` components, chapter 02). Removed on 2026-11-04 as not serving PEMS: Tailwind/shadcn styling, brand, banner, slide and design-system generators.

## 4. Tools and add-ons for a developer machine

Nothing here ships with the product (the offline rule is about the product); record a licence in [chapter 03](03-libraries-tools-and-research.md) before relying on a tool in a script that others run.

| Tool | Why | Status |
|---|---|---|
| .NET 10 SDK, Git, `gh`, Python 3, Node | build, test, PRs, plan scripts, UiAudit | required (chapter 05) |
| `ripgrep` (`rg`) | the fastest search; agents' search tool uses it when present | recommended |
| `jq` | trim JSON (Jira exports, OpenAPI snapshot) before reading it | recommended |
| `graphify` | a queryable graph of the code for orientation questions (where does X live, how do A and B relate); a query costs 1 600 to 3 400 tokens, so it pays only when it replaces several file reads. **No hook calls it**: the per-call hook text cost more than it saved (measured 2026-11-04, section 1) and was removed from `.claude/settings.json`. Install: `pip install graphifyy` (on Windows, if pip cannot write `graphify.exe`, put a `graphify` launcher that runs `python -m graphify "$@"` in a folder on your PATH). Refresh after big changes with `python tools/graphify/update.py` in the background (minutes; it runs `graphify update .` and then prunes the isolated stub nodes, so never run the bare command for a commit; `.graphifyignore` keeps plans, docs and data out) | optional, recommended |
| Claude Code commands | `/context` shows what fills the window, `/cost` the spend, `/compact` after a merge, `/clear` between unrelated tasks | built in |
| `ccusage` (npm, reads the local session logs) | per-day and per-session token and cost report, to see which habit costs most | optional, check the licence first |
| Browser DevTools, Playwright (tools/UiAudit) | the QA lines that need a browser (chapter 07) | as needed |

Do not add an MCP server "just in case": each one adds its tool schemas to every session. The Atlassian connector is the one the workflow needs; use it with the field rules of section 1.

## 5. What the instruction files hold

`AGENTS.md` holds the mandatory order and the five rules, and points here. `CLAUDE.md` (root and `.claude/`) holds the read-first pointer and the graphify rule, nothing else. A rule that is only needed for one kind of task belongs in a chapter or a skill, not in a file every session loads. Before adding a line to an instruction file ask: would every session need it? If not, it goes in a skill.
