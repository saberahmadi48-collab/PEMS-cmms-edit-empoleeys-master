# REST inventory of the MVC controllers (2026-10-28)

Plan row #432 (SCRUM-432). Source of truth: [rest-inventory.csv](rest-inventory.csv), produced by `python3 tools/rest-inventory/rest_inventory.py` (static scan of `PEMS.Web`; `--check` fails when the CSV is stale). Standard being measured against: [ADR-0009](../../plan/adr/0009-rest-api-standard.md) and [guideline chapter 08](../guidelines/08-rest-api-standard.md).

Limits: the scan reads source, it does not compile. `class` is a classification of what the action returns (page, JSON, file, redirect), `permission` is `attribute` ([Authorize] on class or action), `inline` (the body calls an access check) or `none` (only the global signed-in filter applies). A `none` row is a question to check, not proof of a hole. The attribute totals match a plain count of `[HttpGet]`/`[HttpPost]` in the source (163 and 668).

## 1. Totals

- Controllers: **147** (25 in Admin area, 12 in Warehouse area); actions: **1062**.
- Verbs: GET 163, POST 668, no attribute (any verb, in practice GET pages) 231; PUT/PATCH/DELETE 0.
- Class: view-only MVC 315, form post 559, JSON endpoint 170, download 9, other 9.
- Permission signal: attribute 169, inline 612, anonymous 12, **none 269** (in 48 controllers; 31 controllers have none on every action).
- Every POST action has `[ValidateAntiForgeryToken]` (or a class-level auto filter): 0 without.
- Controllers with at least one JSON endpoint: **57** (the earlier plan figure of 22 counted only files with `return Json(`).

## 2. Findings

