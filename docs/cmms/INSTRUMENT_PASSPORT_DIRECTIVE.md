# Instrument Equipment Passport - implementation directive (archived)

> Moved on 2026-10-25 (SCRUM-367) from `.claude/CLAUDE.md`, which is now a pointer to `AGENTS.md`. This is a task-specific directive for the Instrument Passport / calibration module, not a project-wide rule. Project-wide rules live in `docs/guidelines/`. Read the matching Jira issue before using it.


## Project Overview

**PEMS** is a .NET multi-layer engineering + CMMS platform for oil & gas / petrochemical projects.

### Solution Structure (Clean Architecture)

| Project | Responsibility |
|---------|----------------|
| `PEMS.Domain` | Entities, Enums, Common (`BaseEntity`) — **no dependencies** |
| `PEMS.Application` | DTOs, Interfaces, Application Services |
| `PEMS.Persistence` | EF Core DbContext, Configurations, Repositories, Migrations, Seed |
| `PEMS.Infrastructure` | Cache, external integrations |
| `PEMS.AI` | Ollama, OCR (Tesseract), Document parsing, AI extraction |
| `PEMS.Web` | ASP.NET Core MVC, Controllers, Views, Areas (Admin) |
| `PEMS.Shared` | Shared models/helpers |
| `PEMS.Tests` | Unit/Integration tests |
| `PEMS.WorkflowPlatform` | Workflow-related logic |

**Rules (mandatory):**
- Business logic lives in `PEMS.Application` / `PEMS.Persistence`, **never** in Controllers.
- New entities inherit `BaseEntity` when possible (`CreatedBy`, `ModifiedBy`, `RowVersion`, `TenantId`).
- Use existing patterns for DTOs, Services, Controllers, Views.
- Prefer Persian comments in domain entities when explaining business rules (project convention).
- After schema changes:  
  `dotnet ef migrations add <Name> --project PEMS.Migrations --startup-project PEMS.Web`  
  `dotnet ef database update --project PEMS.Migrations --startup-project PEMS.Web`

---

## Existing CMMS Core (already implemented)

### Key Domain Entities

- **Equipment** — central equipment register (`TagNumber`, `EquipmentType`, `Area`, `PIDNumber`, Manufacturer, Model, Serial, Status, Project, Discipline…)
- **Asset** — links to Equipment (`AssetNumber`, `FunctionalLocation`, `Criticality`, `MaintenanceClass`, `CommissioningDate`)
- **Instrument** — instrumentation specialization of Equipment (`InstrumentType` PT/LT/FT/TT…, RangeMin/Max, Unit, SignalType, CalibrationInterval)
- **WorkOrder** — aligned with **ISO 14224** (FailureModeCode, FailureCauseCode, RootCause, DowntimeHours, Labor via `WorkOrderLabor`, Team/Employee assignment)
- **MaintenancePlan** — PM schedules (Calendar / Meter / Both triggers, IntervalDays, MeterInterval, NextDueDate…)
- **MaintenanceStrategy**, **MaintenanceHistory**, **MaintenanceSpareUsage**, **MaintenanceDocument**
- **SparePart**, **FailureRecord**
- **EquipmentCriticality**, **FunctionalLocation**
- Specialized equipment: `RotatingEquipment`, `StaticEquipment`, `ElectricalEquipment`, `MechanicalEquipment`, `Valve`, `Cable`, `Panel`, `IOPoint`, `ControlLoop`…

### Existing Controllers (CMMS-related)
`EquipmentController`, `EquipmentImportController`, `EquipmentCriticalityController`,  
`WorkOrderController`, `MaintenancePlanController`, `MaintenanceStrategyController`,  
`SparePartController`, `EmployeeController`, `TeamController`…

### Important Conventions Already in Code
- WorkOrder types: `Preventive`, `Corrective`, `Emergency`
- Priority: `Low`, `Medium`, `High`, `Critical`
- Status flow: `Open` → `Assigned` → `InProgress` → `Completed` → `Closed`
- ISO 14224 fields are **required** for Corrective/Emergency analysis (MTBF, recurring failure patterns).
- Equipment import stores raw `MRNumberRaw` / `PONumberRaw`; real links only after human confirmation in Relationship Explorer.

---

## Current Goal: Instrument Equipment Passport + Full CMMS Instrumentation Module

The user has an Excel template “Instrument Equipment Passport” and wants it properly integrated into CMMS.

### Source of Truth Concepts (from the Excel template)

1. **Master Instrument List** → central register (one row per Tag)
2. **Equipment Passport** → printable individual card (common fields only)
3. **Calibration History** → append-only events (never overwrite)
4. **Maintenance History** → PM / CM / Inspection / Repair / Replacement
5. **Spare Parts** → linked by Tag No.
6. **Documents** → linked by Tag No.
7. Lookup lists: System (DCS/ESD/F&G/PLC), Signal Class (AI/AO/DI/DO…), Criticality, Priority, Status, Calibration Result, Maintenance Type

### Recommended Database Design Direction

Extend / refine existing entities rather than creating a parallel system:

