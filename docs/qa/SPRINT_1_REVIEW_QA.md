# Sprint 1 review QA: step-by-step checklist

Used by the two identical Jira tasks "Sprint 1 review QA" (one for Hamid, one for Saber). Procedure: guideline 07 section 8 "Sprint review procedure".
Source of the lines: the open lines of the QA comments of the 98 issues of Sprint 1 (audit 2026-11-06), the README "Manual checks in the UI" (M-1 to M-62), and the checks that were never defined.

**Result of every line:** `Pass`, `Fail (SCRUM-n)` (a bug issue was created in Sprint 1), `Blocked (reason)` or `N/A (reason)`. Put the evidence (screenshot name, URL, test counts) after the result.
**When both tasks are fully Pass or N/A with a reason:** every Sprint 1 issue becomes Done with its checklist checked, and the sprint is completed.

Browser conventions for every browser line (call it **the matrix**): Persian (RTL) and English (LTR); Light, Dark and Turquoise theme; desktop and a 375 px window; the console has no errors; no request fails in the network tab; nothing stays on "Loading".

## A. Prepare the environment

- [ ] A1. `git pull` on `main`; `dotnet build PEMS.slnx -c Release` -> 0 errors.
- [ ] A2. SQL Server Express `.\SQLEXPRESS` is running with Integrated Security; the regression database `PEMSDB_RegressionTest` is created by the tests (README "Local tests"). `sqlcmd -S .\SQLEXPRESS -E -C -Q "SELECT 1"` answers (`-C` trusts the server certificate with ODBC Driver 18; if the instance is the default one use `-S .`, and set `PEMS_TEST_CONNECTION` for the tests). If it does not, mark every SQL line `Blocked (no SQL Server)` and ask the owner for a machine.
- [ ] A3. Start the site: `dotnet run --project PEMS.Web`; sign in as the administrator; the setup page does not appear again.
- [ ] A4. Fresh browser profile (no cache); developer tools open on Console and Network. Create (or reuse) test users: one SystemAdmin, one user with a normal module role (for permission checks), one user without roles.
- [ ] A5. Write down the commit under test (`git rev-parse --short HEAD`) at the top of your result.

## B. Automated runs (need SQL Server)

- [ ] B1. `dotnet test PEMS.UI.Tests` -> all pass (499 at the time of writing).
- [ ] B2. `dotnet test PEMS.Tests --filter "FullyQualifiedName~PEMS.Tests.Web"` -> all pass (643 plus the SQL test `DynamicSidebarSqlTests`).
- [ ] B3. SQL families, one command each, all pass: `DynamicSidebarSqlTests`, `ContractCreationAtomicitySqlTests` (T12 and T13 were red before SCRUM-498), `OrganizationImportSqlServerTests` (SCRUM-497), `NetDemandSqlTests`, `DatabaseUpgradePathSqlTests`, the material requisition concurrency tests, `AdminPasswordSurvivesUpdateTests` (SCRUM-410), the numbering concurrency tests (SCRUM-40), the definition store tests (SCRUM-434).
- [ ] B4. Full run: `dotnet test PEMS.slnx`. Write the counts. Only the known failures listed in README "Local tests" may be red; every other failure becomes a bug.
- [ ] B5. HTTP smoke tests: `dotnet test PEMS.Tests --filter "FullyQualifiedName~FileSecurityHttpTests"` and `CmmsEndpointSecurityTests` pass.
- [ ] B6. On a Windows (CRLF) checkout: `Controller_NeverRunsDestructiveDockerCommands_OrMigrations_AndIsLfOnly` (SCRUM-445/447) and the PEMS.UI literal-text gate (SCRUM-422) pass.
- [ ] B7. `docker build` of the Dockerfile succeeds (the restore layer includes PEMS.Licensing, PEMS.UI and PEMS.UI.Abstractions, SCRUM-445/447).
- [ ] B8. GitHub Actions on `main`: the workflow "Model drift gate" is green; it is red on a throw-away branch whose model change has no migration (SCRUM-327).
- [ ] B9. GitHub Actions "PEMS Migration Chain Gate", run by hand on `main` with an older `base_ref`: green; its log shows the migrations applied in order and the step "Upgrade from every supported release, with data": nothing pending, no table lost rows, the schema equals a fresh install (README M-15, M-16; SCRUM-280, 413).
- [ ] B10. GitHub Actions "PEMS Build" by hand: the two API-gates steps (OpenAPI lint, REST conformance) run and pass (SCRUM-436; README M-26).
- [ ] B11. The k6 API script against a real server with SQL Server (command in docs/performance/API_PERFORMANCE_BASELINE.md section 3); store the result file (SCRUM-436; README M-27).
- [ ] B12. Migration `DefinitionStore` on SQL Server: 4 new tables, the filtered unique index `UX_DefinitionVersions_OneOpenDraft`; migration `DisciplineRowVersion`: `Disciplines.RowVersion` exists and the Discipline master-data page works; migration `PasswordHistory` and `TenderInvitationDispatches` apply and upgrade (SCRUM-434, 435, 412, 429; overlaps SCRUM-452).

