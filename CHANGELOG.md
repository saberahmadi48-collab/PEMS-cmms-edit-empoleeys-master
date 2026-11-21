# Changelog

All notable changes to PEMS. No system version is defined yet (plan row F_CI), so entries are grouped by date
and theme and were reconstructed from the git history (initial import 2026-09-05). Newest first.

















































































## 2026-11-21 - SCRUM-755: personal task create and status step as definitions

### Changed
- A new personal task and the status step are now definitions (/a/task-create, /a/task-update-status) over ITaskItemOperations (the actor is the current user; the task manager's object-level rules and error keys are unchanged; a task the user may not touch is refused). The list and the details page stay on TaskController (2 actions). The permission-only controller tests were replaced by operation tests.

## 2026-11-21 - SCRUM-764: work order task, labor, supply code and warehouse request as definitions

### Changed
- Complete and reopen a task, add a labor entry, set the supply code of a material and request the materials are now definitions (/a/work-order-*) over IWorkOrderTaskOperations; the section guard (section x phase x permission, denial record, change log) is the sectionGuard of each action. The details page, status steps, create and the discipline review (a list of ids) stay on WorkOrderController (6 actions). The permission-only controller tests were replaced by operation tests; the section guard tests run through the definitions. The labor-rate-missing hint id is no longer kept in TempData (the message is shown).

## 2026-11-21 - SCRUM-725: maintenance strategy lifecycle and assignment commands as definitions

### Changed
- Submit, review, activate, retire, assign a tag, unassign, request and decide an override are now definitions (/a/strategy-*) over IMaintenanceStrategyOperations (the actor is the current user, rule errors are translated). The strategy Details view posts to them. The list, details, create, edit, bulk assign, tag view and reports stay on MaintenanceStrategyController (12 actions). The legacy Deactivate action is removed. The permission-only controller tests of the removed actions are removed (the operation catalog holds the Edit/Approve floors).

## 2026-11-21 - SCRUM-723: maintenance master data as definitions

### Changed
- IMaintenanceMasterDataOperations: the 14 lists as one row shape and the 29 commands of MaintenanceMasterDataController moved with their validation and messages (generated from the controller), permissions as operation floors; page /p/maintenance-master-data (a grid per list with a toggle row action) and a form and an action per command. Gaps: free text where the old page had a fixed choice (evaluation status, priority, condition, variable type); ids typed on toggle forms; the permission-only controller tests were removed (CreateGroup was ported to the operations).

## 2026-11-21 - SCRUM-734: permits (list, create, approval response, close) as definitions

### Changed
- IPermitOperations (the list with the status filter and the waiting-for-my-approval queue from the workflow runtime, create, the submit and issue steps, the approval response and close); permit-respond-approval and permit-close are stepUp actions (security key) and the operations check the permit authority eligibility first; page /p/permits and forms/actions. PARTIAL: the details page (isolation points, gas tests, SIMOPS, execution gate, approval chain) stays on PermitController (1 action) and posts its commands to the action host. Gaps: the PermitApprovalAuditRecord written on success by the old sensitive-action check is not written by the generic step-up (denials are audited); the pagination test file was removed (the grid pages the rows in memory; the waiting-queue ordering is not covered by a test any more).

## 2026-11-21 - SCRUM-765: work requests (list, create, commands) as definitions

### Changed
- IWorkRequestOperations (list with status filter, choices, create, legacy status step with its permission rule, reject, cancel, convert to a work order); page /p/work-requests and forms/actions; the details page and its workflow view stay on WorkRequestController (1 action) and post the commands to the action host. Gaps: a converted work order is not opened; ids typed on the command forms; the list view test was removed.

## 2026-11-21 - SCRUM-759: tenders (list, create, invitations, clarifications) as definitions

### Changed
- ITenderOperations (list with a package filter, queued email invitations, raise and answer a clarification) and the Create service attribute; page /p/tenders and forms/actions; the details page posts the converted commands to the action host. PARTIAL: the details page and the approval of a negotiation, the choice of the winner and the contract from the winning bid stay on TenderController (4 actions): they carry creator-separation and SoD guards whose controller tests are the safety net, and the details page renders their antiforgery forms. Gaps: list and create tests of the old views were removed.

## 2026-11-21 - SCRUM-676: contracts as definitions

### Changed
- IContractOperations (list, details with the originating tender, discipline reviews, revisions, documents, NTPs, kick-off meetings, ITPs; add document with the editor event, issue NTP, schedule kick-off); pages /p/contracts and /p/contract-details; forms and actions for the commands and for the existing workflow actions. The create form (creator event, uncertain commit recovery, SoD) stays on ContractController (2 actions). Gaps: the workflow history of the instance is not shown; ids typed on forms; the change-status action was replaced by the existing submit-for-review action and its two tests, the list/details forbid assertions and the Contract list view tests were removed.

## 2026-11-21 - SCRUM-724: maintenance plans as definitions

### Changed
- IMaintenancePlanOperations (list, record a meter reading, generate one work order or every due one with the work order governance, deactivate); page /p/maintenance-plans and forms/actions; the create form (it needs the strategy assignment context) stays on MaintenancePlanController (2 actions). Gaps: a generated work order is not opened (the list is shown); ids typed on forms; the plan controller permission tests for the converted actions and the list render tests were removed.

## 2026-11-21 - SCRUM-711: job plans as definitions

### Changed
- IJobPlanOperations reads (list, head, tasks, skills, materials, choices) and attributes on the eight IJobPlanService commands; pages /p/job-plans and /p/job-plan-details and one form and action per command. Gaps: the edit form does not preload the saved values (retype every field); ids typed on remove forms; the controller authorization tests and the list view test were removed (operation floors replace them).

## 2026-11-21 - SCRUM-749: shift handovers as definitions

### Changed
- IShiftHandoverOperations reads (list with a status filter, head, items, history, people) and attributes on the create, prepare, acknowledge and complete service methods; pages /p/shift-handovers and /p/shift-handover-details and one form and action per command. Gaps: the prepare/acknowledge/complete links are always shown (the service enforces who may act); ids are typed on the command forms; the navigation exposure controller tests were removed.

## 2026-11-21 - SCRUM-675, 722: condition reading and maintenance history imports as definitions

### Changed
- IWorkImportOperations (batches, one batch, rows with a status filter, upload staged into a batch, row include toggle, commit, discard) for both staged CMMS imports; pages reading-import, history-import and their review pages, forms and actions; the Import Center cards link the pages by Url. Gaps: the upload opens the batch list, not the new batch; the file name comes from a field; the commit counts are shown on the batch page, not in a message; the history import controller permission tests were removed (the operation permission floors replace them).

## 2026-11-21 - SCRUM-720: management of change as definitions

### Changed
- IMocOperations (list, counts, details with workflow stage, attachments, choices, attachment upload) and the Raise service attribute; pages /p/mocs and /p/moc-details, forms moc-raise, moc-upload-attachment and one form per workflow action. Gaps: the workflow links are always shown (the service enforces the rule); the upload takes the file name from a field; a new MOC returns to the list.

## 2026-11-21 - SCRUM-758: technical queries as definitions

### Changed
- ITechnicalQueryOperations (list, counts, details with workflow stage, attachments, choices, attachment upload) and the Raise service attribute; pages /p/technical-queries and /p/technical-query-details, forms tq-raise, tq-upload-attachment, technical-query-respond/confirm/reopen. Gaps: respond, confirm and reopen links are always shown (the service enforces the rule); the upload takes the file name from a field; a new TQ returns to the list; the two list-page render tests were removed.

## 2026-11-21 - SCRUM-677: crews as definitions

### Changed
- ICrewOperations (crew list, members, choices, create crew, add and remove member with the work order section guard and its change record inside the operation); page /p/crews and crew-* forms and actions. The section-guard test now runs against the operations; the six permission-only controller tests were removed (the operation permission floors replace them). Gap: ids are typed on the add member form.

## 2026-11-21 - SCRUM-699: findings as definitions

### Changed
- IFindingOperations (the inline context logic of FindingController moved into an operations service: list, choices, create from reading or inspection, close, raise a work request with the configured priority mapping), page /p/findings, forms and actions finding-*; the equipment and reliability pages link or post to the definitions. Gap: ids are typed numbers on the close and raise forms; the old list view test was removed.

## 2026-11-21 - SCRUM-737, 738, 739: production dashboard, orders, runs and batches as definitions

### Changed
- IProductionOperations reads (dashboard counts, active orders, recent batches and losses, order list and details with runs, run inputs/outputs/losses/events, batch) plus attributes on IProductionRunService and the order create; pages production-dashboard, production-orders, production-order-details, production-run-details, production-batch-details; one form and action per command (run lifecycle, record input/output/loss, post to inventory, batch QC with the separation-of-duties guard on release and reject). Gaps: ids are typed numbers on forms; the production order create form has no dependent equipment default from the work center; reconciliation is shown as fields.

## 2026-11-21 - SCRUM-707, 728: meetings and inspections as definitions

### Changed
- Meeting list/details/create/add action item and the fleet inspection list, inspection create and one-click work request are operations (IMeetingInspectionOperations), pages meetings, meeting-details, inspections and forms/actions. Gaps: the meeting minutes form does not preload the saved text; a new meeting returns to the list; ids are typed numbers.

## 2026-11-21 - SCRUM-750: SIMOPS as definitions

### Changed
- Create is an action over ISimopsService; Confirm, Resolve and AcceptRisk are stepUp actions over IPermitSafetyOperations (permit authority eligibility first). SimopsController deleted; the permit page posts to /a/simops-*.

## 2026-11-21 - SCRUM-696, 702, 710: execution gate, gas test and isolation verify as definitions

### Changed
- ExecutionGate StartWork and EmergencyOverride and Isolation Verify are actions with stepUp (security key) over IPermitSafetyOperations, which checks the permit authority eligibility first; GasTest create (fixed O2, LEL, H2S, CO fields) and invalidate are actions. The three controllers are deleted; the permit page posts to /a/... Gap: the approval audit record that AuthorizeSensitiveActionAsync wrote on success is not written by the generic step-up (denials are audited).

## 2026-11-21 - SCRUM-662: Part Catalogue requests as definitions (list, details, workflow, files)

### Changed
- The list, the details, the new draft, approve/return/cancel/register, retry routing, notes, the attachment download and the INWH-PLA-FO-001 Excel and PDF are now IPartCatalogueRequestOperations (model PartCatalogueRequest), the pages pc-requests and pc-request-details, forms/actions pc-request-*, file sources and routes. The long edit form with its scripts, equipment and attachment upload stays on PartCatalogueRequestController (10 actions) until a form can be loaded from an operation.

## 2026-11-21 - SCRUM-658: Part Catalogue administration as definitions

### Changed
- The Part Catalogue administration controller and view are replaced by IPartCatalogueAdminOperations (model PartCatalogueAdmin), the page pc-admin with read data sources, one form and action per command (settings, reference import with upload, sections, group maps, steps, approvers, responsibilities, commodity responsibles), file sources for the preview and the two lookups, and routes from every old address. The preview is now JSON (the old partial is gone); ids are typed numbers instead of search selects.

## 2026-11-21 - Part Catalogue lookups, duplicate checks and reports as file-host sources and a page (SCRUM-659, 660, 661)

### Changed
- The lookup and duplicate-check endpoints of the request form (SCRUM-660, 659) are file sources served by GET /x/pc-... over PartCatalogueLookup.* and PartCatalogueDedup.* operations (the same services and JSON; each is allowed to the users the old controllers allowed); the old addresses redirect to them with the query so the form scripts keep working. /p/pc-report replaces PartCatalogueReportController (SCRUM-661): filters, tiles and grids over PartCatalogueReport.* operations and the workbook (whole or one sheet) as a file source.

## 2026-11-21 - Barcode as definition pages and a generic file host (SCRUM-671, 6b)

### Changed
- New generic input: a data source with the provider operation-file names a read operation whose answer is a file (Bytes, FileName, ContentType) or any object, and GET /x/{key} (FileHostController) serves it under the permission of the operation and the definition; OperationReader.InvokeAsync is the one place a read answer is produced for rows, choices and files; the route table accepts /x/ targets. /p/barcodes, /p/barcode-scan, /p/barcode-verify and /p/barcode-bulk with their forms replace BarcodeController: the SVG, PNG, label PDF, label sheet and the scan JSON are file sources, print and bulk generation are actions that download the label, the shared barcode panel links to the definition forms. The old routes redirect.

## 2026-11-21 - MRS import as definition pages (SCRUM-657)

### Changed
- /p/mrs-import and /p/mrs-import-review (with the upload, commit and reject forms) replace MrsImportController over MrsImport.* operations (same IMrsImportService calls; View, Create and Approve permissions kept). The upload stores the file as a document attachment like before, saved as mrs-import.xlsx; rows are committed or rejected by typed ids or by a whole match status; the Import Center card links the page. The old routes redirect; the controller authorization tests were removed.

## 2026-11-21 - Incoming receipts (IRN) as definition pages (SCRUM-655)

### Changed
- /p/irn-receipts, /p/irn-details and /p/irn-osd (with forms for create, add a line, submit, cancel, the disposition, the QA decision, receiving against order lines, the rejection decision and the hold resolution) replace IrnReceiptController. The disposition and QA approval workflow logic (start the QA workflow with compensation, execute the step, post the accepted quantity, recover a posting that failed after the step) moved unchanged into IrnReceiptOperations; submit and cancel are marked on IIrnReceiptService, the rejection decision and hold resolution on IPurchaseOrderReceivingService. The disposition is typed as a mode for every line or as lines lineId:accepted:rejected:hold. Controller-bound IRN tests were removed with the controller; the old routes redirect.

## 2026-11-21 - Purchase orders as definition pages (SCRUM-741)

### Changed
- /p/purchase-orders and /p/purchase-order-details (with forms for create, create from an MR, add lines, edit and remove a line, submit, route again, approve, reject, issue and release inspection) replace PurchaseOrderController. The reads are PurchaseOrder.* operations, both create commands keep the access-control record behind the separation-of-duties rule, the line, approval and inspection commands are the operations marked on the purchase-order services, and issue runs under the strict access guard. Controller-bound authorization and break-glass tests were removed with the controller (the service-level tests stay); the old routes redirect.

## 2026-11-21 - Procurement packages as definition pages (SCRUM-736)

### Changed
- /p/procurement-packages and /p/procurement-package-details (with the create, stage, MR link, clarification, response, status, approval and close-out forms) replace ProcurementPackageController: the reads are ProcurementPackage.* operations, the create keeps the access-control record behind the separation-of-duties check, and the commands are the operations marked on IProcurementPackageService; approve and close out run under the strict access guard (denials recorded, the creator may not approve). The controller-bound authorization, close-out and guard tests were removed with the controller (the service-level close-out tests stay); the old routes redirect.

## 2026-11-21 - Material requisitions as definition pages (SCRUM-726)

### Changed
- /p/material-requisitions, /p/mr-details and /p/mr-quotations (with the create, status, quotation and evaluation forms) replace MaterialRequisitionController over MaterialRequisition.* operations (same service calls; reaching Approved still needs MaterialRequisition.Approve; an MR issued to procurement still enters the procurement queue). The item lines are typed one per line (description | part number | quantity | unit); the part lookup is the operation MaterialRequisition.PartLookup. The old routes redirect.

## 2026-11-21 - Vendors as definition pages (SCRUM-763)

### Changed
- /p/vendors and /p/vendor-details (with their forms and actions) replace VendorController: create, edit and activate / deactivate are the generic entity actions on a new Vendor registration (permissions Vendors.Create and Vendors.Edit as before), recalculating the risk and setting the qualification are the operations marked on IVendorRiskService and IVendorQualificationService, the reads are Vendor.* operations. The old routes redirect.

## 2026-11-21 - Spare parts controller removed (SCRUM-751)

### Changed
- SparePartController and its view are gone: /p/spare-parts, /p/spare-parts-low-stock and the create, edit and deactivate commands were already generic entity definitions; /SparePart redirects to /p/spare-parts and the menu and sidebar point to it.

## 2026-11-21 - AI intake queue as definition pages (SCRUM-709)

### Changed
- /p/intake and /p/intake-review (with the route and reject forms) replace IntakeController: the reads and the two decisions are Intake.* operations over the same IIntakeService calls (route with the reviewer's corrections, reject). The review form no longer pre-fills the AI suggestion (it is shown above it). /Intake redirects.

## 2026-11-21 - Store requisition as definition pages (SCRUM-664)

### Changed
- /p/store-requisitions and /p/store-requisition-details (with their forms and actions) replace StoreRequisitionController. The reads are StoreRequisition.* operations; the commands (create, add a line, advance, reject, cancel, close with a shortage, set an approved quantity, issue everything or one line) call IStoreRequisitionLifecycleService, which keeps the permission, separation-of-duties and transition rules. The per-line quantity form became per-line commands; the new requisition lands on the list. The old routes redirect.

## 2026-11-21 - Replenishment and the procurement queue as definition pages (SCRUM-663, 735)

### Changed
- /p/replenishment, /p/replenishment-details, /p/pre-procurement and /p/pre-procurement-details (with their forms and actions) replace ReplenishmentController and PreProcurementController. The Replenishment.* operations keep the feature switch (a switched-off module answers nothing) and the commands run under the strict access guard like ProtectedActionGuard; the pre-procurement decisions are the operations marked on IPreProcurementService (convert to a new or a Draft requisition, accept, dismiss or return, send a recommendation). The row version of a recommendation is read when the command runs, not carried by the form. The old routes redirect.

## 2026-11-21 - Material Master as definition pages (SCRUM-656)

### Changed
- /p/material-items, /p/material-item-details and their forms replace MaterialItemController: create, edit and activate / deactivate are the generic entity actions on a new MaterialItem registration (same permissions as before), receive, adjust and transfer stock are the operations MaterialStock.Receive, Adjust and Transfer marked on IMaterialStockService (permissions kept), the reads are MaterialItem.* operations over IMaterialItemService. The barcode panel of the old details page moves with the Barcode controller (SCRUM-671). /Warehouse/MaterialItem redirects.

## 2026-11-21 - Inventory master data landing page (SCRUM-653)

### Changed
- /p/inventory-master-data replaces InventoryMasterDataController (its create and activate / deactivate commands were already the generic entity pages /p/uoms, /p/material-categories, /p/warehouses, /p/storage-locations); /Warehouse/InventoryMasterData redirects; the Barcode redirect and the Quick Setup step point to the page.

## 2026-11-21 - Inventory dashboard, ledger and warehouse intake as definition pages (SCRUM-652, 654, 665)

### Changed
- /p/inventory, /p/inventory-ledger and /p/warehouse-intake (with the two source forms) replace InventoryController, InventoryTransactionController and WarehouseIntakeController; the reads and the sourcing are the operations Inventory.* and WarehouseIntake.* over the same services. The old per-line quantity form became two commands: source every open line, or one line with its quantity. /Warehouse/Inventory, /Warehouse/InventoryTransaction and /Warehouse/WarehouseIntake/Details/{id} redirect.

## 2026-11-20 - Executive dashboard host as a definition page (SCRUM-697)

### Changed
- /p/executive-dashboard mounts the dashboards bundle through the `script-island` component (the Exec_ texts in both languages and the links are in the definition) and replaces the Index view of ExecutiveDashboardController; the controller answers only the JSON read. /ExecutiveDashboard redirects.

## 2026-11-20 - My Profile as a definition page (SCRUM-729)

### Changed
- /p/my-profile (profile tiles, photo, drawn signature through the `signature-pad` component, the photo form /f/my-profile-photo) over `IMyProfileOperations` replaces MyProfileController, its view and script; /MyProfile redirects. New component `image`.

## 2026-11-20 - Components: signature pad and script island (SCRUM-729, SCRUM-697)

### Added
- `signature-pad` (a drawn signature posted to an action definition) and `script-island` (the host of a /dist front-end bundle with its boot data) in the component registry, with samples, render tests and rows in guideline 02. Their manifest snapshots are recorded with `PEMS_GOLDEN_RECORD=1`.

## 2026-11-20 - Definition kind harness and the layout reference of a page (SCRUM-451)

### Added
- `DefinitionKindHarnessTests`: every registered kind has a sample (a shipped definition or a named test); a page now lists its layout as a reference.

## 2026-11-20 - Teams as definition pages (SCRUM-757)

### Changed
- /p/teams and /p/team-details (members, sub-teams and shifts, positions, assignment history, with their forms and row actions) over `ITeamOperations` replace the Index and TeamDetails pages and the 14 write actions of TeamController. The discipline scope rule is unchanged (SystemAdmin and EngineeringManager across disciplines, everybody else only their own discipline, fail closed); Team.View/Create/Edit are the permission floors. The organization chart page and its JSON reads stay in the controller.

## 2026-11-20 - Employees as definition pages (SCRUM-687)

### Changed
- /p/employees (filters, search, details, edit, activate/deactivate, Excel export, add) and /p/employee-details (profile, account, skills, qualifications, certifications, labor rates, team history, with their add forms and remove actions) over `IEmployeeOperations` replace EmployeeController and its views; /Employee and /Employee/Details/{id} redirect. Editing the personal fields is the generic `entity.update` action checked by `Employee.ValidateEdit`.
- The roles of a new account are no longer chosen in the add form: assign them in Administration, Users. An Internal employee must not have a vendor (it is refused instead of cleared). Quick setup links to converted pages work again.

## 2026-11-20 - Home dashboard as a definition page (SCRUM-705)

### Changed
- /p/home (what needs your attention, twelve counts, documents-by-status donut, discipline performance, my widgets) over `IHomeOperations` replaces the Index view of HomeController; `/` forwards to it. Privacy, the status page and the error page stay. The tiles no longer link to their lists and the setup progress is a row, not a bar.

## 2026-11-20 - AI extraction review as definition pages (SCRUM-667)

### Changed
- /p/ai-extraction (analysed files) and /p/ai-extraction-review (tags and specifications with approve/reject actions; the tag approval is the form /f/ai-tag-approve with an equipment choice) over `IAiExtractionOperations` replace AIExtractionController and its views. Permissions AIExtraction.View/Approve/Create are the floors of the operations. Approve/Reject links show on every row; the service ignores rows that are not Draft.

## 2026-11-20 - Discipline workspace as a definition page (SCRUM-682)

### Changed
- /p/discipline-workspace (filters, totals, tasks with status actions, documents, equipment, VDR, MR/MRQ; the add-task form /f/discipline-task) over `IDisciplineWorkspaceOperations` replaces DisciplineController and its view; /Discipline/Details/{id} redirects. Permissions DisciplineWorkspace.View/Create/Edit are the floors of the operations.

## 2026-11-20 - Help center list as a definition page (SCRUM-704)

### Changed
- /p/help (search, category filter, article grid) over `IHelpOperations` replaces the Index, Category and Search pages of HelpController; /Help, /Help/Category/{id} and /Help/Search redirect. The article page, the contextual help JSON and the AI explainer stay in the controller.

## 2026-11-20 - Engineering dashboard as a definition page (SCRUM-688)

### Changed
- /p/engineering-dashboard (filters, KPI tiles, most rejected, revision pressure, attention queue, discipline risk) over `IEngineeringDashboardOperations` replaces EngineeringDashboardController and its view; /EngineeringDashboard redirects. The discipline restriction is unchanged. The tiles no longer link to the filtered document list.

## 2026-11-20 - Definition kinds view and theme (SCRUM-451)

### Added
- `view` (columns, empty text) and `theme` (`--pems-*` design tokens, light or dark) can be stored, validated, published and rolled back like the other kinds; samples and refusal tests; guideline 04 section 7 has the how-to-add-a-kind checklist.

## 2026-11-20 - Charts on the report and KPI pages (batch 5 gap)

### Added
- The five report pages and the executive KPI page now show charts (volume by month, by discipline/status/priority/vendor, equipment by status, top equipment by cost) through `def:entity-chart`; they follow the page filters (a chart `source` may carry `?f.x=...`).
- Report rows carry a stable `Key` and numbers (`Number`, `Number2`); a grid data source of the `operation` provider accepts fixed `where` filters, so one operation feeds several charts.

## 2026-11-20 - Quick setup as a definition page (SCRUM-743)

### Changed
- /p/quick-setup (grid over `QuickSetup.Steps`, permission Setup.View, licence-free like before) replaces QuickSetupController and its view; /QuickSetup redirects. The progress bar is a progress row for now.

## 2026-11-20 - Dashboard settings and chat assistant as definition pages (SCRUM-680, 672)

### Changed
- /p/dashboard-settings (grid, reset, form /f/dashboard-widget) and /p/chat-assistant (history grid, form /f/chat-ask, clear) replace their controllers and views; old addresses redirect. The chat has no in-page async send any more (the form reloads the page).

## 2026-11-20 - Import center as a definition page (SCRUM-706)

### Changed
- /p/import-center (grid over `ImportCenter.Entries`) replaces ImportCenterController and its view; the employee and instrument spreadsheet imports are now forms (/f/employee-import, /f/instrument-import) over `IImportCenterOperations` and their controller actions are removed.
## 2026-11-20 - Warehouse bilingual review polish (SCRUM-540)

- Material details, stock movement labels, receive/edit dialogs and warehouse barcode messages now use the selected language. The part catalogue report shares one period display formatter with its Excel export, eliminating duplicated All time captions.

## 2026-11-20 - Notification inbox as a definition page (SCRUM-731)

### Changed
- /p/notifications (filter bar, grid, mark read / dismiss / mark all / dismiss all as actions over `INotificationOperations`) replaces the Index page of NotificationController; the bell dropdown, the unread counter and the open-redirect stay in the controller (shell widget, code-only).

## 2026-11-20 - Report and KPI labels in both languages, KPI choice list in the target form (SCRUM-713, 747)

### Changed
- Report rows, KPI names, sections and statuses are localized (resource keys `Rpt_*`, `Kpi_*`); the target form picks the KPI from a list (`Kpi.TargetChoices`).

## 2026-11-20 - KPI pages as definitions (SCRUM-713)

### Changed
- /p/kpi and /p/kpi-executive (date filter bar, grids over `IKpiOperations`) plus the form /f/kpi-target with the action kpi-save-target replace KpiController and its views; /Kpi and /Kpi/Executive redirect. Charts and tiles are grids for now. Also removed the chart tests of the deleted Report views (left behind by SCRUM-747).

## 2026-11-20 - The six report pages as definitions (SCRUM-747)

### Changed
- /p/report-correspondence, -engineering, -vdr, -cmms, -procurement (with its pipeline) and -operations (filter bar plus a grid of the report sections over `IReportReads`) replace ReportController and its six views; old addresses redirect. The charts and tiles of the old pages are not part of the definitions yet.

## 2026-11-20 - AI search page as a definition (SCRUM-669)

### Changed
- /p/ai-search (filter bar with query, discipline, document type and status; grid over `AiSearch.Search`) replaces AISearchController and its view; /AISearch redirects.

## 2026-11-20 - Filter bar for report and search pages (SCRUM-747, 713, 669)

### Added
- Component `page-filters` and the instance binding `filterOptions`.

## 2026-11-20 - Global search page as a definition (SCRUM-703)

### Changed
- /p/global-search (grid over `GlobalSearch.Search`) replaces GlobalSearchController and its view; the top-bar form and the command palette link open it; /GlobalSearch redirects.

## 2026-11-20 - CMMS readiness as definitions (SCRUM-674)

### Changed
- /p/cmms-readiness (tiles and class activation backlog) and /p/cmms-readiness-equipment (readiness of every equipment) over `ICmmsReadinessReads` replace CmmsReadinessController and its views; old addresses redirect (the class detail opens the all-equipment grid).

## 2026-11-20 - CMMS operations dashboard and maintenance backlog as definitions (SCRUM-673)

### Changed
- /p/cmms-operations and /p/maintenance-backlog (operations `CmmsOperations.*`, `ICmmsOperationsReads`) replace CmmsOperationsController and its views; old addresses redirect.

## 2026-11-20 - My Work as a definition (SCRUM-730)

### Added
- Operation `MyWork.Approvals` (`IMyWorkOperations`), grid action urls with `{raw.field}`, `Url` on command palette actions.
### Changed
- My Work is the definition page `my-work` (module, stage, personal, record, assigned, due, overdue; open the record); MyWorkController and its view are deleted; /MyWork redirects.

## 2026-11-20 - Engineering Copilot page as a definition (SCRUM-668)

### Added
- Record binding `first` (first row of the source); operations `AiInsights.*` (`IAiInsightsOperations`).
### Changed
- The Engineering Copilot page is the definition `ai-insights` (NCR patterns grid, delay totals as tiles, narrative, three delay grids); AIInsightsController and its view are deleted; /AIInsights redirects.

## 2026-11-20 - Discipline master data and workspace list as definitions (SCRUM-682)

### Changed
- /p/disciplines (master data: create, edit, activate, deactivate through the DisciplineMaster operations) and /p/discipline-workspaces (summary list) replace the Manage and Index actions and views of DisciplineController; Details, AddTask and SetTaskStatus (the workspace) stay in the controller until its page is converted. Old addresses /Discipline and /Discipline/Manage redirect.

## 2026-11-20 - Spreadsheet import by definition; Departments (SCRUM-681)

### Added
- Action handler `entity.import` (see guideline 04) and the Import Center entry field `Url`.
### Changed
- Departments: import and Excel export are the definitions `departments-import` (form and action) and `departments-export`; the list page has Import and Export buttons; both DepartmentControllers and the view are deleted; routes for /Department and /Admin/Department; the Import Center card opens the import form.

## 2026-11-20 - Knowledge base search as a definition, AI smoke test removed (SCRUM-712, 666)

### Added
- Operation grids take the quick-search text as `param:search`; the knowledge-base semantic search is the page `knowledge-base` over `KnowledgeBase.Search`.
### Removed
- `AIController` (`/AI/Test`, a developer smoke test of the language model service that nothing referenced) and `KnowledgeBaseController` with its view.

## 2026-11-20 - Projects as definitions (SCRUM-740)

### Changed
- Projects are the generic entity layer: entity `Project` registered, page, details page, create and edit forms and actions, toggle action, grid data source and an old-address route (all from `tools/definitions/make_entity_pages.py projects`); ProjectsController and its three views are deleted. The unique project code is a `unique` rule of the actions; the generator takes a key to run one spec and Persian labels of static options (`staticFa`).

## 2026-11-20 - Settings hub as a definition (SCRUM-748)

### Added
- `content-blocks` block `cards` (icon, title, text, links).
### Changed
- Settings is the definition page `settings` (roles decide the administration part) with routes for /Settings and /Admin/Settings; both SettingsControllers and the view are deleted; the sidebar entry points at /p/settings.

## 2026-11-20 - Text pages as definitions: content-blocks, Performance page (SCRUM-733)

### Added
- Component `content-blocks` (PEMS.UI): a page that only shows text is a list of blocks written in both languages in a page definition.
### Changed
- The Performance testing page is the definition `performance` (page and old-address route); PerformanceController and its view are deleted.

## 2026-11-19 - Platform-owned system fields in shipped forms (SCRUM-515)

### Changed
- Six shipped forms (po-authority create/edit, routing-rules edit, sod-policies create, storage-routing create/edit) mark their system fields `owner: platform`; business fields stay the customer's.

## 2026-11-19 - Stored file download by definition (SCRUM-615)

### Added
- Action handler `file.download`: a stored file by key, served only inside the folder the definition names, with the action's permission and roles; returned through `ActionOutcome.Download`.

## 2026-11-19 - One field-error shape (SCRUM-380)

### Added
- `ErrorShape`: a refused form or grid action posted for JSON now carries `errors` in the API's field-error shape (pointer, code, messageKey, message, params) beside the older `error` text; an import cell has the same shape (`ImportCell`).

## 2026-11-19 - Shipped reference data and its update rule (SCRUM-516)

### Added
- Definition kind `reference-data` (list, owner, rows with stable keys) and `ReferenceDataSync.Plan`: insert missing rows, update unedited platform rows, never overwrite customer-owned or edited rows (reported as upstream changes), retire rows no longer shipped, never delete.

## 2026-11-19 - Shipped workflow documents (SCRUM-517)

### Added
- `ShippedWorkflow` (document, parse and check, content hash) and `WorkflowShipper`: create-and-publish a missing workflow, never overwrite an existing one, store a changed shipped document as an upstream-changed Draft version with a diff; running instances and published versions are untouched.

## 2026-11-19 - Role grants for definitions (SCRUM-591)

### Added
- Table `DefinitionGrants` (migration `Scrum591DefinitionGrants`) and `IDefinitionAccess.CanOpenAsync(kind, key, ...)`: an administrator opens or closes a page, form, action or data source for a role without a deploy (allow adds, deny wins, the code permission stays the floor); managed with the generic entity pages (entity `DefinitionGrant`), audited.

## 2026-11-19 - Aggregates, menu definitions and validator on entity writes (SCRUM-592, 590, 373)

### Added
- Data source provider `entity-aggregate` (SCRUM-592): group by a property with count, sum, avg, min and max over the rows of a registered entity, fixed filters, `maxRows`, the rights and paging of the entity grid.
- Definition kind `menu` (SCRUM-590): a migrator feed puts a page, form or link into the sidebar with no code; checked when loaded (keys, kinds, targets, icons, the sidebar's visibility rule language), synced at start-up with the ownership rule of the sidebar; `NavigationMenuItemDefinition` carries optional shipped titles (set when an item is created, never overwritten).
- `EntityConstraints` (SCRUM-373): `entity.create` and `entity.update` check a mapped value against the `[MaxLength]`, `[StringLength]` and `[Range]` of the entity with the one validator, in the catalog's texts.

## 2026-11-19 - Render plans, hydration cache and sidebar visibility cache (SCRUM-574, 575, 576)

### Added
- `RenderPlanCache` (SCRUM-576): definitions are parsed once per content hash into an immutable plan, requests work on copies, a new version swaps in atomically, an edit that does not compile keeps the last good plan; the pipeline reports `planCompiles`.
- `HydrationCache` (SCRUM-574): hydrated results shared by the users of one permission set (key: definition hash, sorted roles, culture, scope), with a time to live, hit and miss counts in the log line; data source providers opt in with `CacheSeconds` (none does yet).
- Sidebar (SCRUM-575): the visibility rules of the menu items are evaluated once per permission set for 60 seconds and cleared when the menu changes; the tree, titles, links and the active item are still made per request.

## 2026-11-19 - File upload and file export as settings of an action (SCRUM-596, 615)

### Added
- Handler `file.upload` (SCRUM-596): one uploaded file checked by the existing upload guard with the profile the action definition names (attachment, image, spreadsheet, csv, pdf), an optional smaller size limit, stored by the storage service; returns `key`, `fileName`, `size` and `contentType` for later steps. The generic hosts (`/f`, `/a`) accept files (`[DefinitionUploadPolicy]`), `ActionInvocation.Files` carries them, and a chosen file counts as the value of its form field.
- Handler `file.export` (SCRUM-615): the rows of a grid data source as CSV (UTF-8 with BOM) or Excel, read with the rights of the user, paged, limited by `maxRows`, formula-safe; `ActionOutcome.Download` makes the host send the file.

## 2026-11-19 - Ratchet gate and pipeline diagnostics (SCRUM-611, 577)

### Added
- `DefinitionRatchetTests` and `plan/sheets/definition-ratchet.json` (SCRUM-611): the gate fails with the file or controller when a new Razor view, controller or controller action is added where a definition can express it; host and static controllers of the classification table are exempt; `PEMS_WRITE_RATCHET=1` rewrites the baseline for a reviewed exception.
- Pipeline diagnostics (SCRUM-577): `PipelineResult` carries counts next to the stage timings, the page and form hosts log one line with the trace id (resolve, authorize, hydrate, evaluate, render and the counts) and a warning when a stage is over its budget; settings `Definitions__Budgets__ResolveMs`, `AuthorizeMs`, `HydrateMs`, `EvaluateMs`, `RenderMs` in every `env/*.env`; regression guards for a list page with 1000 rows and a form with 80 fields (`PipelineDiagnosticsTests`).
## 2026-11-19 - Action steps, result binding and step-up as settings of an action (SCRUM-593, 594, 595)

### Added
- Handler `action.steps` (SCRUM-593): several handlers as ordered steps of one action definition, run in one database transaction; the first refusal or exception rolls everything back and stops the later steps. Definitions are checked when loaded (unknown handler, nesting, forward reference, duplicate name, at most 20 steps).
- Result binding (SCRUM-594): `ActionOutcome.Returned` carries values a handler produced; later steps read them with `step:as.key`, and `{as.key}` or `{Name}` in the redirect of an action definition is replaced by them (the entity create action returns the row it wrote).
- Step-up (SCRUM-595): an action definition may declare `stepUp: { resource, action }`; the host gives the WebAuthn challenge (`ActionStatus.StepUpRequired`, HTTP 428 for a fetch), verifies the answer, audits a denial and only then runs the operation; without a verifier it never runs.
- `ActionScope`: handlers open their scope through it so they share the transaction of `action.steps`.

## 2026-11-19 - Definitions: upstream diff and reset, classification table, one field validator (SCRUM-448, 628, 372, 373, 380)

### Added
- Shipped definitions (SCRUM-448): `GET /api/v1/definitions/{id}/upstream` (is the definition customised, did the product ship a newer version, the differences between the customer's published payload and the shipped one) and `POST /api/v1/definitions/{id}/actions/reset-to-shipped` (republishes the shipped payload as a new version; the customer's versions stay in the history).
- Classification closure (SCRUM-628): `plan/sheets/classification.json` lists every controller, hosted service and SignalR hub as `pending`, `host` or `static` (with the reason) or `converted` (receipt); `ClassificationClosureTests` fails when a type is missing or an entry is false.
- One field validator (SCRUM-372, 373, 380): `PEMS.Application/Validation/FieldValidator` with stable constraint codes, create and update modes, hard and advisory fields and a cap on the number of errors (`Validation__MaxErrors` in every `env/*.env`); `ValidationMessages` gives the texts in fa and en and maps violations to the RFC 9457 error list; the catalog is `docs/architecture/FIELD_VALIDATION_CATALOG.md`.

### Changed
- `FormEngine` runs the built-in checks of a field through the new validator; messages and behaviour are unchanged.

## 2026-11-10 - VDR details: no error on load, discipline summary works again (SCRUM-560)

### Fixed
- SCRUM-560: every VDR details page with the start-review form showed "Something went wrong" on load, and ticking disciplines no longer updated the summary next to the start button. When the summary became a `pems-info-grid` (SCRUM-508), its value lost the id `vdrSummaryDisc` that `vdr-details.js` reads. The item has the id again (`value-id`), and the script uses the item's own text as its "none" text and skips a missing element. Test: `VdrDetailsScriptIdsTests` (every id the script reads exists in the view).

## 2026-11-10 - VDR details: More actions menu (SCRUM-559)

### Added
- SCRUM-559 (owner decisions 2026-11-10): VDR details has a More actions menu like Engineering Documents. Each item shows only with its permission:
  - **Edit item details** (VDR Edit): description, class, planned first issue, equipment by tag number, discipline code and vendor-format number, with an audit line.
  - **Generate document numbers**: the existing link to Admin > Document numbering moved into the menu.
  - **Put on hold / Release hold** (VDR Hold / ReleaseHold): uses the shared document hold, with a banner showing the reason.
  - **Deactivate** (VDR Delete): a soft delete with a reason; the item leaves the lists and the page shows a banner.
- Every review step is refused on a held or deactivated item.
- VDR hold grants follow Engineering Documents: EngineeringManager, EngineeringSupervisor and DocumentController can hold and release, DisciplineEngineer can hold. They are in the `PermissionService` defaults and in the one-time data migration `Scrum559VdrHoldGrants`. Tests: `VdrMoreActionsTests`.

## 2026-11-09 - The project code is unique (SCRUM-558)

### Fixed
- SCRUM-558 (follow-up of SCRUM-539, owner decision 2026-11-09): project create and edit accepted a code another project already had. `ProjectService` now refuses it (spaces trimmed, case ignored) with a `ConflictException` (`unique`), and the form shows `Prj_Err_CodeExists` in its validation box. `EngineeringRevisionQualityTests` cut its project codes so short that the random part was lost and left repeating codes in the shared database; it now makes unique codes. Existing databases still hold those leftover test projects, so the unique database index is a separate issue. Tests: `ProjectAndVendorCreateValidationTests`.
## 2026-11-09 - Sprint 2 bug batch D: the deploy runbook and the tracker rules match reality (SCRUM-556, 557)

### Fixed
- SCRUM-556: the deploy runbook said pushes to `staging` and `demo` are limited by GitHub branch rules, but neither branch is protected and the repository has no ruleset. Owner decision 2026-11-09: no ruleset; the runbook now says who may push by agreement (the owner and Hamid, never a force-push or a branch deletion), and the `plan-sync.yml` comment no longer mentions branch protection.
- SCRUM-557: the guideline expected Jira to require *Parent* on the create screen and to have a *Plan ID* field; the team-managed project has neither. Owner decision 2026-11-09: the `plan-<n>` label and the `[#n]` title prefix are the Plan ID and `tracker-check` enforces Parent. Guideline 00 section 5d, guideline 06, `PLAN_TRACKER_WORKFLOW.md`, rule 25 of `plan/sheets/instructions.md` and line C7 of `SPRINT_1_REVIEW_QA.md` say so.

## 2026-11-09 - Sprint 2 bug batch C: vendor qualification, PO authority and re-routing, receiving with the default roles (SCRUM-544, 546, 547)

### Fixed
- SCRUM-544: a vendor could not be qualified in the UI, so a tender that requires qualification could invite no vendor. Vendor details has a qualification panel: the current status, category, validity and evidence, the history (newest first) and, with Vendor Edit, a form posting to the existing `SetQualification` action. Its messages are resource keys (`Vq_*`), and an expiry before the effective date is refused. Tests: `ProcurementDecisionPagesTests`.
- SCRUM-546: the PO authority table accepted any role and overlapping levels, and a PO routed to a role that could not open it was stuck. An active rule now needs a role with PurchaseOrder Approve (the list offers only those; an old rule can still be switched off), and may not overlap another active rule of the same currency. A matching rule whose role lost Approve leaves the responsible not set. A submitted PO whose route is no longer valid shows a warning and "Route again" (owner decision 2026-11-09: only then). Routing again cancels the open approval task and routes by the current table, and the buyer may take such a PO back. Tests: `PurchaseOrderApprovalServiceTests`, `ProcurementDecisionPagesTests`.
- SCRUM-547: receiving against PO lines could not be finished with the default roles. The Warehouse Manager gets IRN Edit (Hold resolution) and the Buyer IRN View (close short / replacement decision): this is in `RoleCatalog` for new databases, plus the one-time data migration `Scrum547IrnDefaultGrants` for existing ones (owner decision 2026-11-09). The IRN QA Approve / Reject buttons show only with IRN Approve. On PO submit, a stock line of a catalogued part without a material record is linked to an existing record with its part number or refused, naming the line and part. Tests: `RoleCatalogTests`, `PurchaseOrderApprovalServiceTests`, `ProcurementDecisionPagesTests`.

## 2026-11-09 - Sprint 2 bug batch B: Persian messages and titles, Jalali date and time, top bar at phone width (SCRUM-540, 542, 543)

### Fixed
- SCRUM-540: on Persian pages the validation, password-policy and Identity messages and several tab titles were English. Identity messages come from `Id_*` keys (`LocalizedIdentityErrorDescriber`); the default validation messages ("The {0} field is required." and similar) become resource keys (`ValidationMessageKeys`) so the data-annotation localizer translates them, also for the implicit [Required]; the messages the review found (MDB, Transmittal, Users) go through the localizer; `_AuthLayout` and `_LayoutEmbed` translate the title like `_Layout`; every literal page title has a Persian value and none is written in Persian in code. English pages read as before. Test: `PersianMessagesAndTitlesTests`.
- SCRUM-543: the eight date-and-time fields (Letter reminder, Meeting, Permit gas test, condition monitoring reading, shift handover, work order planned start and finish) used the browser's Gregorian `datetime-local` picker. `<pems-date>` has a `time` mode: Jalali date and time with the picker's time panel on a Persian page (date-only fields keep their date-only picker), `datetime-local` elsewhere, posting `yyyy-MM-ddTHH:mm` either way. `PemsDate.value` / `PemsDate.setValue` let scripts read and write it (the Letter reminder uses them). Tests: `DateTagHelperTests` (time mode), `NoNativeDateTimeInputTests`.
- SCRUM-542: at 375 px the top bar was 55 px wider than the window on pages with the Help button, so the page scrolled sideways and its title and pager were cut off. Below 576 px the top bar uses less padding and smaller gaps and the Help button shows its icon only. Test: `TopbarPhoneWidthTests`.

## 2026-11-08 - Sprint 2 bug batch A: material edit, help search links, project and vendor forms, sidebar marker (SCRUM-539, 541, 545, 548)

### Fixed
- SCRUM-545: saving the Material Master edit modal cleared the unit, category, discipline, vendor and specification, because the modal did not post them and the service saves every field. The modal now shows the five fields, prefilled (a value that is no longer active stays selectable). Test: `MaterialItemEditKeepsFieldsTests` (every parameter of `Edit` has a field in the modal).
- SCRUM-548: help article results in the Ctrl+K palette and global search linked to `/Help/Article/{code}` (404); they now link to `/Help/Article?code=...`. Test: `SearchProviderUrlTests` checks the URL of every search provider against its action.
- SCRUM-539: an empty Projects form was saved as a project without code and name; code and name are now required on the server and in the browser (create and edit). Vendor create shows server-side errors in the validation box like the other create forms. Test: `ProjectAndVendorCreateValidationTests`. Open question for the owner: should the project code be unique?
- SCRUM-541: the active sidebar link had an accent bar on both edges. The marker is now one `border-inline-start` rule in site.css (left in LTR, right in RTL); admin.css, admin.rtl.css and the turquoise theme no longer set left or right borders, and admin.css uses the colour token. Test: `SidebarActiveMarkerTests`; `ThemeContrastTests` and `PremiumGlassSystemTests` follow the logical edge.

## 2026-11-08 - Deploy run shows why a site did not start (SCRUM-555, slice 1)

### Fixed
- When the staging or demo site answered 5xx after a deploy, the run log showed `sed: ... unknown option to `s'` instead of the error page text: the filter in `deploy-site.yml` used `/` as the sed delimiter while the pattern contains `</style>`. It now uses `#`, and the log step also lists the newest log file names, so a start that wrote no new log is visible. Test: `DeployWorkflowShellTests`. Staging itself still answers 500 (slice 2, needs the server logs in Plesk).

## 2026-11-07 - Related-document links open the right page (SCRUM-538)

### Fixed
- The related document on Letter, MOC and Technical Query details always linked to the engineering document page, so a vendor (VDR) document gave 404. The links now go to the new `DocumentRegister/Open/{id}`, which redirects by the document type: engineering document, VDR item, the contract of a contract document, or the work order (else the equipment) of a maintenance document. The target page checks the permissions as before. Tests: `DocumentLinksTests`.
## 2026-11-07 - Sprint 1 review QA, sections D7 and D8 fixes (SCRUM-549, 550, 551, 552, 553, 554)

### Fixed
- SCRUM-549: the last browser pop-ups are gone. The task board (moving a card to a status it cannot reach, a failed move or reorder), the rich-text editor (failed image upload) and the maintenance calendar fallback show a PEMS toast (`PemsErrors.show`). Test `NoBrowserPopupScriptTests` fails if any of our scripts or views calls `alert(`, `confirm(` or `prompt(`.
- SCRUM-550: deleting a letter tag that has sub-tags, or someone else's personal tag, reloaded the Tags page with the tag still there and no message. `ITagService.DeleteAsync` now returns why (`TagDeleteOutcome`) and the page shows it in Persian and English; the owner of a personal tag may delete it (the controller now passes the user). Tests: `TagDeleteOutcomeTests`.
- SCRUM-551: `<pems-stat-grid>` was a grid only inside admin pages; on Warehouse > MRS import > Review the tiles stacked one per row. `pems-ui.css` now gives the grid and the tile label/value to every page (4 columns, 2 below 992 px, 1 below 576 px; admin pages keep their own rules). The MRS import and table extraction review labels and help text come from resources. Test: `StatGridAndModalTextsTests`.
- SCRUM-552: English text on Persian pages: Letter (Reject, Sign / Acknowledge), Asset (Edit), Engineering Document (approval workflow, freeze, new revision, client response with its body), Maintenance master data (new strategy type, standard reference, activity, and four titles whose Persian value was still half English), Work order badges (status, priority, type) and its Reject confirm, Criticality score labels, and the Organization / Diagnostics / Replenishment page titles now use resource keys in both languages.
- SCRUM-553: the audit log wrote EF's temporary key for a foreign key that points at a row inserted in the same save (definition rollback: `PublishedVersionId: 2 -> -2147482645`). Such values, and the field list of a newly inserted security record, are now written after the save with the real id. Test `AuditTemporaryKeySqlTests` (SQL Server). README M-25 now says that create-draft and rollback need `If-Match`.
- SCRUM-554: adding a required material with an unknown or inactive material id to a work order answered 500 (foreign key); it now returns to the work order with a message. Test in `WorkOrderSectionGuardTests`.

## 2026-11-07 - Letters page: no more "1" confirm (SCRUM-537)

### Fixed
- On Letters (`/Letter`) most clicks opened a confirm dialog with the text "1" and were swallowed. `pems-cartable.js` armed every `[data-confirm-trash]` element with `data-pems-confirm`, which also matched the page container `#ctShell` (its `data-confirm-trash="1"` is the "confirm before trash" setting). It now arms only `button[data-confirm-trash]`. Test: `CartableWebTests.TrashConfirm_ArmsOnlyTheTrashButtons_NeverTheShell`.
- Follow-up 2026-11-17: row-level Trash and purge actions use `data-row-op`, not the bulk form's submit marker. The cartable now arms those real buttons from the document, so the trash confirmation remains available without making the page shell or Archive a confirmation target.

## 2026-11-07 - Script templates before the scripts (SCRUM-536)

### Fixed
- Every main-layout page threw `PemsTemplate: the page has no <template data-pems-template="sb-search">` and showed no sidebar search box: `pems-sidebar-search.js` starts as it loads, but the `shell` template set came after it (since SCRUM-511/512, kept by the SCRUM-527 restore); the global error handler then failed on `pems-error-toast` too. `_Layout` now renders the `global-search`, `layout` and `shell` sets before the first script, `_LayoutEmbed` its `shell` set too, and `_AuthLayout` loads `pems-template.js` (the login button's busy state threw `PemsTemplate is not defined`). Tests: `MainLayoutAssetsTests`.

## 2026-11-07 - Sprint 1 review QA, section B fixes (SCRUM-532, 533, 534, 535)

### Fixed
- SCRUM-532: `docker build` failed at `COPY . .` because the build context held the local graphify knowledge graph (2.6 GB). `.dockerignore` now also leaves out `graphify-out/`, `.claude/`, `.kilo/`, `**/logs/`, `tools/loadtest/results/` and `PEMS.Web/filestore/` (uploaded files are runtime data and are never baked into an image).
- SCRUM-533: 9 tests failed on `main`. The replenishment sidebar test host now registers PEMS.UI like `Program.cs` (the sidebar uses `<pems-icon>` since SCRUM-508), and the two permission view tests check the checked/disabled input itself instead of the old attribute order. The pages themselves were not changed.
- SCRUM-534: `.gitattributes` checks out `deploy/docker/*`, `deploy/tests/*.sh` and `deploy/tests/fakes/*` with LF, so a Windows checkout no longer carries CRLF to the Linux VM and the LF check passes. README "Local tests" no longer lists it as a known failure.
- SCRUM-535: both k6 scripts (`pems-api-baseline.js`, `pems-baseline.js`) set `noCookiesReset: true`. They log in once per virtual user, and k6 had cleared the login cookie before every iteration, so 97% of the API calls got 401. The passing run (0% errors, p95 15-27 ms) is in `tools/loadtest/results/api_baseline_summary.json`.
- Tests: `LinuxDeploymentArtifactsTests` (gitattributes, dockerignore, k6 cookie option).

## 2026-11-06 - VDR Files tab: upload (SCRUM-531)

### Added
- The VDR Files tab has an upload form: revision, kind (document, comment sheet, reply sheet) and file. New action `VDRController.UploadSubmissionFile` with the same rules as the other VDR uploads (upload policy, VDR Edit permission), and the submission must belong to the item. The file is stored in PEMS (`vdr/documents`, `vdr/comment-sheets`, `vdr/reply-sheets`), linked to the submission (a new file replaces the link of that kind; the old file stays stored), and the page opens on the Files tab with it in the preview. This is the first way to attach the main document of a VDR submission on the VDR page; before, it only came from AI Intake or the import. "Upload to FTP" keeps no copy in PEMS, which is why files sent that way never showed. Tests in `VdrNumberingAndFilesTests`.

## 2026-11-06 - VDR: number generation in Admin > Document numbering, Files tab with preview (SCRUM-529, 530)

### Changed
- SCRUM-529 (owner decision): the CONTRACTOR official number and SUB-CONTRACTOR vendor-format number forms left VDR Details > Overview. They now live in Admin > Document numbering, section "VDR document numbers": search an item (document number, description, vendor; 20 results), select it, and the same two forms appear, prefilled. They post to the same `VDRController.GenerateContractorNumber` / `GenerateVendorNumber` actions (same VDR Edit permission, same reference-data updates, same numbering service) with `returnTo=numbering`, so the user comes back to the Admin page. VDR details shows the vendor-format number in its info grid and, for a system administrator, a "Generate document numbers" link to the Admin section for that item.

### Added
- SCRUM-530: VDR details has a Files tab listing the main file, comment sheet and reply sheet of every submission (newest revision first) with Preview and Download. The preview uses the new PEMS.UI component `pems-file-preview` (registry key `file-preview`), which shows `Files/Preview/{id}` inline (SCRUM-202 rules: PDF, images and text only). `?tab=files&file={id}` opens the tab on a file. The comment-pin PDF viewer stays in the Comments tab.
- Tests: `VdrNumberingAndFilesTests` (8), `FilePreviewTests` (3).

## 2026-11-06 - Sprint 1 review fixes: lost layout lines and link buttons (SCRUM-526, 527, 528)

### Fixed
- SCRUM-526: the stage diagrams (Engineering Documents, VDR, WorkOrder) showed every stage on its own line. The merge 97e11c85 had dropped `pems-stage-diagram.css` from `_Layout.cshtml`; it is back.
- SCRUM-527: the notification bell stayed on "Loading..." and every `data-pems-click/change/submit` action did nothing on main-layout pages (call, back, print, check-all, toggles). The same merge had dropped `pems-actions.js` and `<pems-script-templates set="shell">` (confirm dialog and error toast templates); both are back. `MainLayoutAssetsTests` keeps these lines in the three layouts.
- SCRUM-528: Cancel, Open, Back and the other link buttons went nowhere. `<pems-button as="a" asp-action asp-route-*>` rendered no `href` since SCRUM-53, because the routing attributes bind to `PemsButtonTagHelper` (derived from the built-in anchor helper) and it never called `base.Process`. It does now when any routing attribute is set, which fixes 326 links. Tests: `ButtonTagHelperTests`.

## 2026-11-04 - Jalali date picker: pems-date (SCRUM-225 / #206, SCRUM-385 / #368 slice 1)

### Added
- Library search done first (guideline 03 section 3 step 0, new): adopted `@majidh1/jalalidatepicker` 1.0.0 (MIT, vanilla, no dependencies, 26 KB js + 8 KB css), vendored unmodified under `PEMS.Web/wwwroot/lib/jalalidatepicker` with its licence; rejected persian-datepicker (jQuery, 2022), jalali-moment (moment, 2023); conversion libraries (jalaali-js, date-fns-jalali) were not needed because the browser's own Persian calendar (`Intl`) and .NET's `PersianCalendar` agree on every year start 1300 to 1500 (fixture `PEMS.Tests/TestData/jalali-year-starts.json`, `JalaliDateTests`).
- `<pems-date>` (PEMS.UI): always posts the Gregorian ISO date; Persian pages show and pick Jalali dates, other pages keep the native date input. `pems-date.js` writes typed dates too (Persian/Arabic/Latin digits, `/ - .` separators), refuses impossible dates such as 1404/12/30, and falls back to the native input if the library does not load; `pems-date.css` maps the picker to the theme tokens (Light, Dark, Turquoise); `Date_Invalid` text in both languages.
- Tests: `DateTagHelperTests` (13), `JalaliDateTests`, and `node tools/js-tests/pems-date.test.js` (all 73,049 valid days of the Jalali years 1300 to 1500 convert correctly, 1,351 impossible days are rejected). Checked in a browser test page: typed entry, invalid and out-of-range dates, a real click on the picker, the dark theme, the fallback without the library.
- 84 hand-written `<input type="date">` in 47 views became `<pems-date>` (the page keeps every attribute; the same native input on English pages, the Jalali picker on Persian pages, the same ISO value posted). Skipped on purpose and still native (23 left in the ratchet): fields bound with `asp-for`, inputs a script reads by id or a data hook, inputs with handlers or Razor attribute expressions, and 15 tags the converter could not parse. Ratchet: new category `date-input` (107 at the start, 23 now); `text-input` 192 to 48 (the date inputs carried two `form-control` hits each).
- Not in this slice: a date with a time, `asp-for` fields, ranges and relative windows, public holidays and SLA, user time zones and display presets (SCRUM-385 open parts).

## 2026-11-04 - graphify hooks removed (SCRUM-504)

### Changed
- `.claude/settings.json` no longer runs `graphify hook-guard` before every Read, Glob, Grep and search-like Bash call: it added the same 330 to 450 characters (80 to 110 tokens) each time, 25 000 to 35 000 tokens in a long session, for a tool that helps only with orientation questions (a query costs 1 600 to 3 400 tokens). Chapter 09 has the measured costs, the "orientation only" rule and the install note; the root `CLAUDE.md` says the same in two lines.

## 2026-11-04 - Four more project skills (SCRUM-504)

### Added
- `.claude/skills`: `pems-localization` (English and Persian keys, component texts, the key coverage test), `pems-plan-sync` (plan rows and Jira in step), `pems-shared-modal` (one modal for many rows, `data-fill-*`, test registration), `pems-migration-guard` (schema rules, drift gate, when a session without SQL Server stops). Listed in chapter 09 and AGENTS.md.

## 2026-11-04 - Static inline styles become generated classes (SCRUM-347, part 2)

### Changed
- 198 static `style="..."` attributes in 128 views became classes of a generated stylesheet `pems-inline-styles.css` (one class per distinct declaration list, every declaration `!important` so the result equals the old inline style; linked by the three layouts). A proof script checked that each class reproduces its original declarations (198 of 198). Left inline on purpose (587 in 135 files): anything a script sets or clears (`display`, `visibility`, `transform`, `transition`, `box-shadow`, custom properties), widths and positions of table headers and of elements with an id or a data hook, component tags, Razor-driven styles, and two views that use `style.cssText`. A guard test (`ThemeSystemTests`) stops the count from rising.
- Still open in SCRUM-347: the 587 inline styles (each needs a browser check), 60 inline scripts, the Content-Security-Policy itself.

## 2026-11-04 - AI agent efficiency: token rules, project skills, trimmed instructions (SCRUM-504)

### Added
- Guideline chapter 09 (`docs/guidelines/09-ai-agents-and-token-efficiency.md`): what costs tokens with measured numbers (a Jira write echoes the whole issue, about 3k tokens; a search that asks for `parent` about 10k for 15 rows), rules for reading, batching, testing, Jira and reporting, the skill rules, the recommended developer tools, what the instruction files may hold.
- Project skills in `.claude/skills`: `pems-work-block`, `pems-test-gates`, `pems-jira-handover`, `pems-list-page`, `pems-errors` (one-line descriptions, procedures linked to the chapters). A `git pull` of `main` gives every agent the same set.

### Changed
- Root `CLAUDE.md` cut from 2.3 KB to 0.9 KB (the Zaemit tool list is in the tool schemas already; the graphify block says the same in fewer lines); `AGENTS.md` points to chapter 09 and the skills.
- The descriptions of the two skills kept (`graphify`, `ui-ux-pro-max`) are one line each (every description is loaded into every session).
- Removed (owner confirmed 2026-11-04): the third-party skills `banner-design`, `brand`, `design`, `design-system`, `slides` and `ui-styling` (generic marketing, slide and Tailwind/shadcn generators, about 960 KB, nothing in PEMS referred to them) and the stray `.claude/New Text Document.txt`; recoverable from git history. Kept: `graphify`, `ui-ux-pro-max`.

## 2026-11-04 - Test budget for sessions (guideline)

### Added
- `tools/test-gates/gates.py`: prints, from the changed files, the build, the UI tests, the guard classes and the exact test classes that mention the changed files (slow families never chosen), and what is left to the end-of-sprint QA.
- Guideline chapter 05 "Test budget" (measured times and the safe classes per kind of change), chapter 07 section 2b (run every test that fits, the very large suites are left to the end-of-sprint QA, each task keeping its own line d2 (nothing is combined between tasks); two minutes per command, exact class names, never a word or namespace filter, the slow families listed) and the standard not-run line d2 (the broad render suite and the guard classes not run); README "Local tests" note; learnings-log entry. Cause: a whole-namespace run took 11 minutes and a word filter pulled in the SQL families, to find what a one-second guard class finds.

## 2026-11-04 - Work blocks start with a pull and a whole-backlog audit (guideline)

### Added
- Guideline chapter 0, section 0 and AGENTS.md step 0: every go-ahead opens a work block: (0.1) pull `main`; (0.2) audit the whole open backlog against the current code with one verdict per issue (go, modify, split, merge, cancel with the owner's yes, blocked); (0.3) group the survivors into related batches, a blocker travels with the task it blocks, each safe slice merged at once, every issue started in Jira before its first commit; (0.4) state the plan in the first message, the go-ahead is its approval; (0.5) close the block with every issue in In Review or a stated reason. Includes the anti-patterns that happened.

## 2026-11-04 - Error taxonomy and one mapping table (SCRUM-464, slices 2 to 4)

### Added
- `PemsErrorKind` and the taxonomy exceptions in `PEMS.Application.Exceptions`; `PemsErrorMapper` (kind, status, stable code, SQL numbers 2601/2627, 1205, -2, connection errors, concurrency, timeouts) used by the API controller filter, a new `PemsExceptionHandler` (/api failures outside controllers) and the error page.
- The error page now shows the localised status page for the mapped status (404, 403, 409, 429, 503, 500) with the trace id and never the exception message; new texts for 409, 429 and 503 in both languages; 503 sends `Retry-After`. Table documented in guideline chapter 08.
- Still open in SCRUM-464: replacing the 131 generic `throw new Exception(` and the 89 user-visible `ex.Message` controller by controller, the start-up failure report (needs SCRUM-415).

## 2026-11-04 - Client error handling: one fetch wrapper and one toast (SCRUM-467, slice 1)

### Added
- `PEMS.UI/wwwroot/pems-errors.js` and `.css`, loaded by both layouts: `PemsFetch.request/json` (antiforgery header on unsafe verbs, timeout, abort when the page is left, problem+json and `X-Trace-Id` read, retry with backoff only for GET/HEAD on a network failure or 502/503/504, one toast per failed call with the localised message and the reference with a copy button), `PemsErrors.show`, and global capture of `window.onerror` / `unhandledrejection`. Texts come from the layout (`Err_*` keys in both languages). Tests in `ConfirmScriptTests`; behaviour smoke-tested in Node (retry, no retry on POST, token and trace id).
- Not yet in SCRUM-467: the existing 35 `fetch(` calls still use their own handling (they move onto the wrapper file by file), the report endpoint (needs the error store of SCRUM-465), the dynamic-page fallback (needs PageHost, SCRUM-438).

## 2026-11-04 - One trace id from browser to log (SCRUM-464, slice 1)

### Added
- `X-Trace-Id` on every response (the W3C trace id, or the request id when no trace is active), the same value as the `TraceId` property of every log line of the request, as `traceId` in every problem+json body, and as the reference on the error and status pages (it was the full Activity id before). Code in `PEMS.Web/Diagnostics/PemsTrace.cs`, tests in `TraceIdTests`.
- Next slices of SCRUM-464: exception taxonomy and mapping, IExceptionHandler, localised status pages, removing user-visible `ex.Message`.

## 2026-11-04 - Page style blocks move to stylesheets (SCRUM-347, part)

### Changed
- Six `<style>` blocks (navigation visibility index and effective user, admin user details, workflow designer, letter details, the embed layout) moved verbatim to stylesheets under `wwwroot/css` and are linked from the page `Styles` section; a test lists the only two allowed exceptions (the stage diagram partials, which write Razor values into their rules). The pdf.js CDN references were already gone.
- Still open in SCRUM-347: 765 static inline `style=` attributes in 213 views, 60 inline scripts (13 with Razor values), the Content-Security-Policy itself.

## 2026-11-04 - Design tokens: other themes and literal colours move to tokens.css (SCRUM-476, part)

### Changed
- The Turquoise, Ocean, Warm and Midnight theme blocks moved verbatim from `site.css` to the end of `PEMS.UI/wwwroot/tokens.css` (cascade checked: none of their properties is defined in `pems-ui.css` or later in `site.css`).
- The 169 literal hex colours of `site.css`, `pems-workspace.css`, `pems-themes.css` and `administration-design-system.css` became raw palette tokens `--pems-color-<hex>` declared once in `tokens.css` with the same value; substituting them back reproduces the old files exactly. A test keeps literal hex out of the four files.
- Fixed: five list-page texts added earlier today (Inspection and Test Plans, Intake Queue, No ITPs yet., No NCRs yet., No projects yet.) had no Persian entry (`LocalizationKeyCoverageTests`); added to both resource files.
- Not done yet: the physical left/right rules (needs RTL screenshots), the theme blocks in `admin.css`, `pems-glass.css`, `auth.css`, `pems-cartable.css`, and the before/after screenshots.

## 2026-11-04 - Projects are a list page, list search can filter live (SCRUM-337)

### Added
- `pems-list-search` option `live-filter="true"`: for a short list without server paging, typing filters the rows already on the page and the form is not submitted. Default behaviour unchanged. Test in `ListPageComponentTests`.

### Changed
- The projects list is built from `pems-list-page` using it (the old inline search script is gone); "New Project" opens the existing modal; the status colours became badge tones.

## 2026-11-04 - Tasks are a list page (SCRUM-337)

### Changed
- The task list is built from `pems-list-page`: the two tab buttons became the tab filter, the "new personal task" form moved from under the list into a toolbar popover (same fields and post), the task manager link is in the page menu, the status change forms stay in the rows.

## 2026-11-04 - My Work is a list page (SCRUM-337)

### Changed
- My Work (approvals) is built from `pems-list-page`: the chip row became module, personal and overdue filters with removable chips (the counts stay as KPI chips behind the toggle), notifications and action items moved to the page menu, the page-number row became the shared pager.

## 2026-11-04 - Classification review queue and AI extraction list are list pages (SCRUM-337)

### Changed
- The equipment classification review queue (status filter, accept/reject forms kept, shared pager instead of the page-number row) and the AI extraction list are built from `pems-list-page`.

## 2026-11-04 - Maintenance backlog and OCR processing are list pages (SCRUM-337)

### Changed
- The maintenance backlog (status, priority and overdue-only filters with chips) and the OCR processing list (status filter, count tiles kept above) are built from `pems-list-page`; the OCR status colours became badge tones.

## 2026-11-04 - Letter templates are a list page (SCRUM-337)

### Changed
- The letter template list is built from `pems-list-page` (help line = heading tooltip, "Correspondence" in the page menu, activate/deactivate form unchanged). VDR/MDR import pages are upload wizards with a batch table and stay as they are.

## 2026-11-04 - NCR, ITP and referred-letter lists are list pages (SCRUM-337)

### Changed
- QC NCR list, QC ITP list and "Letters referred to me" are built from `pems-list-page`; the NCR and ITP status colours (`bg-*` classes) became badge tones. Guideline: the cancel rule now uses the new Jira status **Cancelled**.

## 2026-11-04 - Re-audit before starting, and how to cancel (guideline)

### Added
- Guideline chapter 0, step 2a: re-audit a task against the current code before starting (go, modify, split, merge or cancel), work from each page as it is now. Cancel rule while Jira has no Won't Do status: comment with reason, label `wont-do`, Done, only with the owner's yes.

## 2026-11-04 - Intake queue is a list page (SCRUM-337)

### Changed
- The Intake queue is built from the `pems-list-page` component: the stage buttons became the stage filter with a removable chip, the intro text the heading tooltip, paging sits in the footer. Page test added to `CmmsListPagesTests`. The DCC page is a dashboard, not a list, and stays as it is.

## 2026-11-04 - Badges with a real tone (SCRUM-502)

### Changed
- The role-count badges of Navigation Visibility and the notification badge of `_Layout` use `tone` (success, neutral, danger) instead of `tone="none"` with `text-bg-*` classes. Two anchor badges (Organization issue count, Permission Matrix role chip) stay until `pems-badge` can be a link.

## 2026-11-04 - ImportCenter uses one shared modal (SCRUM-500)

### Changed
- The ImportCenter import dialog is one `icModal` filled from the opener (`data-fill-formaction`, `data-fill-accept`, title and hint); `pems-modal-fill.js` learned the two keys. The Employee edit modal stays one per row on purpose (its options depend on the row).

## 2026-11-04 - Meetings on the list-page component (SCRUM-337)

### Changed
- `Meeting/Index` is built from `<pems-list-page>`; Schedule Meeting opens the page modal; column resize and paging come from the component. Page test added.

## 2026-11-04 - Production orders and the procurement cartable on the list-page component (SCRUM-337)

### Changed
- `ProductionOrder/Index` and `PreProcurement/Index` are built from `<pems-list-page>` (status and source filters, search, chips, paging in the footer). Page tests added to `CmmsListPagesTests`.

## 2026-11-04 - Browser alert() calls replaced by the layout toast (SCRUM-502)

### Changed
- The 4 `alert()` calls of Warehouse MaterialItem/Details and the webauthn.js fallback use `showLiveToast`; the `alert-call` ratchet is now zero.

## 2026-11-04 - Merge rule made explicit (SCRUM-503)

### Changed
- Chapter 00 now calls a delayed merge a defect: merge once the quick gates pass; unrun tests go on the In Review checklist.

## 2026-11-03 - Adoption leftovers, second part: list-group rows, toasts, stacked progress, spinner attributes (SCRUM-502)

### Added
- `<pems-list-group-item>` (anchor routing attributes, `action`, `active`, `as`), `<pems-toast>`, `<pems-progress-segment>` (stacked bars, `ProgressOptions.Segments`), and `<pems-spinner>` now keeps `css`, `id` and aria attributes.

### Changed
- 5 list-group rows, the 2 toasts of `_Layout`, the ProjectIntelligence stacked bar and the Users permissions spinner moved. Still open in #502: the Pems skin of the small blocks (needs a design decision), the toast built in a script string (#333), `webauthn.js` `alert()` fallback (the auth layout has no dialog), the letter status helper (custom palette).

## 2026-11-03 - Department and SparePart edit modals are shared modals (SCRUM-500)

### Changed
- The Department and SparePart edit modals (one per row, every field prefilled) became one modal each; the opening button carries the row values as `data-fill-*` (`pems-modal-fill.js`). Left on purpose: the Employee edit modal (a position partial and department, discipline and vendor options that differ per row, plus a per-row inline script) and the per-entry import modals of ImportCenter.

## 2026-11-03 - Test policy for sessions written into the guideline (SCRUM-503)

### Changed
- Chapter 07 section 2b: what a session runs (build, PEMS.UI.Tests, the page tests of touched views) and what goes to the QA checklist as `not done` lines (SQL Server tests, the full suite, browser checks); only SQL-safe changes without SQL Server; a stalled command is not a failed job. Chapter 01 and chapter 05 (test recipes, recording flags, known red test) point to it; four learnings added to the log (bound attributes of derived tag helpers, boolean attributes and scope in tag helpers, ratchet scope, timeouts).

## 2026-11-03 - One shared modal per list for the row actions (SCRUM-500)

### Added
- `pems-modal-fill.js` (PEMS.UI): a Bootstrap modal is filled from the opening button (`data-fill-<field>` for form fields by name, `data-fill-text` for text) and its form is reset before each opening; loaded by both layouts.

### Changed
- Seven per-row modals became one shared modal each: VDR and engineering document comment replies, QC inspection, production run post input and post output, RCM decision apply, warehouse storage location. Left for a follow-up in SCRUM-500: the three edit modals with many prefilled fields (Department, Employee, SparePart) and the per-entry import modals of ImportCenter (each posts to its own action).

## 2026-11-03 - No browser confirm() is left; required fields before the question (SCRUM-499)

### Changed
- `pems-confirm.js`: `data-pems-require="field"` checks the named field of the button's form before asking (marks it `is-invalid`, focuses it), and a blank `data-pems-confirm` means no question.
- Work order and work request buttons declare their required comment and the Reject/Cancel question as attributes instead of an inline script; the cartable and the letter bulk form use `data-pems-confirm` (the attribute set by the cartable script and the Letter index views), their own `window.confirm` handlers are gone. Ratchet confirm-call 4 to 0. Lifecycle posts keep their fields: the reasons were already collected by the pages' own comment boxes and modals, so PemsConfirmDialog with a reason stays available for new flows.

## 2026-11-03 - Adoption leftovers: tone helpers, last alerts, validation summaries (SCRUM-502)

### Added
- `<pems-validation-summary>` (derives from the built-in helper), `<pems-alert>` keeps the other attributes of the tag and takes `tone="light"` and `tone="none"`.

### Changed
- Status helpers that only feed badges return tone names: `RcmDisplay` (consequence, status, criticality), `TaskManagerUi` (category, priority) and 43 view-local helpers; 22 alert blocks and 13 validation summaries moved; ratchet alert 22 to 0. Kept as class helpers: `LetterStatusBadge` (custom palette), 9 badges whose class is also used on another element. Still open in #502: list-group items, the Pems skin of the small blocks, the segmented progress bar, the JS-toggled spinner.

## 2026-11-03 - Every hand-written table is a `<pems-table>` (SCRUM-337)

### Added
- `<pems-table kind scale css>` frame helper (`FrameState.Table`): the table element and its classes, rows stay page markup, every other attribute kept.

### Changed
- 255 tables in 134 views (the views and the Admin and Warehouse area views) moved, class order preserved; ratchet table 257 to 1 (`LetterCartable/Report`, class `ct-table`, kept). Record lists keep the pems-list family; moving cell-heavy tables to descriptor-driven PemsTable is the next step of SCRUM-337.

## 2026-11-03 - Decision drafts for the owner: main host platform, module catalogue, price list structure (SCRUM-305, SCRUM-132, SCRUM-28)

### Added
- `plan/adr/0012-main-host-platform.md` (Proposed): one artifact, Windows/IIS for the shared main host, Linux with Docker Compose and Caddy as the second supported install, SQL Server per environment; says why supporting both is possible and when it is worthwhile.
- `docs/commercial/MODULE_CATALOGUE_DRAFT.md` (draft): core parts, 12 module keys with dependencies, three editions, all marked proposed.
- `docs/commercial/PRICE_LIST_V1.md` and `PROPOSAL_TEMPLATE.md` (Persian and English): structure with every number as a placeholder. No price, edition or platform is decided by these files.

## 2026-11-03 - Replan of the component adoption work (SCRUM-499 to SCRUM-503)

### Added
- Plan rows and Jira issues for the remainders of SCRUM-53, 481, 482, 483 and 487: #499 lifecycle confirms with a required reason, #500 one shared modal per list, #501 field groups through pems-field and the date inputs, #502 adoption leftovers (tone helpers, alerts, small blocks), #503 browser QA pass for the batches. The finished parts of the original issues stay In Review for QA.

## 2026-11-03 - TreeView wave T2: the help tree (SCRUM-337)

### Changed
- The help tree (decision trees and entity relationships in help articles, `_HelpNodeTree`) is a `<pems-tree>`: its branches open and close and the keyboard moves through it. One recursive local function replaces the per-node partial `_HelpNodeTreeItem` (removed): a nested tree item has to run inside its parent to count as its child. The inline `<style>` block moved to `site.css` (only the edge label).

### Not moved, on purpose
- The navigation visibility editor (Administration > Navigation visibility) stays a `<details>` disclosure: every node holds role forms and buttons, and a WAI-ARIA tree item may not hold controls.

## 2026-11-03 - TreeView wave T1: one tree (SCRUM-337)

### Added
- `<pems-tree label="..." toggle-label="..." remember-key="...">` with nested `<pems-tree-item label key href current count expanded link-class>` and an optional `<pems-tree-label>` (the page's own label markup), in PEMS.UI: a WAI-ARIA tree (`tree` / `treeitem` / `group`), a named toggle per branch, and `pems-tree.js` (loaded by both layouts): expand / collapse, the collapsed branches remembered per tree in the browser (a branch holding the current item is never collapsed), and the tree keyboard (one tab stop; Up / Down, Right / Left mirrored in Persian, Home / End, Enter follows the link). Styles in `pems-ui.css`.

### Changed
- The organization positions tree (Administration > Organization > Tree) opens and closes its branches and is keyboard navigable (it was a static list).
- The correspondence tag tree is built from `<pems-tree>`; its open / close code left `pems-cartable.js` and the same storage key keeps what a user had collapsed.

## 2026-11-02 - Chart wave C4: no hand-written charts come back (SCRUM-337)

### Added
- Adoption ratchet pattern `chart`: `new Chart(`, a hand-written `data-bk-chart=` host or a chart `<canvas>` in a view or a page script. The baseline is zero, so a new hand-written chart fails the build; use `<pems-chart spec="...">`.

## 2026-11-02 - Chart wave C3: the home donut; one chart engine (SCRUM-337)

### Changed
- The home dashboard's "Documents by status" donut is a `<pems-chart>` (`BkChartSpec.Donut`, the Bklit donut with its hover-linked legend and the total in the centre) instead of its own partial and script. Every chart in the product now goes through `<pems-chart>`.

### Removed
- `_DonutChart`, `DonutChartViewModel`, `pems-donut.js`, `pems-donut.css` and `pems-charts.js` (its colours are the `--pems-chart-*` tokens `BkChartSpec` already uses).

## 2026-11-02 - Chart wave C2: line charts; Chart.js retired (SCRUM-337)

### Added
- A `line` kind for `<pems-chart>`: `BkChartSpec.Line(ariaLabel, emptyText, times, decimals, series...)` (labels are invariant ISO times) and a `LineView` in the Bklit island (`frontend/dashboards/src/islands/ChartIsland.tsx`, the library's `LineChart`); a zero reading is a value, only no readings is empty. The bundle `dist/dashboards/dashboards.js` is rebuilt (`npm run build`, offline).

### Changed
- The condition monitoring trend page (Reliability > Trend) draws its readings with `<pems-chart>` instead of a canvas and Chart.js; the inline chart script is gone.

### Removed
- The vendored Chart.js (`wwwroot/lib/chartjs`, versions 4.4.1 and 4.4.4): no page uses it any more. `pems-charts.js` stays until the home donut moves (C3).

## 2026-11-02 - Chart wave C1: one chart host (SCRUM-337)

### Added
- `<pems-chart spec="..." />` in PEMS.UI: the host of a Bklit chart (`data-bk-chart`, drawn by `dist/dashboards/dashboards.js`) and its visually hidden table (or line, for a gauge) with the same numbers, the markup the `_BkChart` partial rendered. `BkChartSpec` / `BkSeries` moved to PEMS.UI.Abstractions (namespace kept, no page or controller changed).

### Changed
- The five report pages, the maintenance KPI page, the letter cartable report and the part catalogue reports take their 31 charts from `<pems-chart>`. They look the same.

### Removed
- The `_BkChart` partial.

## 2026-11-02 - Confirm and alert dialog; the Admin and Warehouse area views adopted (SCRUM-483, SCRUM-481, SCRUM-482, SCRUM-487)

### Added
- `pems-confirm.js` (PEMS.UI): one shared native dialog for `data-pems-confirm="text"` (button, link, form; tone, title and live-count options) and `window.PemsDialog.confirm / alert` for scripts; layouts carry the texts (`Confirm_Title`, `Confirm_Ok`, `Confirm_Cancel`, fa and en). `ConfirmScriptTests` checks the contract.

### Changed
- 93 browser `confirm(` calls reduced to 4 (two are validated first by their own handler, two belong to the letter bulk form); 4 `alert(` calls in the work order view use the dialog.
- **The 84 area views (Admin, Warehouse) were never converted and never counted by the ratchet.** Converted now: 58 alerts, 60 badges, 145 buttons, 339 controls, 237 labels, 13 modals, 11 tab strips; the ratchet scans `PEMS.Web/Areas` too (its baselines now include what is left there: tables 52, date and conditional controls, a few alerts and badges).

## 2026-11-02 - Labels, modal frames, dropdowns, tabs and breadcrumbs; library-first protocol (SCRUM-482, SCRUM-487)

### Added
- `pems-label`, `pems-modal` and `pems-offcanvas` (frame mode of `PemsModal` / `PemsDrawer`: `ModalOptions.Frame`, `DrawerOptions.Frame`, `OverlaySize.ExtraLarge`), `pems-dropdown-menu`, `pems-nav`, `pems-crumb-trail` (`FrameState`) in `PEMS.UI`.
- `GuidelineRegistryTests`: every `pems-*` tag helper and every registered component key must be listed in chapter 02 of the guideline.

### Changed
- 945 form labels, 99 modals, 3 side panels, 16 dropdown menus, 9 tab strips and 1 breadcrumb in the views moved; ratchet: form-label 946 to 1, modal 99 to 0, dropdown 16 to 0, tabs 8 to 0, breadcrumb 1 to 0, offcanvas 2 to 0. Modals whose form was the `modal-content` itself (4) now put the form inside the frame.
- Guideline chapter 02: the library-first protocol for agents (search, modify safely, add to the library first, never hand-write), the registry table rebuilt as one table with current status; AGENTS.md rule and the PR template line strengthened.
## 2026-11-02 - Skeleton wave S3: no "Loading..." placeholders come back (SCRUM-337)

### Added
- Adoption ratchet pattern `loading-text`: "Loading...", "در حال بارگذاری" and "در حال جستجو" written as a placeholder in a view or a page script. A skeleton's own `label="..."` is not counted. The baseline is zero, so any new loading text fails the build; use `<pems-skeleton>` or `PemsSkeleton.show`.

### Removed
- The organization chart's unused `data-loading-text` attribute.

## 2026-11-02 - Skeleton wave S2: panels that load content (SCRUM-337)

### Changed
- The organization chart shows a card skeleton instead of a spinner while it loads; the correspondence cartable shows skeleton rows while a thread's replies load; the letter composer's distribution-group menu, the part catalogue duplicate search and check, and the technical class description show a skeleton while their data loads (the result panel used to stay empty).
- The panels that only save (user permissions, permission matrix, task manager, notifications, maintenance calendar) keep their busy spinner: a skeleton stands for content that is coming, not for a save.

## 2026-11-02 - Skeleton wave S1: one loading placeholder (SCRUM-337)

### Added
- `<pems-skeleton kind="text|title|card|kpi|table|form" />` (Razor) and `PemsSkeleton.show(element, kind)` / `PemsSkeleton.html(kind)` (`pems-skeleton.js`, loaded by both layouts): the grey placeholder a part of the page shows while its data loads. Both build the same markup as the Blazor `PemsSkeleton`, which now takes the same `Kind`, `Rows` and `Cols`. A status region (`role="status"`, `aria-busy`) with "Loading..." for screen readers in the page language (`<body data-pems-skeleton-label>`); the pulse stops when the user asks for reduced motion.

### Changed
- The help drawer, the global search, the letter preview and the position org chart show the skeleton while they load, instead of "Loading..." text. Global search no longer shows Persian text in the English interface.

### Removed
- The unused skeleton styles in `pems-glass.css`; the styles live in `pems-ui.css`.

## 2026-11-02 - KpiTile wave K4: dashboards (SCRUM-337)

### Changed
- Inventory, AI insights (delays), DCC, equipment classification, QC and QC KPIs, the relationship gap report, the CBM dashboard, reliability data quality and the availability simulation take their 41 number tiles from `<pems-kpi-tile>` instead of a large bold number over a muted label. Coloured numbers keep their colour through `tone`; a label that had an icon in front of it (DCC, QC) shows the icon in the tile's coloured square; CBM keeps its second line as `sub`; tiles inside a card row (equipment classification, data quality) are compact.
- The DCC "Relationship Explorer" card stays as it is: it is a link, not a number.

## 2026-11-02 - KpiTile wave K3: detail-page tiles (SCRUM-337)

### Added
- `<pems-kpi-tile compact>`: a small tile for a detail page (tighter, 18px number, no lift on hover), and `tone="danger|success|warning"` to colour the value.

### Changed
- Asset details (status, criticality, open work orders, total cost), reliability details (MTBF, MTTR, availability, failure count), replenishment details (usable with on-hand minus reserved under it, gross demand, covered, shortage) and the production run reconciliation (input, output, loss, difference in red or green) take their 19 tiles from the component. The number now sits over its label like every other tile; empty values show a dash instead of "N/A".

## 2026-11-02 - KpiTile wave K2: the other kpi-card pages, `_KpiCard` retired (SCRUM-337)

### Changed
- Discipline details, the engineering dashboard (health score, first-pass approval), engineering document intelligence and OCR processing take their 16 tiles from `<pems-kpi-tile>`.
- `<pems-kpi-tile model="...">` takes a `KpiCardViewModel` built in code (title, count with a dash for zero, icon, colour, link, hint; an attribute on the tag wins). The home dashboard, the engineering dashboard summary, the production dashboard (now a list in a code block) and the part catalogue reports use it.

### Removed
- The `_KpiCard` partial and its two golden-markup tests; `KpiCardViewModel` stays as the data of a tile built in code.

## 2026-11-02 - KpiTile wave K1: report pages (SCRUM-337)

### Added
- `<pems-kpi-tile label="..." icon="bi-..." color="green">value</pems-kpi-tile>` in PEMS.UI: one number with its label, the same kpi-card markup as the `_KpiCard` partial. The value is the page's own text (counts, decimals, units); an empty value shows a dash; `href` makes the tile a link, `hint` its tool tip, `sub` adds a line under the label. Adoption ratchet pattern `kpi-tile` (hand-written `class="kpi-card"`).

### Changed
- The six report pages (correspondence, CMMS, engineering, operations, procurement, VDR) take their 40 tiles from the component. The look stays the same; the icons are hidden from screen readers.

## 2026-11-02 - InfoGrid wave I4: CMMS and transmittal headers (SCRUM-337)

### Changed
- Job plan, maintenance strategy, permit, work order, work request and transmittal details take their header blocks from `<pems-info-grid>` (6 grids, 56 items). Labels are localised keys without a trailing colon; 13 new keys (`Info_*`, English and Persian) replace hard-coded English labels such as "Permit Issuer:", "Started:", "From Plan:". Empty values show a dash instead of "N/A"; the permit "Validity" became valid from / valid to. On the work request the emergency, HSE and risk badges and the priority rationale sit over the grid.

### Not moved, on purpose
- Asset, reliability and production run details show quantity tiles (KpiTile, later); the letter list cards keep their one-line meta text.

## 2026-11-01 - InfoGrid wave I3: procurement and warehouse headers (SCRUM-337)

### Changed
- Purchase order, contract (and its procurement origin), tender, material requisition and material item details take their header blocks from `<pems-info-grid>` (7 grids, 43 items): the label over the value in the same grid as the other detail pages, instead of a bold "Label:" before the value or a muted label in a Bootstrap column. Labels are localised keys without a trailing colon (the hard-coded English "Contract No:", "MR Number:", "Equipment Tag:" ... are now localised); the contract's "Start / End" becomes two items; numbers, codes and dates read left to right in Persian.

## 2026-11-01 - InfoGrid wave I2: description lists of detail pages (SCRUM-337)

### Added
- `<pems-info-grid layout="rows">`: each label beside its value (side panels, long technical lists), label over value again at phone width. `value-class` keeps a page's own hook on a value.

### Changed
- Part catalogue request, pre-procurement, purchase order and task details take their description lists from the component (6 grids, 54 items). The page styles `pc-dl` and `tm-dl` are removed. An empty value now shows a dash.

## 2026-11-01 - InfoGrid wave I1: the label / value block of detail pages (SCRUM-337)

### Added
- `<pems-info-grid>` / `<pems-info-item label="...">` in PEMS.UI: the label / value block at the top of a detail page, written as a description list (screen readers read each label with its value) in the responsive grid the pages already used. Options: `wide`, `num` (numbers, codes and dates in a fixed-width left-to-right run), `mono`, `ltr` (phone numbers), `tone="danger"`; an empty value shows a dash. Styles in `pems-ui.css`, loaded by the layout.
- Adoption ratchet pattern `info-grid` (hand-written `vs-info__label` and `<dt>`), so the hand-written form cannot come back.

### Changed
- User, engineering document, equipment, VDR and vendor details take their label / value blocks from the component (8 grids, 49 items). The look stays the same.

## 2026-11-01 - RelatedList wave R3d: work order details (SCRUM-337)

### Changed
- Work order details: its five tables come from `<pems-related-table>`: workflow history, labour, spare parts used, tasks and required materials. The page keeps its tabs, its actions, its forms and the height-limited scroll areas around the labour and spare parts tables. This finishes the RelatedList waves for the heavy detail pages.

## 2026-11-01 - RelatedList wave R3c: VDR details (SCRUM-337)

### Changed
- VDR document details: its four tables come from `<pems-related-table>`: submission history, comment management, workflow history and related correspondence. The page keeps its tabs, its actions and its forms.

## 2026-11-01 - RelatedList wave R3b: permit details (SCRUM-337)

### Changed
- Permit details: its seven tables come from `<pems-related-table>`: execution gate checks, approval steps, LOTO points, isolations, SIMOPS conflicts and links, and status history. The page keeps its sections, its actions and its forms.

## 2026-11-01 - RelatedList wave R3a: part catalogue request and RCM / FMEA class (SCRUM-337)

### Changed
- Part catalogue request (edit): the equipment, attribute and note tables come from `<pems-related-table>`; removing an equipment line uses `<pems-related-remove>`. The page keeps its own sections, the sticky table headers and the attribute script (it finds the table by its `id`).
- RCM / FMEA class detail: the functional failure, FMEA item and RCM decision tables come from `<pems-related-table>`.

## 2026-11-02 - Bare form controls: `pems-input`, `pems-select`, `pems-textarea` (SCRUM-487, #487)

### Added
- `pems-input`, `pems-select`, `pems-textarea` tag helpers and `ControlState` in `PEMS.UI`: the Bootstrap control classes (size, `kind="check"`, `css`) with every other attribute kept; `asp-for` / `asp-items` work because they derive from the built-in helpers.

### Changed
- 1271 inputs, selects and textareas in 177 views moved; ratchet: text-input 1373 to 169, select 706 to 0, textarea 120 to 0, check-radio-switch 75 to 34 (what is left: date and time inputs, controls with a conditional attribute, `form-switch` wrappers).
- Page tests no longer depend on attribute order (class first). `LetterPhase2ViewTests` follows the progress bar markup of the 2026-11-01 batch (it was not run then).

## 2026-11-01 - Sprint membership is set and verified when a batch starts (SCRUM-479)

### Changed
- Guideline chapter 0 and AGENTS.md: moving an issue to In Progress does not put it in the sprint; set the sprint field (`customfield_10020`) in the same step and verify with `sprint in openSprints()`. The session sweep also lists In Progress issues with no sprint. Found when eight issues of the component batch sat outside Sprint 1.

## 2026-11-01 - The rest of the buttons in the views (SCRUM-53, #31)

### Changed
- `<pems-button>` takes `tone="none"` (the page passes the class), `disabled="@(cond)"` and `as="label"`/`as="span"`; `ButtonOptions.Tone` is nullable. 91 more buttons in 49 views moved; the button ratchet is down to the 4 buttons built inside JavaScript strings in `_ContextualHelpDrawer`.

## 2026-11-01 - Card, progress, accordion, list-group and spinner components (SCRUM-53, #31)

### Added
- `PemsCard`, `PemsProgress`, `PemsAccordion`/`PemsAccordionItem`, `PemsListGroup`, `PemsSpinner` in `PEMS.UI` (options records, manifests, samples, tag helpers `pems-card`, `pems-progress`, `pems-accordion`, `pems-accordion-item`/`-header`, `pems-list-group`, `pems-spinner`) writing the Bootstrap markup the pages wrote by hand.

### Changed
- All 25 hand-written blocks in 14 views moved; ratchet baselines for card, progress, accordion, list-group and spinner are now empty (the accordion-item counter no longer matches `pems-accordion-item`). `Letter/Dashboard` `BarRow` became async because it contains a tag helper.

## 2026-11-01 - RelatedList wave R2: tender, procurement package and receipt details (SCRUM-337)

### Changed
- **Tender details:** invited vendors, invitations, bids and clarifications are related sections.
- **Procurement package details:** MRs, quotations, communications and approvals are related sections. Its English-only headings, empty texts and buttons now read in the language.
- **Incoming receipt (IRN) details:** lines, disposition records and status history are related sections; so are receiving from the PO and recording a disposition.
- **Inside the sections:** add forms use `<pems-related-add>` where they are simple and keep their layout otherwise. Every table comes from the component, and the buttons are `pems-button`. Confirmations read in the language.
- 16 texts in English and Persian. The header workflow buttons of the receipt are not changed (SCRUM-53 / SCRUM-483).

## 2026-11-01 - RelatedList: child collections of a detail page (SCRUM-337)

### Added
- **PEMS.UI RelatedList tag helpers:**
  - `<pems-related-list>`: a section with icon, title, count and empty text.
  - `<pems-related-table>`: the child table; it keeps the page's `id` and `data-*` hooks.
  - `<pems-related-add>`: the add form under the table, with the parent key and the antiforgery token.
  - `<pems-related-remove>`: the delete form of one row, with the row and parent keys and an optional confirmation.
  - `<pems-related-row-form>` + `<pems-related-save>`: a row edited in place; the inputs point at the hidden form with `form="..."`.
  - Styles `lp-rel-*` in `pems-list.css`.

### Changed
- JobPlan details: the task and material lists are related lists, with labelled add fields.
- Administration: Numbering, Routing rules and Contract approval authority use the list frame. Each row is edited in place through the component. 8 texts in English and Persian.

## 2026-11-01 - Contract creation SQL tests pass again (SCRUM-498)

### Fixed
- `ContractCreationAtomicitySqlTests` T12 and T13 failed with Forbid: the test user had no user name, so the tender creator separation (P0-10) could not verify the approver and failed closed. The test user now has its name; the product behaviour is unchanged.

## 2026-11-01 - List page component, wave 4b: Administration settings lists (SCRUM-337)

### Changed
- SoD policies, storage providers, storage routes, workflow definitions, help articles and the PO authority table are built from the list-page component; 32 texts in English and Persian (headings, notes, empty texts, provider health, confirmations, publish / unpublish).
- The storage provider count cards are the KPI row; the help filters are chips; the PO authority rule form opens from the toolbar (`pems-list-popover`). Row actions stay.
- Not converted: contract approval authority, numbering and routing rules edit their rows in place; they move with the RelatedList / DataTable row-form mode.

## 2026-10-31 - List page component, wave 4a: users, roles and base data (SCRUM-337)

### Changed
- Users, Roles, Departments, Disciplines, Employees, the Audit Log, Security Events and the Authorization Audit are built from the list-page component; 68 texts in English and Persian (headings, notes, columns, empty texts, statuses, confirmations, audit actions, personnel types).
- Filter rows moved into the filters dropdown, each active filter (also a user opened from another page) is a chip, and paging keeps the filters. The user confirmations read in the language. Row actions, the reset-administrator link and the create / edit modals stay.
- The Admin area loads the PEMS.UI tag helpers from its `_ViewImports`. The adoption baseline is lower for these views. The settings tables of Administration (approval authorities, numbering, SoD, storage, routing, workflow, help) follow in wave 4b.

## 2026-10-31 - List page component, wave 3: procurement and warehouse lists (SCRUM-337)

### Changed
- Purchase Orders, Tenders, Material Requisitions, Vendors, Contracts, Procurement Packages, Inventory Transactions, Store Requisitions, Incoming Receipts (IRN), Replenishment, the Material Master and Part Catalogue Requests are built from the list-page component; 145 texts in English and Persian (headings, columns, empty texts, notes, and the statuses and transaction types that were shown as stored values).
- Filter rows, status selects and the Part Catalogue side panel moved into the filters dropdown (`<pems-list-filter-select>`), each active filter is a chip, and paging keeps the filters (the Material Master pager lost "low stock" and "critical"). A tender list opened from a package shows the package as a chip and offers New for that package.
- The warehouse views load the PEMS.UI tag helpers from the area's `_ViewImports`. The create modals stay until SCRUM-482. The adoption baseline is lower for these views.

## 2026-10-31 - Badge completed in the views (SCRUM-53, #31)

### Changed
- `<pems-badge>` takes `tone="none"` (the page passes the classes), a runtime tone (`tone="@x"`) and the other attributes of the tag; 258 more badges in 106 views moved, the `text-bg-*` view helpers now return tone names, the badge ratchet is down to the 4 toast lines in `_Layout` (the counter no longer counts `bi-person-badge` icons).

## 2026-10-31 - `<pems-button>` takes `asp-*` routing (SCRUM-53, #31)

### Changed
- `<pems-button as="a" asp-controller asp-action asp-route-*>` builds its href through the built-in anchor helper (the tag helper derives from it); 291 more links in 147 views moved; the button ratchet fell again.

## 2026-10-31 - `<pems-button>` and the buttons in the views (SCRUM-53, #31)

### Added
- `<pems-button tone size outline css as>text</pems-button>`: writes a `button` (or an `a` with `as="a"`) with the classes from `ButtonState`, keeps the page's own attributes (type, id, href, data-*, onclick) and content. Tones neutral, primary, danger, success, warning, info, light, dark; sizes small, medium, large.

### Changed
- 741 static buttons and links in 181 views now use `<pems-button>` with the same classes; the button ratchet fell from 1126 to 385. Not moved: tags with `asp-*` attributes (the built-in anchor helper does not run on a renamed element), tags with Razor expressions in the start tag, buttons inside `<script>` strings, solid `btn-primary`, `btn-link`, and other special forms.

## 2026-10-31 - Badge and Button skin, `<pems-badge>` (SCRUM-53, #31)

### Added
- `BadgeOptions` and `ButtonOptions` get `Skin` (default Bootstrap: today's `badge bg-*` / `btn btn-*` markup; Pems: the library classes) and `CssClass` (layout helpers such as `ms-1`, `w-100`). `ButtonOptions.Outline` writes `btn-outline-*`. `Tone` gains Info, Light and Dark (no Pems-skin colour yet).
- `<pems-badge tone="warning" css="ms-1">text</pems-badge>`: tones neutral, primary, danger, success, warning, info, light, dark.

### Changed
- 265 static `<span class="badge bg-*">` blocks in 82 views now use `<pems-badge>` with the same output; the ratchet baseline for badges fell from 689 to 405. Badges with computed classes (`@badgeClass`) and `text-bg-*` stay for the next batch.
- The button and badge golden manifests were re-recorded; the empty and error states keep the Pems skin for their button.

## 2026-10-31 - List page component, wave 2b: the other CMMS lists (SCRUM-337)

### Added
- `open="true"` on `<pems-list-kpis>` and `<pems-list-kpi-toggle>`: the KPI row shows first until the viewer hides it.
- `<pems-list-filter-select id name label value all-text items>`: one select in the filters dropdown, so a page no longer writes the select by hand (SCRUM-341 ratchet).
- `<pems-list-popover id icon label primary>`: a toolbar tool that opens a dropdown panel with the page's own short form (no modal).

### Changed
- Criticality Assessments, Equipment Criticality, Mechanical Equipment, Failure Mode Catalogue, Shift Handovers, RCM / FMEA coverage and the Equipment / Functional Location assignment batches are built from the list-page component; 88 texts in English and Persian. Paging on Criticality Assessments now keeps the filters. The assignment upload opens from the toolbar's Upload button. The adoption baseline is lower for these seven views (badges, buttons, tables, inline scripts).

## 2026-10-31 - Organization import saves again (SCRUM-497)

### Fixed
- **Administration > Organization > Excel import:** committing a valid file failed with "Nothing was saved: The configured execution strategy 'SqlServerRetryingExecutionStrategy' does not support user-initiated transactions". The save now runs inside the SQL Server retry strategy, like the other services. New `OrganizationImportSqlServerTests` (real SQL Server with `EnableRetryOnFailure`); guideline 01 section 3 states the rule.

## 2026-10-31 - Adoption ratchet (SCRUM-341)

### Added
- **PEMS.UI.Tests/AdoptionRatchetTests** and **tools/UiAudit/adoption-baseline.json**: per-view and per-script counts of 22 hand-written patterns (buttons 1148, text inputs 1379, badges 689, selects 716, tables 248 ...). The test fails when a count rises or a new file brings a pattern, and when a count falls without lowering the baseline. Pull request template and guideline 02 updated.

## 2026-10-31 - Alerts come from the component (SCRUM-481)

### Changed
- **PEMS.Web views:** 133 hand-written `alert alert-*` blocks in 67 views are now `<pems-alert tone=... css=...>` (new tag helper in PEMS.UI); the markup is the same, with a `role` added. `PemsAlert` takes page markup as content, extra classes (`CssClass`) and the tones primary and secondary.

## 2026-10-31 - Part Catalogue reports (SCRUM-478)

Owner request 2026-10-31: reports of the Part Catalogue for any period, with an Excel download. Everyone who may view the Part Catalogue sees them; the rows follow the request list's visibility rule.

### Added
- **Warehouse > Part Catalogue reports** (`PartCatalogueReport`). The page uses the list component and has six tabs. Each tab is one table and one Excel sheet:
  - **Summary:** KPIs and charts.
  - **By person:** who created, submitted, approved, returned or cancelled requests, and who registered IFS part numbers.
  - **Activity:** every action with its request.
  - **By equipment:** requests, registered part numbers, in progress and critical.
  - **Parts per equipment:** manufacturer, part numbers, supplier, class, unit, installed quantity, critical, specifications.
  - **Stages:** counts by status and by approval step, age of open requests, completed, average and median days to complete.
- **Period:** this month, last month, this quarter, this year, all time, or a custom range (both dates included). There are also filters for section, type, status, person and equipment.
- **Excel:** the Summary tab downloads the whole workbook; the other tabs download their own sheet. Headers follow the language, and the sheets are right to left in Persian.
- **Links:** both sidebars (Warehouse) and the report hub.
- **Accounts without an employee record** (for example a system admin) can open the reports. With Admin, Review or Approve they see every request; otherwise they see nothing.

### Changed
- The request list and the reports share one visibility rule (`PartCatalogueRequestScope`). The list behaves as before.

## 2026-10-31 - Agents run the issue-creation check themselves until SCRUM-475 (SCRUM-475)

### Changed
- Chapter 0 section 5d and AGENTS.md: until the nightly `tracker-check` runs, an agent reads each issue back after creating it and sweeps the open issues from SCRUM-400 on (parent epic, `[#N]` title, labels, `qa-pending`) at the start of a session and before any In Review, fixes metadata findings and reports the rest.

## 2026-10-31 - PEMS.UI components render today's look (SCRUM-480)

### Changed
- **PEMS.UI:** alert, modal, drawer, confirm-dialog, tabs and breadcrumb take `Skin`; the default `Bootstrap` renders the Bootstrap markup the pages use now, `Pems` keeps the library classes and native dialog. `pems-overlay.js` copies the row key of a Bootstrap modal opener.
- **Layouts** (`_Layout`, `_LayoutEmbed`, `_AuthLayout`): load `pems-ui.css`, `pems-overlay.js` and `pems-form.js` from PEMS.UI.

## 2026-10-31 - List page component, wave 2: CMMS (SCRUM-337)

### Added
- `<pems-list-new modal="...">` opens the page's own create modal (a button instead of a link); `<pems-list-export label="...">` names an export that is not the filtered list. A page's TempData message can sit inside the full-height list page.

### Changed
- Work Orders, Work Requests, Equipment, Functional Locations, Instruments, Letter Tags, Maintenance Plans, Maintenance Strategies, Job Plans, Spare Parts, Permits, Inspections, Findings and Assets are built from the list-page component. Each page keeps its filters, columns, modals, row actions and permissions. Count cards became KPI chips on demand; status button rows (Work Requests, Permits) became the status filter, and "pending my approval" a switch in it; page notes became the title tool tip; coverage, the failure-mode catalogue and bulk assignment moved into the strategies' "more" menu; status badges became tone pills.
- The English and Persian texts written in these pages (headings, columns, empty texts, notes, confirmations and work-order, work-request, permit, finding, instrument and asset statuses, priorities) are resource keys in both languages now (prefixes `Wo_`, `Wr_`, `Tg_`, `Ins_`, `Fn_`, `Mp_`, `Jp_`, `Pt_`, `Eq_`, `Fl_`, `In_`, `Ms_`, `Sp_`, `As_`, `Pri_`).

## 2026-10-31 - Login page in one language (SCRUM-477)

### Fixed
- **Login page:** the page was partly hard-coded. With the Persian language cookie, only the link "Use a recovery code" was Persian among English text, and the sign-in errors were always Persian. Every text of the login page now comes from SharedResource in both languages: the title, hints, Remember me, Sign In, the loading text, Forgot password, the validation messages and the sign-in errors.
- **Sign-in layout** (`_AuthLayout`): now takes `lang`, `dir` and the Bootstrap RTL stylesheet from the current language, so the Persian sign-in pages are right to left. The brand line and the footer are translated.

### Changed
- **Validation messages and `[Display]` names** of view models are now looked up in SharedResource (`AddDataAnnotationsLocalization` provider). A text without a key is shown as written, as before.

## 2026-10-30 - One guard for every upload; sandboxed preview (SCRUM-202)

### Added
- `IUploadGuard` (PEMS.Application/Uploads): every uploaded file is checked against the action's profile (Attachment, Image, Spreadsheet, Csv, Pdf, LicenceFile) - extension allowlist, declared content type, first bytes, size, then the malware-scan hook. Executables, scripts, HTML and SVG are never accepted.
- A global MVC filter runs the guard before model binding for all 34 upload actions (each carries `[UploadPolicy(...)]`); files sent to an action without a policy are refused. A refused page post returns to the page with the message; fetch/AJAX gets 400 JSON, `/api` a problem.
- Malware-scan hook `IMalwareScanner`, offline only: `None` (default) or `Command` (a scanner installed on the server, e.g. ClamAV `clamscan` or Defender `MpCmdRun`, run on a quarantined copy; a failed or timed-out scan refuses the file).
- Settings `Uploads__MaxFileSizeMb` (25), `Uploads__MaxRequestSizeMb` (100) and `Uploads__MalwareScan__*` in all six env files; Kestrel, IIS in-process and the form reader follow the request limit.
- `Files/Preview/{id}`: same access check as Download; PDF, raster images and text are shown inline, every other type is downloaded.
- Served files carry `X-Content-Type-Options: nosniff`, a `Content-Security-Policy` (sandbox; PDFs without `sandbox` because Chromium's viewer refuses it) and PEMS's own content type for the extension.

### Changed
- Profile photos, drawn signatures and letter editor images are stored in the file store (outside wwwroot) and served by `Files/Image`, which needs a login. Old `/uploads/profile-photos|signatures|letters/inline` URLs keep working through the same action; nothing under `/uploads` is served as a static file any more (old editor images, 404 since the containment work, show again).
- The correspondence signature check uses the shared allowlist table.


## 2026-10-30 - Receiving against PO lines and incoming supply (SCRUM-473)

Owner decisions 2026-10-30:
- **D6a:** quantity in QC counts as incoming until the IRN is closed.
- **D6b:** the buyer decides "replacement from vendor" (the default, the PO line reopens) or "close short" for a rejected quantity.
- **D7:** the Warehouse Manager resolves Hold from My Work.
- **Over-receipt:** allowed, with a warning and on the OS&D report.
- **D8:** imported POs without lines stay out of incoming supply.

### Added
- **Receiving against a PO:** an IRN raised against an issued PO receives its lines ("Receive from PO"). Each IRN line links to its PO line (`IrnReceiptLines.PurchaseOrderLineId`, migration `PoLineReceivingAndIncomingSupply`). These are warnings, not errors:
  - more than the open quantity;
  - another warehouse than the line's delivery warehouse;
  - inspection required and not released.

  The vendor must be the PO's vendor.
- **Delivery warehouse per PO line:** defaults from the pre-procurement demand. A stock line needs one to be submitted.
- **Inspection release before shipment per PO line:** optionally against the project's ITP. A user with IRN Approve can release.
- **OS&D report** per IRN, printable: over, short, rejected/damaged and Hold against the open quantity before the receipt.
- **Quantity buckets per PO line** (`PoLineSupply`), shown on the PO page: ordered, in QC, accepted, Hold, rejected (replaced / closed short), open, over-received.
- **Workflow `IRN / HoldResolution`** (17 module workflows): Hold left by an approved disposition is a task for `WH.Manager`. The accepted part is posted to stock; the rejected part follows the buyer's decision.
- **Incoming supply in net demand:**
  - open quantity of stock lines of issued POs at the delivery warehouse, plus quantity in QC at the receiving warehouse;
  - the result is now `CompleteNetDemand` (Projected = Usable + Incoming - Unreserved);
  - a line in another unit, or without a delivery warehouse, makes the result not definitive.

### Changed
- Pre-procurement planned supply no longer counts quantity already on an issued or closed PO line (it is incoming supply), so no unit is counted twice. The replenishment calculator version is now 2.
## 2026-10-30 - Command palette and permission-checked global search (SCRUM-64)

### Added
- Command palette: Ctrl+K / Cmd+K on every page, a click in the top-bar search box, or the search icon on a phone. It finds:
  - menus, read from the sidebar the server already filtered for the user;
  - actions (new letter, document, VDR item, work request, work order, equipment, MR, contract; My work, notifications, help, change password, theme and language), each shown only when the user passes the check of its page;
  - records of every module the user may open, five per type, with "Show all results".
- Keyboard and screen-reader support (dialog, combobox, listbox, `aria-activedescendant`), Light, Dark and Turquoise themes, RTL, phone layout. Component `pems-command-palette.js/.css` in PEMS.UI; no library, no external call.
- Security contract of search providers: every `ISearchableEntityProvider` declares `SearchAccessRule Access` (any user, module + action, or roles; lookup-only), equal to the page its results open. `GlobalSearchService` checks it before the provider runs; row scopes (letter confidentiality, own discipline, own notifications) stay in the providers.

### Changed
- `/GlobalSearch` (the top-bar search page) shows the same checked results as the palette. Before, it queried nine tables directly with no permission or row check, so work orders, confidential letters and documents of other disciplines could be found by any signed-in user.
- Vendor and contract results now need Vendors View / Contracts View, discipline results DisciplineWorkspace View (palette and search page). Form pickers (`/SearchApi?type=`) are unchanged.

## 2026-10-30 - Rich text as a standard field type, sanitized on the server (SCRUM-59)

### Added
- Field kind `RichText` (JSON `"kind":"richText"`) in `FieldDescriptor`: a form from the form builder can hold formatted text. `PemsField` renders it as a labelled `<textarea data-pems-rte>` (a plain textarea without JavaScript).
- `PEMS.UI/wwwroot/pems-rte.js` + `pems-rte.css`, loaded by the layout: the one rich-text editor of PEMS (vendored Quill 1.3.7, BSD-3-Clause, loaded only on pages with a rich-text field).
  - A Persian page starts right-to-left, an English page left-to-right; the direction button switches a paragraph.
  - Every toolbar control has a localised name (`Rte_*`); a required editor is checked on submit; the image button appears only when the field gives an upload URL.
- `FormValueSanitizer` (`PEMS.Web/Definitions`): the server step for posted form values. Rich-text values go through `IRichTextSanitizerService`, an empty editor becomes empty, names the form does not declare are dropped, other values stay as posted.

### Changed
- `_RichTextEditorScripts` (Letters) is now an adapter over `pems-rte.js`: no inline script, same behaviour (editor under its textarea, image upload, `rte-change`).
- `RichTextSanitizerService` is stricter:
  - only the editor's `ql-*` classes are kept (no page CSS classes in stored content);
  - links and images only use http, https, mailto or a relative URL;
  - a link with a `target` gets `rel="noopener noreferrer"`.
  It applies when content is saved; stored content is unchanged until it is saved again.

## 2026-10-30 - List page component, wave 1: document control (SCRUM-337)

### Added
- Two optional list-page tools in `PEMS.UI`: `<pems-list-menu>` (an icon tool with the page's own dropdown items) and `<pems-list-bulk>` (a bar shown while rows are ticked with `data-lp-select` / `data-lp-select-all`; its forms post the ticked ids). Three generic keys (`Lp_SelectedCount`, `Lp_SelectAll`, `Lp_SelectRow`).

### Changed
- Document Register, Document Revisions, Transmittal, TQ / TSR / RFI, MDB, MOC and Document Workflow are built from the list-page component: full-window table, one icon toolbar, filters in a dropdown with chips, counts as KPI chips on demand, the shared empty state. Each page keeps its filters, columns, actions and paging. The Document Register's saved and quick filters moved into one toolbar menu, and its row selection into the shared bulk bar (no page selection script). Document Workflow shows the queue and the recent history as two panels.
- The English texts written in these pages (headings, columns, empty texts, TQ and MOC statuses and risk levels) are resource keys in both languages now (prefixes `Dr_`, `Rv_`, `Tq_`, `Moc_`, `Mdb_`, `Dw_`).

## 2026-10-30 - Purchase-order lines and approval by the PO authority table (SCRUM-470)

### Added
- Purchase-order lines (table `PurchaseOrderLines`, migration `PurchaseOrderLinesAndAuthority`) from the pre-procurement path:
  - "Create PO" on an MR issued to procurement, or raised from the procurement cartable, adds one line per MR item not already ordered, keeping the registered part;
  - the tender and awarded bid are linked when the MR's package has one;
  - "Add lines from MR" works on a draft PO.
- Line rules on submit (SAP text item / IFS no-part line):
  - a line that goes into stock must be catalogued;
  - a free-text line must be direct use or service, with a commodity group and a reason;
  - setting `Procurement__CatalogueOnlyLines` (all six env files, default `false`) allows catalogued parts only.
- The PO authority table, separate from the contract rules (table `PurchaseOrderAuthorityRules`, admin page Administration > PO authority). Nothing is preset.
- Workflow `PurchaseOrderApproval` (16 module workflows): on submit the PO goes to the role of the lowest active level whose currency and amount range match, as a task in My Work. With no match the PO waits as "responsible not set" and "Route again" retries.
- The approver must hold the routed role, and the creator or submitter can never approve. A rejection returns the PO to draft with the reason.
- `PRC.Buyer` gets PurchaseOrder Create/Edit; `PRC.Manager` (Procurement Manager) gets PurchaseOrder Approve and issues approved POs (owner decision 2026-10-30). Both rows are also added at startup on databases where these roles already exist; a row an admin already set is kept.

### Changed
- `PurchaseOrderService.IssueAsync` issues only an **approved** PO (Draft → Submitted → Approved → Issued). The issue action keeps its guard (PurchaseOrder Approve permission, creator cannot issue, SystemAdmin rule). Imported POs still arrive as Issued.
- R6: a concurrent issue now shows a clear message instead of the error page.

## 2026-10-31 - Jira issue-creation guard (SCRUM-479)

### Added
- `plan_sync.py tracker-check` also fails on an issue (SCRUM-400 and later) without a parent epic, without a `[#N]` title equal to its `plan-N` label, without `area-`, `doer-`, `effort-` labels, or In Review without `qa-pending`; the REST client now reads `parent` and the issue type. Tests added.
- Creation checklist in `AGENTS.md` and chapter 00 section 5d; learnings-log entry; open points for the owner in chapter 06 (required Parent in Jira, `JIRA_*` secrets).
- Plan rows 0423 and 0478 for issues that had none.

## 2026-10-29 - Pre-procurement intake in the procurement cartable (SCRUM-471)

### Added
- Every purchase now enters the **procurement cartable** first (owner decision 2026-10-29): table `ProcurementDemands` (migration `PreProcurementDemands`) with one live item per source (filtered unique index `UX_ProcurementDemands_LiveSource`), and `MaterialRequisitionItems.ProcurementDemandId` as the two-way origin link.
- Sources:
  - "Send to procurement" on an open replenishment recommendation (the recommendation becomes `Converted`, history `SentToProcurement`);
  - direct-purchase lines of a released work order, raised with "Request from warehouse" (SCRUM-472);
  - an engineering MR when it reaches "Issued to Procurement".
- New workflow `PreProcurement`, one task per item in the procurement role queue of My Work (contract and binding; 15 module workflows now). New roles `PRC.Buyer` and `PRC.Manager` and permission module `PreProcurement`.
- Procurement cartable page (`/PreProcurement`, sidebar Purchasing). On each item the buyer can:
  - add a material demand to a new MR or an existing Draft MR, carrying the registered part;
  - accept an engineering MR, or return it to engineering (back to Approved);
  - dismiss an item with a reason.

### Changed
- Replenishment run: quantity already in procurement (open items, and converted items whose MR is not closed or cancelled) is subtracted before recommending (design §6.1 PlannedSupply), so a shortage is never recommended twice. A recommendation fully covered closes as `CoveredByProcurement`.

## 2026-10-31 - PEMS.UI: overlays, navigation, more field kinds and the form shell (SCRUM-336, SCRUM-338, SCRUM-352)

### Added
- Components in `PEMS.UI` (each with typed options, texts as inputs, a manifest snapshot, a sample, render tests in fa and en): `alert` (trace id + copy), `modal` (native dialog, one modal for many rows through the opener's row key), `drawer`, `confirm-dialog` (optional required reason, antiforgery token, posts the row key), `tabs`, `breadcrumb`, `stepper`, `menu`, `side-panel-nav`, `form-section`, `action-bar`.
- Field kinds: Password (value never rendered back), Money, Time, Radio, Switch, Tags, CheckboxMatrix (`Options` rows x `Columns`, posts `row|column` pairs); `<pems-field>` maps DataType Password, Currency, Time.
- `pems-overlay.js` (dialogs, tabs, menu, copy, dismiss) and `pems-form.js` (leave guard when `FormDefinition.WarnOnLeave`), both offline and idempotent; `ButtonOptions.Opens` / `RowKey` open a dialog for a row.

### Not built (follow-ups)
- No page uses the new components yet: adoption tasks per component are in the plan (see the adoption tasks of SCRUM-338). Lookup/TreeSelect fields stay with SCRUM-57 and the registry (#10).

## 2026-10-29 - Design token file moved into PEMS.UI (SCRUM-29, SCRUM-19)

### Changed
- The Light and Dark token blocks (`:root`, `[data-theme="dark"]`, `:root[data-theme="dark"]`) moved verbatim from `PEMS.Web/wwwroot/css/site.css` to `PEMS.UI/wwwroot/tokens.css`, served as `/_content/PEMS.UI/tokens.css` and linked right before `site.css` in `_Layout`, `_AuthLayout` and `_LayoutEmbed`. Same selectors and order, so no computed value changes. The theme and accessibility tests read the token file as well as `site.css`.

### Not built (follow-up SCRUM-476)
- Turquoise and the other theme blocks, hex colour and left/right replacement in the four shared CSS files, screenshot comparison.

## 2026-10-29 - Warehouse intake of work-order materials (SCRUM-472)

### Added
- Work-order materials have a supply code: **From stock** (default) or **Direct purchase** (SAP stock / non-stock component, IFS supply code). Migration `WarehouseIntakeSupplyCode`: `WorkOrderMaterials.SupplyCode` (default `Stock`) and `StoreRequisitionLines.WorkOrderMaterialId` (nullable link, no cascade).
- "Request from warehouse" on a released work order (Assigned, In Progress, On Hold) puts one task in the warehouse role queue of My Work (`WH.Manager`, `WH.Storekeeper`); new workflow `WarehouseIntake` with its module contract (14 module workflows now) and binding.
- Warehouse intake page (Warehouse area): the storekeeper picks the supplying warehouse (never pre-filled by inference) and the quantities; a Draft store requisition is raised for that warehouse with each line linked to its work-order line, and the task closes when every stock line is sourced. Submit, approve, reserve and issue stay in the existing store requisition flow.
- `IWarehouseIntakeService.GetDirectPurchaseDemandAsync`: the direct-purchase lines of released work orders, for the procurement intake (SCRUM-471).

### Changed
- Net demand: a work-order line supplied through linked requisition lines is counted on the requisition side (with its warehouse) and keeps only its unsourced quantity; only unlinked requisitions for the same work order and material are still an ambiguous overlap. Direct-purchase lines are excluded as `DirectPurchaseNotStockDemand` (informational, not blocking).
- A work-order material line that a live store requisition supplies can no longer be removed or have its supply code changed.

## 2026-10-29 - Demo pipeline: backup, smoke test, rollback, staging-first rule, runbook (SCRUM-313, SCRUM-314, SCRUM-317)

### Added
- `scripts/ci/smoke-test.sh`: anonymous post-deploy smoke test (health, database state, login form, stylesheet, protected page redirect, 404) run by `deploy-site.yml` after the health wait.
- `scripts/ci/backup-database.sh`: copy-only `BACKUP DATABASE` before a deploy when the optional secret `<P>_BACKUP_DIR` is set; a failing configured backup stops the deploy.
- `deploy-site.yml` inputs `ref` (rollback), `require_staged`, `allow_unstaged`, `backup`; `deploy-demo.yml` / `deploy-staging.yml` accept `rollback_ref` on a manual run.
- A staging run records the tag `staged/<tree hash>`; a demo run refuses a source tree without it (every demo deploy is preceded by a staging deploy of the same tree).
- `docs/deployment/RUNBOOK_DEPLOY_SITES.md`: deploy, roll back, reset and restore per site, access and owner table.

### Not built (follow-up task)
- Sample data pack load, demo licence install from a secret and automatic rollback after a failed smoke test: their dependencies (#140, #145, #352/#353, #301) do not exist yet. The landing site has no pipeline (#294).

## 2026-10-29 - plan-sync: tracker reconcile, pull, check and push (SCRUM-350)

### Added
- `tools/plan-sync/plan_sync.py`: `reconcile` (status differences, rows without issue, issues without row or Plan ID, duplicate titles), `pull` (tracker status into the plan rows), `tracker-check` (exit 1 on drift other than status) and `push` (summary, priority and plan-managed labels; dry run unless `--apply`). Issues come from the Jira REST API (standard library, `JIRA_*` environment variables, retry on 429/5xx) or offline from a Jira CSV/JSON export. `check` also fails on a duplicate task title.
- `.github/workflows/plan-sync.yml`: nightly and manual run (reconcile report, pull with a pull request, tracker-check); inert without the four `JIRA_*` repository secrets.
- 12 offline tests with a fake HTTP transport.

## 2026-10-29 - UI pattern audit: 30 heaviest pages reviewed (SCRUM-18)

### Added
- `docs/design/ui-pattern-review.csv`: hand review of the 30 heaviest pages (A convertible as is 13, B needs new components 16, C custom 1; 15 calls marked uncertain for the QA step).
- `docs/design/UI_PATTERN_AUDIT.md`: the heaviest-pages table lists 30 pages, the review table, the missing components (RelatedList, PdfViewer, TreeView, SidePanelNav, CheckboxMatrix, CommentsThread replies) and the convertible percentage per wave W1-W5 (reviewed pages measured, the rest estimated by a rule).

### Changed
- `tools/UiAudit/pattern-audit.py` reads the review file, counts table rows holding forms and PDF.js use, and writes the review and per-wave sections; static totals refreshed.
- Plan rows #322-#325 prompts list the components the audit found missing.

## 2026-10-29 - Jira key check on pull requests (SCRUM-349)

### Added
- `.github/workflows/commit-key-check.yml` (pull_request only, read-only, no secrets, no deploy) fails a PR when `SCRUM-<n>` is missing from the PR title, the branch name or any commit subject; merge commits, bot commits/PRs and `main`/`staging`/`demo` heads are exempt; batch keys missing from the title only warn. Logic and unit tests (standard library, offline) in `tools/commit-key-check`.

### Changed
- `docs/process/PLAN_TRACKER_WORKFLOW.md` section 3 now uses the `SCRUM-123` branch/commit/title convention of AGENTS.md (was `PEMS-123`); chapter 5, README and the `build.yml` comment no longer say that pull requests run nothing.

## 2026-10-29 - Docker image restore layer (SCRUM-445)

### Fixed
- `Dockerfile`: the restore layer now copies the project files of `PEMS.Licensing`, `PEMS.UI.Abstractions` and `PEMS.UI`, which `PEMS.Web` references; `dotnet restore` inside the image (also the Azure UAT build `az containerapp up --source .`) could not find them. `LinuxDeploymentArtifactsTests` is green again.

## 2026-10-29 - Registered part numbers in material requisition items (SCRUM-428)

### Added
- MR items link to the registered part their part number resolves to: a completed part catalogue request (`PartCatalogueRequestId`) or Material Master (`MaterialItemId`); migration `MaterialRequisitionItemRegisteredPart` (two nullable columns, indexes, restrict FKs). The service resolves on the server (exact, normalised); a registered part fills an empty description and unit; a typed number that is not registered stays free text, shown "Not catalogued".
- New MR form: the part number of each item is a searchable lookup over the registered parts (part number, manufacturer part number, description) that fills description and unit; a typed part can still be used; "Request a new part" opens the part catalogue. MR details show catalogued / not catalogued per item.
- `pems-select`: `data-pems-select-url` (page endpoint source) and `data-pems-select-create` (allow a typed value).

## 2026-10-29 - Tender invitations by the organisation's email (SCRUM-429)

### Added
- Email through the organisation's own SMTP server on MailKit 4.18.2 (MIT): `IEmailSender` with `SmtpEmailSender`; settings `Email__Enabled`, `Email__Host`, `Email__Port`, `Email__Security`, `Email__FromAddress`, `Email__FromName`, `Email__TimeoutSeconds` in every `env/*.env` (off everywhere), `Email__UserName` and `Email__Password` only in the secrets files.
- Tender details: "Send invitations" (one email per invited vendor from a fa/en template) and "Resend" for failed or skipped vendors, with the Tender Edit permission; sending runs in the background (Hangfire, 3 attempts); with email off the page says so.
- Table `TenderInvitationDispatches` (migration `TenderInvitationDispatches`): the send log on the tender (recipient, exact text, status Queued/Sent/Failed/Skipped, attempts, last error, who and when); an audit entry `SendInvitations`/`ResendInvitations` on the tender.

## 2026-10-28 - API quality gates (SCRUM-436)

### Added
- `tools/api-lint`: a Spectral ruleset of the REST standard (path form, no GET actions, 401/403 and problem+json on every operation, If-Match with 412/428 on writes, 201 on collection POST, camelCase names, declared security, summaries), `lint.sh` to lint the approved OpenAPI snapshot and `lint.sh selftest` to prove each rule fires on a deliberately bad document.
- `ApiConformanceTests`: every operation of the OpenAPI document driven through the real pipeline (401, 403, antiforgery, 404, 428/412, 422, 500, problem+json shape); `ApiPipelineBaselineTests`: in-process p95 guard.
- `tools/loadtest/pems-api-baseline.js` (k6, read-only API scenarios with per-endpoint budgets) and `docs/performance/API_PERFORMANCE_BASELINE.md` (budgets and the stored in-process baseline).
- The OpenAPI document now has operation ids, tags, summaries and descriptions, the declared cookie security scheme and no server list.
- The manual build workflow runs the API tests, the lint and its self-test as failing steps.

### Fixed
- Error responses of controller actions were sent as `application/json` instead of `application/problem+json` (the controller-level `[Produces]` overrode them); problems the framework builds itself (antiforgery 400, bare 404/405/415) now have the `urn:pems:problem:` type and an `errors` list.
- `PUT`/`POST` actions on `/api/v1/definitions` judged the body before looking the resource up; an unknown id is now 404 first.

## 2026-10-28 - Definition store REST API (SCRUM-434)

### Added
- Tables `Definitions`, `DefinitionVersions`, `DefinitionReferences`, `DefinitionStamps` (migration `DefinitionStore`) and the service behind them: draft, validate, publish (the old version is retired), rollback (an older version is published again as a new number), discard draft, export and import (hash-checked package), a runtime read side that serves the published version from a cache refreshed by a per-kind stamp.
- `/api/v1/definitions`: list, get, versions, draft (GET/PUT/DELETE with If-Match), actions `create-draft`, `validate`, `publish`, `rollback`, `export`, `import`; two concurrent edits give 412 for the second.
- Kinds `form` (the FormDefinition record) and `page` (regions of component instances, options checked against the component manifest); limits `Definitions__MaxPayloadBytes`, `Definitions__MaxInstancesPerPage`, `Definitions__MaxJsonDepth` in every `env/*.env`.
- Permission module `Definitions` in the permission matrix (grant it to a role to use the API; publish and rollback need Approve).

### Changed
- The generic audit trail no longer stores the old/new text of `PayloadJson` columns.

## 2026-10-28 - The plan is markdown files, not an Excel workbook (SCRUM-446)

### Changed
- `plan/rows/NNNN.md`: one file per backlog row (front matter, then Evidence, Prompt, Requirement, Report); `plan/sheets/*.md`: the other former sheets. A row edit now changes only that file's lines; two people editing different rows no longer conflict.
- `tools/plan-sync/plan_sync.py` reads the folder and gained `show`, `set`, `report`, `new` and `summary` (the old Summary sheet and Roadmap formulas, now over all rows); `export` no longer writes `plan/backlog.csv`. Standard library only (no openpyxl).
- AGENTS.md, guidelines 00, 01, 03-07 and README, the PR template, the workflow doc, ADR sources and the plan pages point at the folder; the manual build workflow runs the plan check.

### Removed
- `plan/backlog.csv`; `plan/PEMS_Backlog_Roadmap.xlsx` moved to `0 - trash/` (history only).

## 2026-10-28 - REST API walking skeleton and first resource (SCRUM-435)

### Added
- `/api/v1` (ADR-0009, guideline chapter 08): `PEMS.Web/Api` with RFC 9457 `application/problem+json` errors carrying an `errors` list, ETag / `If-Match` (412, 428), `Idempotency-Key` on creates, the list contract (`sort`, `filter[field]`, `q`, `fields`, offset paging capped at 10,000 rows), `allowedActions`, permission policies `[ApiPermission(module, action)]` over the permission matrix, 401/403 problems instead of login redirects for `/api`, the antiforgery header `X-CSRF-TOKEN` on every unsafe verb, and the OpenAPI 3.1 document at `/api/v1/openapi.json` (generated by Microsoft.AspNetCore.OpenApi, MIT, signed-in users only).
- Pilot resource `/api/v1/disciplines`: GET list, GET one, POST (201 + `Location`), PUT, PATCH (JSON Merge Patch) and DELETE (409 when other records use the discipline), on the same service as the Discipline master-data page.
- Migration `DisciplineRowVersion`: `Disciplines.RowVersion` (concurrency token).
- Tests: contract tests of the pilot resource, a gate that fails for an API action without a permission policy, and an approved OpenAPI snapshot.

### Changed
- `tools/rest-inventory` skips `PEMS.Web/Api` (the REST controllers are the target of the migration, not part of the map of what still has to move).

## 2026-10-28 - Searchable dropdowns on every form (SCRUM-57)

### Added
- `PEMS.UI` searchable select (`pems-select.js`/`.css`, on the vendored Tom Select), loaded once by the layout: every single `<select class="form-select">` with 8 or more options becomes searchable; `data-pems-select-source="<type>"` searches the permission-aware `/SearchApi` as you type for large lists; `data-pems-select` forces it, `data-pems-select="off"` opts out. Opens in place inside modals and dropdown panels, re-reads options a page replaces, keeps a "-- None --"/"All" choice only where the page has one, RTL and dark theme, fa/en texts from the layout (`Sel_*`).

### Changed
- The user Create/Edit pages no longer load Tom Select themselves (the layout does).

## 2026-10-28 - Automatic numbering of procurement documents (SCRUM-40)

### Added
- Admin > Numbering: the system administrator sets the numbering pattern of each procurement document (MR, procurement package, tender, bid, purchase order, contract): tokens `{YYYY}` `{YY}` (Jalali or Gregorian year), `{PROJECT}`, `{DISC}`, `{SEQ}`, counter digits, yearly or no reset, active, manual entry allowed. Live preview, validation, a version per change (and the generic audit log).
- New table `NumberingSchemes` (migration `NumberingSchemes`); a startup seeder adds an active Jalali yearly default for each type that has none (`MR-1405-0001`, `PP-…`, `TND-…`, `BID-…`, `PO-…`, `CT-…`).

### Changed
- Procurement package, MR, purchase order and contract numbers (also the contract from a winning bid) are assigned automatically on save; tender and bid numbers follow the pattern too. A typed number is kept only when the pattern allows manual entry; with the pattern switched off the form asks for the number as before. The atomic counter is separate per year/project/discipline as the pattern says, a number that already exists is skipped, existing numbers never change, imports keep their numbers.
- The create forms show the next number (or the pattern shape when it depends on the project) with the `DocumentNumberField` view component.

## 2026-10-27 - Procurement & supply chain section in the sidebar (SCRUM-423)

### Changed
- One sidebar section "Procurement & Supply Chain" (fa: تدارکات و زنجیره تأمین), like CMMS: Vendors, then the sub-headings Purchasing (Material Requisition, Procurement Package, Tender, Purchase Orders), Contracts (Contracts, Contract Milestones, Claims) and Warehouse (all former warehouse links). Engineering Management no longer lists the purchasing pages; the Contract Management and Warehouse sections are gone. Both sidebars changed (data-driven `NavigationMenuDefaults` and the classic `_SidebarMenu.cshtml` used in demo, UAT and production). Each link keeps its former gate plus its former section module, so no role sees more or less. Existing databases: the startup sync adds the new menu rows and deactivates the old system rows (not deleted); no migration.

## 2026-10-28 - ADR-0009, ADR-0010 and ADR-0011 accepted

### Changed
- The owner accepted the REST API standard, the metadata registry envelope and the UI definition model. One change: decimals in the API are plain JSON numbers (not strings); fields needing more than about 15 significant digits are declared as strings in OpenAPI.

## 2026-10-28 - Component manifests (SCRUM-433)

### Added
- Every PEMS.UI component now describes itself from its code: `[PemsComponent]` with category and icon, a generated manifest with the JSON Schema of its options, and `ComponentManifestCatalog` to validate stored options JSON (field error list) and convert it to parameters. This is the base for the page and form designers; no page changes. New dependency in PEMS.UI: LateApexEarlySpeed.Json.Schema 4.2.2 (BSD-3-Clause).

## 2026-10-28 - REST inventory of the controllers (SCRUM-432)

### Added
- `tools/rest-inventory/rest_inventory.py` (static scan, `--check` for staleness), `docs/audits/rest-inventory.csv` (every action: verb, kind, permission signal, target resource, wave) and `docs/audits/REST_INVENTORY_2026-10-28.md` (totals, findings, the 30 heaviest controllers classified by hand, target shape of every JSON endpoint). Result: 147 controllers, 1,062 actions, 170 JSON actions in 57 controllers, 269 actions with no permission signal beyond sign-in (a review list), 2 preference changes through GET.

## 2026-10-28 - Metadata registry and UI definition model designed (SCRUM-25, SCRUM-430)

### Added
- `docs/architecture/METADATA_REGISTRY_DESIGN.md`, ADR-0010 (one definition envelope with Draft/Validated/Published/Retired versions, rollback by republish, shipped vs customer definitions, cache stamp) and ADR-0011 (pages, layouts, component instances, data sources and actions as definitions), with a compile-checked C# sketch. Status: proposed, waiting for the owner. Design only; no code or migration.

## 2026-10-28 - REST API standard written (SCRUM-431)

### Added
- ADR-0009 and guideline chapter 08: resource-oriented JSON under `/api/v1`, verbs and status codes, ETag with If-Match, Idempotency-Key, RFC 9457 errors, one list query contract with offset paging for grids and cursor paging for exports and very large tables, OpenAPI 3.1 as the contract. Status: proposed, waiting for the owner to accept. No code yet (the walking skeleton is #435).

## 2026-10-28 - Title row on five pages that showed no heading (SCRUM-425)

### Changed
- Findings (list), Import Center, New Shift Handover, New Criticality Assessment and Verify Barcode now show the shared title row (page title, plus the module label when it differs) above their content; before, they set a title but showed no heading, so it appeared only in the browser tab. The layout still does not render the row; every other page is unchanged.

## 2026-10-28 - Persian digits in the pagination footer (SCRUM-426)

### Changed
- On Persian pages the pagination footer shows Persian digits in the "showing X to Y of Z" text, the page numbers and the rows-per-page buttons (new `IPemsLocale.Digits`, which swaps digits without reformatting). Links, the `page` and `pageSize` parameters and `aria-label` numbers stay Latin; English pages are unchanged byte for byte.

## 2026-10-28 - Page title row moved into PEMS.UI (SCRUM-425)

### Changed
- The page title row is now the `PEMS.UI` partial `_PemsPageHeader` (`PageHeaderModel`: Title, Section, texts as inputs); `_PageHeader` in `PEMS.Web` stays as an adapter that localizes the texts and honours `HidePageTitle`. Output is byte-identical (7 golden cases, en and fa). No page calls it yet: the layout does not render it by owner decision, so adding callers waits for the owner's choice of pages.

## 2026-10-27 - Unified loading, empty and error states (SCRUM-424)

### Added
- Component inputs are now named-property objects (`new ButtonOptions { Label = ... }`) instead of positional arguments, for buttons, badges, the new states and the field/column/view/form descriptors; one shared property-name table is in guideline chapter 02. Rendered output is unchanged (goldens identical).
- `PemsEmptyState`, `PemsErrorState` and `PemsLoadingState` in `PEMS.UI` with a `_PemsState` partial for existing pages: one look, one accessibility contract (status, alert, busy), texts as inputs, registry keys `empty-state`, `error-state`, `loading-state`. No existing page changes yet.

## 2026-10-27 - List page component, wave 0 (SCRUM-337)

### Added
- `PEMS.UI` list-page component: `<pems-list-page>`, `-panel`, `-toolbar`, `-search`, `-tools`, `-filters`, `-kpi-toggle`, `-export`, `-reset-columns`, `-new`, `-kpis`, `-chips`, `-chip`, `-grid`, `-table`, `-footer` tag helpers with the Engineering Documents look; texts are inputs (`PemsListTexts`, `ListPageTexts.From(Localizer)`). `docs/design/LIST_PAGE_COMPONENT.md` (usage, what stays with each page, waves).
- Five generic resource keys (`Lp_Kpis`, `Lp_ApplyFilters`, `Lp_Total`, `Lp_RemoveFilter`, `Lp_ResetAll`).

### Changed
- Engineering Documents and VDR lists are built from the component (same look and behaviour; about 80 lines less each, no page script).
- The list styles and script moved into the component library: `PEMS.Web/wwwroot/css/pems-letter-index.css` -> `PEMS.UI/wwwroot/pems-list.css` (the Letter pages only changed the link), `pems-list-page.js` -> `PEMS.UI/wwwroot/pems-list.js` (now also wires the search autocomplete, column widths and reset-columns for every list page).

## 2026-10-27 - Pagination footer moved into PEMS.UI (SCRUM-418)

### Changed
- The list pagination footer is now the component `_PemsPagination` in `PEMS.UI`; its texts are inputs (`PaginationTexts`). `_Pagination` in `PEMS.Web` stays as a thin adapter, so the 54 list pages are unchanged. The HTML output is byte-identical (8 golden cases, en and fa).

## 2026-10-27 - Persian pages accept decimals and Persian digits (SCRUM-421)

### Fixed
- A decimal such as 12.5 posted by a number input, and Persian or Arabic-Indic digits typed by the user, are now accepted when the page language is Persian; one model binder (`PEMS.UI.Binding`) is registered for numeric and date types. English binding and MVC's error messages are unchanged. Known-issue tests became regression tests; ADR-0008, guideline chapter 02 and manual check M-19 updated.

## 2026-10-27 - Bugs are normal tasks; gate works on Windows (SCRUM-422)

### Changed
- Guideline chapter 00 section 3a: a reported bug gets its own Jira issue and plan row first, a regression test, a PR and QA; chapter 07 and AGENTS.md point to it.

### Fixed
- `LiteralTextGateTests` no longer fails on Windows (CRLF) checkouts (fix merged in atapems-tech/PEMS#44).

## 2026-10-27 - Every module workflow editable in Workflow Management, guarded by module contracts (SCRUM-103)

### Added
- Module contracts on the workflow engine (`IWorkflowModuleContractRegistry`, `PemsWorkflowContracts`): for each of the 13 module workflows, the actions, stages, dynamic-participant hand-overs and BusinessStatus its code relies on. The validator refuses a version that breaks them (`CONTRACT_*` errors), so Validate/Publish cannot break a module.
- Startup seeder `WorkflowBindingBackfillSeeder`: an explicit binding, pinned to today's published version, for every module workflow that had none (same version the legacy path started, so behaviour is unchanged).
- `docs/workflow/WORKFLOW_PLATFORM_MODULE_CONTRACTS.md`.

### Changed
- Admin > Workflow Management: all 13 definitions are editable (was 3 by whitelist); the reason and the designer show which parts the module contract keeps.
- The legacy module action names moved from `Program.cs` to `PemsWorkflowContracts.CreateActionRegistry()` (one list for the app, seeders and tests).

## 2026-10-27 - Component texts are inputs (SCRUM-416)

### Added
- `TranslatableText` (JSON map of language slug to text, fallback chain, completeness check), `IPemsLocale` with the default `PemsLocale` (Jalali dates and Persian digits for Persian cultures, ISO dates otherwise, number formatting, options), `PemsJson`, and a culture parameter on the request-free renderer. ADR-0008.

### Changed
- `PEMS.UI` descriptors and components take `TranslatableText` for every user-visible text and hold none; table rows carry values of any type formatted by the locale. A test fails when a literal word appears in a component. `PEMS.Web` pages are unchanged.

## 2026-10-27 - PEMS.Web consumes PEMS.UI (SCRUM-29, step 1)

### Changed
- `PEMS.Web` references `PEMS.UI` (component registry and renderer registered, `<pems-field>` tag helper available, `_content/PEMS.UI/pems-ui.css` published but not linked, so no page changes). The shared partials `_KpiCard` and `_PemsLoadingOverlay` and `KpiCardViewModel` moved into `PEMS.UI`; their output is byte-identical (golden tests in `PEMS.Tests/Ui`). ADR-0007 accepted.

## 2026-10-27 - Database upgrade from every supported release, pre-update backup (SCRUM-280)

### Added
- `deploy/db/supported-releases.json`: the database states PEMS must upgrade from (2026-10-22 self-installing build, 2026-10-25 demo), each as its exact migration set; `deploy/db/released-migrations.sha256` fixes the shipped migrations.
- `DatabaseUpgradePathSqlTests` (category `DbUpgrade`): each supported release is upgraded with representative data through the real migrator and must equal a fresh install (schema) and keep its rows; also proves the pre-update backup and its restore. Runs in the Migration Chain Gate workflow after the chain script.
- `MigrationCompatibilityRulesTests`: released migrations unchanged, manifest valid, unique ids with Designer files.
- Automatic pre-update backup: `Database__BackupBeforeUpdate` (true in production/uat) and `Database__BackupDirectory` in every `env/*.env`; an update first writes a COPY_ONLY `<db>_preupdate_<utc>.bak` and is not started when that fails. The file is recorded in the run history (migration `DatabaseUpdateRunBackupFile`) and shown on Admin > Database Update.

### Changed
- Admin > Database Update: with the automatic backup on, the manual "backup taken" box is replaced by a notice; the run table shows the backup file.
- `CODE_FIRST_DATABASE.md`: supported releases, compatibility rules (immutable released migrations, expand/contract) and the rollback (restore) procedure; guideline chapter 01 section 3 updated.

## 2026-10-27 - Component library spike (SCRUM-23)

### Added
- `PEMS.UI.Abstractions` (descriptor contract), `PEMS.UI` (Razor class library: button, badge, skeleton, field, table, form, descriptor host, component registry, request-free renderer, `<pems-field>` Tag Helper facade, View Component for comparison) and `PEMS.UI.Tests` with an extension-pack fixture. Standalone: `PEMS.Web` does not reference them yet, so no page changes.
- ADR-0007 (proposed): static Razor components with a Tag Helper facade, with the measured comparison.

## 2026-10-27 - Password history (SCRUM-412)

### Added
- A new password must differ from the current one and from the last N (`Authentication__Password__HistoryCount`, default 5, 0 = off) on every path that sets a password: forced first change, voluntary change, administrator reset, second-administrator reset, recovery code and break-glass. Replaced hashes are kept in the new table `PasswordHistory` (migration `PasswordHistory`, hashes only). Message in fa and en.
- README manual check M-19; `PasswordHistoryTests`.

## 2026-10-27 - Offline PDF viewers and an external-URL check (SCRUM-20)

### Changed
- The PDF viewers of Engineering Documents and VDR load pdf.js 4.0.379 from `wwwroot/lib/pdfjs` through `wwwroot/js/pdfjs-loader.js` instead of cdnjs.cloudflare.com (the product is offline).

### Added
- `OfflineAssetsTests`: no view, component, script or stylesheet may reference an external host (XML namespaces and one placeholder text are allow-listed); `tools/UiAudit/pattern-audit.py` now exits with an error on a CDN reference.
- README manual check M-17.

## 2026-10-27 - Migration chain gate (SCRUM-413)

### Added
- Workflow `migration-chain.yml` and script `scripts/ci/test-migration-chain.sh`: the migration chain is applied to a real SQL Server (empty install, upgrade from `origin/main`, model agrees) and a failure names the migration.
- `AccountSafetyOfMigrationsAndPipelinesTests`: no migration, workflow or deploy script touches accounts or passwords.
- Guideline chapter 01 rules (a model change ships its migration; the gate before staging/demo), `CODE_FIRST_DATABASE.md` section, `DEPLOYMENT_README.md` note, manual check M-15.

## 2026-10-27 - Deploys and updates never reset the administrator password (SCRUM-410)

### Added
- `AdminPasswordSurvivesUpdateTests`: the start-up seeder changes nothing when users exist and the development reset needs Development plus its variable.
- Rule in guideline chapter 01 (security), `DEPLOYMENT_README.md` section 9, a note in `LINUX_VM_DEPLOYMENT.md` and manual check M-14.

## 2026-10-27 - VDR list with the Engineering Documents look (SCRUM-409)

### Added
- VDR list: quick search (document number, description, vendor) and an Excel export of the filtered list (`VDR/Export`, same columns as the table, capped at 20,000 rows).

### Changed
- VDR list: same frame as Engineering Documents - full-page table, one icon toolbar, project/vendor/status filters in a dropdown with a count badge and chips, KPI chips and the vendor delay/risk view hidden until the KPI button is pressed (remembered per browser).

## 2026-10-26 - Architecture decision log (SCRUM-14)

### Added
- `plan/adr`: ADR template, index and six accepted ADRs (hybrid storage, Razor + PEMS.UI library, code-first database, env/secrets files, licensing threat model and VM policy, demo/staging branch policy), linked from the docs that explain them.

## 2026-10-26 - Engineering Documents list with the correspondence look (SCRUM-408)

### Changed
- Engineering Documents list: same frame as the correspondence list - the table fills the window (sticky header, scrolls inside, pager at the bottom), one icon toolbar (title with count, search over the title, filters, KPIs, Excel, reset columns, New), filters in a dropdown with a count badge and chips instead of the side panel, KPI chips hidden by default and shown on demand (remembered per browser).
- `pems-letter-index.css` gained a generic full-page frame `.lp-page` (the cartable frame without the folder column) and `wwwroot/js/pems-list-page.js` (search open/close, KPI toggle) for list pages that follow this look.

## 2026-10-26 - Searchable multi-select for user roles (SCRUM-407)

### Added
- Tom Select 2.6.2 (Apache-2.0) vendored offline in `PEMS.Web/wwwroot/lib/tom-select` (complete build, Bootstrap 5 theme, source maps, licence).
- `wwwroot/js/pems-multiselect.js` + `wwwroot/css/pems-multiselect.css`: any `<select multiple data-pems-multiselect>` becomes a searchable multi-select with removable chips and option groups, in the PEMS themes and in RTL/LTR; the list opens on <body> so cards cannot clip it.

### Changed
- Admin > Users create/edit: the role checkboxes are one grouped multi-select (responsible, expert, custom roles) with search. The posted field (`SelectedRoles`) is unchanged and works without JavaScript. The old `_RoleCheckboxes` partial moved to `legacy-trash/views`.

## 2026-10-26 - Administrator recovery: one-time recovery codes and server break-glass (SCRUM-355, SCRUM-356)

### Added
- One-time recovery codes for administrators (migration `AdminRecoveryCodes`): 10 codes per set, 80 bits each, stored only as PBKDF2 hash + salt, append-only rows (use/revoke recorded once). User menu > Recovery codes shows how many are left and generates a new set (current password required; unused old codes are revoked); the codes are shown once (no-store) with copy, print and download.
- `/AccountRecovery` (link on the sign-in page): user name + recovery code + new password. The code is consumed once, the password is set, MFA is reset for re-enrolment, all sessions are revoked, the event is audited and all administrators are notified. Wrong codes count toward the Identity lockout; a locked account is refused even with a valid code.
- Rate limit for the sign-in-free recovery pages: 5 POSTs per client IP per 15 minutes (`Security:Recovery:RateLimit:*`), answer 429.
- Server break-glass (`/break-glass`): placing `pems-recovery.request` in `Security:BreakGlass:RequestFolder` opens the page for 15 minutes, reachable only from localhost or `Security:BreakGlass:ManagementNetworks` (every X-Forwarded-For/Forwarded hop must be allowed too); anything else is 404. One administrator is reset (temporary password shown once, forced change, MFA reset); the file is deleted after use or on expiry.
- Optional vendor-signed recovery token (ECDSA P-256, at most 24 hours, bound to a machine fingerprint, single use), off by default (`Security:BreakGlass:VendorRecovery:Enabled=false`).

### Changed
- The MFA reset and unlock step of the second-admin reset moved into a shared helper used by all three recovery paths.

### Notes
- The first-run wizard (SCRUM-188) does not exist yet; it will call the same recovery-code service. Until a licence engine exists, the vendor-token machine fingerprint is a setting.

## 2026-10-27 - Branch hygiene and local gates (SCRUM-394)

### Changed
- Guideline: weekly branch audit and rules for In Review/Done branches that are not on `main` (chapter 00 section 4a, chapter 05), no direct Done, restart cloud sessions from `main`, local gates and the verified cloud-session toolchain (SDK, `dotnet-ef --tool-path`, drift gate) in chapter 05; chapter 07 now expects cloud authors to run everything except the SQL Server tests.

## 2026-10-26 - QA check in the UI, merge before In Review (SCRUM-394)

### Changed
- Workflow (owner decision): the PR is merged into `main` right before the issue moves to In Review (label `qa-pending`); In Review means merged and waiting for the QA check; Done means the QA check in the UI passed.
- Entering or re-entering In Review requires a QA checklist comment (how to test by README references) and a Tests paragraph, re-checked and updated after every failed QA (chapter 07 section 1a).
- A failed QA check sends the issue back to To Do (label `qa-failed`, defects in a comment, assignee kept).
- Tests and review are one step for now: the local test run is line 1 of every QA checklist, an issue may move to In Review with tests listed as not run, Done needs the tests green and the QA check passed.
- Manual checks and local tests are documented in the existing READMEs (root `README.md` "Local tests" and "Manual checks in the UI", `env/README.md` "Verifying settings", `docs/deployment/DEPLOYMENT_README.md` section 8); QA checklists refer to them and stay on the Jira issue after Done.
- New guideline chapter `docs/guidelines/07-qa-check-in-the-ui.md` (who, where, "fully in the UI", checklist template, defects, tasks without a screen); chapters 00, 01, 06, README, `AGENTS.md`, `PLAN_TRACKER_WORKFLOW.md` and the PR template updated.

## 2026-10-26 - Configuration constants second pass (SCRUM-335)

### Changed
- New settings in every `env/*.env`: `Hosting__HstsMaxAgeDays` (30), `Authentication__Cookie__SlidingExpiration` (true), `Database__MigrationCommandTimeoutMinutes` (10); values equal the previous behaviour.
- Production password policy: minimum length 12 in `env/production-kangan.env` (other environments stay at 8); the first administrator password must meet it.
- `docs/design/CONFIGURATION_CONSTANTS.md` classifies the remaining literals (setting, policy constant, domain rule, tuning with trigger).

## 2026-10-26 - Secrets audit: no built-in credentials (SCRUM-17)

### Security
- Removed every built-in password found in the audit: users created from an employee now get a random one-time password (`PEMS.Shared/Security/TemporaryPasswordGenerator.cs`); no default administrator password in the guides; the load-test password comes from `LoadTest__Password` in `env/local.secrets.env` or `PEMS_LOADTEST_PASSWORD`; the example administrator password is a placeholder.
- `docker-compose.yml` publishes SQL Server, Ollama and the app on `127.0.0.1` only (a test keeps it so).
- Audit report `docs/audits/SECRETS_AUDIT_2026-10.md` (HEAD plus all 202 commits of all refs), rotation runbook `docs/deployment/CREDENTIAL_ROTATION_RUNBOOK.md` and the read-only `scripts/security/Report-DefaultCredentialExposure.sql`. Environments created before 2026-10-23 still need rotation (plan row #317); the old values remain in git history (held, #374).

### Changed
- Workflow: the SCRUM project has an On Hold status; the Jira report is kept current during the work (Instructions rule 27); existing tasks are studied before a new one is added (rule 28).

## 2026-10-25 - Administrator recovery: audit trail, last-admin guard, second-admin reset (SCRUM-357, SCRUM-354)

### Added
- `SecurityEvents` table (migration `SecurityEventsRecoveryAudit`): append-only record of every administrator recovery attempt (actor, target, method, IP, UTC time, result, fixed failure-reason code); only the SIEM marker `ForwardedToSiemAt` may change. Read-only list at Admin > Security Events (fa/en).
- `IRecoveryEventService`: one service for all recovery paths. A successful recovery revokes all sessions and remember-me cookies of the target (security stamp), writes the audit record, notifies every active SystemAdmin and logs under `PEMS.Security`; a failure only audits.
- Last-admin guard: the last active SystemAdmin cannot be deactivated or removed from the role (checked on the server in the user edit and activate/deactivate actions).
- Second-admin reset (Admin > Users, key button on other administrators): random temporary password shown once and valid for 24 hours, forced password change, lockout cleared, passkeys and authenticator key removed, `MfaEnrollmentRequired` set (migration `AdminRecoveryUserFlags`). Resetting your own account is refused. An expired temporary password is refused at sign-in with the generic message.

### Changed
- A password change of an administrator in the user edit form is audited (method `AdminConsole`) and its Identity result is no longer ignored.

### Notes
- `MfaEnrollmentRequired` is only a flag until MFA at sign-in exists (SCRUM-188 / SCRUM-206).
## 2026-10-25 - Staging environment live

### Added
- Staging deploys from the `staging` branch: run 40208748572 is green, the database installed (HTTP 200, up to date) and only the changed files (33.8 MB) were uploaded. Root causes found on the way: stale migration-chain test, FTP protocol default, SQL login with a wrong user name.

## 2026-10-25 - Faster deploys: no repeated tests, health check shows the real error

### Changed
- The deploy pipeline records `gate-pass/<source tree hash>` after build, drift gate and tests pass and skips them when the same tree is deployed again (for example demo after staging); NuGet packages are cached; `force_tests` runs the gate anyway.
- The post-deploy health check prints the readable error page and, after 20 consecutive 5xx answers, the newest `logs/stdout*` and `logs/pems-*.log` lines read over FTP, then fails.

## 2026-10-25 - EF migrations in their own assembly (SCRUM-371)

### Changed
- New project `PEMS.Migrations` holds the 119 migrations, the model snapshot and the design-time factory; `PEMS.Persistence.dll` shrinks from 29 MB to 6 MB and `PEMS.Migrations.dll` (23 MB) stays byte-identical when only data-layer code changes, so deploys no longer re-upload the migrations.
- `PEMSDbContext` defaults `MigrationsAssembly` to `PEMS.Migrations`; Web and Tests reference the project. Add a migration with `dotnet ef migrations add <Name> --project PEMS.Migrations --startup-project PEMS.Web`; the drift gate, `deploy.ps1`, the Dockerfile and the docs use the new project.
- Tests: the update-run test no longer assumes the last two migrations are the code-first ones; a new database-free test checks that a plain `UseSqlServer` finds the migrations.

## 2026-10-25 - Smaller deployment package

### Changed
- `Directory.Build.props`: the git commit hash is no longer written into assembly versions, so unchanged PEMS assemblies stay byte-identical between commits and the FTP difference upload skips them.
- Environment packages (`-p:PemsEnv=<name>`) drop debug symbols, satellite resources of languages other than en/fa and the x86 native libraries: 285 MB -> 245 MB. `wwwroot/uploads` is excluded from publishes.
- `wwwroot/uploads` (17 runtime files incl. signatures and documents) is no longer tracked: added to `.gitignore`, files stay on disk.
- FTP steps treat an empty `<P>_FTP_PROTOCOL` as `ftps` everywhere; the pipeline checks FTP login, folder and write access before the long build; the migration-chain test no longer requires `FullTextSearch` to be the last migration.

## 2026-10-25 - Pipelines only on demo and staging, staging environment, deployment README

### Changed
- No workflow runs on pushes or pull requests to `main` or working branches: `build.yml` is manual (`workflow_dispatch`).
- The demo pipeline is now a reusable workflow `deploy-site.yml` (target and secrets prefix as inputs); `deploy-demo.yml` (push to `demo`) and the new `deploy-staging.yml` (push to `staging`) call it.

### Added
- `env/staging.env` and `env/staging.secrets.env.example` (same keys as every environment; the environment-files test includes `staging`); secrets `STAGING_*`.
- `docs/deployment/DEPLOYMENT_README.md`: subdomains, databases, FTP, secrets, Actions, manual deploy and manual reset, rollback, new-environment checklist.
- Plan rows #351 (this work), #352 (versioned, hashed, importable data package in the admin UI) and #353 (reset on deploy with compatibility precheck); the two data features are not built yet.
- Branch `staging` (main + demo + dev); branch `dev` retired after merging into `main`.

## 2026-10-24 - Demo pipeline works end to end

### Fixed
- Model-drift gate failed in CI (design-time factory needs a connection string): dummy value for the model-only check.
- FTP upload failed with `550 ... being used by another process` (IIS file locks): `app_offline.htm` is uploaded first and removed after a successful upload.
- FTP `Timeout (control socket)`: 180 s timeout and one automatic retry.
- Health check could not see the app state behind a missing TLS certificate: probe uses `--insecure`; the certificate is reported separately.
- `demo` pushes that only change docs/plan no longer redeploy.

### Added
- Startup output of the demo app is logged to `logs/stdout*`; `docs/deployment/DEMO_DEPLOYMENT.md` records the runs, causes and fixes.
- Result: run 5 on `demo` is green and the runner's check reports `HTTP 200 UpToDate` (demo database installed). Open: TLS certificate (#340).

## 2026-10-24 - Tracker-free plan workbook, Sprint 1, clearer task text

### Changed
- The plan workbook no longer mentions or stores anything tracker-specific (the `Jira Key` column and all Jira wording are gone);
  tracker-specific export lives only in `tools/plan-sync`. `docs/process/PLAN_JIRA_WORKFLOW.md` is now `PLAN_TRACKER_WORKFLOW.md`.
- Backlog columns `Sprint` and `Owner`; new `Sprint 1` sheet (goal, dates, capacity, committed and stretch items).
- Sprint 1 tasks rewritten with an unambiguous *What to do* and *Done when*; new rows #338 (metadata registry design) and #339 (licensing technical design).
- Export: summaries `[#<Plan ID>] <task>`, structured descriptions (why, what to do, done when, dependencies with titles), Sprint and Assignee
  columns (assignees from the git-ignored `plan/members.json`), epics per milestone.

## 2026-10-23 - Component library plan, UI audit, plan/Jira workflow

### Added
- `tools/UiAudit/pattern-audit.py`: static audit of every view (pattern, target components, migration wave, hand-written markup counts);
  results in `docs/design/UI_PATTERN_AUDIT.md` and `docs/design/ui-pattern-audit.csv`.
- `docs/design/COMPONENT_LIBRARY_PLAN.md`: research, decisions, taxonomy, Definition of Done and delivery plan for the PEMS.UI library.
- `docs/process/PLAN_TRACKER_WORKFLOW.md` and `tools/plan-sync/plan_sync.py` (export to Jira CSV, `plan/backlog.csv` mirror, offline check);
  `.github/pull_request_template.md`.
- Plan workbook: backlog #319-#337 (component program, migration waves, Jira workflow), a `Jira Key` column, instructions 25-26.

## 2026-10-23 - Environment settings and secrets files

### Added
- `env/<name>.env` (all non-secret settings, committed) and `env/<name>.secrets.env` (secrets, git-ignored) for
  `local`, `docker`, `demo`, `uat` and `production-kangan`, plus `*.secrets.env.example` templates and `env/README.md`.
- `PEMS_ENV` selects the environment; precedence is env file < secrets file < real environment variables < command line.
  Loader: `PEMS.Shared.Configuration.EnvFiles` and `AddPemsEnvFiles` (fails fast on a missing file or required setting).
- Demo pipeline writes `env/demo.secrets.env` from GitHub secrets (`DEMO_SQL_CONNECTION_STRING`, `DEMO_ADMIN_PASSWORD`)
  and sets `PEMS_ENV=demo` in `web.config`; `dotnet publish -p:PemsEnv=<name>` ships the matching non-secret file.
- Tests: file format, precedence, identical key sets in every environment, no secrets in the committed files.
- Backlog #314: "What's new" panel on the login page (planned).

### Changed
- `appsettings.json` is empty; `appsettings.Development.json` and `appsettings.UAT.json` are removed (moved to env files).
- Hard-coded settings moved out of the code: log level/path/retention, password policy, auth cookie name/lifetime,
  security-stamp interval, WebAuthn domain/name/origins/drift, Ollama URL/models/timeout, default and supported
  cultures, Work Request segregation-of-duties flag, and the first administrator (user name, email, password).
- No first administrator is created unless `Seed:Admin:*` is configured (the built-in default account is gone).
- The design-time EF factory reads the env files and no longer has a hard-coded connection string.
- `docker-compose.yml` reads `env/docker.env` and `env/docker.secrets.env`; the default SQL Server password is gone.
- Linux release package (`deploy/Build-Package.ps1`) ships `env/*.env`; the VM must set `PEMS_ENV`.

## 2026-10-22 / 2026-10-23 - Fully code-first database

### Added
- `IDatabaseUpdateService`: tells a first run (empty database) from an update (pending migrations), reports an
  unmanaged schema (tables without migration history, never touched) and a database newer than the application,
  applies the install/update and records every run in the new `DatabaseUpdateRuns` table.
- Admin > System Monitoring > **Database Update** page (SystemAdmin, backup confirmation) and anonymous
  `GET /health/database` (200 only when every migration is applied).
- Migration `FullTextSearch` (full-text catalog and indexes, non-transactional, idempotent).
- Model-drift gate in `build.yml`; Persian and English UI strings for the new page.
- Plan workbook in the repository: `plan/PEMS_Backlog_Roadmap.xlsx`.

### Changed
- Startup now creates the database if missing and installs/updates it before the seeders run
  (`Database:CreateIfMissing`, `Database:MigrateOnStartup`; set `Database__MigrateOnStartup=false` to keep the
  reviewed-script flow).
- Demo pipeline (`deploy-demo.yml`): drift gate, runtime configuration, FTP upload, then waits for
  `/health/database` to report up to date.

### Removed
- Hand-run schema SQL moved to `legacy-trash/manual-sql/` (kept, not deleted): `InitialCreate.sql`, the full-text
  script, the procurement and TaskManager migration scripts.

## 2026-10-20 / 2026-10-21 - Correspondence, accessibility, themes, KPI
- Formal internal letter compose, approval chain and compliance; letter list filters, bulk actions, export,
  dashboard, response-due tracking, notifications, templates and reminders (schema migrations included).
- Accessibility: skip link, AA-safe status text colours, sidebar menu search, friendly status pages.
- Theme audit (Turquoise, Light, Dark) for contrast and cascade.
- Executive KPI dashboard, permission UI auto-save, Part Catalogue rules and settings, org-chart vacancy views.
- Fix: null-key crash for class-level RCM strategies in the criticality matrix.

## 2026-10-15 / 2026-10-19 - Navigation, task manager, procurement gates, merges
- Data-driven sidebar with per-item permissions, live badges and admin management (enabled in Development).
- Enterprise Task Manager on `TaskItem` (spaces, backlog, board, sprints) and a task IDOR fix.
- Procurement: read-view gates (P0-09) and segregation of duties (P0-10) per owner decisions D-11..D-13.
- Fix: user creation is atomic when role assignment fails; employee/discipline/role-management work merged;
  personal source merged into the company repository.

## 2026-10-08 / 2026-10-14 - Organization, procurement, CMMS audit fixes, deployment kit
- Enterprise organization hierarchy, actor resolution, org chart and test isolation.
- Employee account and authorization panel, role synchronization, discipline master-data UI and gating,
  employee imports rejected without a valid discipline.
- Procurement/inventory: Phase 1 hardening, Phase 2 net demand (P0-P5), part-catalogue duplicate detection,
  contract-creation hardening (P0.4/P0.5); CMMS audit fixes and Work Order section policy; RCM strategy work.
- My Work inbox and a UI repair program.
- Docker Compose deployment kit for the Linux test VM.
- Graphify AST cache no longer versioned.

## 2026-09-28 / 2026-10-07 - RBAC-ABAC, warehouse, maintenance planning
- Part Catalogue Request module, Warehouse area, Role Catalog, notification lifecycle.
- Navigation Visibility architecture with a hierarchical admin UI; RBAC-ABAC phases 3-5 and Work Order governance fixes.
- Work Order scheduling and Maintenance Calendar; dynamic maintenance organization, Work Order discipline scoping.
- Package Procurement contract finalization and IRN receiving.
- Fix: Admin-area ambient route values leaking into application links; sidebar hover-intent delay.

## 2026-09-19 / 2026-09-27 - Workflow platform, Help, UI redesign
- Central Workflow Platform: hardening (phase 1), Work Request migration (phase 2), Work Order lifecycle (phase 3B),
  Workflow Administration and Visual Designer (phase 4, closure audit accepted with documented limitations).
- Bilingual Help system with a knowledge system and standards references; Reliability Analytics audit.
- VDR/Vendor redesign, single Import Center, filtered Engineering Documents export; global compact page header and
  Digital Equipment Passport Work Request history.
- Fixes: MRS import 500-row ceiling and description truncation; VDR duplicate-vendor-key crash on commit.

## 2026-09-13 / 2026-09-18 - Operations, barcode, EDMS lifecycle, UAT readiness
- Operations and Production core with CMMS boundary correction.
- Enterprise Barcode/QR core with Warehouse/Inventory integration.
- Resource Header pilot, Equipment/Functional Location assignment, bulk strategy scope, history import.
- Controlled letter revision lifecycle and Gmail-inspired workspace.
- UAT hosting audit, hardening and an Azure Container Apps runbook.
- Portal shell consistency, search security, authorization wiring.

## 2026-09-08 / 2026-09-12 - CMMS intelligence, reliability, EDMS integrity, file storage
- Maintenance master data and standards taxonomy; Equipment Classification, ISO 14224 failure taxonomy and PM
  template intelligence; hierarchy-aware maintenance intelligence with human-reviewed classification.
- RCM/FMEA/FMECA decision intelligence, Data Quality coverage gate, Reliability Analytics, Failure Pareto, Bad Actors.
- CMMS execution lifecycle closure, operational readiness, engineering criticality assessment, operations dashboard,
  RAM/predictive intelligence, Functional Location master data import.
- EDMS: revision-file binding, revision-scoped client review, transmittal revision and acknowledgement integrity,
  self-approval segregation-of-duties enforcement.
- Enterprise File Storage Management; Quick Setup onboarding.
- RBAC hardening: Team/Crew/Discipline/Master Data authorization, navigation restructure, AI pipeline endpoints.

## 2026-09-05 / 2026-09-07 - Initial import and hardening
- Initial import of the PEMS engineering and CMMS platform.
- Phase 6B hardening waves: authorization remediation, Material Request state machine, WorkOrderTask ownership,
  database-level uniqueness, server-side pagination for the highest-volume grids, final repository audit.
- Engineering Documents: consolidated search and compact density.
