# Read Me

PEMS - Backlog & Roadmap (rebuilt edition)

Created 2026-10-15 from the static PEMS audit plus lessons and capabilities of a reference platform. Since 2026-10-28 (SCRUM-446) the plan is a folder of markdown files, not an Excel workbook: one file per backlog row in plan/rows, the other pages in plan/sheets. The files are updated in place.

Pages (plan/sheets/*.md; the Backlog is plan/rows/NNNN.md, one file per row)

| Page | Purpose |
| --- | --- |
| Overview | Orientation - read first. |
| Read Me | This page: field guide, statuses, priorities. |
| Instructions | Standing rules applied in every task. |
| Backlog | The unified task list (source of truth): plan/rows/NNNN.md, one file per row. |
| Test Plan | QA cases for Done rows and for P0 rows after implementation. |
| Roadmap | Milestones and execution order. |
| Supportability Matrix | What is supported (capabilities, limits, platforms, versions) - updated in every finished task. |
| Licensing Design | Machine/install-locked licensing design and threat table. |
| Capability Checklist | Capabilities PEMS must offer, sourced from the reference platform. |
| Lessons | Durable lessons and facts (reference platform and PEMS). |
| Old to New Map | Mapping of every old backlog row to the new rows. |
| Summary | Automatic counts over the Backlog: `python tools/plan-sync/plan_sync.py summary`. |

Backlog fields (front matter of plan/rows/NNNN.md; Evidence, Prompt, Requirement and Report are sections of the file)

| Field | Meaning |
| --- | --- |
| # | Permanent number; never reused. |
| Doer | Claude = doable in a code session; User = decision or legal/commercial/operational work of the owner. |
| Area | Work area. |
| Track | Feature, Bug Fix, Tech Debt, Decision Needed, Library Modernization, Research, Docs, Legal/Business, Ops Process. |
| Status | Not Started, In Progress, Partial, Done, Hold (decided but paused by the PM until released), Later (deliberately deferred, kept in the backlog), Cancelled, Obsolete. Done only with evidence. |
| Confirmed by PM | Yes when the owner/PM has reviewed and confirmed the row (its scope, priority and milestone); No until then. Only the PM sets this flag; automated updates of the row preserve what the PM entered. |
| Sprint | Sprint the row is planned in (Sprint 1, Sprint 1 stretch); empty = backlog. The Sprint 1 page has goal, capacity and the committed list. |
| Owner | Accountable person for the row in a sprint (Member A / Member B until names are mapped); the doer may still be the assistant. |
| Group | Related tasks grouped into one workstream; filter on this column to see and decide a whole group together. |
| Priority | P0 sales blocker/dynamic core, P1 needed for commercial 1.0, P2 competitive, P3 later. |
| Evidence | What was seen in code/documents today (or a known pitfall). |
| Prompt | An executable instruction for one session. |
| Requirement | A testable acceptance criterion. |
| Report | After completion: the commit message and version (Instructions rule 7). |
| Milestone | M0...M6 per Roadmap. |
| Effort | S up to 1 day, M about 1 week, L about 2-3 weeks, XL over 3 weeks (first estimate). |
| Commercial | Commercial impact: Sales blocker, High, Medium, Low. |
| Depends | Dependencies by row number (#). |
| Reference (Titan) | Related row number in the reference platform's backlog (Titan); shown as # numbers. |
| Old Ref | Related row in the old audit (OLD-n) or OLD-Dn for existing capabilities. |

Components and versions (bill of materials)

| Component | Version / status |
| --- | --- |
| PEMS (system) | No official version defined - action: F_CI |
| PEMS.Web / Application / Domain / Persistence / WorkflowPlatform / AI | .NET 10 SDK, EF Core 10.0.10 |
| Plan files | 2.0 - 2026-10-28 (markdown folder plan/; the former Excel workbook 1.2 is kept in '0 - trash') |

Priorities

| Priority | Definition |
| --- | --- |
| P0 | Sales blocker or dynamic core - nothing proceeds without it |
| P1 | Needed for commercial release 1.0 |
| P2 | Competitive / next stage |
| P3 | Later or uncertain |

Areas

| Area | Internal code |
| --- | --- |
| Foundation, Architecture Decisions & Baseline | FND |
| Metadata Core & Dynamic Platform | DYN |
| Component Library & Design System | CMP |
| Dynamic Form Builder | FRM |
| Dynamic Module Builder | MOD |
| Workflow Studio & BPMS | WFL |
| Static-to-Dynamic Module Migration | MIG |
| Licensing & Protection | LIC |
| Company, Brand & Commercial Packaging | BRD |
| Online Demo | DEM |
| Commercial Website | WEB |
| Installation, Deployment & Updates | INS |
| Access, RLS, Sessions & Security | SEC |
| Integration, Events & API | INT |
| Quality, Testing & Performance | QA |
| Persian Localization, RTL & Jalali Calendar | LOC |
| Reporting, Dashboards & Export | RPT |
| Artificial Intelligence | AI |
| Domain: Engineering, EDMS/VDR & Records | EDM |
| Domain: CMMS/EAM & Field Operations | EAM |
| Domain: HR, Projects, Finance & Procurement | BIZ |
| Support, Operations & Documentation | OPS |
| Existing PEMS Capabilities (estimated from code) | DONE |
| DevOps, Source Control & Release Management | DEV |
| Customer Version & Deployment Management | CVM |
| Supportability Matrix & Module Versioning | SUP |
| Main Host: Landing & Demo Deployment | HST |
