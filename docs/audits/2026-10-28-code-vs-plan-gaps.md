# Code versus plan: gap audit (2026-10-28)

Requested by Hamid (owner): check the gaps between the code and the tasks; modify, merge or add tasks only where really needed; plan only, no implementation.
Method: (1) compare every controller and entity file name and a list of module keywords with the text of the 435 plan rows; (2) compare the migration-wave rows (#85-#95) with the controllers; (3) compare the status of every Jira issue that is not To Do with the plan row; (4) match Jira issues to plan rows by Plan ID; (5) read the REST inventory (#432) for findings nobody owns; (6) check the files named by plan rows exist; (7) look at the top-level folders of the repository.

## 1. Result in one table

| # | Finding | Evidence | Action |
|---|---|---|---|
| 1 | No task reviews the 269 MVC actions without a permission signal or the 3 state-changing GET requests | `docs/audits/rest-inventory.csv` (permission = none: 269 in 48 controllers; Culture/SetCulture, LetterCartable/Settings, Theme/SetTheme change state on GET) | **New #461** |
| 2 | Stray top-level folders not in the solution and not in the README: `WebApplication1` (79 files), `frontend/dashboards` (152), `src/stitch-temp` (329, 16 MB), `Permission`, `dev/null`, `PEMS_PROJECT_INTELLIGENCE_REPORT.md` | `PEMS.slnx`, README layout, `git ls-files` | **New #462** |
| 3 | About 25 modules in the code are named by no migration wave | section 3 | **Merged into #85** (machine-readable classification + coverage check) and provisional lists added to #88, #89, #90, #91, #93, #58 |
| 4 | Jira issues without a plan row: SCRUM-407, 408, 409, 428, 429, 445 (and SCRUM-353 to 357, whose numbers collide with other plan numbers) | Jira versus `plan/rows` | Rows #407, #408, #409, #428, #429, #445 created as mirrors; 353-357 recorded in #240 |
| 5 | Duplicate bug: SCRUM-445 (started by another session) and SCRUM-447 (created in this session) are the same Dockerfile bug | Jira | #447 cancelled as a duplicate, #445 is the plan row of the started issue |
| 6 | Plan row statuses lag Jira (about 70 of the first 100 issues that are not To Do differ; Jira owns status) | Jira versus `plan/rows` | **Merged into #336**: `plan_sync reconcile` / `pull`; no one-off edit by hand |
| 7 | Stale evidence: #311 names a script that moved to `legacy-trash`; the Existing-capabilities rows (#239-#256) do not list modules built since | section 6 | #311 and #250 evidence updated |
| 8 | #336 still describes a workbook and `backlog.csv` | row text | Updated |

Checked and found consistent: every file path named by a Done row exists except the moved script (#311) and git-ignored secret files; there are no plan rows with a dependency cycle or a dependency on a later milestone (`plan_sync check`); the REST/definition work (#430-#460) matches the code that exists.

## 2. Plan versus Jira

Jira owns status; the plan keeps the planning facts. On 2026-10-28 many plan rows still say Not Started while the issue is Done or In Review (for example the DevOps rows #258-#275 are Done in Jira, the migration waves W1-W5 #328-#332 are Done in Jira, the validation rows #355-#357 and the formatting rows #367-#371 are Done in Jira). Editing 70 rows by hand from a chat session would copy mistakes; the planned `reconcile` and `pull` commands of #336 do it from the tracker data and make the drift visible in CI.

Issue numbers before #423 are not the plan numbers (for example SCRUM-410 is plan #380, SCRUM-421 is plan #391); the Plan ID label and the `[#n]` prefix in the summary carry the plan number. Issues without that prefix and label are the ones listed in finding 4.

## 3. Modules in the code that no migration wave names

The waves #87-#93 list the main modules; the controllers also contain the modules below. This is a **provisional** assignment so that no module is forgotten; #85 confirms or changes it and keeps the final table in `plan/migration-classification.csv`.

| Module (controllers) | Provisional wave | Note |
|---|---|---|
| GasTest, Isolation, ExecutionGate, Simops, Intake | W2 (#88) | permit-to-work family and request intake |
| JobPlan, MaintenanceCalendar, MaintenanceMasterData, CriticalityAssessment, EquipmentCriticality, MechanicalEquipment, Finding, FailureModeCatalog, ConditionReadingImport, MaintenanceHistoryImport, StrategyImport, CmmsOperations, CmmsReadiness | W3 (#89) | assets and maintenance |
| Tender (RFQ, bids, evaluation, negotiation, NTP), PurchaseOrder, IRN, PartCatalogue (request, lookup, dedup), Replenishment, MaterialItem | W4 (#90) | procurement and warehouse |
| Letter, LetterCartable, LetterCompose, LetterTemplate, DocumentHold, KnowledgeBase | W5 (#91) | letters are probably hybrid (numbering and routing stay code); decide in #85 |
| AIInsights, AISearch, ChatAssistant, EngineeringDocumentIntelligence, ProjectIntelligence | permanently static (#93) | AI family |
| ExecutiveDashboard, EngineeringDashboard, Kpi, Report | dashboards and reports (#58, #59) | the static versions the designers replace |
| Theme, Culture, Settings, Notification, MyProfile, MyWork, Help, ComingSoon, NavigationBadge | shell (W1, already migrated to PEMS.UI by #328) | stays code |

## 4. Why a new task and not a change of an old one

- **#461** is security work on existing pages. No row covers it: #20 only adds an orphan-controller test for generated menus, #435/#436 gate the new API, #432 only found the facts and recommended "a separate security task".
- **#462** is repository hygiene; #9 (secrets audit) and #29 (UI pattern audit) do not touch these folders.
- Everything else was a change to an existing row (#85, #88-#93, #58, #240, #250, #311, #336) or a plan row for an existing Jira issue, to avoid new tasks for the same work.

## 5. Not done on purpose

No code, no migration, no Jira status change except closing the duplicate SCRUM-447 and no bulk edit of plan statuses. The owner decides the provisional wave table and the fate of the stray folders when #85 and #462 are taken.
