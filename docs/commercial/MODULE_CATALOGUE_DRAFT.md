# Core edition and module catalogue (DRAFT for owner approval)

Plan row #112 (SCRUM-132). **Status: draft.** Everything marked `[proposed]` is Claude's reading of the repository and needs the owner's approval; nothing here is a commercial decision until the "Approved by" line is filled. Prepared 2026-11-03 for Hamid Maleki.

Decided already (owner, 2026-10-18): the BPMS core has its own edition and version (the installed product version is the core version); every module is a separate component with its own version; pricing is a perpetual licence plus annual maintenance; **no user limit** (limits are on sites and modules only, see [LICENSING_TECHNICAL_DESIGN.md](../design/LICENSING_TECHNICAL_DESIGN.md) C3). Builder limits live in the Supportability Matrix, not here.

Approved by: _not yet_

## 1. How this draft was made

Modules and dependencies are read from the controllers and areas of `PEMS.Web` (about 100 controllers, areas `Admin` and `Warehouse`), the sidebar, and the licensing design (`grant.modules`, `ModuleGrantedAsync`). Where the code gives no boundary, the grouping is a proposal. The licence payload already carries `edition` and a list of `modules`, so each module below needs a stable **module key** (lower-case, never renamed).

## 2. Core (included in every edition) `[proposed]`

| Core part | What it contains today | Key |
|---|---|---|
| Identity and security | Account, AccountRecovery, RecoveryCodes, WebAuthn, BreakGlass, MyProfile, Roles, Users, navigation visibility | `core.identity` |
| Organization and people | Employee, Team, Department, Discipline, organization chart and assignments | `core.organization` |
| Platform services | Settings, Theme, Culture, Notification, Help and KnowledgeBase, GlobalSearch and SearchApi, ImportCenter, Files and storage, DashboardSettings, QuickSetup, Database Update, License | `core.platform` |
| Work and tasks | MyWork, Task, TaskManager, ShiftHandover (cross-module work inbox) | `core.work` |
| Reporting | Report, ExecutiveDashboard, Kpi, EngineeringDashboard shell | `core.reporting` |
| UI library and builder runtime | PEMS.UI components, metadata registry, definition store (as the BPMS core grows) | `core.builder` |

## 3. Modules (each separately licensed and versioned) `[proposed]`

| Module key | Name | Contains (controllers) | Depends on | Core versions |
|---|---|---|---|---|
| `mod.assets` | Assets and equipment | Asset, Equipment, EquipmentClassification, EquipmentCriticality, MechanicalEquipment, Instrument, FunctionalLocation, Tag, Barcode, imports of equipment and functional locations | core | from 1.0 |
| `mod.maintenance` | Maintenance (CMMS) | WorkRequest, WorkOrder, JobPlan, MaintenancePlan, MaintenanceStrategy, MaintenanceCalendar, MaintenanceMasterData, MaintenanceHistoryImport, Crew, CmmsOperations, CmmsReadiness | `mod.assets` | from 1.0 |
| `mod.reliability` | Reliability (RCM, FMEA, condition monitoring) | RcmFmea, FailureModeCatalog, Reliability, ConditionReadingImport, CriticalityAssessment, StrategyImport | `mod.assets`, `mod.maintenance` | from 1.0 |
| `mod.permits` | Permit to work and safety | Permit, GasTest, Isolation, Simops, ExecutionGate, Inspection | `mod.assets`, `mod.maintenance` | from 1.0 |
| `mod.inventory` | Warehouse and inventory | area Warehouse (material items, IRN receipt, store requisition, replenishment, part catalogue), SparePart | core | from 1.0 |
| `mod.procurement` | Procurement | Vendor, Tender, Contract, MaterialRequisition, PurchaseOrder, PreProcurement, ProcurementPackage | `mod.inventory` | from 1.0 |
| `mod.documents` | Engineering documents | EngineeringDocuments, DocumentRegister, DocumentRevisions, DocumentUpload, DocumentWorkflow, DCC, VDR and imports, MDB and MDR imports, Transmittal, TechnicalQuery, Finding, Intake | core | from 1.0 |
| `mod.quality` | Quality and change | QC, MOC, Meeting | `mod.documents` | from 1.0 |
| `mod.correspondence` | Correspondence | Letter, LetterCompose, LetterCartable, LetterTemplate | core | from 1.0 |
| `mod.production` | Production | Production, ProductionOrder, ProductionRun | `mod.assets`, `mod.inventory` | from 1.0 |
| `mod.ai` | AI assistance | AI, AIExtraction, AIInsights, AISearch, ChatAssistant, OCRProcessing, TableExtraction, EngineeringDocumentIntelligence | `mod.documents` (extraction), core | from 1.0 |
| `mod.projects` | Projects | Projects, Performance | `mod.documents` | from 1.0 |

Not modules: `ComingSoon`, `Culture`, `Theme`, `NavigationBadge`, `Files`, `SearchApi` (platform pieces of core).

## 4. Editions `[proposed]`

No user limit in any edition. Editions differ by included modules and builder capabilities, not by seats.

| Edition | Core | Modules | Builder and automation |
|---|---|---|---|
| **Foundation** | included | any one operational module of the customer's choice, sold per module | form and list builder within the Supportability Matrix limits |
| **Professional** | included | the maintenance suite (`mod.assets`, `mod.maintenance`, `mod.inventory`, `mod.permits`) plus any others per module | builder plus workflow studio |
| **Enterprise** | included | all modules | builder, workflow studio, AI, multi-site |

Open choices for the owner: whether modules can be bought without an edition; whether `mod.ai` is an add-on to every edition; whether Foundation exists at all.

## 5. Dependency rules (to enforce in the licence and the registry)

1. A module can only be granted if every module it depends on is granted.
2. A module runs on the core versions listed in its row; the registry refuses to enable it on an older core.
3. Disabling a module hides its menu and API but never deletes its data.

## 6. Open points for approval

- The module keys and names in section 3.
- Whether `Meeting`, `Projects` and `Performance` belong where proposed.
- Edition names and what each includes (section 4).
- Which controllers stay in core when a module is not licensed (for example Notification entries of a missing module).
