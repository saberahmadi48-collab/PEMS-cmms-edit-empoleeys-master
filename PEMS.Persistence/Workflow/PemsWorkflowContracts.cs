using PEMS.Persistence.Seed;
using PEMS.Persistence.Services;
using PEMS.WorkflowPlatform.Interfaces;
using PEMS.WorkflowPlatform.Services;

namespace PEMS.Persistence.Workflow;

/// <summary>
/// SCRUM-103: the module contracts of every workflow PEMS code drives (13 definitions). Each lists only what the module
/// code really relies on - the actions it executes, the stages it reads by name, the hand-overs that carry dynamic
/// participants, or BusinessStatus when the record status comes from the stage. With these in the validator, every one
/// of the definitions is editable in Admin > Workflow Management without being able to break its module.
/// EntityType = the BusinessObjectType the module passes to StartInstanceAsync (also the binding key).
/// Keep in sync with the services named in each comment; PemsWorkflowContractTests checks the seeded definitions and keys.
/// </summary>
public static class PemsWorkflowContracts
{
    private const string Parallel = "Parallel";

    private static WorkflowStageRequirement S(string name, string? type = null) => new(name, type);
    private static WorkflowTransitionRequirement T(string? from, string action, string? to) => new(from, action, to);

    public static readonly IReadOnlyList<WorkflowModuleContract> All = new[]
    {
        // DocumentWorkflowService: Approve at Check passes the IDC disciplines; status is mapped from Check/IDC/Approval/Issue.
        new WorkflowModuleContract(EngineeringDocumentWorkflowSeeder.Module, EngineeringDocumentWorkflowSeeder.Category, "Document",
            new[] { "Approve", "Reject", "Issue" },
            new[] { S("Check"), S("IDC", Parallel), S("Approval"), S("Issue") },
            new[] { T(null, "Start", "Check"), T("Check", "Approve", "IDC") }),

        // VdrWorkflowService: disciplines are passed at start; the review sheet reads the "DisciplineReview" stage; lead codes and Issue.
        new WorkflowModuleContract(VdrWorkflowSeeder.Module, VdrWorkflowSeeder.Category, "VDRItem",
            new[] { "Approve", "Reject", "AP", "NC", "AC", "CO", "RE", "Issue" },
            new[] { S("DisciplineReview", Parallel) },
            new[] { T(null, "Start", "DisciplineReview") }),

        // ContractWorkflowService: reviewing disciplines are passed at start.
        new WorkflowModuleContract(ContractWorkflowSeeder.Module, ContractWorkflowSeeder.Category, ContractWorkflowSeeder.BusinessObjectType,
            new[] { "Approve", "Reject" },
            new[] { S("DisciplineReview", Parallel) },
            new[] { T(null, "Start", "DisciplineReview") }),

        // MOCService: Assess records the risk assessment; Approve at Approval passes the senior approvers; Close ends it.
        new WorkflowModuleContract(MOCWorkflowSeeder.Module, MOCWorkflowSeeder.Category, "MOC",
            new[] { "Assess", "Approve", "Reject", "Close" },
            new[] { S("RiskAssessment"), S("Approval"), S("SeniorApproval") },
            new[] { T(null, "Start", "RiskAssessment"), T("Approval", "Approve", "SeniorApproval") }),

        // TechnicalQueryService: Respond passes the confirmers; Confirm closes; Reopen goes back for a new response.
        new WorkflowModuleContract(TechnicalQueryWorkflowSeeder.Module, TechnicalQueryWorkflowSeeder.Category, "TechnicalQuery",
            new[] { "Respond", "Confirm", "Reopen" },
            new[] { S("PendingResponse"), S("PendingConfirmation") },
            new[] { T(null, "Start", "PendingResponse"), T("PendingResponse", "Respond", "PendingConfirmation") }),

        // PermitService: decision is Approve/Reject on the current stage; the permit status follows the instance outcome.
        new WorkflowModuleContract(PermitApprovalWorkflowSeeder.Module, PermitApprovalWorkflowSeeder.Category, PermitApprovalWorkflowSeeder.BusinessObjectType,
            new[] { "Approve", "Reject" }, Array.Empty<WorkflowStageRequirement>(), Array.Empty<WorkflowTransitionRequirement>()),

        // QCController (NCR disposition).
        new WorkflowModuleContract(NcrApprovalWorkflowSeeder.Module, NcrApprovalWorkflowSeeder.Category, "NonConformanceReport",
            new[] { "Approve", "Reject" }, Array.Empty<WorkflowStageRequirement>(), Array.Empty<WorkflowTransitionRequirement>()),

        // IrnReceiptController (IRN disposition).
        new WorkflowModuleContract(IrnDispositionWorkflowSeeder.Module, IrnDispositionWorkflowSeeder.Category, IrnDispositionWorkflowSeeder.BusinessObjectType,
            new[] { "Approve", "Reject" }, Array.Empty<WorkflowStageRequirement>(), Array.Empty<WorkflowTransitionRequirement>()),

        // MaintenanceStrategyCommandService.
        new WorkflowModuleContract(MaintenanceStrategyWorkflowSeeder.Module, MaintenanceStrategyWorkflowSeeder.Category, MaintenanceStrategyWorkflowSeeder.BusinessObjectType,
            new[] { "Approve", "Reject" }, Array.Empty<WorkflowStageRequirement>(), Array.Empty<WorkflowTransitionRequirement>()),

        // PartCatalogueService: Approve or Return on the current stage; status follows the instance outcome.
        new WorkflowModuleContract(PartCatalogueWorkflow.Module, PartCatalogueWorkflow.Context, PartCatalogueWorkflow.EntityType,
            new[] { "Approve", "Return" }, Array.Empty<WorkflowStageRequirement>(), Array.Empty<WorkflowTransitionRequirement>()),

        // WorkOrderReviewService: reviewing disciplines are passed at start.
        new WorkflowModuleContract(WorkOrderReviewWorkflowSeeder.Module, WorkOrderReviewWorkflowSeeder.Category, WorkOrderWorkflow.ReviewEntityType,
            new[] { "Approve", "Reject" },
            new[] { S("DisciplineReview", Parallel) },
            new[] { T(null, "Start", "DisciplineReview") }),

        // WorkOrderLifecycleService: the Work Order status is the stage's BusinessStatus; Approve moves on, Cancel cancels.
        new WorkflowModuleContract(WorkOrderWorkflow.Module, WorkOrderWorkflow.LifecycleContext, WorkOrderWorkflow.LifecycleEntityType,
            new[] { "Approve", "Cancel" }, Array.Empty<WorkflowStageRequirement>(), Array.Empty<WorkflowTransitionRequirement>(),
            RequiresBusinessStatus: true),

        // WarehouseIntakeService (SCRUM-472): Complete when every stock line is sourced; Cancel when the work order no longer needs them.
        new WorkflowModuleContract(WarehouseIntakeWorkflowSeeder.Module, WarehouseIntakeWorkflowSeeder.Category, WarehouseIntakeWorkflowSeeder.BusinessObjectType,
            new[] { "Complete", "Cancel" }, Array.Empty<WorkflowStageRequirement>(), Array.Empty<WorkflowTransitionRequirement>()),

        // PreProcurementService (SCRUM-471): Complete when the buyer converts or accepts the demand; Cancel when dismissed or returned.
        new WorkflowModuleContract(PreProcurementWorkflowSeeder.Module, PreProcurementWorkflowSeeder.Category, PreProcurementWorkflowSeeder.BusinessObjectType,
            new[] { "Complete", "Cancel" }, Array.Empty<WorkflowStageRequirement>(), Array.Empty<WorkflowTransitionRequirement>()),

        // PurchaseOrderApprovalService (SCRUM-470): the approver role of the PO authority rule is passed at start; Approve or Reject.
        new WorkflowModuleContract(PurchaseOrderApprovalWorkflowSeeder.Module, PurchaseOrderApprovalWorkflowSeeder.Category, PurchaseOrderApprovalWorkflowSeeder.BusinessObjectType,
            new[] { "Approve", "Reject" },
            new[] { S(PurchaseOrderApprovalWorkflowSeeder.StageName, Parallel) },
            new[] { T(null, "Start", PurchaseOrderApprovalWorkflowSeeder.StageName) }),

        // PurchaseOrderReceivingService (SCRUM-473, D7): Complete when every Hold of the IRN is resolved; Cancel is not used by the service.
        new WorkflowModuleContract(IrnHoldResolutionWorkflowSeeder.Module, IrnHoldResolutionWorkflowSeeder.Category, IrnHoldResolutionWorkflowSeeder.BusinessObjectType,
            new[] { "Complete", "Cancel" }, Array.Empty<WorkflowStageRequirement>(), Array.Empty<WorkflowTransitionRequirement>()),

        // WorkRequestService: the request status is the stage's BusinessStatus; Complete converts it to a Work Order.
        new WorkflowModuleContract(WorkRequestService.WorkflowModule, WorkRequestService.WorkflowContext, WorkRequestService.WorkflowEntityType,
            new[] { "Approve", "Reject", "Cancel", "Complete" }, Array.Empty<WorkflowStageRequirement>(), Array.Empty<WorkflowTransitionRequirement>(),
            RequiresBusinessStatus: true),
    };

