# Build status (shared by all sessions)

**BUILD AND COMBINED TEST RUN DONE 2026-11-20 (owner input in chat: "run build and end of batch tests for all six batches"). `dotnet build PEMS.slnx` is green (0 errors); automated tests are green except the SQL-instance tests. No hold is active.**

## What was run (on main plus PRs 434, 435, 436 and the follow-up of this note)

| Step | Result |
|---|---|
| `dotnet build PEMS.slnx` | 0 errors (first run: 4 compile errors, fixed in PR 434) |
| `PEMS.UI.Tests` | 628 of 628 |
| `PEMS.Tests` filter `Definitions` | 500 of 501; the one failure is `AuditTemporaryKeySqlTests` (needs SQL Server, none reachable) |
| `PEMS.Tests` filter `Web` | 750 of 751 expected; the one failure is `DynamicSidebarSqlTests` (needs SQL Server) |
| `gates.py --slice --run` | UI 628 of 628; PEMS.Tests slice 375 of 376, the failure again a SQL test |
| Browser checks (the unchecked "Browser" lines of each issue) | NOT run; they stay unchecked lines on the issues |
| SQL-instance families | NOT run (no SQL Server instance reachable on this machine) |

## What the run found and fixed (all merged)

- Build: missing usings in `QuickSetupOperations` and `ReportReads`; tests of deleted controllers (QuickSetup, AIExtraction, HomeController constructor).
- Operation names declared twice: `DisciplineWorkspace.Summary` (the list is now `Workspaces`) and `Help.Articles` (the admin list is now `AdminArticles`).
- The test harness did not register every action handler (`steps`, `file.upload`, `file.download`, `file.export`, `entity.import`); `Equipment` was missing from the entity catalog; `Discipline` was registered twice (the second wiped the master-data permissions).
- 31 shipped pages had no `texts` for `data-grid`: the grid now has built-in fa/en default texts (`GridTexts.Default`); 7 files used the tone `secondary` on a button, which is `neutral`.
- `kpi-tile` and `info-item` wrapped their options because of the host-set `Empty` flag; the new attribute `[PemsHostSet]` keeps it out of the options schema (options are flat again). `kpi-tile` label, sub and hint are `TranslatableText`.
- Layout `dashboard-page`: `main` is no longer required (a tile-only dashboard is valid). A corrupt spreadsheet is refused with a field error. A component-definition `text` parameter accepts a plain string.
- Search results for projects and disciplines and the sidebar entry of the navigation menu pointed at deleted controllers: now `/p/...` pages.
- Tests of deleted views, controllers and pages were removed or updated; the classification and ratchet tables were regenerated (deleted controllers marked converted, the HomeController baseline lowered).
- Manifest snapshots re-recorded: `matrix`, `kpi-tile`, `info-item`, `field`, `form`, `form-section`, `record-view`, `data-grid`.

## Rules (owner decisions 2026-11-20)

- A build or a test run needs the owner's explicit input in chat; a go-ahead on a batch is not that.
- While a line here says BUILD IN PROGRESS, other sessions do not start `dotnet build`, `dotnet test` or any gate, and do not edit files under `PEMS.*`. Reading, Jira and plan/docs edits are fine.
- After merging new code, compile only the project you changed; the full suites run again only on the owner's input.