## C. Live infrastructure and process (needs access; whoever has no access writes `N/A (no access)`)

- [ ] C1. Demo site TLS (SCRUM-27): in Plesk issue the Let's Encrypt certificate for `demo.parmisata.ir`; re-run `deploy-demo.yml`: the TLS report step has no verification warning and the health probe works without `--insecure`.
- [ ] C2. On a phone network (mobile data, not Wi-Fi) `https://demo.parmisata.ir/health/database` opens and answers 200 UpToDate (SCRUM-27).
- [ ] C3. Staging-first rule (SCRUM-313, 314): a demo deploy without the tag `staged/<tree>` is refused; after a staging deploy of the same tree it runs. The repository secret `STAGING_BACKUP_DIR` / `DEMO_BACKUP_DIR` is decided (a folder on the DB server writable by the SQL Server service account); a deploy takes the pre-deploy backup.
- [ ] C4. Runbook (SCRUM-317): read docs/deployment/RUNBOOK_DEPLOY_SITES.md; the access and owner table (who holds GitHub admin, Plesk, FTP, SQL, application admin) is correct; the landing section (manual) is confirmed with the landing owner.
- [ ] C5. Commit-key check (SCRUM-349): open a throw-away PR whose branch, title and commit subject have no `SCRUM-<n>`: `jira-key` fails with `::error::` lines; a Dependabot PR and a PR from `main`, `staging` or `demo` are not failed; a batch PR with a second key only in a commit subject shows `::warning::` and passes.
- [ ] C6. Plan sync (SCRUM-350): `plan_sync.py reconcile` against the live tracker lists the status differences and the issues without a row; `push` without `--apply` lists only expected differences; the workflow "Plan sync (nightly)" run by hand has a job summary and a pull request.
- [ ] C7. Tracker setup (SCRUM-24, 475, 479): `python tools/plan-sync/plan_sync.py check` passes; board columns exist; the Plan ID is the `plan-<n>` label and the `[#n]` title (no Plan ID field) and Parent is enforced by `tracker-check`, not by the create screen (owner decision 2026-11-09, SCRUM-557); an export of the live project run through `plan_sync.py tracker-check --json issues.json` has 0 hygiene findings for SCRUM-400 and later.
- [ ] C8. Environment settings (README M-4, M-5, M-6, M-7): Docker ports are loopback only; the default-credential report is empty; a changed setting takes effect; a deployed environment works.

## D. Browser checks, step by step (the matrix applies to every line)

### D1. Accounts and security
- [ ] D1.1. README M-1, M-2, M-3: one-time password for a user created from an employee; password policy; the load-test seed refuses to run without a password.
- [ ] D1.2. README M-8, M-9, M-10: last-admin guard and second-admin reset; one-time recovery codes; server break-glass recovery.
- [ ] D1.3. README M-14: restart the site / apply an update: the administrator password you chose still works (SCRUM-410).
- [ ] D1.4. README M-19 (password history, SCRUM-412): reset a user to P1, sign in, the forced change refuses P1 and accepts P2; later P1 and P2 are refused; after more than 5 changes the oldest is accepted again.
- [ ] D1.5. Login page language (SCRUM-477): sign out, switch to English: every text on the login page is English; switch to Persian: every text is Persian ("Use a recovery code" too).
- [ ] D1.6. Login, ChangePassword, Roles, Users, Projects, Vendor, MDB, Transmittal create forms: submit an invalid form: the red validation box appears; a valid first load shows no box.

