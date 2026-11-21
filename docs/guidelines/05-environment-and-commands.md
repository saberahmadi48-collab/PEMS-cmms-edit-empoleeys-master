# 05 - Environment and commands

## Prerequisites
.NET 10 SDK, SQL Server (LocalDB, Express or container), Node only for tools in `tools/UiAudit`, Python 3 (standard library only) for plan scripts, `gh` CLI, graphify.

## Run
```bash
cp env/local.secrets.env.example env/local.secrets.env   # connection string + admin password
dotnet run --project PEMS.Web                            # http://localhost:5089
```
First start on an empty database creates it, installs the schema and runs seeders (a few minutes). `PEMS_ENV` selects `local|docker|demo|uat|production-<customer>`; real environment variables override files.

## Build / test / migrate
```bash
dotnet build PEMS.slnx -c Release
dotnet test PEMS.slnx                       # many tests need SQL Server; DB PEMSDB_RegressionTest; PEMS_TEST_CONNECTION overrides
dotnet test PEMS.Tests --filter "FullyQualifiedName~EnvironmentFilesTests|FullyQualifiedName~TemporaryPasswordGeneratorTests"   # database-free: env files, compose ports, one-time passwords
dotnet ef migrations add <Name> --project PEMS.Migrations --startup-project PEMS.Web
dotnet ef migrations has-pending-model-changes --project PEMS.Migrations --startup-project PEMS.Web   # drift gate
```

