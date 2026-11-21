# Workflow platform - module contracts (SCRUM-103)

Status: 2026-10-27. Follows [WORKFLOW_PLATFORM_PHASE4_WORKFLOW_ADMINISTRATION.md](WORKFLOW_PLATFORM_PHASE4_WORKFLOW_ADMINISTRATION.md).

## Why

All 13 module workflows already ran on the single engine (`IWorkflowRuntime`), but Workflow Management showed only 3 as
editable. Three things kept the other 10 read-only:

1. a whitelist in `WorkflowAdministrationService`;
2. no explicit binding: they started through the legacy Module + Category path;
3. module code that depends on parts of its definition. For example, the document status is derived from the stage
   names Check/IDC/Approval/Issue, VDR reads the `DisciplineReview` stage, and MOC passes the senior approvers on
   `Approval -Approve-> SeniorApproval`.

Opening the whitelist alone would let an administrator break a module by renaming a stage or deleting a transition.

## What changed

| Piece | Where |
|---|---|
| Contract types and registry (engine level, reusable by BPMS) | `PEMS.WorkflowPlatform/Interfaces/WorkflowModuleContracts.cs`, `Services/WorkflowModuleContractRegistry.cs` |
| Contract check in the validator: `CONTRACT_ACTION_MISSING`, `CONTRACT_STAGE_MISSING`, `CONTRACT_STAGE_TYPE`, `CONTRACT_TRANSITION_MISSING`, `CONTRACT_BUSINESS_STATUS_MISSING` (errors, so Validate/Publish refuse) | `WorkflowVersionValidator.Contract` |
| The 13 contracts, plus the shared module action list (moved out of Program.cs) | `PEMS.Persistence/Workflow/PemsWorkflowContracts.cs` |
| Explicit, pinned binding for every module workflow | `PEMS.Persistence/Seed/WorkflowBindingBackfillSeeder.cs` (startup, after all workflow seeders) |
| Edit policy: active definition + active binding (the whitelist is gone); the reason names the contract | `WorkflowAdministrationService.EditablePolicy`, designer shows it |

The binding is pinned to the version the legacy path chose (the single active definition and its newest Published
version), so behaviour does not change. After an edit is published, an administrator moves the binding to the new
version (Workflow Management > Bindings). Running instances stay on the version they started with. The backfill
never recreates a binding an administrator turned off. It also skips a key that has several active definitions or no
Published version, and logs a warning.

## The contracts

| Module / Category | Entity type | Actions the code runs | Stages read by name | Hand-overs | BusinessStatus |
|---|---|---|---|---|---|
| EngineeringDocument / Approval | Document | Approve, Reject, Issue | Check, IDC (Parallel), Approval, Issue | start->Check, Check-Approve->IDC | - |
| VendorDocument / Review | VDRItem | Approve, Reject, AP, NC, AC, CO, RE, Issue | DisciplineReview (Parallel) | start->DisciplineReview | - |
| Contract / Finalization | Contract | Approve, Reject | DisciplineReview (Parallel) | start->DisciplineReview | - |
| MOC / ChangeApproval | MOC | Assess, Approve, Reject, Close | RiskAssessment, Approval, SeniorApproval | start->RiskAssessment, Approval-Approve->SeniorApproval | - |
| TechnicalQuery / Response | TechnicalQuery | Respond, Confirm, Reopen | PendingResponse, PendingConfirmation | start->PendingResponse, PendingResponse-Respond->PendingConfirmation | - |
| Permit / PermitApproval | Permit | Approve, Reject | - | - | - |
| NCR / CriticalApproval | NonConformanceReport | Approve, Reject | - | - | - |
| IRN / DispositionApproval | IrnReceiptDisposition | Approve, Reject | - | - | - |
| CMMS / StrategyApproval | MaintenanceStrategy | Approve, Reject | - | - | - |
| PartCatalogue / Approval | PartCatalogueRequest | Approve, Return | - | - | - |
| WorkOrder / MultiDisciplineReview | WorkOrderMultiDisciplineReview | Approve, Reject | DisciplineReview (Parallel) | start->DisciplineReview | - |
| WorkOrder / Standard | WorkOrderLifecycle | Approve, Cancel | - | - | every stage |
| WorkRequest / Standard | WorkRequest | Approve, Reject, Cancel, Complete | - | - | every stage |

Free to edit everywhere:
- participants (roles, permissions, employees, responsibilities);
- SLA and escalation;
- approval strategy;
- conditions;
- the display labels (`NameFa`, transition labels);
- extra stages and transitions;
- the order of stages that the contract does not pin.

## Rules for developers

- A module that starts a workflow ships three things in the same change: its seeder, its binding key (the `EntityType`
  it passes to `StartInstanceAsync`) and its contract in `PemsWorkflowContracts`.
  `PemsWorkflowContractTests` checks that each seeded definition meets its contract, that the binding resolves to
  the legacy choice, and that the entity types match the module constants.
- When module code starts reading something new from the definition (a stage name, an action), add it to the
  contract in the same change.
- Prefer `BusinessStatus` over stage names for record status. It frees the stage names, and the contract then only
  asks for a BusinessStatus on every stage. Converting the document, MOC and TQ status maps is follow-up work.

## Next for BPMS (existing tickets)

- SCRUM-90 (#69): migrate or continue in-flight instances on publish.
- SCRUM-88 (#67): visual studio on top of the current designer.
- SCRUM-98 (#77): simulator.
- SCRUM-94 (#73): routing, sub-process and loops.
- SCRUM-93 (#72): form per stage.
- SCRUM-89 (#68): SLA, timers and escalation in the runtime.

A user-built process has no contract, so it is limited only by the structural, security and runtime checks.