### D2. Shell, themes, offline
- [ ] D2.1. README M-18 (SCRUM-29, 480): open a list page and a detail page in all three themes: nothing looks different from before; the new CSS and JS load; no console error.
- [ ] D2.2. Tokens (SCRUM-19, 476): five pages in Light, Dark and Turquoise: colours come from the theme (no hard-coded colour visible), the sidebar active marker is on the start edge in RTL and LTR.
- [ ] D2.3. README M-17 (SCRUM-20): block the network (developer tools offline); Engineering Documents and VDR pages show the PDF; pins and page buttons work; no request goes to another host.
- [ ] D2.4. Page title row (SCRUM-425, 418): the title row of the chosen pages in English and Persian, desktop and 375 px; the pagination text in a Persian list uses Persian digits for the range, the page numbers and the size buttons; English is unchanged (SCRUM-426).
- [ ] D2.5. Numbers and dates on a Persian page (README M-19 second entry, SCRUM-421): type 12.5 and Persian digits in a number input: accepted; an invalid value shows the same error as in English.
- [ ] D2.6. Jalali calendar (SCRUM-225): date pickers, list filters and reports on a Persian page show and accept Jalali dates; Gregorian is stored (check one record in the database or an export).

### D3. List pages
- [ ] D3.1. README M-12, M-13 (SCRUM-408, 409): Engineering Documents and VDR lists have the correspondence look: full-page table, filter dropdown with badge and chips, KPI toggle remembered, search, export.
- [ ] D3.2. README M-21, M-36 to M-41 (SCRUM-337 waves 0 to 4b): one list of each wave: filters, chips, paging, column menu, resize and sort, export, new button.
- [ ] D3.3. Letter index (SCRUM-509, 511): search box opens and searches with suggestions; filter popover (type and priority segments); column menu check boxes hide and show columns; bulk tag menu; select-all and row check boxes.
- [ ] D3.4. Every list in the sidebar opens without error as SystemAdmin (a quick walk, 5 minutes).

### D4. Related lists, info grids, KPI tiles, charts, trees
- [ ] D4.1. README M-42 to M-47 (related list waves R1 to R3d): add, remove and save a row in one page of each wave.
- [ ] D4.2. README M-48 to M-51 (info grid I1 to I4), M-52 to M-55 (KPI K1 to K4): one page of each wave; links on tiles work.
- [ ] D4.3. README M-56, M-57 (skeletons), M-58 to M-60 (charts C1 to C3), M-61, M-62 (tree views T1, T2).

### D5. Procurement, warehouse, numbering
- [ ] D5.1. README M-22 (SCRUM-423): the Procurement and supply chain section in both sidebars; Tender, PO and Warehouse > Material Item are active and the section is open.
- [ ] D5.2. README M-23 (SCRUM-40): package, MR, PO, contract and tender create forms say "assigned automatically" and get the number on save; manual entry only where the scheme allows, with a duplicate message; the numbering scheme page edits a pattern.
- [ ] D5.3. README M-24 (SCRUM-57): dropdowns on procurement forms, a list filter and a modal are searchable; the Tom Select multi-select for user roles works (M-11).
- [ ] D5.4. README M-28 (SCRUM-429): with a local test SMTP server (smtp4dev) invitations arrive and the tender shows the send log; with `Email__Enabled=false` the page explains that email is off.
- [ ] D5.5. README M-29 to M-32, M-34 (SCRUM-428, 472, 471, 470, 473): registered parts in MR items; warehouse intake of work-order material; procurement cartable intake; PO lines Draft -> approval -> Issued; receiving against PO lines, the rejection and hold decisions.
- [ ] D5.6. README M-35 (SCRUM-478): Part Catalogue reports; the downloaded Summary file has six sheets, headers follow the language, sheets are right to left in Persian; the Warehouse link in both sidebars and the Part Catalogue button on the report hub open the page.

### D6. Search, rich text, upload, workflows
- [ ] D6.1. README M-33 (SCRUM-64): Ctrl+K palette in the running app: groups, options, Esc, Enter, keyboard, 375 px; the global search results respect permissions; vendor pickers on Contract pages still work.
- [ ] D6.2. Rich-text editor (SCRUM-59): in Persian (RTL) and English (LTR) type, format, paste HTML with a script: it is sanitized.
- [ ] D6.3. Upload (SCRUM-202): upload a disallowed type and an oversized file: refused with a clear message; open a PDF preview in real Chromium: inline, no CSP error; other types download as attachment.
- [ ] D6.4. README M-20 (SCRUM-103): the document approval, MOC, permit and work order workflows are editable in the workflow designer and the module contract guard refuses an unsafe edit.

