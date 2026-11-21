# Plan files, Jira and developers: one consistent workflow

Status: **decided 2026-10-23** (owner chose "field ownership + scripted sync"). Backlog rows #335-#337.

## 1. Principle

Two systems, one record per task. The backlog row number is the permanent **Plan ID**; the Jira issue key is stored in the plan
and the Plan ID is stored in Jira. Each field has exactly **one owner**; the other system only displays it. Anything that can drift is
checked by a script in CI, not by memory.

## 2. Field ownership

| Concept | Plan row field | Jira field | Owner | Direction |
|---|---|---|---|---|
| Identity | # (Plan ID) | label `plan-<n>` and the `[#n]` summary prefix (no custom field: the team-managed project has none, owner decision 2026-11-09, SCRUM-557) | Plan | plan -> Jira |
| Jira key | - (the plan stays tracker-free) | issue key | Jira | kept in Jira and in `plan/tracker-map.csv` written by `push` |
| Title | Task | Summary | Plan | -> Jira |
| What and why | Evidence, Prompt | Description | Plan | -> Jira |
| Acceptance criteria | Requirement | Description section "Acceptance" | Plan | -> Jira |
| Area | Area | Component (company-managed projects) or label `area-<name>` (team-managed, e.g. SCRUM) | Plan | -> Jira |
| Type | Track | Issue type (Feature->Story, Bug Fix->Bug, other->Task) | Plan | -> Jira |
| Priority | Priority P0-P3 | Priority Highest/High/Medium/Low | Plan | -> Jira |
| Milestone | Milestone M0-M6 | Fix version / Epic | Plan | -> Jira |
| Dependencies | Depends | Issue links "is blocked by" | Plan | -> Jira |
| Effort estimate | Effort S/M/L/XL | label `effort-M` (estimate), actuals stay in Jira | Plan | -> Jira |
| Doer | Doer | label `doer-user` / `doer-claude` | Plan | -> Jira |
| Status | Status | Status | **Jira** | Jira -> plan |
| Assignee, sprint, time spent, comments | - | same | **Jira** | not copied |
| Done evidence | Evidence/Report (written at close) | comment + development panel | Developer | plan row (in the closing PR) |
| PM confirmation | Confirmed by PM | label `pm-confirmed` | Plan | -> Jira |

Status mapping: In Review (conditional done: work verified, PR merged into `main`, waiting for the tests and the QA check in the UI, which are one step for now) = plan In Progress; Not Started = To Do; In Progress and Partial = In Progress (Partial adds label `partial`); Done = Done;
Hold = On Hold; Later = Backlog (label `later`); Cancelled and Obsolete = Won't Do. A row becomes Done in the plan only through the
pull from Jira **and** a closing PR that fills Evidence (rule 13: no Done without evidence).

## 3. Developer workflow

1. Pick the Jira issue (it carries the Plan ID). Transition it to In Progress (this is the "set the row In Progress" step of rule 7).
2. Branch: `feature/SCRUM-123-short-name` (key first; through the shared `atapems-tech` account add the person: `-hamid` / `-saber`).
3. Commits start with the key: `SCRUM-123: short summary (hamid)` (Jira links them; `#comment`/`#time` smart commands are optional;
   avoid smart commands in squash-merge titles, which re-run them).
4. Pull request title: `SCRUM-123: ...` (a batch PR lists every key); description lists the Plan ID and ticks the checklist: plan row updated (Evidence/Report),
   Supportability Matrix row, CHANGELOG, tests, migration/seed if the schema changed (rule 22).
5. PR merged into `main`: Jira moves to In Review (conditional done, label `qa-pending`). QA check in the UI failed: Jira moves back to To Do (label `qa-failed`; the plan shows Not Started, the label tells why). QA check passed (`docs/guidelines/07-qa-check-in-the-ui.md`): Jira moves to Done; the nightly sync writes the status back; a small PR updates Evidence and the Test Plan status.
6. Changing scope, priority or milestone happens **in the plan** by pull request; the sync pushes it to Jira. Never edit those
   fields in Jira (the sync would overwrite them and report the conflict).

## 4. Automation (`tools/plan-sync`, backlog #336)

