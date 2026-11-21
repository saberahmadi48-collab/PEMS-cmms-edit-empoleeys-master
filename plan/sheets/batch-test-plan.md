# Combined test run: batches 1, 2, 3, 4b and 5 (owner decision 2026-11-20; was 1, 2 and 4 on 2026-11-19)

**RUN DONE 2026-11-20 (owner input in chat).** Automated result: build 0 errors; UI 628 of 628; Definitions 500 of 501; Web 750 of 751; gates slice 375 of 376; the single failure in each is a SQL-instance test (no SQL Server reachable). **Not run: the browser checks and the SQL-instance families; they stay unchecked lines on the issues.** What the run found and fixed is in `plan/sheets/build-status.md`. Steps 1 to 4 of the order below were done; step 5 (SQL) and step 6 (browser) are open.

The text below is the plan as it was written before the run.


Written and merged; **not run**. One combined run covers the five batches (components and screen engines, data/action/security engines, designers/governance/migrator, the Admin-area conversions still open, shell/organization/AI/dashboards conversions). **Trigger: batches 3, 4b and 5 are all In Review; then ask the owner; nothing heavy and no build of a test project before that, and none without the owner's answer.** Run it once, when the machine is quiet, then fix and cite it on every issue's QA line 1.

Gap batches 1b, 2b and 3b (sprint-batches.md) are done after the combined run is decided; they are separate issues, so they do not hold the run.

Batch 3 adds (to expect): the definition workbench (grid providers `definitions`, `definition-kinds`, `definition-usages`; stored pages `definition-workbench`, `definition-details`, `definition-kinds`; forms and actions `definition-*`; operations model `Definition`), designer and migrator pieces as they land; its test classes are listed in the batch-3 Jira comments.

## Browser checks are lines, not issues (owner decision 2026-11-20)

The issues SCRUM-503, 612 and 613 were cancelled: their checks are lines of the proper issues (SCRUM-342 adoption pass, SCRUM-768 generic hosts and routes, SCRUM-437 converted actions) and of the checklist of every issue in the batches. Step 6 below runs those lines.

## Gap batches 1b, 2b, 3b (written 2026-11-21, not built, not run)

Run them with the combined run after the owner says so. New or changed test classes to expect (all in `PEMS.Tests` unless noted): `ChangeManifestCoverageTests`, `DefinitionStorageHealthTests`, `OwnershipPreflightReportTests`, `DefinitionPublishGuardTests`, `DefinitionPolicyTableTests`, `DefinitionHistoryTests`, `DefinitionPreviewTests`, `GridViewsTests`, `DesignerEditorsTests`, `PackageFeedApplierTests`; extended: `StepActionsTests` (shipped action), `FileActionsTests` (pdf, record-level download), `FileDownloadTests`, `ShippedKinds` (new handlers and providers); node: `node tools/js-tests/pems-definition-client.test.js`. Re-record: OpenAPI snapshot and the `data-grid` UI manifest. SQL-instance lines: the rollback proof of the two-table action, the hand-written migration against a real database, the EF transaction of the feed.
Browser lines (one per issue, on the issue): history page of a definition (versions, verdict, diff, export, import), preview of a catalog component in fa/en and RTL/LTR, saved views per role on two devices, Database Update preflight table, definition storage page (growth, health), migrator run and history, role grants and separation-of-duties pages, menu tree, a list page with a filter bar, a details page with a child table.

## Order

1. **Record the new component snapshot** (batch 1 added `record-view`, `matrix`; changed `kpi-tile`, `field`). Only the files that changed are kept:
   `cd PEMS.UI.Tests && PEMS_GOLDEN_RECORD=1 dotnet test --filter "FullyQualifiedName~The_generated_manifest_matches"`, then `git checkout -- PEMS.UI.Tests/Manifests` for every file except `matrix.json`, `kpi-tile.json`, `record-view.json`, `field.json`, review the diff of those four, commit.
2. **UI suite** (golden manifests, literal-text gate, registry and guideline drift, ratchets): `cd PEMS.UI.Tests && dotnet test`.
3. **Definition and engine suite** (kinds, pipeline, hosts, operations, entity layer, routes, components, record binding, chart, matrix, masks, catalog): `dotnet test PEMS.Tests/PEMS.Tests.csproj --filter "FullyQualifiedName~Definitions"`.
4. **Guards the batch touched** (Persian keys, theme, accessibility): `python tools/test-gates/gates.py --slice --run`.
5. **SQL-instance families** (need the SQL instance; `AuditTemporaryKeySqlTests` fails without it and is not a regression): run where SQL is available.
6. **Browser checks**: the unchecked "Browser" lines of each issue (the QA checklists in Jira, batch comments of 2026-11-19).

## What batch 1 added to the suite (to expect in the run)

| Area | Tests |
|---|---|
| Component definitions | `ComponentDefinitionTests` (validation, expansion, each/if, templates, inline, shipped entity-list) |
| Details, chart, matrix, dashboard, views | `RecordViewTests` (record binding, map, chart, matrix, entity-dashboard, entity-views, request values, one theory for "may not read") |
| Routes | `RouteTableTests` |
| JSON actions | `ActionJsonValuesTests` |
| Catalog | `ComponentCatalogProviderTests` |
| Masks | `FieldMaskTests` |
| UI components | manifest snapshots, literal-text gate, registry drift (`record-view`, `matrix`, `kpi-tile.value`, `field.mask`) |

## What batch 4 added (Admin conversions, the pages to open in the browser)

Every page below is a definition page now; its old address (`/Admin/...`) is a `route` definition that must land on the new one. Sign in as `SystemAdmin` unless a line says otherwise. The Jira comment of each issue has the full line list.

| Issue | Old address | New page | Check |
|---|---|---|---|
| 644 | /Admin/Roles | /p/roles | list, create a custom role, catalog roles cannot be deleted, custom role can; sidebar link active |
| 643 | /Admin/PurchaseOrderAuthority | /p/po-authority | new level; an overlapping range shows the service message in the form; edit loads the values; approver list has only approver roles; Active switch replaces the old toggle |
| 649 | /Admin/StorageRouting | /p/storage-routing | create (unknown provider refused by the service), edit, remove (soft delete) |
| 645 | /Admin/RoutingRules | /p/routing-rules, /p/routing-confidence | edit target module; thresholds edit loads 0.85 and 0.5 when empty; caches are refreshed (service) |
| 632 | /Admin/ContractApprovalAuthority | /p/contract-approval-authority | service validation message on overlap; toggle |
| 629, 630, 646, 647 | audit log, authorization audit, SoD policies, security events | /p/audit-log, /p/authorization-audit, /p/sod-policies, /p/security-events | list, filters, details; SoD policy create and toggle; a non-admin cannot open them |

Also: `DynamicSidebarTests` mirror test (legacy sidebar and catalog menu must list the same `/p/...` links), the Web test family (`dotnet test PEMS.Tests --filter "FullyQualifiedName~Web"`), `MigrationClassificationTests`, `PoAuthorityDefinitionsTests`, `OperationTests`.

## Still open in batch 4 (not in this run)

Not converted yet, each waits on a generic input (issues in `plan/sheets/sprint-batches.md`): 631, 633, 634, 635, 636, 637, 638, 639, 640, 641, 642, 648, 650, 651. Their controllers still serve their pages, so this run is valid without them.

## Known, not regressions

- `AuditTemporaryKeySqlTests` and other SQL-instance families fail without SQL.
- The `matrix` snapshot does not exist until step 1; the manifest test fails until it is recorded.