### D7. Component adoption (SCRUM-481 to 487, 499, 500, 502, 506 to 512, 339)
- [ ] D7.1. Alerts and toasts (SCRUM-481): Asset details, a Permit page, a WorkRequest page: trigger a success and an error message; the live toast (normal and error) appears and closes.
- [ ] D7.2. Confirm dialogs (SCRUM-483, 499): delete, deactivate, revoke on Users, Roles, Storage routes, Tags, Department filter: a dialog opens, Cancel does nothing, Confirm acts; work order Reject and Cancel with an empty comment: the comment box is marked invalid and focused and nothing is asked; no browser pop-up dialog appears anywhere.
- [ ] D7.3. Modals (SCRUM-482, 500, slice 4): open one modal on each of InventoryMasterData, MaintenanceMasterData, EngineeringDocuments details (client response, issue, freeze, hold), Letter details (return, reject, sign, tags), VDR details (comment, reply), PartCatalogueRequest edit, Asset, Contract, Employee, TaskManager create: title, close button, body, footer buttons; fields are filled when opened from a row and the form resets on reopen; ImportCenter: each Import button shows its title, hint and file type filter and posts to the right action.
- [ ] D7.4. Tabs (SCRUM-484, 511): Help forms (StandardCreate), Organization index, Users details, Equipment details, VDR details, MaintenanceMasterData: tabs and pills switch with mouse and arrow keys; the active tab keeps its look; panes fade in and none is empty; the Equipment pending-suggestion badge still shows.
- [ ] D7.5. Breadcrumb and menus (SCRUM-485): Help article (breadcrumb trail), an Admin page, a normal page: spacing 14 px below and colours as before; dropdown menus open and close.
- [ ] D7.6. Forms and fields (SCRUM-487, 510): the forms of Account, Contract, Permit, Letter, WorkOrder, MaterialRequisition look as before; submit one post form of each kind and the antiforgery token is accepted (Letter details: Send, Archive, Approve, Issue, Close, tags, recipients, upload, refer; PreProcurement: convert, accept, dismiss; WarehouseIntake; EngineeringDocuments create; MaintenanceStrategy edit); GET filter forms submit with their query; MrsImport review and TableExtraction review bulk commit and reject work (check boxes outside the form).
- [ ] D7.7. Buttons and inputs (SCRUM-511, 509): vs-btn, lp-act and icon buttons in Letter index, Chart pages and Index pages look and click as before; dropdown items in the Letter column menu and bulk-tag menu; bare check boxes and radios (DCC register channel segments, switches), file pickers (Letter compose, DCC register).
- [ ] D7.8. Badges and links as buttons (SCRUM-53): badges on 5 pages and buttons on 5 pages in all themes and both languages: look, escaped database text, tooltips, badge links, link-style buttons (Cancel, Open, Passport) open the right page with their route values.
- [ ] D7.9. Page wrappers, flex rows, panels, dashboards (SCRUM-339 slices 1 to 5): an Admin page, a workspace page, Letter details panels, the WorkRequest collapsible panel (open when there is no workflow); toolbar rows keep the title left and buttons right and wrap on a narrow window (Workflow management designer, bindings, details, Help edit); Employee details headings; Home KPI links; QC ITP cell; number tiles in a responsive grid (4 then 2 columns): ProjectIntelligence, MrsImport review, TableExtraction review; intro banners (Authorization, Database update); KPI and CriticalityAssessment big numbers.
- [ ] D7.10. Tables (SCRUM-339 slice 6): typed tables show headers, dates, badge colours and empty text (WorkRequest, PartCatalogueRequest, Task, Contract, Discipline, EngineeringDocuments details, ShiftHandover, Meeting, ProductionRun, Production dashboard, QC KPI, Reliability pages, KnowledgeBase, Report, Asset, Instrument passport, MaintenanceStrategy coverage, AIInsights, Kpi executive, CriticalityAssessment, ManagePasskeys, Organization diagnostics, Users details, WorkflowManagement details, Inventory, MaterialItem, PartCatalogueAdmin, Replenishment, StoreRequisition, EquipmentClassification); other tables look and sort as before (a list with resizable columns, a related list, a details page with action buttons in cells, a table with colspan, a table in a modal).
- [ ] D7.11. Performance of tables: open the heaviest table pages (Equipment details, MaintenanceMasterData, InventoryMasterData, EngineeringDocuments details, Letter index and details, a list with 100+ rows): no visible slowdown (note the load time).
- [ ] D7.12. Icons, hidden blocks, styles (SCRUM-508): Letter details and index icons; validation boxes hidden when valid; Employee vendor field, MOC temporary-expiry, MaintenancePlan meter and calendar fields, EngineeringDocuments issue fields (department, external, letter fields), Help compliance warning, MaintenanceStrategy RTF warning and CBM section toggle; signature images, PDF canvas wrapper, QC bars, notification dropdown scroll, NavigationMenu indent; stage diagrams (EngineeringDocuments, VDR, WorkOrder) look as before.
- [ ] D7.13. Scripts without hand-built DOM (SCRUM-512): Letter compose (recipient chips and dropdown, group menu, approval chain, alerts); DCC register (party dropdown with create option, empty result, error summary); confirm dialogs and error toasts with copy; palette and sidebar search; searchable selects (no results, loading); cartable (clock, bulk tag and untag, thread replies); permission matrix toast and search highlight; maintenance calendar error banner; chat assistant bubbles; auto-submit selects (24 views), print buttons, back button, step-up submit (login, passkeys, permit decisions), toggle fields.
- [ ] D7.14. Component states (SCRUM-424): render an empty state with an action, an error state with retry, a loading state, in English and Persian, desktop and 375 px.

