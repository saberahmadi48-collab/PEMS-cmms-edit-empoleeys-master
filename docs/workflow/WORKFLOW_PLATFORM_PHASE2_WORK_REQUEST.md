# PEMS Workflow Platform — Phase 2: Work Request Migration + Dynamic Workflow UI

Scope: **Work Request only**. No other module was migrated. Uses the existing `PEMS.WorkflowPlatform` (Phase 1); no second engine.

## 1. Executive summary

New Work Requests are submitted into the central workflow runtime through an **explicit binding** (`WorkRequest / WorkRequest / Standard`, pinned to a published version). The Work Request Details page draws the timeline, current stage, available actions, participants and workflow history from `IWorkflowReadModelService`. The dynamic proof (test `Reliability_Review_added_by_configuration_appears_for_new_requests_only_without_any_UI_or_service_change`) publishes a version 2 with an extra "Reliability Review" stage, re-pins the binding, and shows that new requests get the stage while in-flight requests stay on version 1 — with no change to the view or to `WorkRequestService`.

What is *not* done (see §14): in-flight legacy Work Requests (no workflow instance) keep the old status buttons; participants are permission-based pseudo-roles. The page was subsequently verified in a real browser during the closure audit — see `WORKFLOW_PLATFORM_PHASE2_CLOSURE_AUDIT.md` (which also records the defects that audit found and fixed; this document reflects the final state).

## 2. Pre-migration architecture (verified against code)

- `WorkRequest.Status` string (`Draft, Submitted, UnderReview, Approved, Rejected, Planned, Converted, Cancelled`). Default `Draft`.
- `WorkRequestService.AdvanceStatusAsync` validated a hard-coded `LegalTransitions` dictionary, set Reviewed/Approved/Rejection fields, wrote `WorkRequestStatusHistory` (append-only). `ConvertToWorkOrderAsync` (Planned + equipment + once) created the Work Order and set `Converted`.
- `WorkRequestController` held a second hard-coded map (`NextStatusMap`, removed in the closure audit — the single legacy map now lives in `WorkRequestService.GetLegacyNextStatus`) and per-status permission mapping: Submit/StartReview/Cancel need `WorkRequest.Create`; Approve/Plan/Reject/Convert need `WorkRequest.Approve` (via `IPermissionService`, which delegates to `IAccessControlService`).
- `Details.cshtml` decided every button by `Model.Status` checks and showed no timeline; history came only from `WorkRequestStatusHistory`.
- No use of `PEMS.WorkflowPlatform`. No return/assign/escalate actions existed.
- Other consumers of `WorkRequest.Status`: dashboards/counts (`CmmsOperationsService`), Equipment passport, Work Order — all keep reading the same column.

## 3. Workflow definition (mirrors the existing flow — no invented stages)

`WorkRequestWorkflowSeeder` (idempotent; built through Draft → Validate → Publish with config audit):

| Stage (order) | BusinessStatus | Outgoing transitions (permission) |
|---|---|---|
| Submitted (10) | Submitted | Approve→Under Review "Start Review" (Create); Cancel (Create) |
| Under Review (20) | UnderReview | Approve→Approved (Approve); Reject (Approve); Cancel (Create) |
| Approved (30) | Approved | Approve→Planned "Plan" (Approve); Reject (Approve); Cancel (Create) |
| Planned (40) | Planned | Complete "Convert to Work Order" (Approve); Cancel (Create) |

Persian stage names/labels are stored as data (`NameFa`, `LabelFa`). Draft is deliberately outside the workflow: *Submit* is the instance start.

## 4. Platform extensions (module-neutral, additive)

