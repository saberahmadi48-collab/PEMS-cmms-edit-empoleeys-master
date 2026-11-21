# Sprint 2 batches (definition-only goal, owner direction 2026-11-19)

Every batch is 10 to 30 issues done together on one branch (guideline 00 section 0: related issues travel together, a blocker travels with what it blocks). Componentization comes first (owner priority). Each batch ends with one PR, one Jira comment per issue with its QA checklist, then In Review.
Order is by dependency: a later batch only uses what an earlier one shipped.

| # | Batch | Issues | Count | Done when | Status |
|---|---|---|---|---|---|
| 1 | **Components and screen engines** | 589 (done), 600 details page, 599 list page (saved views, export, bulk), 601 dashboards, 602 import wizard, 616 page behaviours, 617 shared layout and partials, 618 result messages, 619 chart, 620 kanban, 621 calendar and timeline, 622 tree, 623 document viewer, 624 rich text and letter editor, 625 barcode and print, 626 OCR and chat, 627 FMEA matrix, 769 pinning and density, 770 virtualization, 771 view toggle, 772 drag to reschedule, 340 component catalog, 56 form component standards, 374 input shaping, 386 masked inputs, 348 designer rendering | 27 | every screen kind the 140 controllers need is a component or a component definition | In Review (tests pending: one run with 2 and 4) |
| 2 | **Data, action and security engines** | 592 read data sources (entity registry as definitions), 593 multi-table transaction, 594 result binding, 595 step-up, 596 uploads, 615 file results, 590 menu definitions, 591 permission administration, 448 shipped definitions, 515 field ownership, 516 reference-data ownership, 517 shipped workflows, 574 to 577 pipeline optimisation, 372 / 373 / 380 validation catalog, engine and error contract, 628 classification table, 611 ratchet gate | 22 | any read, write, file, step-up and menu entry is a definition | In Review (tests pending: one run with 1 and 4) |
| 3 | **Designers, governance and migrator** | 439 page designer, 604 form designer, 606 action designer, 607 data-source designer, 608 menu and permission editor, 609 preview and translation, 419 translation completeness, 449 manager screen, 450 references, 451 remaining kinds, 454 API client, 455 storage report, 457 safe publish, 459 republish precheck, 440 governance, 581 package format, 582 migrator center, 583 feed engine, 584 developer panel, 585 change manifest, 614 guideline, 612 browser regression suite, 613 browser QA of converted actions, 503 adoption QA | 25 | a designer builds and ships a page, a form, an action and a menu entry as a package the migrator installs | Not started |
| 4 | Conversion: Admin area | 629 to 651 | 23 | no compiled Admin-area screen | Closed 2026-11-20: 18 In Review (light tests passed); 12 moved to 4b |
| 4b | Admin conversions that need new generic inputs (follow-up of batch 4, owner decision 2026-11-20) | 634 Help, 635 License (file), 636 NavigationMenu (tree), 637 NavigationVisibility (matrix), 638 Numbering (VDR panel moves with batch 9), 639 Organization (tree), 640 OrganizationImport (file), 641 PermissionMatrix (matrix), 650 Users, 651 WorkflowManagement (tree), 782 matrix editor, 783 hierarchy editor | 12 | the inputs exist with tests, then the 10 conversions; batch 4 itself closed with 18 In Review (build and 77 light tests passed 2026-11-20) | Started 2026-11-20: 10 In Review (634, 635, 636, 637, 638, 639, 641, 651, 782, 783), 12 of 12 In Review after merge 2026-11-20: 650 Users and its split 816 (generic one-time secret response), 640 organization import (validate and confirm as operations, the Import Center card links the page by Url; not compiled, no test run: builds wait for the end of the day's batches) |
| 5 | Conversion: shell, organization, AI, dashboards | 666 to 669, 672 to 674, 680 to 682, 687 to 688, 697, 703 to 706, 712 to 713, 729 to 731, 733, 740, 743, 747 to 748, 757 | 28 | no compiled shell, organization, AI or dashboard screen | Not started: the owner starts it in its own session (its 28 issues are To Do) |
| 6 | Conversion: Warehouse and procurement | 652 to 665, 671, 709, 726, 735 to 736, 741, 751, 763 | 22 | no compiled warehouse or procurement screen | Not started |
| 7 | Conversion: Work, permits, maintenance, tasks, contracts, production | 675 to 677, 696, 699, 702, 707, 710 to 711, 720 to 725, 728, 734, 737 to 739, 749 to 750, 755 to 756, 758 to 759, 764 to 765 | 28 | no compiled work, contract or production screen | Not started |
| 8 | Conversion: Assets, reliability, QC | 670, 678, 691 to 695, 698, 700 to 701, 708, 727, 742, 744 to 746, 752, 754 | 18 | no compiled asset or reliability screen | Not started |
| 9 | Conversion: Documents, VDR and letters | 679, 683 to 686, 689 to 690, 714 to 719, 732, 753, 760 to 762 | 18 | no compiled document or letter screen | Not started |
| 10 | **Cutover and rehearsal** | 766 final cutover, 767 release rehearsal, 614 docs | 3 | the application serves only definitions; the rehearsal runs on demo with no build | Not started |

## The generic engines and their inputs (owner decision 2026-11-19)

One generic engine per kind; every page, form, action or menu entry is an input to it, never a new engine. A conversion that needs something new adds an **input** to the generic engine (its own issue, in the batch that needs it), never a provider, handler or controller for one page.

| Engine | Generic inputs it already takes |
|---|---|
| Page (`/p/{key}`) | layout, regions, components with options, data-source keys, row actions, route definitions |
| Form (`/f/{key}`) | fields (kind, rules, messages, optionsFrom, readOnly, static options), action key |
| Action (`/a/{key}`) | handlers `entity.create/update/delete/toggle`, `operation.invoke` (args from field, literal, context; record and options arguments; guard, sectionGuard; `load`), `workflow.execute`; rules `required`, `unique`, `validate`, `deny`, `defaults`, `redirect` |
| Data source (`/d/{key}`, options) | providers `entity`, `entity-options`, `entity-aggregate`, `operation`, `operation-options`, `components` |
| Menu and routes | menu catalog rows, `route` definitions |

Inputs still missing (each is an issue in batch 4; a conversion waits on it; SCRUM-780 result and status display, 781 action preconditions, 782 matrix editor, 783 hierarchy editor, plus 595, 596, 615 for files and step-up): result and status display of an operation (test connection, update status), action preconditions (confirm fields, entitlement), matrix editors (permission, navigation visibility), hierarchy editors (menu, organization) on the tree component, file and JSON endpoints (595, 596, 615).

## Heavy end-of-batch tests (owner decision 2026-11-20)

No heavy test and no build run until **batches 3, 4b and 5 are all In Review**; then ONE combined run (batches 1, 2, 3, 4, 4b and 5) with the owner's input, never automatically. Until then each session writes code, merges, and moves its issues to In Review without a build or test run (a build or a light class only when the owner approves it). Sessions: batch 3 and batch 5 are started by the owner in their own sessions; batch 4b runs in the session that closed batch 4.

## Gap batches (owner decision 2026-11-20, guideline 07 section 1c)

A gap found in a batch is an issue of the same sprint, listed here under the batch letter plus `b` (a second round of gaps of the same batch becomes `c`). They stay To Do in the sprint until someone claims the whole letter batch; a gap that belongs to no batch goes to the backlog instead.

| Batch | Gap issues | Of |
|---|---|---|
| 1b | 792 live preview in the catalog, 793 saved views per role on the server, 794 filter bar part, 795 related-records part | 340, 599, 600 |
| 2b | 796 platform field marks (owner decides), 797 ownership preflight report on Database Update and /health/database, 798 ADR for field ownership, 799 prove action.steps with a shipped definition, 800 generic PDF output, 801 download of a stored file by key | 515, 593, 615 |
| 3b | 784 manager history, diff, export and import screens, 785 used-by and references REST and the check in REST publish, 786 storage growth, cache statistics, retain setting and size warning, 787 fallback and live fa/en RTL/LTR preview, 788 smoke test, post-publish check and auto-rollback, 789 precheck-rollback REST and verdict on the screen, 790 browser tests of the API client, 791 manifest coverage test, 807 apply a feed plan (gap of 583), 808 two-person publish and policy tests (440), 809 page designer canvas (439), 810 form designer rules and sections (604), 811 action designer pickers (606), 812 data-source designer editors (607), 813 menu tree, grants and SoD view (608), 814 migrator center run (582), 815 vendor panel scripts, CSV and preview (584) | 449, 450, 455, 419, 609, 457, 459, 454, 585, 583, 439, 440, 604, 606, 607, 608 |

**Status 2026-11-21:** 1b, 2b and 3b are In Progress as a group while their slices merge, then In Review; the written results and what is still open are in the section "Gap batches 1b, 2b and 3b: written 2026-11-21" below. No build or test ran.

Batch 3 has no open issue of its own left; 612, 613 and 503 (browser QA) were cancelled on 2026-11-20 because there is no QA issue: their checks are lines of SCRUM-768, 437 and 342 (guideline 07 section 1c). The missing QA checklists of 372, 448, 516, 517, 574 to 577, 590 to 592, 594 to 596, 611 and 628 are not gaps: they belong to the owners of those batches (2 and 1).

## Gap batches 1b, 2b and 3b: written 2026-11-21, not built or run (owner decision 2026-11-21: no heavy test or build until the end of the batches)

Started together in one session (21 + 6 issues, all In Progress, then In Review as their slice merged). Everything below is written and merged **without a build or a test run**; the end-of-batch run decides. Merged in PRs 438, 439 and the follow-up of 2026-11-21.

| Issue | What was written | Still open (a line of the issue, not a new issue) |
|---|---|---|
| 794 filter bar, 795 related records | `entity-list` takes `filters`; `entity-details` takes `related` (component definition `related-table`) | browser check of both |
| 791 manifest test | `ChangeManifestCoverageTests`; the manifest names the migration of this batch | run in the Definitions slice |
| 796, 798 field ownership | `code` of 7 master-data forms is a platform field; ADR-0016 | owner may mark more fields |
| 799 steps proof | shipped action `warehouses-create-with-location` and its form; tests of commit and rollback | SQL Server rollback proof of two tables (SQL family) |
| 800 PDF, 801 download | `file.export` format `pdf`; `file.download` `authorizeBy` (record-level decision) | Persian font in the PDF (browser/QA) |
| 786 storage | growth per month, health grid with cache counters and size warning, `RetainRetiredVersions=forever` in every env file, fail-closed start, no-delete test | numbers move in the browser |
| 797 preflight report | Database Update page and `/health/database` list form, field, reason | none |
| 785, 788, 789 safe publish | used-by, references, precheck endpoints; 422 for blocked, 409 for warnings without `acknowledge`, smoke check, auto-rollback with loop guard; verdict per version on the history page | OpenAPI snapshot re-record; the 412 reload prompt of 784; a runtime probe per kind (the smoke check is the reference and validity check today) |
| 784 history, diff, export, import | page `definition-history`, `definition.export` and `definition.import` actions | browser check |
| 792, 787 preview | preview host `/preview/component/{key}` and `/preview/definition` (fa/en, RTL/LTR, light/dark) | form preview (a form version is not rendered by the page renderer): QA line |
| 793 saved views | tables `GridSavedViews` and `AppliedPackages` by migration `20261121010000_Scrum807PackagesAndViews`; data source `grid-views`, actions `grid-view-save` and `grid-view-delete`; the grid script shows them with a scope picker | browser check on two devices; model snapshot check of the hand-written migration |
| 790 client tests | `tools/js-tests/pems-definition-client.test.js` (stub fetch: 200, 412, 422, 401, 403, retry, autosave) and the documented events | run with `node`; real Playwright against a stub server is not needed for these cases |
| 807 feed apply, 814 migrator run, 815 vendor panel | `PackageFeedApplier` (entity catalog, permissions, validation, all-or-nothing), `Migrator.Run` with confirmation and history page, CSV rows with key fields and a preview | migration scripts do not travel in packages (code-first migrations travel with the release, decision 2026-10-22); a refeed through the browser |
| 808 two-person publish | `Definitions:ProtectedDefinitions`; `DefinitionPolicyTableTests` has a test per cell | workflow-platform second approver (not built: a second person with `Definitions.Approve` is the rule) |
| 809 page designer | edit an option by component id, move a component between regions, preview the draft, history | the drag and drop canvas and undo/redo in the browser (JS) are not written |
| 810, 811, 812, 813 designers | typed editors for a form field, layout, rules, action guards, entity grid, menu move; operation arguments picker; menu tree page; role grants page; separation-of-duties page | preview with sample data of a data source (the JSON of `/d/{key}?pageSize=5` is the preview); drag reorder of the menu tree in the browser |

To expect in the end-of-batch run (besides the usual order): (1) build: this batch adds C# in `PEMS.Web`, `PEMS.Persistence` and `PEMS.Application` that nobody compiled; (2) re-record the OpenAPI snapshot (`PEMS_GOLDEN_RECORD=1`, new endpoints under `/api/v1/definitions`) and the UI manifest snapshot (`data-grid` has the optional text `viewScopeMe`); (3) `plan/sheets/classification.json` has the new host controller `DefinitionPreviewHostController`; (4) the model-snapshot test and `MigrationCompatibilityRulesTests` read the hand-written migration `20261121010000_Scrum807PackagesAndViews` (written by script from the snapshot, not by `dotnet ef`: if the model test fails, regenerate it with `dotnet ef migrations add` and delete the hand-written files).

## Combined heavy run: RESULT (2026-11-20)

Run on the owner's input: build green; UI 628 of 628; Definitions 500 of 501; Web 750 of 751; gates slice 375 of 376; each single failure is a SQL-instance test (no SQL Server reachable). **Batches 1, 2, 3, 4b and 5: automated tests done ("Tested", browser checks and SQL families still open as unchecked lines on the issues).** Findings and fixes: `plan/sheets/build-status.md`. Still open from the run: the browser checks, the SQL-instance families, and the QA lines of the In Review issues (cite this run on line 1).

## Combined heavy run (owner decision 2026-11-20)

Batches 1, 2, 3, 4b (the Admin conversions not yet In Review) and 5 are tested **together, once**, after batches 3, 4b and 5 are all In Review. Until then no batch runs a heavy test, a solution or test-project build, `gates.py --run` or a browser run, and after that only with the owner's input (guideline 00 section 0.3). Batch 3 is worked in session B3 here; 4b and 5 are started by the owner in their own sessions. The order and the lines to run are in [batch-test-plan.md](batch-test-plan.md).

Rules for every batch
- **Keep the Status column of the table above current** (owner decision 2026-11-20): Not started, Started (all issues In Progress), In Review (all issues In Review; heavy tests pending), Tested, Closed. Update it in the same PR that starts a batch, moves its last issue to In Review, or runs its tests, with the date and the counts.
- A started batch has no issue in To Do: claim all of its issues (In Progress, assigned) when it starts and again whenever an issue is created for it (guideline 00 section 0).
- The engines of batch 1 to 3 are written once and generic; conversions (4 to 9) add only definitions, services and feed (skill `pems-definition-engineer`), never handlers or controllers.
- A conversion issue is closed only when its controller, views and inline scripts are deleted and its routes ship as `route` definitions.
- Every batch: tests per issue run through the real handler, definitions validated by the shipped kinds, plan rows updated, the Jira comment lists the browser checks.
- If a batch finds a gap, the gap becomes a separate issue before the batch ends (guideline 04 section 7), never a tail.

## Plan rows with status Partial (checked 2026-11-19)

54 plan rows have the status Partial. **None is open work**: every one belongs to a Jira issue that is Done or In Review (checked by the `plan-<n>` label, none was To Do, In Progress or On Hold). A Partial row is a finished issue whose plan row names a remainder; reopening the issue would undo delivered work, so none was moved back to In Progress and none was added to a batch.
Where the remainder is real, a batch of this sprint already covers it; where it is not, it is its own later issue:
- covered by batch 1: the component library rows 31, 35, 37, 319 to 324, 327, 333, 386, 388, 424, 425, 433, 482, 483, 487, 499, 500, 502, 506, 507, 549, 551 (the specialised components 619 to 627, the page and form engines, the adoption ratchet) and 43 (global search, shell: batch 5);
- covered by batch 2: 183 (upload pipeline: SCRUM-596), 553 and 554 (validation and error contract: SCRUM-373, 380), 16 (auto-numbering: its own later issue), 423 (sidebar: SCRUM-590);
- covered by batch 3: 262 and 309 (migrator, SCRUM-581 to 585); batch 10: 295 and 340 (demo rehearsal, SCRUM-767); batch 9: 550 (letters conversion);
- no batch of this sprint: 112 and 130 (licensing catalogue and price list), 287 (main host platform), 335, 336 and 337 (tracker and plan-sync set-up, owner tasks); 29, 30, 42, 391, 392, 408, 409, 426, 467, 476, 552 are small remainders to be raised as their own issues when their area is next worked.
The plan status of a row is set from Jira by `plan_sync.py pull`; a row that is still Partial after its issue is Done is a statement about its remainder, not about the issue.