## Verification pointers (no copies here)
Local tests and the known failures: [../../README.md](../../README.md) "Local tests". Manual UI checks: README.md "Manual checks in the UI". Settings checks: [../../env/README.md](../../env/README.md) "Verifying settings". Deployed environment: [../deployment/DEPLOYMENT_README.md](../deployment/DEPLOYMENT_README.md) section 8. QA procedure: [07](07-qa-check-in-the-ui.md).
## Licensing settings and the vendor tool
`Licensing__*` keys are in every `env/*.env` (see LICENSING_TECHNICAL_DESIGN.md section 14.1); `Licensing__Enforce` stays `false` until rollout. The vendor tool runs on an owner computer only: `cp tools/PemsVendorTool/vendor.env.example vendor.env`, then `VENDOR_ENV_FILE=./vendor.env dotnet run --project tools/PemsVendorTool` (loopback only, http://127.0.0.1:5290). Its tests run with the normal `dotnet test PEMS.slnx` (`PEMS.Tests/Licensing`, no SQL Server needed).

## Cloud sessions
A fresh cloud container has no .NET SDK: `apt-get update && apt-get install -y dotnet-sdk-10.0` works through the proxy (the `dot.net` install script host is blocked). `dotnet ef` needs `dotnet tool install -g dotnet-ef --version 10.0.10` and a Debug build of `PEMS.Web` first; `ConnectionStrings__PEMSConnection` can be a dummy value because `migrations add` does not connect. A full build takes about 4 minutes.
Verified 2026-10-27 in a cloud container: `apt-get update && apt-get install -y dotnet-sdk-10.0` (about 2 minutes), `dotnet build PEMS.slnx -c Release` (0 errors), the database-free tests (about 20 seconds), `dotnet tool install dotnet-ef --tool-path <scratch folder> --version 10.0.*` (use `--tool-path` so nothing global is touched), and the drift gate below. **So a cloud session can run everything except the tests that need SQL Server**; only those are listed as "not run" in the report.

### Local gates (the same as CI; run them before the merge)
```bash
dotnet build PEMS.slnx -c Release                                  # 0 errors
dotnet test PEMS.Tests -c Release --no-build --filter "FullyQualifiedName~EnvironmentFilesTests|FullyQualifiedName~TemporaryPasswordGeneratorTests"   # database-free
export ConnectionStrings__PEMSConnection='Server=localhost;Database=PEMS_ModelCheck;Trusted_Connection=True;TrustServerCertificate=True'   # dummy, the gate does not connect
dotnet ef migrations has-pending-model-changes --project PEMS.Migrations --startup-project PEMS.Web --configuration Release --no-build   # must print: No changes have been made to the model since the last migration.
```
`dotnet test` without `--no-build` rebuilds and disturbs a test run that is already in progress: build once, then test and run `ef` with `--no-build`.

### Test recipes (SCRUM-503, 2026-11-03)

Quick gates a session runs (details of what goes to the checklist: [chapter 07 section 2b](07-qa-check-in-the-ui.md)):

```bash
dotnet build PEMS.Tests                                 # compiles every Razor view AND the tests (PEMS.Tests references PEMS.Web); `--no-build` test runs use this build, so build the test project first
dotnet test PEMS.UI.Tests                               # components, tag helpers, golden manifests, script contracts, ratchet, guideline registry
# the render tests of the views you touched: exact class names, see "Test budget" below
dotnet test PEMS.Tests --filter "FullyQualifiedName~CmmsListPagesTests"
```

### Test budget (owner decision 2026-11-04)

**No build and no test run without the owner's input (owner decision 2026-11-19).** While a batch is worked, do not run `dotnet build` or `dotnet test` (builds are slow and load the machine); write code, definitions and tests, merge, and ask the owner before the one build and test run at the end of the batch. Work on other issues of the batch until then.

**Heavy tests only at the end of a batch or of the sprint** (owner decision 2026-11-19, SCRUM-632; there is no time for them inside a batch): while a batch is worked, each issue or group of issues runs only the **light tests**: the build and the exact classes of what it touched (a filter of a few classes, under two minutes; `PEMS.UI.Tests` when components changed). The **heavy families** (the whole `PEMS.Tests.Web` family, the whole `Definitions`, `Authorization`, `Security` and `Plan` namespaces together, `dotnet test PEMS.slnx`) run **once at the end of the batch, or at the end-of-sprint QA** when the batch has no time for them, on the branch that holds the whole batch, with a time limit that fits (run them in the background, a combined run that hits its limit proves nothing: split it). Every issue of the batch says on its QA checklist which heavy families ran at the end and their result, or that they did not run and why. A failure at the end is fixed on the batch branch before the last merge. **The owner decides before the large run** (owner decision 2026-11-19): when every issue of the batch is In Review, the agent stops, reports the batch (merged pull requests, light tests run, the heavy families it proposes and their cost) and **asks the owner** whether to run them now, later at the sprint QA, or in part; it starts the heavy run only after the answer.

**Builds follow the same rule** (owner decision 2026-11-19): while a batch is being built no `dotnet build` of the test projects and no full-solution build is run; at most the one compile of the changed web project when it is really needed. The build of the test projects and every test run happen **after the end of the batch, and only with the owner's input** (the agent asks which batches go together and when the machine is free, then runs once). **Hand over with the minimum tests, merge the repeated ones at the end** (owner decision 2026-11-19, SCRUM-632): an issue of a batch is handed over (merged, In Review) with only the tests it really needs: the build and the one or two classes that prove its own new rule (a new rule, a refusal, the data it must not change). Every other check (neighbour classes, the sidebar mirror, the heavy families) is **a line of the Jira QA checklist**, unchecked, with the command to run it. Tests that are nearly the same across the issues of a batch (the same grid, create, edit, toggle and route checks of several pages) are written once as they are needed and **merged at the end of the batch** into one parameterised class per kind (one theory over the page keys instead of one class per page), in a last pull request of the batch; never add a new near-copy class per page when one exists.

**Build once, at the end of the batch** (owner decision 2026-11-19): a full build of the solution costs 10 to 20 minutes on a busy machine, so a batch builds once, when its last issue is written, together with the heavy tests (tier 2). A slice is written carefully, read through once for compile errors (usings, signatures, every call site of a changed method, the tests that construct a changed type), and merged without a build; when the change lives in a small project (for example `PEMS.Application`) a `dotnet build` of that project alone (about a minute) is the allowed compile check. The build at the end of the batch is mandatory: a break it finds is fixed at once in a commit named `SCRUM-<key>: fix build of the batch (hamid)`, before any further work, because other sessions pull `main`. The issues list the tests that could not run for lack of a build as `not done - end-of-batch build and run` QA lines. **Light tests per slice, heavy tests at the end of the batch** (owner decision 2026-11-19): after a slice run `python tools/test-gates/gates.py --slice --run` (light: the classes that mention the changed files and the guard classes, no whole namespace, no API suite); run plain `python tools/test-gates/gates.py --run` once when the last issue of the batch is done (heavy: the definition and validation namespaces without SQL, the API suite, the UI tests, the guards of everything the batch changed). The huge issues of a batch are done last (chapter 00 section 0.3). Three levels of testing, named by the existing tiers (chapter 00 section 0.0): **tier 1, light** after each slice (the exact classes and guard classes, `gates.py --slice --run`); **tier 2, heavy** once at the end of the batch (whole definition and validation namespaces without SQL, the API suite, the UI tests: `gates.py --run`); **tier 3, very large** only at the end of the sprint (owner decision 2026-11-19): the whole `PEMS.Tests.Web` namespace, `dotnet test PEMS.slnx` and the SQL Server families. A batch never runs tier 3; the issues list it as `not done - end-of-sprint QA` lines with the exact command (chapter 07 section 2b). **When time is short, only tier 1** (owner decision 2026-11-19): a heavy or very large run that was started by mistake is stopped (`TaskStop`), not waited for; the slice goes on with its light tests, and what was deferred is written as `not done - end-of-batch run` or `not done - end-of-sprint QA` lines with the exact command on the issue. **Hand over with the minimum tests that are really needed** (owner decision 2026-11-19). An issue of a batch leaves for In Review with only the tests that guard what it adds: one test per rule or branch that could silently break (a permission, a refusal, a data-shaping rule, a validator), not one per line or per property. Every other check (a second spelling of the same case, rendering in both languages, the broad suites, the browser) is **not written now**: it is an unchecked line of the issue's QA checklist, named so a tester can run it. **At the end of the batch, merge nearly repeated tests**: tests that arrange the same world and differ in one input become one `[Theory]` with `[InlineData]`, shared set-up moves to one fixture or helper, a test that another test already implies is deleted. The merge is part of closing the batch (one commit "tests merged"), done before the heavy run, never per slice.

**Batch rhythm** (owner decision 2026-11-19, SCRUM-592): in a batch the cost is the build and the start-up of each `dotnet test`, not the tests. Write the whole slice first, then run `python tools/test-gates/gates.py --slice --run`: one quiet build, one `dotnet test --no-build` that holds the API, guard and chosen classes together (each invocation costs 8 to 16 s of start-up), and only what changed since the last green run (`--run` records it in the git directory of the worktree). Never edit a compiled file while a build or test run is going (the build nodes crash and the run is lost): edit documents and plan files meanwhile, or wait. When several sessions build at once (the machine then has 20 or more dotnet processes) MSBuild nodes die (`MSB4166 Child node exited prematurely`) and the build is lost after 10 minutes: build with `dotnet build PEMS.Tests --nologo -v q -m:2 -nodeReuse:false` (about 12 minutes, but it finishes), run a build and its tests as ONE background command, and do not start a second build in the same worktree. The first build of a new worktree is cold (about 10 minutes); keep one worktree per batch instead of making a new one per issue. The definition engines (`PEMS.Tests.Definitions`) and the validator (`PEMS.Tests.Validation`) are in memory and have a guard of their own (about a minute), without the classes that need SQL Server.

**No full build or heavy test without the owner's input** (owner decision 2026-11-19): when a batch is written, the agent stops, reports the status and asks; the end-of-batch build, tier 2 and tier 3 run only after the owner says so (the owner may run one pass for several batches together). Until then only a small project alone (`PEMS.Application`, `PEMS.Persistence`, `PEMS.Migrations`) may be built, and only when the change lives in it; every build or test not run is an unchecked QA line. 

**Compile and test pitfalls of a batch written without building** (owner decision 2026-11-19, SCRUM-591: learn from past errors; every one below cost a slice or a build). Read each changed file against this list before the merge, because the build comes only at the end of the batch: (1) **usings and namespaces**: a new file uses every type from a namespace it names (`PEMS.Persistence.Services` is spelled `Services`, check the folder `Servises` against the namespace line); (2) **required members**: a record, definition or DTO built in code or in a test sets every required member (a `FormDefinition` needs its submit label, a select needs its options); read the type before constructing it; (3) **changed signature = all call sites**: search the solution (code and tests) for every caller of a method, constructor or record whose parameters you changed, and give new parameters defaults; (4) **tuples and types in assertions**: `Assert.Equal` of tuples needs the same element types on both sides (`(string?)null`, `int` against `long`); a `with` expression works on records, not on classes; (5) **enums and shared types**: reuse an existing enum or constant set instead of a second one (a duplicated `UploadProfile` broke the build); (6) **line endings**: files in this repo are CRLF; patch scripts read and write with `newline=""` and convert, written as files with the Write tool (an inline heredoc breaks quotes and `$`); (7) **stale output**: after an edit never run tests with `--no-build` against an older build, and never edit a compiled file while a build or test runs; (8) **tests that depend on the machine**: no timing, load or SQL Server dependent assertions in a light test (the API baseline test failed under load); (9) **additive conflicts**: files every slice touches (`Program.cs`, `DefinitionKinds.cs`, the guideline, `CHANGELOG.md`) are merged from `main` before the push and the merge result is read; (10) **stray files**: `git status` before a commit, stage only your paths, delete dump or stackdump files. The first thing the end-of-batch build does is fix what this list would have caught, in its own commit `SCRUM-<key>: fix build of the batch`.

A session runs **every test that fits** (the build, the UI tests, the guard classes and the exact classes of what it touched, each about two minutes at most) and leaves the **very large suites to the end-of-sprint QA**, where the tester runs them once for all the tasks In Review ([chapter 07](07-qa-check-in-the-ui.md) section 2b; each task still lists them as its own QA line, nothing is combined between tasks). `python tools/test-gates/gates.py` prints the commands for your changed files. Measured on 2026-11-04 (build done, `--no-build`):

| Command | Tests | Time | Use |
|---|---|---|---|
| `dotnet build PEMS.Tests` | - | 1 to 2 min | always (builds the web project too; never run `--no-build` against a build older than your last edit of a test) |
| `dotnet test PEMS.UI.Tests` | 313 | 5 to 15 s | always |
| `PEMS.Tests` filter `~CmmsListPagesTests` (list pages) | 35+ | 8 s | when a list page changed |
| filter `~ListPageComponentTests` | 10+ | 1 s | when `pems-list-*` changed |
| filter `~ThemeContrastTests\|~ThemeSystemTests\|~PremiumGlassSystemTests` | 63 | 1 s | when a stylesheet, token or theme changed |
| filter `~ShellAccessibilityTests\|~LocalizationKeyCoverageTests` | 24+ | 1 s | when a layout, a view text or a resource key changed (**always when you add a `Localizer["..."]` key: it needs a Persian entry**) |
| filter `~TraceIdTests\|~ErrorMapperTests` | 35+ | 1 s | error handling |
| filter `~PEMS.Tests.Api` | 87 | 15 s | when `/api` or `Program.cs` pipeline changed |
| filter `~PEMS.Tests.Web` (the whole namespace) | 640+ | 7 to 11 min | **end-of-sprint QA**: a line of the task's checklist, not run per task |
| a word filter such as `~Letter`, `~Migration`, `~Numbering` | many | minutes | **never**: it matches SQL families (each case waits 15 s for a connection) |
| `dotnet test PEMS.Tests`, `dotnet test PEMS.slnx` | all | stalls without SQL Server | **end-of-sprint QA** (tester with SQL Server, CI): a line of the task's checklist, not run per task |

Each `dotnet test` call also costs 8 to 16 seconds of start-up: put all the classes you need in **one** filter. Measured slow classes (do not pick them, list them as QA lines): `MrRegisteredPartTests` (18 s) (`DynamicSidebarTests` was over 100 s until 2026-11-06: it seeded a database per test; it now copies one seeded baseline and runs in 32 s, SCRUM-393); the script knows them (`SLOW_MEASURED`), add a class there when it measures over 10 s. Rules: (1) filter by exact class names; to find the classes of a view, `grep -l "Views/<Area>/<View>.cshtml" PEMS.Tests -r`; (2) a view, layout, resource or stylesheet change runs the matching guard classes above (they read the files and catch a missing Persian key, a hex colour, a broken theme block in a second) instead of the whole namespace; (3) run a long command in the background only when it is on the table above as acceptable, never "to see"; (4) what you did not run is a QA line ([chapter 07](07-qa-check-in-the-ui.md) section 1b, line d2).

- **Recording flags:** `PEMS_RATCHET_RECORD=1 dotnet test PEMS.UI.Tests --filter AdoptionRatchet` rewrites `tools/UiAudit/adoption-baseline.json`; `PEMS_GOLDEN_RECORD=1 dotnet test PEMS.UI.Tests` rewrites every manifest snapshot: commit only the manifests that really changed and `git checkout` the line-ending noise. After merging `main` into a branch, re-record the ratchet baseline (the file conflicts or goes stale whenever two branches lower different lines); keep both sides of CHANGELOG and registry conflicts.
- **Known red on `main` (2026-11-03):** `ShellAccessibilityTests.ProgramConfiguration_ReExecutesEmptyErrorResponsesToTheStatusPage` (reads `Program.cs` text); every test of the SQL families listed in chapter 07 section 2b fails without SQL Server.
- **Page tests must not depend on attribute order** (the library puts `class` first and re-adds bound attributes after it): match attributes with `[^>]*` or a lookahead, not by position.
- **The full `PEMS.Tests` run** is for a machine with SQL Server; here it is stopped when it stalls and its results are not claimed.


## Branch audit (run at the start of a session and once a week)
```bash
git fetch --prune origin
for b in $(git for-each-ref --format='%(refname:short)' refs/remotes/origin | sed 's#origin/##' | grep -v -e '^HEAD$' -e '^main$'); do
  echo "$b | ahead $(git rev-list --count origin/main..origin/$b) behind $(git rev-list --count origin/$b..origin/main) | $(git log -1 --format='%ad %an' --date=short origin/$b)"; done
```
Rules for what it shows are in [chapter 00](00-task-lifecycle.md) section 4a.

## Docker / deploy
`docker compose --env-file env/docker.secrets.env up --build`; docs in `docs/deployment/` (CODE_FIRST_DATABASE, DEMO_DEPLOYMENT, LINUX_VM_DEPLOYMENT, PEMS_UAT_DEPLOYMENT_RUNBOOK). Pipelines in `.github/workflows`: only pushes to `demo` and `staging` start one (`deploy-demo.yml`, `deploy-staging.yml`, both calling the reusable `deploy-site.yml`); `build.yml` and `deploy-uat.yml` are manual; the only check that runs on pull requests is `commit-key-check.yml` (the Jira key `SCRUM-<n>` must be in the PR title, the branch name and every commit subject; merge and bot commits and `main`/`staging`/`demo` heads are exempt; logic and tests in `tools/commit-key-check`, no secrets, no deploy; SCRUM-349); pushes to `main` and working branches run nothing. Full setup and manual deploy/reset: `docs/deployment/DEPLOYMENT_README.md`. Do not deploy, push to protected branches or change pipeline secrets without the user's explicit go-ahead.

## Letting an agent merge its own pull request (one-time setup per person)
The owner rule ([chapter 0](00-task-lifecycle.md)) lets the author merge its own PR into `main`, but the Claude Code permission classifier can still deny the call as "Merge Without Review". An agent must not edit its own permissions, so the person who runs the agents allows it once in their user settings. Windows: `notepad %USERPROFILE%\.claude\settings.json`, add the line below to `permissions.allow` (create the block if it is missing), then start a new session:

```json
{ "permissions": { "allow": [ "Bash(gh api -X PUT repos/atapems-tech/PEMS/pulls/*/merge*)" ] } }
```

Merge by hand instead: `gh pr merge <branch-or-number> --squash`. If GitHub answers `TLS handshake timeout`, repeat the call; it is flaky, not refused.

## Windows notes
Primary shell is PowerShell on Windows 11; Git Bash is available. Quote paths with spaces (`0 - refs`, `task manager`). Line endings follow `.gitattributes`.

## Knowledge graph
`graphify query "<question>"` before raw grep for architecture questions; `graphify update .` after code changes. `.claude/settings.json` hooks call a graphify binary at a per-user path - fix the path on your machine locally, do not commit personal paths.

## Atlassian MCP
Site `atapems.atlassian.net`, cloudId `9290a713-17c1-44bd-99fc-f1b83705cc69`, project `SCRUM`. Tools: `getJiraIssue`, `searchJiraIssuesUsingJql`, `transitionJiraIssue`, `addOrEditJiraIssueComment`, `createJiraIssue`, `editJiraIssue`. Slim large responses with `responseFields`. Custom fields (Plan ID) need `view: evidence/full` or the site's `customfield_*` id.

## Slack notifications (set up 2026-10-25)
Workspace `pems` (pems-corp.slack.com).
- `#pems-jira`: Jira Cloud app, project SCRUM, all event groups (issue created/updated/deleted including description and sprint changes, comments, assignee, status, issue links, worklogs), full-text style. Manage it in Jira: space PEMS > Slack integration.
- `#pems-github`: GitHub app, repo `atapems-tech/PEMS`: issues, pulls, reviews, comments, commits on all branches, branches, releases, deployments, workflows (push, pull_request, workflow_dispatch). Change it with `/github subscribe|unsubscribe atapems-tech/PEMS <features>` in the channel; `/github subscribe list features` lists the options.
- New team members join both channels; the GitHub app links per person with `/github signin`.