| Command | What it does |
|---|---|
| `export` | writes `plan/jira-import.csv` (Jira CSV import format: Summary, Description, Priority, Issue type, Component, Fix version, labels, Plan ID, Work item key for updates; parents before children; max ~1,500 rows per file) |
| `push [--apply]` | updates the issues matched by Plan ID (or `plan/tracker-map.csv`): summary, priority and the plan-managed labels (`plan-N`, `effort-*`, `doer-*`, `area-*`, `partial`, ...); foreign labels such as `qa-pending` and the status are never touched. A dry run unless `--apply` |
| `reconcile` | reads every issue and lists: status differences, rows without issue, issues with a Plan ID but no row, issues without a Plan ID (no `plan-N` label, no `[#N]` title prefix), duplicate titles |
| `pull [--dry-run]` | writes the tracker status into the plan rows (Jira owns status: To Do=Not Started, In Progress, In Review=Partial, Done, On Hold=Hold, Backlog=Later, Won't Do=Cancelled); a plan status that already agrees (Partial for In Progress, Obsolete for Won't Do) is left alone |
| `tracker-check` | exit 1 on: an issue without Plan ID, a duplicate title (a duplicate closed as Won't Do is resolved), an issue whose Plan ID has no row, a row without issue |
| `check` (offline, CI) | fails when: a Plan ID or task title is duplicated; a Done row has no Evidence; unknown dependencies/status/priority; dependency cycles |

**Commit-key check (live, SCRUM-349).** `.github/workflows/commit-key-check.yml` runs on every pull request (opened, edited, reopened, new commits; read-only token, no secrets, no deploy) and calls `tools/commit-key-check/check_commit_keys.py`. It fails when the Jira key `SCRUM-<n>` is missing from the PR title, from the head branch name, or from the subject of any commit of the PR. Exempt: merge commits, commits by bots and PRs opened by bots (`dependabot[bot]`), and PRs whose head branch is `main`, `staging` or `demo`. A key found in commits but missing from the title and branch (batch PR) is a warning, not a failure. To fix a failure: rename the PR title, rename the branch, or reword the commits (rebase/squash) and force-push your own branch. Offline test: `python3 -m unittest discover -s tools/commit-key-check`; dry run: `python3 tools/commit-key-check/check_commit_keys.py --title "SCRUM-1: x" --branch feature/SCRUM-1-x --base origin/main --head HEAD`. The check does not call Jira, so it does not verify that the issue exists; that stays in `plan_sync.py check` once the Jira credentials are set.

`reconcile`, `pull`, `tracker-check` and `push` read the issues from the Jira REST API or offline from a Jira CSV export (`--csv file`) or a JSON list (`--json file`).
Credentials are **CI secrets / environment variables, never app settings or files in git, never printed**: `JIRA_BASE_URL`, `JIRA_PROJECT_KEY`,
`JIRA_USER_EMAIL`, `JIRA_API_TOKEN`. The workflow `.github/workflows/plan-sync.yml` runs nightly (22:30 UTC) and by hand: reconcile report in the job summary,
`pull` with a pull request when rows changed, then `tracker-check`; without the four repository secrets it does nothing. The tests use a fake HTTP transport (no live call).

## 5. Keeping the plan trustworthy

- The plan changes only by pull request. A row is one file (`plan/rows/NNNN.md`), so two people editing different rows never conflict and a
  reviewer sees exactly the lines that changed; `python tools/plan-sync/plan_sync.py check` must pass (it also runs in CI).
- A weekly plan review reads the `check` report; `plan_sync.py summary` is the reporting view, Jira boards the execution view.
- Rows are never reused or renumbered (permanent Plan ID); cancelled work stays as Cancelled/Obsolete.
- Edits can be made in the file or with `plan_sync.py set <id> key=value`, `report <id> "text"` and `new <id> "title"`; both end in the same file.

## 6. One-time Jira setup (owner task, #337)

Site and project (2026-10-24): `https://atapems.atlassian.net`, project key **SCRUM** (a team-managed Scrum space). Team-managed spaces have
no Components, so the plan area is exported as the label `area-<name>`; custom fields, statuses and releases (fix versions) are available.
Secrets already set in GitHub: `JIRA_BASE_URL`, `JIRA_PROJECT_KEY`; still to add by the owner: `JIRA_USER_EMAIL`, `JIRA_API_TOKEN`.


Project key; issue types Story/Task/Bug; workflow statuses To Do, In Progress, On Hold, Done, Won't Do; custom field "Plan ID" (text,
unique); components = the 27 plan areas; fix versions = M0-M6; GitHub-for-Jira connection so commits and PRs show on issues; an API token
for a bot user stored as the CI secrets above.

## 7. Sprints, owners and the generated import file

The plan is tracker-free: it carries only planning facts. Two planning columns exist in the row front matter: **Sprint** (`Sprint 1`,
`Sprint 1 stretch`, empty = backlog) and **Owner** (the accountable person, `Member A` / `Member B` until real names are mapped). The `Sprint 1`
page (`plan/sheets/sprint-1.md`) holds the goal, dates, capacity and the committed list.

`python tools/plan-sync/plan_sync.py export` turns the plan rows into the Jira import file (`plan/jira-import.csv`, not committed):
- every row becomes a work item whose summary is `[#<Plan ID>] <task>` and whose description has fixed sections: identity line (area, group,
  milestone, priority, effort, owner), *Why*, *What to do*, *Done when*, *Depends on* (with the titles of the blocking tasks) and *Report*;
- milestones M0-M6 become epics and parents; rows with Sprint `Sprint 1` get the Sprint field, `Sprint 1 stretch` rows stay in the backlog with
  the label `sprint-1-stretch`;
- Assignee is filled from `plan/members.json` (copy `plan/members.example.json`; the real file with e-mail addresses is git-ignored);
- the area becomes the label `area-<name>` (team-managed spaces have no Components); effort, doer, PM confirmation and sales-blocker become labels.

Import: Jira -> project SCRUM -> Space settings (or the "..." menu) -> Import work items -> CSV; map Summary, Description, Priority, Status,
Fix Version, Plan ID, Sprint, Assignee and the repeated Labels columns; keep "Work item ID / Parent" so tasks attach to their epic.