#### 1. Strengthen `Instrument` (or create `InstrumentPassport` detail table)
Add missing passport fields that are currently absent or incomplete:

- Service / Description, Loop No., System, Signal Class
- Process Variable, Engineering Unit, Measuring Range, Accuracy
- Output Signal, Power Supply, Communication, IP/Enclosure
- Process Connection, Electrical Connection
- Hazardous Area, Ex Protection, Zone/Gas Group/Temp Class
- Fail Action / Position, Alarm/Trip Setpoint, SIL / Safety Function
- Junction Box / Terminal, Marshalling Cabinet, Cable No., Barrier/Relay
- I/O Card / Channel, System Cabinet, Related Equipment
- Calibration Interval (Months), Last Calibration, Next Calibration Due
- Last Maintenance, Next Maintenance Due
- Current Status

Keep `Equipment` as the parent; `Instrument` holds instrumentation-specific passport data.

#### 2. Calibration History (new or extend existing)
```text
CalibrationHistory
- Id, InstrumentId / EquipmentId, TagNo
- CalibrationDate, CalibrationType, StandardReference
- RangeUnit, AsFound, AsLeft, Result
- TechnicianId, CertificateNo, NextDue, Remarks
- CreatedAt (append-only)

3. Reuse / enhance MaintenanceHistory + WorkOrder
Do not duplicate. Link passport actions to existing WorkOrder and MaintenanceHistory.
4. Spare Parts & Documents

Use existing SparePart + junction table (AssetSparePart / InstrumentSparePart)
Reuse document entities (Document, EngineeringDocument, MaintenanceDocument) with Tag/Equipment link

5. Lookups
Prefer proper lookup tables or enums instead of free strings where possible (System, SignalClass, Criticality, Priority, Status, CalibrationResult, MaintenanceType).
Functional Requirements for Claude
When implementing the CMMS Instrumentation / Passport module:

Master Instrument List view
Filterable, sortable, exportable
Columns aligned with the Excel Master list
Calculated NextDue and status (Overdue / Due Soon / OK)

Equipment Passport page (printable card)
Read from Equipment + Instrument
Show latest calibration & maintenance dates
Use N/A for non-applicable fields

Calibration Management
History grid (append-only)
Auto-calculate Next Due from Interval
Overdue dashboard widget

Integration with existing CMMS
Creating a calibration or PM should be able to generate a WorkOrder
Criticality must influence priority and scheduling
Spare parts consumption should update stock and link to WorkOrder

Import
Support Excel import compatible with the provided Instrument Equipment Passport template
Follow the same safe import pattern already used for Equipment (staging + human confirmation where needed)



Architecture & Coding Rules for Claude
Do

Follow existing naming and folder structure exactly.
Put new DTOs in PEMS.Application/DTOs/.
Put interfaces in PEMS.Application/Interfaces/.
Put implementations in PEMS.Application/Services/ or PEMS.Persistence/.
Add EF Configurations in PEMS.Persistence/Configurations/.
Keep Controllers thin.
Use existing PagedResult<T> for lists.
Respect IsActive soft-delete style already used in the project.
Write Persian XML comments on domain entities when explaining business/ISO rules (project style).
After adding entities → create and apply EF migration.
Prefer extending Instrument + new CalibrationHistory over inventing a completely separate passport table hierarchy.

Don’t

Do not put business logic or EF queries in Controllers/Views.
Do not break existing Equipment / WorkOrder / MaintenancePlan flows.
Do not hard-delete history records (calibration & maintenance are append-only).
Do not invent new parallel “Asset vs Equipment vs Instrument” models that conflict with current relationships.
Do not ignore ISO 14224 fields on corrective work orders.

Preferred Implementation Order

Extend Instrument entity + EF configuration + migration
Add CalibrationHistory entity + migration
DTOs + Application services
Controllers + Views (Master List, Passport card, Calibration History)
Overdue calculation & dashboard widgets
Excel import aligned with the Passport template
Link to WorkOrder / SparePart / Documents


Tech Stack Notes

.NET (ASP.NET Core MVC)
EF Core + SQL Server (or whatever is configured in the project)
Identity / custom User & Role system already present
Ollama for local AI (existing module — do not break it)
Docker support exists (Dockerfile)


Knowledge Graph
This repo uses graphify.
When exploring relationships or answering architecture questions:

Prefer graphify query "...", graphify path "A" "B", graphify explain "..." if graphify-out/ exists.
Run graphify update . after significant code changes.


Language

User communicates mainly in Persian.
Code, class names, and technical identifiers stay in English.
UI labels and domain comments may be Persian where the existing codebase already does so.


Summary for Claude
You are working on PEMS, a real engineering + CMMS system.
The immediate task is to turn the “Instrument Equipment Passport” Excel concept into a proper, integrated CMMS module that:

Uses the existing Equipment → Instrument hierarchy
Adds full passport fields + Calibration History
Stays compatible with WorkOrder, MaintenancePlan, SparePart, and ISO 14224 practices already in the codebase
Follows Clean Architecture and project conventions strictly

Always inspect existing entities and controllers before creating new ones. Extend, don’t duplicate.