### D8. Dynamic platform and errors
- [ ] D8.1. README M-25 (SCRUM-434, 435): REST by hand: GET and POST on disciplines and definitions with the cookie and antiforgery header; ETag and If-Match give 412 on a stale edit; ProblemDetails on errors.
- [ ] D8.2. Errors (SCRUM-464, 467): force a server error: the status page shows a trace id; a failed fetch shows a toast with a copy button; a GET is retried on a 503, a POST is not.
- [ ] D8.3. Data entry (SCRUM-427 onward): the Persian and English pages of one create form accept the same valid data.

## E. Owner decisions to record (answer, or write `N/A (reason)`)

- [ ] E1. ADR-0007 (component technology, SCRUM-23) approved by Member A; the licensing technical design (SCRUM-26) approved by Member A; its open risks answered by the EULA (#120).
- [ ] E2. Metadata registry design questions 1 to 5 (SCRUM-25): JSON Schema library and licence, second approver rule, limits, merge of upstream changes.
- [ ] E3. Secrets audit open decisions (SCRUM-17): untrack and purge the 13 runtime files in `PEMS.Web/filestore`; FTPS `--insecure` in deploy-site.yml; actions pinned by tag; rotation hand-off (SCRUM-334).
- [ ] E4. UI audit (SCRUM-18): hand-review a sample of lighter pages to validate the rule estimate (default: follow-up task); read the audit document.
- [ ] E5. Price list v1 and proposal template (SCRUM-28); core edition and module catalogue (SCRUM-132); platform of the shared main host, ADR-0012 and the supportability matrix SM-01 to SM-03 (SCRUM-305).
- [ ] E6. Persian digits: apply the pagination digit rule to other numbers on lists (counts, KPI tiles)? (SCRUM-426).
- [ ] E7. REST inventory (SCRUM-432): confirm the target resource names for the 30 heaviest controllers; decide who reviews the 269 actions without a permission signal.
- [ ] E8. Plan as markdown (SCRUM-446): keep rows whose history text mentions the workbook as written (default keep); consistency audit decisions (SCRUM-463) read.
- [ ] E9. Library adoption (SCRUM-484, 502): record that PemsTabs, PemsBreadcrumb and PemsMenu stay descriptor-driven for the BPMS designers; the small-block Pems skin needs a design decision.
- [ ] E10. Component manifest snapshots (SCRUM-433): the nine approved snapshots in PEMS.UI.Tests/Manifests are the options designers should offer.
- [ ] E11. Hard-coded data audit (SCRUM-514): answer the 3 questions in section 7 and rank section 5 of docs/architecture/HARD_CODED_DATA_AUDIT.md.

## F. Documents to read (docs-only issues)

- [ ] F1. docs/audits (UI pattern audit, secrets audit, REST inventory), docs/procurement/PROCUREMENT_STANDARDS_GAP_ANALYSIS.md, docs/architecture/METADATA_REGISTRY_DESIGN.md, ADR-0009 to ADR-0013, docs/architecture/HARD_CODED_DATA_AUDIT.md: each reads correctly; no secret value appears in any text.
- [ ] F2. Guidelines: chapter 00 section 0.0 (words), chapter 07 (tiers, browser checks, sprint review procedure) and chapter 02 section 7 (component rows) match how the team works.

## G. Close

- [ ] G1. Every failed line has a Bug issue in the active sprint (label `qa-failed`, area label, steps and expected result, plan row), linked from this task (guideline 07 section 7a step 6, owner decision of 2026-11-08 and 2026-11-10).
- [ ] G2. Write the summary in a comment: commit under test, counts of B1 to B5, number of lines Pass, Fail, Blocked, N/A.
- [ ] G3. Tell the owner that this task is finished. When both QA tasks are finished and every bug is fixed and re-checked: the owner (or the agent on the owner's yes) marks every Sprint 1 issue Done, checks its checklist, and completes the sprint.