| # | Finding | Detail | Next step |
|---|---|---|---|
| 1 | No resource verbs | 0 PUT/PATCH/DELETE; every update and delete is a POST to an action name | the API gets real verbs (ADR-0009); MVC pages stay |
| 2 | Actions with verbs in the name | 476 actions start with Create/Edit/Delete/Approve/Toggle/... | become resources and `actions/<name>` sub-resources |
| 3 | State change through GET | `Culture/SetCulture` and `Theme/SetTheme` set a preference cookie on a GET (low risk, but a link can change it). `LetterCartable/Settings` is flagged by its name only: it is a read. | move to `PUT /me/preferences` when the API has user preferences |
| 4 | No permission signal | 269 actions in 48 controllers; all-none controllers: AI, AIInsights, AISearch, Asset, ChatAssistant, ComingSoon, DCC, DocumentRegister, DocumentRevisions, EngineeringDashboard, GlobalSearch, ImportCenter, Intake, KnowledgeBase, LetterCartable, LetterTemplate, MDB, MOC, MechanicalEquipment, Meeting, MyWork, Notification, Projects, QC, SearchApi, Settings, Settings, SparePart, Tag, TechnicalQuery, Theme | review each: add the policy or record why signed-in is enough (this is a security review item, not an API item) |
| 5 | Anonymous actions | 12 (login, recovery, break-glass, status page, culture switch) | expected; keep an explicit allow-list in the API gate (#436) |
| 6 | Ad hoc JSON | 170 JSON actions in 57 controllers, each with its own shape | target shapes below and in the CSV (`target` column) |

Partial gaps (actions without a permission signal / all actions): EngineeringDocuments 19/29, Letter 17/35, Instrument 13/14, PartCatalogueRequest 12/17, LetterCompose 9/14, VDR 9/17, PartCatalogueLookup 8/9, TaskManager 8/31, StoreRequisition 6/10, Report 5/6, DocumentWorkflow 3/4, Home 2/4.

## 3. Migration waves (same split as the UI waves, #328-#332)

| Wave | Controllers | Actions |
|---|---|---|
| W1 admin, account, shell | 25 | 131 |
| W2 CMMS, maintenance, reliability | 32 | 316 |
| W3 engineering documents, letters, import, AI | 27 | 238 |
| W4 procurement, inventory, warehouse | 15 | 132 |
| W5 operations, production, organization, other | 48 | 245 |

The API does not follow the UI waves exactly: the order is decided by value and risk (definition store #434 first, then base data from wave 1 of the module migration #87, then work orders and permits).

## 4. The 30 heaviest controllers (classified by hand)

| Controller | Actions | Mix | Permission none | Wave | Target resource | Hand classification |
|---|---|---|---|---|---|---|
| Letter | 35 | form 23, page 5, JSON 5, download 1, other 1 | 17 | W3 | `/letters` | Core resource `letters`. 23 form posts become commands (`POST /letters/{id}/actions/...`). JSON: ExportPdf is a download (`GET /letters/{id}/export?format=pdf`), GetEmployeesForPicker belongs to a lookup (`GET /employees?q=`), SendReminders is an action, ThreadMembers is `GET /letters/{id}/thread-members`. 17 of 35 actions have no permission signal: verify first. |
| TaskManager | 31 | form 26, page 5 | 8 | W2 | `/task-managers` | Resource `tasks`; 26 form posts, no JSON. Straight command mapping. 23 actions have an inline permission check, 8 have no signal. |
| MaintenanceMasterData | 30 | JSON 15, form 14, page 1 | 0 | W2 | `/maintenance-master-datas` | Many small master-data resources in one controller (certifications, condition-priority mappings, equipment classes, evaluation limits ...). The 15 `Toggle...Active` JSON calls become `PATCH /<resource>/{id}` with `{ "isActive": ... }`. Split the controller by resource when migrating. |
| EngineeringDocuments | 29 | form 13, JSON 9, page 5, other 1, download 1 | 19 | W3 | `/engineering-documentses` | Resource `engineering-documents` with child `revisions` and `attachments`. JSON commands Approve, Check, CreateRevision, Delete are really actions/verbs (`POST .../actions/approve`, `DELETE`). 19 of 29 actions have no permission signal: verify first. |
| Reliability | 26 | page 15, form 10, JSON 1 | 0 | W2 | `/reliabilities` | Mostly read pages (15) and analysis posts; one JSON export (`ExportTrend`) is a download. Low API priority; keep MVC. |
| WorkOrder | 26 | form 18, JSON 5, page 3 | 0 | W2 | `/work-orders` | Core resource `work-orders`. JSON AddSpareUsage, AdvanceStatus, CompleteTask, ReopenTask are state transitions: `POST /work-orders/{id}/actions/advance-status` etc. First candidate after the pilot (wave W2). |
| Admin/Help | 21 | form 12, page 9 | 0 | W1 | `/helps` | Admin help content (articles). Resource `help-articles`; all actions have attribute authorization. Low priority. |
| LetterCartable | 21 | form 12, JSON 8, page 1 | 21 | W3 | `/letter-cartables` | Inbox views over letters: JSON lists (Contacts, Delegations, Groups, Reminders) become collections `GET /contacts`, `/delegations`, `/groups`, `/reminders`. No permission signal on any of its 21 actions: verify first. `Settings` is a read despite its name. |
| MaintenanceStrategy | 21 | form 11, page 7, JSON 2, download 1 | 0 | W2 | `/maintenance-strategies` | Resource `maintenance-strategies`; JSON Deactivate is an action, FailureModes a lookup (`GET /failure-modes`). |
| ProductionRun | 20 | form 17, page 3 | 0 | W5 | `/production-runs` | Resource `production-runs`; 17 form posts, no JSON. Command mapping; wave W5. |
| Team | 20 | form 14, JSON 3, page 3 | 0 | W2 | `/teams` | Resource `teams` with `members`. JSON GetTeamMembers is `GET /teams/{id}/members`; OrganizationTree/TeamNodeDetails are reads of the org tree (shared with the Organization controller). |
| Admin/WorkflowManagement | 18 | form 13, page 5 | 0 | W1 | `/workflow-managements` | Admin workflow definitions: maps onto the definition store (`/definitions` kind `workflow-ref`, ADR-0010) rather than its own resource. All attribute-authorized. |
| Contract | 18 | form 14, page 3, JSON 1 | 0 | W4 | `/contracts` | Resource `contracts` with child `documents`; JSON AddDocument is `POST /contracts/{id}/documents`. |
| Warehouse/PartCatalogueAdmin | 17 | form 13, JSON 2, page 2 | 0 | W4 | `/part-catalogue-admins` | Lookups (commodity groups, employees) become collection reads; 13 form posts are admin commands. |
| Warehouse/PartCatalogueRequest | 17 | form 11, page 3, JSON 3 | 12 | W4 | `/part-catalogue-requests` | Resource `part-catalogue-requests`; Download/ExportPdf/ExportXlsx are downloads (`GET .../export?format=`). 12 of 17 actions have no permission signal: verify first. |
| QC | 17 | page 8, JSON 7, form 2 | 17 | W5 | `/qcs` | Inspection and test plans: 7 JSON commands (ItpAddLineItem, ItpApprove, ItpNotifyHoldPoint, ItpPerformInspection ...) are sub-resources and actions of `inspection-test-plans`. No permission signal on any of its 17 actions: verify first. |
| VDR | 17 | form 10, page 3, JSON 3, download 1 | 9 | W3 | `/vdrs` | Resource `vdr-documents`; JSON GenerateContractorNumber/GenerateVendorNumber are number generators (`POST /vdr-numbers`), UploadToFtp an action. 9 of 17 without a permission signal. |
| RcmFmea | 16 | form 14, page 2 | 0 | W5 | `/rcm-fmeas` | Analysis workbench (14 form posts). Stays MVC; expose results later as read-only resources. |
| Barcode | 15 | JSON 6, form 5, page 4 | 0 | W3 | `/barcodes` | JSON Png/LabelPdf/Print/BulkLabelSheet are label renders: `GET /barcodes/{id}/label?format=png|pdf` and `POST /barcodes/print-jobs` (202 operation). |
| Tender | 15 | form 12, page 3 | 0 | W5 | `/tenders` | Resource `tenders`; 12 form posts, no JSON; command mapping, wave W5. |
| Employee | 14 | form 9, page 2, JSON 2, download 1 | 0 | W2 | `/employees` | Resource `employees`; JSON Edit/ToggleActive become `PATCH /employees/{id}`. All attribute-authorized. |
| Instrument | 14 | JSON 8, page 3, form 2, download 1 | 13 | W2 | `/instruments` | Resource `instruments` with child collections (alarms, calibrations, cause-effect, control-loops): the 8 JSON AddX calls are `POST /instruments/{id}/<child>`. 13 of 14 without a permission signal: verify first. |
| LetterCompose | 14 | JSON 7, form 5, page 2 | 9 | W3 | `/letter-composes` | Compose workflow for letters; JSON ResolveRecipients/Directory are lookups, RemoveAttachment is `DELETE .../attachments/{id}`, ComplianceExport a download. |
| Admin/Organization | 12 | form 7, page 4, JSON 1 | 0 | W1 | `/organizations` | Org structure; ChartData is the tree read shared with Team. Attribute-authorized. |
| Admin/Users | 12 | page 5, form 4, JSON 3 | 0 | W1 | `/userses` | Resource `users`; JSON ToggleActive, SetPermissionAccess, Export become `PATCH /users/{id}`, `PUT /users/{id}/permissions`, `GET /users/export`. ResetAdmin is GET (confirmation page) + POST (the action): correct. |
| MOC | 12 | form 6, page 3, JSON 3 | 12 | W5 | `/mocs` | Management of change; JSON MarkDocumentsUpdated, MarkTrainingCompleted, RecordImplementation are actions on `changes`. No permission signal on any of its 12 actions: verify first. |
| Permit | 12 | form 8, page 3, JSON 1 | 0 | W2 | `/permits` | Resource `permits`; AdvanceStatus action as for work orders. |
| ProcurementPackage | 11 | form 8, page 3 | 0 | W4 | `/procurement-packages` | Resource `procurement-packages`; 8 form posts, command mapping. |
| ProductionOrder | 11 | form 8, page 3 | 0 | W5 | `/production-orders` | Resource `production-orders`; 8 form posts, command mapping. |
| Relationship | 11 | page 9, form 2 | 0 | W3 | `/relationships` | Relationship explorer: 9 read pages; stays MVC, read API later from the relation registry (#22). |

## 5. All controllers (controller, actions, dominant kind, target resource, wave, owner)

Owner is unassigned until a wave is planned. Kind is the dominant class by action count.

| Controller | Area | Actions | Dominant kind | JSON | Target resource | Wave | Owner |
|---|---|---|---|---|---|---|---|
| AI | - | 1 | JSON endpoint | 1 | `/ais` | W3 | - |
| AIExtraction | - | 6 | form post | 0 | `/aiextractions` | W3 | - |
| AIInsights | - | 1 | view-only MVC | 0 | `/aiinsightses` | W3 | - |
| AISearch | - | 1 | view-only MVC | 0 | `/aisearches` | W3 | - |
| Account | - | 9 | view-only MVC | 0 | `/accounts` | W5 | - |
| AccountRecovery | - | 2 | view-only MVC | 0 | `/account-recoveries` | W5 | - |
| Asset | - | 5 | form post | 0 | `/assets` | W2 | - |
| AuditLog | Admin | 1 | view-only MVC | 0 | `/audit-logs` | W1 | - |
| Authorization | Admin | 4 | view-only MVC | 0 | `/authorizations` | W1 | - |
| AuthorizationAudit | Admin | 1 | view-only MVC | 0 | `/authorization-audits` | W1 | - |
| Barcode | - | 15 | JSON endpoint | 6 | `/barcodes` | W3 | - |
| BreakGlass | - | 2 | view-only MVC | 0 | `/break-glasses` | W5 | - |
| ChatAssistant | - | 3 | JSON endpoint | 1 | `/chat-assistants` | W5 | - |
| CmmsOperations | - | 2 | view-only MVC | 0 | `/cmms-operationses` | W2 | - |
| CmmsReadiness | - | 2 | view-only MVC | 0 | `/cmms-readinesses` | W2 | - |
| ComingSoon | - | 1 | view-only MVC | 0 | `/coming-soons` | W5 | - |
| ConditionReadingImport | - | 6 | form post | 0 | `/condition-reading-imports` | W2 | - |
| Contract | - | 18 | form post | 1 | `/contracts` | W4 | - |
| ContractApprovalAuthority | Admin | 4 | form post | 0 | `/contract-approval-authorities` | W1 | - |
| Crew | - | 4 | form post | 0 | `/crews` | W2 | - |
| CriticalityAssessment | - | 10 | form post | 0 | `/criticality-assessments` | W2 | - |
| Culture | - | 1 | other | 0 | `/cultures` | W5 | - |
| DCC | - | 10 | JSON endpoint | 8 | `/dccs` | W3 | - |
| DashboardSettings | - | 3 | form post | 0 | `/dashboard-settingses` | W5 | - |
| DatabaseUpdate | Admin | 2 | form post | 0 | `/database-updates` | W1 | - |
| Department | - | 6 | form post | 2 | `/departments` | W5 | - |
| Department | Admin | 1 | other | 0 | `/departments` | W1 | - |
| Discipline | - | 8 | form post | 0 | `/disciplines` | W5 | - |
| DocumentRegister | - | 5 | form post | 1 | `/document-registers` | W3 | - |
| DocumentRevisions | - | 2 | other | 0 | `/document-revisionses` | W3 | - |
| DocumentUpload | - | 2 | view-only MVC | 0 | `/document-uploads` | W3 | - |
| DocumentWorkflow | - | 4 | form post | 0 | `/document-workflows` | W3 | - |
| Employee | - | 14 | form post | 2 | `/employees` | W2 | - |
| Employee | Admin | 1 | other | 0 | `/employees` | W1 | - |
| EngineeringDashboard | - | 1 | view-only MVC | 0 | `/engineering-dashboards` | W3 | - |
| EngineeringDocumentIntelligence | - | 10 | form post | 0 | `/engineering-document-intelligences` | W3 | - |
| EngineeringDocuments | - | 29 | form post | 9 | `/engineering-documentses` | W3 | - |
| Equipment | - | 5 | view-only MVC | 0 | `/equipments` | W2 | - |
| EquipmentClassification | - | 5 | view-only MVC | 0 | `/equipment-classifications` | W2 | - |
| EquipmentCriticality | - | 2 | form post | 0 | `/equipment-criticalities` | W2 | - |
| EquipmentFunctionalLocationAssignment | - | 7 | form post | 0 | `/equipment-functional-location-assignments` | W2 | - |
| EquipmentImport | - | 8 | form post | 0 | `/equipment-imports` | W2 | - |
| ExecutionGate | - | 2 | form post | 0 | `/execution-gates` | W5 | - |
| ExecutiveDashboard | - | 2 | JSON endpoint | 1 | `/executive-dashboards` | W5 | - |
| FailureModeCatalog | - | 4 | JSON endpoint | 2 | `/failure-mode-catalogs` | W2 | - |
| Files | - | 1 | JSON endpoint | 1 | `/fileses` | W5 | - |
| Finding | - | 5 | JSON endpoint | 4 | `/findings` | W2 | - |
| FunctionalLocation | - | 3 | form post | 0 | `/functional-locations` | W2 | - |
| FunctionalLocationImport | - | 6 | form post | 0 | `/functional-location-imports` | W2 | - |
| GasTest | - | 2 | form post | 0 | `/gas-tests` | W5 | - |
| GlobalSearch | - | 1 | view-only MVC | 0 | `/global-searches` | W5 | - |
| Help | - | 6 | view-only MVC | 2 | `/helps` | W5 | - |
| Help | Admin | 21 | form post | 0 | `/helps` | W1 | - |
| Home | - | 4 | view-only MVC | 0 | `/homes` | W5 | - |
| ImportCenter | - | 1 | view-only MVC | 0 | `/import-centers` | W3 | - |
| Inspection | - | 3 | form post | 1 | `/inspections` | W2 | - |
| Instrument | - | 14 | JSON endpoint | 8 | `/instruments` | W2 | - |
| Intake | - | 4 | view-only MVC | 0 | `/intakes` | W5 | - |
| Inventory | Warehouse | 1 | view-only MVC | 0 | `/inventories` | W4 | - |
| InventoryMasterData | Warehouse | 8 | form post | 3 | `/inventory-master-datas` | W4 | - |
| InventoryTransaction | Warehouse | 1 | view-only MVC | 0 | `/inventory-transactions` | W4 | - |
| IrnReceipt | Warehouse | 8 | form post | 2 | `/irn-receipts` | W4 | - |
| Isolation | - | 5 | form post | 0 | `/isolations` | W5 | - |
| JobPlan | - | 10 | form post | 0 | `/job-plans` | W5 | - |
| KnowledgeBase | - | 1 | view-only MVC | 0 | `/knowledge-bases` | W5 | - |
| Kpi | - | 3 | view-only MVC | 1 | `/kpis` | W5 | - |
| Letter | - | 35 | form post | 5 | `/letters` | W3 | - |
| LetterCartable | - | 21 | form post | 8 | `/letter-cartables` | W3 | - |
| LetterCompose | - | 14 | JSON endpoint | 7 | `/letter-composes` | W3 | - |
| LetterTemplate | - | 5 | view-only MVC | 1 | `/letter-templates` | W3 | - |
| License | Admin | 3 | form post | 0 | `/licenses` | W1 | - |
| MDB | - | 10 | form post | 0 | `/mdbs` | W3 | - |
| MDRImport | - | 8 | form post | 0 | `/mdrimports` | W3 | - |
| MOC | - | 12 | form post | 3 | `/mocs` | W5 | - |
| MaintenanceCalendar | - | 3 | view-only MVC | 1 | `/maintenance-calendars` | W2 | - |
| MaintenanceHistoryImport | - | 6 | form post | 0 | `/maintenance-history-imports` | W2 | - |
| MaintenanceMasterData | - | 30 | JSON endpoint | 15 | `/maintenance-master-datas` | W2 | - |
| MaintenancePlan | - | 7 | form post | 2 | `/maintenance-plans` | W2 | - |
| MaintenanceStrategy | - | 21 | form post | 2 | `/maintenance-strategies` | W2 | - |
| MaterialItem | Warehouse | 8 | form post | 0 | `/material-items` | W4 | - |
| MaterialRequisition | - | 8 | form post | 0 | `/material-requisitions` | W4 | - |
| MechanicalEquipment | - | 8 | JSON endpoint | 4 | `/mechanical-equipments` | W2 | - |
| Meeting | - | 8 | form post | 0 | `/meetings` | W5 | - |
| MrsImport | Warehouse | 7 | form post | 0 | `/mrs-imports` | W3 | - |
| MyProfile | - | 3 | JSON endpoint | 2 | `/my-profiles` | W5 | - |
| MyWork | - | 1 | view-only MVC | 0 | `/my-works` | W5 | - |
| NavigationBadge | - | 1 | JSON endpoint | 1 | `/navigation-badges` | W5 | - |
| NavigationMenu | Admin | 7 | view-only MVC | 3 | `/navigation-menus` | W1 | - |
| NavigationVisibility | Admin | 4 | form post | 0 | `/navigation-visibilities` | W1 | - |
| Notification | - | 8 | JSON endpoint | 3 | `/notifications` | W5 | - |
| Numbering | Admin | 3 | view-only MVC | 1 | `/numberings` | W1 | - |
| OCRProcessing | - | 2 | view-only MVC | 0 | `/ocrprocessings` | W3 | - |
| Organization | Admin | 12 | form post | 1 | `/organizations` | W1 | - |
| OrganizationImport | Admin | 4 | form post | 0 | `/organization-imports` | W1 | - |
| PartCatalogueAdmin | Warehouse | 17 | form post | 2 | `/part-catalogue-admins` | W4 | - |
| PartCatalogueDedup | Warehouse | 2 | JSON endpoint | 2 | `/part-catalogue-dedups` | W4 | - |
| PartCatalogueLookup | Warehouse | 9 | JSON endpoint | 9 | `/part-catalogue-lookups` | W4 | - |
| PartCatalogueRequest | Warehouse | 17 | form post | 3 | `/part-catalogue-requests` | W4 | - |
| Performance | - | 1 | view-only MVC | 0 | `/performances` | W5 | - |
| PermissionMatrix | Admin | 3 | view-only MVC | 1 | `/permission-matrixes` | W1 | - |
| Permit | - | 12 | form post | 1 | `/permits` | W2 | - |
| ProcurementPackage | - | 11 | form post | 0 | `/procurement-packages` | W4 | - |
| Production | - | 1 | view-only MVC | 0 | `/productions` | W5 | - |
| ProductionOrder | - | 11 | form post | 0 | `/production-orders` | W5 | - |
| ProductionRun | - | 20 | form post | 0 | `/production-runs` | W5 | - |
| ProjectIntelligence | Admin | 2 | view-only MVC | 0 | `/project-intelligences` | W1 | - |
| Projects | - | 6 | view-only MVC | 0 | `/projectses` | W5 | - |
| PurchaseOrder | - | 5 | view-only MVC | 0 | `/purchase-orders` | W5 | - |
| QC | - | 17 | view-only MVC | 7 | `/qcs` | W5 | - |
| QuickSetup | - | 1 | view-only MVC | 0 | `/quick-setups` | W5 | - |
| RcmFmea | - | 16 | form post | 0 | `/rcm-fmeas` | W5 | - |
| RecoveryCodes | - | 2 | form post | 0 | `/recovery-codeses` | W5 | - |
| Relationship | - | 11 | view-only MVC | 0 | `/relationships` | W3 | - |
| Reliability | - | 26 | view-only MVC | 1 | `/reliabilities` | W2 | - |
| Replenishment | Warehouse | 5 | form post | 0 | `/replenishments` | W4 | - |
| Report | - | 6 | view-only MVC | 0 | `/reports` | W5 | - |
| Roles | Admin | 4 | view-only MVC | 1 | `/roleses` | W1 | - |
| RoutingRules | Admin | 3 | form post | 0 | `/routing-ruleses` | W1 | - |
| SearchApi | - | 2 | JSON endpoint | 2 | `/search-apis` | W5 | - |
| SecurityEvents | Admin | 1 | view-only MVC | 0 | `/security-eventses` | W1 | - |
| Settings | - | 1 | view-only MVC | 0 | `/settingses` | W5 | - |
| Settings | Admin | 1 | other | 0 | `/settingses` | W1 | - |
| ShiftHandover | - | 7 | form post | 0 | `/shift-handovers` | W2 | - |
| Simops | - | 4 | form post | 0 | `/simopses` | W5 | - |
| SoDPolicy | Admin | 4 | view-only MVC | 1 | `/so-dpolicies` | W1 | - |
| SparePart | - | 4 | JSON endpoint | 2 | `/spare-parts` | W5 | - |
| StorageProviders | Admin | 9 | form post | 0 | `/storage-providerses` | W1 | - |
| StorageRouting | Admin | 6 | view-only MVC | 0 | `/storage-routings` | W1 | - |
| StoreRequisition | Warehouse | 10 | form post | 2 | `/store-requisitions` | W4 | - |
| StrategyImport | - | 5 | form post | 1 | `/strategy-imports` | W3 | - |
| TableExtraction | - | 9 | form post | 1 | `/table-extractions` | W5 | - |
| Tag | - | 3 | form post | 0 | `/tags` | W5 | - |
| Task | - | 4 | view-only MVC | 1 | `/tasks` | W2 | - |
| TaskManager | - | 31 | form post | 0 | `/task-managers` | W2 | - |
| Team | - | 20 | form post | 3 | `/teams` | W2 | - |
| TechnicalQuery | - | 8 | form post | 0 | `/technical-queries` | W5 | - |
| Tender | - | 15 | form post | 0 | `/tenders` | W5 | - |
| Theme | - | 1 | other | 0 | `/themes` | W5 | - |
| Transmittal | - | 9 | form post | 0 | `/transmittals` | W3 | - |
| Users | Admin | 12 | view-only MVC | 3 | `/userses` | W1 | - |
| VDR | - | 17 | form post | 3 | `/vdrs` | W3 | - |
| VDRImport | - | 6 | form post | 0 | `/vdrimports` | W3 | - |
| Vendor | - | 9 | form post | 0 | `/vendors` | W4 | - |
| WebAuthn | - | 3 | JSON endpoint | 3 | `/web-authns` | W5 | - |
| WorkOrder | - | 26 | form post | 5 | `/work-orders` | W2 | - |
| WorkRequest | - | 10 | form post | 1 | `/work-requests` | W2 | - |
| WorkflowManagement | Admin | 18 | form post | 0 | `/workflow-managements` | W1 | - |

## 6. Target shape of every JSON endpoint

One line per controller; the full per-action list with verb, permission signal and wave is the `target` / `operation` column of the CSV. Reading rule: `Toggle...`/`Set...` become `PATCH`, `Add...` become `POST` on a child collection, `Get...`/`Search...`/`Lookup`-style become `GET` collections, exports and PDFs become `GET .../export?format=`, everything else is `POST .../actions/<name>`.

- **AI**: `Test -> GET /ais/test`
- **Barcode**: `BulkLabelSheet -> GET /barcodes/{id}/bulk-label-sheet`; `LabelPdf -> GET /barcodes/{id}/label-pdf`; `Png -> GET /barcodes/{id}/png`; `Print -> POST /barcodes/{id}/actions/print`; `ResolveJson -> GET /barcodes/resolve-json`; `Svg -> GET /barcodes/svg`
- **ChatAssistant**: `Ask -> POST /chat-assistants/{id}/actions/ask`
- **Contract**: `AddDocument -> POST /contracts/{id}/actions/add-document`
- **DCC**: `CheckDuplicate -> GET /dccs/check-duplicate`; `Links -> GET /dccs/links`; `NextNumber -> GET /dccs/next-number`; `OutgoingForReply -> GET /dccs/outgoing-for-reply`; `Parties -> GET /dccs/parties`; `PartyThread -> GET /dccs/party-thread`; `People -> GET /dccs/people`; `RegisterIncoming -> POST /dccs/{id}/actions/register-incoming`
- **Department**: `Edit -> PATCH /departments/{id}`; `ToggleActive -> POST /departments/{id}/actions/toggle-active`
- **DocumentRegister**: `ExportExcel -> GET /document-registers/{id}/export-excel`
- **Employee**: `Edit -> PATCH /employees/{id}`; `ToggleActive -> POST /employees/{id}/actions/toggle-active`
- **EngineeringDocuments**: `Approve -> POST /engineering-documentses/{id}/actions/approve`; `Check -> POST /engineering-documentses/{id}/actions/check`; `CreateRevision -> POST /engineering-documentses`; `Delete -> DELETE /engineering-documentses/{id}`; `DeleteAttachment -> DELETE /engineering-documentses/{id}`; `Issue -> POST /engineering-documentses/{id}/actions/issue`; `Reject -> POST /engineering-documentses/{id}/actions/reject`; `ReviseAndResubmit -> POST /engineering-documentses/{id}/actions/revise-and-resubmit`; `Submit -> POST /engineering-documentses/{id}/actions/submit`
- **ExecutiveDashboard**: `Data -> GET /executive-dashboards/data`
- **FailureModeCatalog**: `Edit -> page (form)`; `Toggle -> POST /failure-mode-catalogs/{id}/actions/toggle`
- **Files**: `Download -> GET /fileses/{id}/download`
- **Finding**: `Close -> POST /findings/{id}/actions/close`; `CreateFromInspection -> POST /findings`; `CreateFromReading -> POST /findings`; `RaiseWorkRequest -> POST /findings/{id}/actions/raise-work-request`
- **Help**: `AskAi -> POST /helps/{id}/actions/ask-ai`; `Context -> GET /helps/context`
- **Inspection**: `RaiseWorkRequest -> POST /inspections/{id}/actions/raise-work-request`
- **Instrument**: `AddAlarm -> POST /instruments/{id}/actions/add-alarm`; `AddCalibration -> POST /instruments/{id}/actions/add-calibration`; `AddCauseEffect -> POST /instruments/{id}/actions/add-cause-effect`; `AssignControlLoop -> POST /instruments/{id}/actions/assign-control-loop`; `Edit -> PATCH /instruments/{id}`; `GenerateCalibrationWorkOrder -> POST /instruments/{id}/actions/generate-calibration-work-order`; `RaiseWorkRequestFromCauseEffect -> POST /instruments/{id}/actions/raise-work-request-from-cause-effect`; `ToggleActive -> POST /instruments/{id}/actions/toggle-active`
- **Kpi**: `SaveTarget -> POST /kpis/{id}/actions/save-target`
- **Letter**: `ExportPdf -> GET /letters/{id}/export-pdf`; `GetEmployeesForPicker -> GET /letters/get-employees-for-picker`; `SendReminders -> POST /letters/{id}/actions/send-reminders`; `ThreadMembers -> GET /letters/thread-members`; `UploadEditorImage -> POST /letters/{id}/actions/upload-editor-image`
- **LetterCartable**: `Contacts -> GET /letter-cartables/contacts`; `Delegations -> GET /letter-cartables/delegations`; `Groups -> GET /letter-cartables/groups`; `Reminders -> GET /letter-cartables/reminders`; `Rules -> GET /letter-cartables/rules`; `Settings -> GET /letter-cartables/settings`; `SettingsSave -> POST /letter-cartables/{id}/actions/settings-save`; `Tags -> GET /letter-cartables/tags`
- **LetterCompose**: `ComplianceExport -> GET /letter-composes/{id}/compliance-export`; `Directory -> GET /letter-composes/directory`; `RemoveAttachment -> DELETE /letter-composes/{id}`; `ResolveRecipients -> POST /letter-composes/{id}/actions/resolve-recipients`; `Save -> POST /letter-composes/{id}/actions/save`; `Submit -> POST /letter-composes/{id}/actions/submit`; `UploadAttachment -> POST /letter-composes/{id}/actions/upload-attachment`
- **LetterTemplate**: `SetActive -> POST /letter-templates/{id}/actions/set-active`
- **MOC**: `MarkDocumentsUpdated -> POST /mocs/{id}/actions/mark-documents-updated`; `MarkTrainingCompleted -> POST /mocs/{id}/actions/mark-training-completed`; `RecordImplementation -> POST /mocs/{id}/actions/record-implementation`
- **MaintenanceCalendar**: `Reschedule -> POST /maintenance-calendars/{id}/actions/reschedule`
- **MaintenanceMasterData**: `ToggleCertificationActive -> POST /maintenance-master-datas/{id}/actions/toggle-certification-active`; `ToggleConditionPriorityMappingActive -> POST /maintenance-master-datas/{id}/actions/toggle-condition-priority-mapping-active`; `ToggleEquipmentClassActive -> POST /maintenance-master-datas/{id}/actions/toggle-equipment-class-active`; `ToggleEvaluationLimitActive -> POST /maintenance-master-datas/{id}/actions/toggle-evaluation-limit-active`; `ToggleEvaluationProfileActive -> POST /maintenance-master-datas/{id}/actions/toggle-evaluation-profile-active`; `ToggleFailureModeActive -> POST /maintenance-master-datas/{id}/actions/toggle-failure-mode-active`; `ToggleGroupActive -> POST /maintenance-master-datas/{id}/actions/toggle-group-active`; `ToggleMaintenanceActivityActive -> POST /maintenance-master-datas/{id}/actions/toggle-maintenance-activity-active`; `ToggleMaintenanceStrategyTypeActive -> POST /maintenance-master-datas/{id}/actions/toggle-maintenance-strategy-type-active`; `ToggleMaintenanceTypeActive -> POST /maintenance-master-datas/{id}/actions/toggle-maintenance-type-active`; `TogglePMTemplateActive -> POST /maintenance-master-datas/{id}/actions/toggle-pmtemplate-active`; `ToggleQualificationActive -> POST /maintenance-master-datas/{id}/actions/toggle-qualification-active`; `ToggleSkillActive -> POST /maintenance-master-datas/{id}/actions/toggle-skill-active`; `ToggleStandardReferenceActive -> POST /maintenance-master-datas/{id}/actions/toggle-standard-reference-active`; `UpdateConditionPriorityMapping -> PATCH /maintenance-master-datas/{id}`
- **MaintenancePlan**: `Deactivate -> POST /maintenance-plans/{id}/actions/deactivate`; `RecordMeterReading -> POST /maintenance-plans/{id}/actions/record-meter-reading`
- **MaintenanceStrategy**: `Deactivate -> POST /maintenance-strategies/{id}/actions/deactivate`; `FailureModes -> GET /maintenance-strategies/failure-modes`
- **MechanicalEquipment**: `ClassifyRotating -> POST /mechanical-equipments/{id}/actions/classify-rotating`; `ClassifyStatic -> POST /mechanical-equipments/{id}/actions/classify-static`; `Edit -> PATCH /mechanical-equipments/{id}`; `ToggleActive -> POST /mechanical-equipments/{id}/actions/toggle-active`
- **MyProfile**: `SaveSignature -> POST /my-profiles/{id}/actions/save-signature`; `UploadPhoto -> POST /my-profiles/{id}/actions/upload-photo`
- **NavigationBadge**: `Counts -> GET /navigation-badges/counts`
- **Notification**: `Dismiss -> POST /notifications/{id}/actions/dismiss`; `MarkRead -> POST /notifications/{id}/actions/mark-read`; `UnreadCount -> GET /notifications/unread-count`
- **Permit**: `AdvanceStatus -> POST /permits/{id}/actions/advance-status`
- **QC**: `ItpAddLineItem -> POST /qcs/{id}/actions/itp-add-line-item`; `ItpApprove -> POST /qcs/{id}/actions/itp-approve`; `ItpNotifyHoldPoint -> POST /qcs/{id}/actions/itp-notify-hold-point`; `ItpPerformInspection -> POST /qcs/{id}/actions/itp-perform-inspection`; `NcrApprove -> POST /qcs/{id}/actions/ncr-approve`; `NcrClose -> POST /qcs/{id}/actions/ncr-close`; `NcrDisposition -> POST /qcs/{id}/actions/ncr-disposition`
- **Reliability**: `ExportTrend -> GET /reliabilities/{id}/export-trend`
- **SearchApi**: `Entities -> GET /search-apis/entities`; `Index -> GET /search-apis`
- **SparePart**: `Deactivate -> POST /spare-parts/{id}/actions/deactivate`; `Edit -> PATCH /spare-parts/{id}`
- **StrategyImport**: `Errors -> GET /strategy-imports/{id}/errors`
- **TableExtraction**: `StageForReview -> POST /table-extractions/{id}/actions/stage-for-review`
- **Task**: `UpdateStatus -> PATCH /tasks/{id}`
- **Team**: `GetTeamMembers -> GET /teams/get-team-members`; `OrganizationTree -> GET /teams/organization-tree`; `TeamNodeDetails -> GET /teams/team-node-details`
- **VDR**: `GenerateContractorNumber -> POST /vdrs/{id}/actions/generate-contractor-number`; `GenerateVendorNumber -> POST /vdrs/{id}/actions/generate-vendor-number`; `UploadToFtp -> POST /vdrs/{id}/actions/upload-to-ftp`
- **WebAuthn**: `CompleteRegistration -> POST /web-authns/{id}/actions/complete-registration`; `GetAssertionOptions -> GET /web-authns/get-assertion-options`; `GetRegistrationOptions -> GET /web-authns/get-registration-options`
- **WorkOrder**: `AddSpareUsage -> POST /work-orders/{id}/actions/add-spare-usage`; `AdvanceStatus -> POST /work-orders/{id}/actions/advance-status`; `CompleteTask -> POST /work-orders/{id}/actions/complete-task`; `ReopenTask -> POST /work-orders/{id}/actions/reopen-task`; `ReturnSpareUsage -> POST /work-orders/{id}/actions/return-spare-usage`
- **WorkRequest**: `AdvanceStatus -> POST /work-requests/{id}/actions/advance-status`
- **NavigationMenu** (Admin): `Delete -> DELETE /navigation-menus/{id}`; `Edit -> PATCH /navigation-menus/{id}`; `Move -> POST /navigation-menus/{id}/actions/move`
- **Numbering** (Admin): `Preview -> GET /numberings/preview`
- **Organization** (Admin): `ChartData -> GET /organizations/chart-data`
- **PermissionMatrix** (Admin): `SetCells -> POST /permission-matrixes/{id}/actions/set-cells`
- **Roles** (Admin): `Delete -> DELETE /roleses/{id}`
- **SoDPolicy** (Admin): `ToggleActive -> POST /so-dpolicies/{id}/actions/toggle-active`
- **Users** (Admin): `Export -> GET /userses/{id}/export`; `SetPermissionAccess -> POST /userses/{id}/actions/set-permission-access`; `ToggleActive -> POST /userses/{id}/actions/toggle-active`
- **InventoryMasterData** (Warehouse): `ToggleCategoryActive -> POST /inventory-master-datas/{id}/actions/toggle-category-active`; `ToggleUomActive -> POST /inventory-master-datas/{id}/actions/toggle-uom-active`; `ToggleWarehouseActive -> POST /inventory-master-datas/{id}/actions/toggle-warehouse-active`
- **IrnReceipt** (Warehouse): `AddLine -> POST /irn-receipts/{id}/actions/add-line`; `Approve -> POST /irn-receipts/{id}/actions/approve`
- **PartCatalogueAdmin** (Warehouse): `CommodityGroups -> GET /part-catalogue-admins/commodity-groups`; `Employees -> GET /part-catalogue-admins/employees`
- **PartCatalogueDedup** (Warehouse): `DuplicateCandidates -> GET /part-catalogue-dedups/duplicate-candidates`; `ExistingParts -> GET /part-catalogue-dedups/existing-parts`
- **PartCatalogueLookup** (Warehouse): `CheckManufacturer -> GET /part-catalogue-lookups/check-manufacturer`; `Equipment -> GET /part-catalogue-lookups/equipment`; `Group1 -> GET /part-catalogue-lookups/group1`; `Group2 -> GET /part-catalogue-lookups/group2`; `Manufacturers -> GET /part-catalogue-lookups/manufacturers`; `ResolveTechnicalClass -> GET /part-catalogue-lookups/resolve-technical-class`; `Suppliers -> GET /part-catalogue-lookups/suppliers`; `TechnicalClass -> GET /part-catalogue-lookups/technical-class`; `TechnicalClasses -> GET /part-catalogue-lookups/technical-classes`
- **PartCatalogueRequest** (Warehouse): `Download -> GET /part-catalogue-requests/{id}/download`; `ExportPdf -> GET /part-catalogue-requests/{id}/export-pdf`; `ExportXlsx -> GET /part-catalogue-requests/{id}/export-xlsx`
- **StoreRequisition** (Warehouse): `AddLine -> POST /store-requisitions/{id}/actions/add-line`; `AdvanceStatus -> POST /store-requisitions/{id}/actions/advance-status`