Only what Work Request required:
- `WorkflowStage.NameFa`, `WorkflowStage.BusinessStatus`; `WorkflowTransition.Label`, `LabelFa` (data for bilingual UI and for mapping a stage to a module's legacy status column).
- Core action **`Cancel`**; a `Cancel` outcome ends the instance as `Cancelled` (previously any non-Reject end was `Completed`).
- Validator understands participant `Permission:Resource.Action` (checked against the permission catalog instead of the role catalog).
- Read model: `GetCurrentInstanceForEntityAsync`, `GetRuntimeViewForEntityAsync`; action `Label/LabelFa`, stage `NameFa/BusinessStatus`.

## 5. Binding

`WorkRequest / WorkRequest / Standard` → definition "Work Request Approval", **pinned** to v1 (`PinnedVersionId`). Publishing a later version alone changes nothing; an administrator re-pins with `IWorkflowBindingService.ChangeAsync` (audited). The seeder never recreates a binding an administrator deactivated. Ambiguous/inactive/missing binding → `Submit` fails with a clear error and does not change the Work Request.

## 6. Runtime integration (responsibility split)

- **Platform**: stage/transition graph, participants, `RequiredPermission` check (via `AccessControlWorkflowAuthorizationHook`), history, version pinning, one-running-instance-per-entity for binding starts.
- **`WorkRequestService`** (domain): permission-to-participant expansion, checks the action is *allowed* in the read model before touching data, projects business status from `BusinessStatus`, sets Reviewed/Approved/RejectionReason, writes `WorkRequestStatusHistory`, enforces conversion rules (Planned, equipment, once) and creates the Work Order. Status change + audit + workflow transition are persisted in **one** `SaveChanges` (the runtime's).
- `Complete` cannot be executed as a generic action; only via `ConvertToWorkOrderAsync`.
- New API: `SubmitAsync`, `ExecuteWorkflowActionAsync`, `GetWorkflowViewAsync`, `ConvertToWorkOrderAsync(…, actor)`. `AdvanceStatusAsync` remains **only** for records without an instance and refuses instance-governed requests and Draft→Submitted while a binding exists.

## 7. UI integration

`Details.cshtml`: timeline via the existing stateless `_PemsStageDiagram`, fed by looping over `Workflow.Stages`; action buttons looped from `Workflow.AvailableActions` (label from `LabelFa/Label`, disabled + reason when not allowed); participants shown under the current stage; *Workflow History* from the runtime; existing status table kept as a collapsed *Business Audit*. A guard test asserts the view contains no hard-coded stage names. Business actions (Convert to Work Order, Submit/Cancel for Draft) stay outside the generic action loop. Legacy in-flight records show the previous buttons with a "Legacy record" badge. Layout stays compact (no new cards beyond a timeline card and a collapsed audit).

## 8. Security model

Chain: session user → roles (server side, never from the form) → `WorkRequestService.ExpandRolesAsync` (real permissions → `Permission:WorkRequest.X` pseudo-roles) → runtime participant match → transition `RequiredPermission` via the hook → Work Request business rule → state change. Tests verify: unauthorized action rejected server-side with nothing changed; the runtime hook rejects even when the service layer is bypassed (forged pseudo-role); legacy path cannot bypass; convert needs Approve. Not applicable to Work Request: SoD (`ForbidInitiator`) — the existing flow had none, so none was added (platform SoD is covered by Phase 1 tests).

## 9. Versioning behavior

Instances pin their `WorkflowVersionId`. v1 is immutable (guard). Test: request A (v1) in review; v2 published and pinned; request B uses v2; A's read model has no Reliability Review and A's next Approve goes straight to Approved; B goes Under Review → Reliability Review → Approved with status staying `UnderReview` (mapped from configuration).

## 10. Dynamic Reliability Review proof

Configuration only: clone v1 → add stage (order 25, `BusinessStatus=UnderReview`, Persian name) → replace the Under Review "Approve" edge → validate → publish → re-pin. Verified in test: timeline order, current stage, available actions/labels for the new stage, participants, history entry, permissions (requester still denied), A unaffected. No source file (view, controller, service) changes for this.

## 11. Database changes

Migration `20260919234615_WorkflowPlatformPhase2StageBusinessMapping` (additive): `WorkflowStages.NameFa nvarchar(200) null`, `.BusinessStatus nvarchar(50) null`; `WorkflowTransitions.Label`, `.LabelFa nvarchar(100) null`. No Work Request table changed. Verified up → down → up on a disposable database (dropped). Applied to the local `PEMSDB`. The workflow definition/binding rows are created by the seeder at the next application start (not by the migration).

## 12. Tests

`PEMS.Tests/Cmms/WorkRequestWorkflowTests.cs` (24 tests after the closure audit: seeder, binding, submit, full lifecycle, reject/cancel, conversion rules, security, read model, legacy, view guard, versioning, Reliability Review). Existing Work Request, workflow, Help and language-purity tests unchanged except the intentional count update in §13.

## 13. Modified existing tests

`HelpContentSeederTests.SeedAsync_TotalArticleCount_MatchesPhase1Through4`: expected article count 55 → 56 because a new bilingual Help article `work-request-workflow` was added (assertion updated by design; comment lists the new article). No other existing test was changed in this phase.

## 14. Remaining limitations

- **Legacy in-flight records** (status past Draft, no instance) continue on the old `LegalTransitions` path; `AdvanceStatusAsync` and the legacy buttons remain until they drain. No backfill/adoption was attempted (would need a "start at stage" runtime capability).
- Participants are permission-based pseudo-roles, so the UI shows permission names ("WorkRequest.Approve") rather than people/teams; there is no per-user assignment, no return action, no SLA/escalation execution, no notifications.
- Stage→status mapping is by `BusinessStatus`; a new stage without it keeps the current status.
- (Fixed in closure audit) the stage where a request was cancelled is now reported by the read model as `Cancelled` and shown with the red stop marker.
- Browser QA was performed in the closure audit (EN/LTR, FA/RTL, 1440/820/390 px); remaining UI limitations are listed there (raw business status values, English workflow-definition names, timeline/table horizontal scroll inside their cards on phones).
- Draft still exists outside the workflow. The legacy status map (`GetLegacyNextStatus` / `LegalTransitions`) remains in the service for instance-less records only.

## 15. Rollback

1. Deactivate the binding (`IWorkflowBindingService.DeactivateAsync`); new Submit will fail with a clear message — so also revert the code (git) if the old flow is required: revert the Phase 2 commit(s); legacy `AdvanceStatusAsync` handles Draft→Submitted again when no binding resolves.
2. Existing instances/history are untouched and remain valid data.
3. Schema: `dotnet ef database update WorkflowPlatformPhase1Hardening --project PEMS.Migrations --startup-project PEMS.Web` (drops only the four new nullable columns).

## 16. Recommended next candidate

**Work Order review (`WorkOrder / MultiDisciplineReview`)** — already on the platform with dynamic participants, so it validates the read model on a parallel-stage workflow with the least new capability required. Do not start without separate authorization.
