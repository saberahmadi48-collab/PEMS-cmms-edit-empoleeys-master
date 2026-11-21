using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PEMS.Persistence.Context;
using PEMS.WorkflowPlatform.Entities;

namespace PEMS.Persistence.Seed;

/// <summary>
/// SCRUM-470: purchase-order approval by the PO authority table. One Parallel stage whose participant is not fixed in the
/// definition: PurchaseOrderApprovalService passes the approver role of the matched authority rule at start (the same
/// mechanism as the VDR / contract discipline reviews). Approve completes it; Reject returns the PO to Draft.
/// No role or amount is hard-coded here; the admin owns the authority table.
/// </summary>
public static class PurchaseOrderApprovalWorkflowSeeder
{
    public const string Module = "PurchaseOrder";
    public const string Category = "PurchaseOrderApproval";
    public const string BusinessObjectType = "PurchaseOrder";
    public const string StageName = "Approval";

    public static async Task SeedAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<PEMSDbContext>();
        if (await db.WorkflowDefinitions.AnyAsync(x => x.Module == Module && x.Category == Category)) return;

        var definition = new WorkflowDefinition
        {
            Name = "Purchase order approval",
            Category = Category,
            Module = Module,
            Description = "Approval of a submitted purchase order by the role of the matching PO authority rule (SCRUM-470).",
            IsActive = true,
            CreatedDate = DateTime.Now
        };
        var version = new WorkflowVersion
        {
            Definition = definition,
            VersionNumber = 1,
            Status = "Published",
            EffectiveDate = DateTime.Now,
            PublishedDate = DateTime.Now
        };
        var stage = new WorkflowStage
        {
            Version = version,
            Name = StageName,
            NameFa = "تأیید سفارش خرید",
            StageOrder = 1,
            StageType = "Parallel",
            ResponsibleRole = null,            // the approver role comes from the authority rule at start
            ApprovalStrategy = "AllApprovalsRequired",
            IsMandatory = true
        };
        version.Stages.Add(stage);
        version.Transitions.Add(new WorkflowTransition { Version = version, FromStage = null, ToStage = stage, ActionType = "Start", Priority = 0 });
        version.Transitions.Add(new WorkflowTransition { Version = version, FromStage = stage, ToStage = null, ActionType = "Approve", Priority = 0 });
        version.Transitions.Add(new WorkflowTransition { Version = version, FromStage = stage, ToStage = null, ActionType = "Reject", Priority = 0 });

        definition.Versions.Add(version);
        db.WorkflowDefinitions.Add(definition);
        await db.SaveChangesAsync();
    }
}