    public static void Register(IWorkflowModuleContractRegistry registry)
    {
        foreach (var c in All) registry.Register(c);
    }

    public static IWorkflowModuleContractRegistry CreateRegistry()
    {
        var registry = new WorkflowModuleContractRegistry();
        Register(registry);
        return registry;
    }

    /// <summary>
    /// Module action names beyond the core ones (name only, no code runs), so the seeded definitions validate. Moved here from
    /// Program.cs so the app, the seeders and the tests use one list.
    /// </summary>
    public static IWorkflowActionRegistry CreateActionRegistry()
    {
        var registry = new WorkflowActionRegistry();
        var legacy = new (string Module, string[] Actions)[]
        {
            (EngineeringDocumentWorkflowSeeder.Module, new[] { "Issue" }),
            (MOCWorkflowSeeder.Module, new[] { "Assess", "Close" }),
            (TechnicalQueryWorkflowSeeder.Module, new[] { "Confirm", "Reopen", "Respond" }),
            (VdrWorkflowSeeder.Module, new[] { "AC", "AP", "CO", "NC", "RE", "Issue" }),
        };
        foreach (var (module, actions) in legacy)
            foreach (var a in actions)
                registry.RegisterModuleAction(module, a, WorkflowActionKind.Outcome, "Legacy module action");
        WorkOrderWorkflow.Register(registry);   // Phase 3B: Hold, Comment
        return registry;
    }
}
