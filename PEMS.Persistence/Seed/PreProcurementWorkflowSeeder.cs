using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PEMS.Persistence.Context;
using PEMS.Persistence.Identity;
using PEMS.WorkflowPlatform.Entities;

namespace PEMS.Persistence.Seed;

/// <summary>
/// SCRUM-471: the pre-procurement workflow. Each procurement demand (replenishment recommendation sent by the warehouse,
/// direct-purchase work-order line, engineering MR issued to procurement) is one task in the procurement role queue of My Work
/// (one stage, Sequential, buyer or procurement manager may act). PreProcurementService completes it when the buyer converts
/// or accepts the item and cancels it when the buyer dismisses or returns it. Roles come from RoleCatalog; nothing is granted here.
/// </summary>
public static class PreProcurementWorkflowSeeder
{
    public const string Module = "PreProcurement";
    public const string Category = "PreProcurement";
    /// <summary>The business object is the procurement demand (its id).</summary>
    public const string BusinessObjectType = "ProcurementDemand";
    public const string StageName = "BuyerReview";

    public static async Task SeedAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<PEMSDbContext>();
        if (await db.WorkflowDefinitions.AnyAsync(x => x.Module == Module && x.Category == Category)) return;

        var definition = new WorkflowDefinition
        {
            Name = "Pre-procurement review",
            Category = Category,
            Module = Module,
            Description = "A procurement demand waits for the buyer's decision (SCRUM-471).",
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
            NameFa = "بررسی کارشناس خرید",
            StageOrder = 1,
            StageType = "Sequential",
            ResponsibleRole = string.Join(",", RoleCatalog.ProcurementBuyer, RoleCatalog.ProcurementManager),
            ApprovalStrategy = "AllApprovalsRequired",   // Sequential: one record whose role list is OR (as in the Permit chain)
            IsMandatory = true
        };
        version.Stages.Add(stage);

        version.Transitions.Add(new WorkflowTransition { Version = version, FromStage = null, ToStage = stage, ActionType = "Start", Priority = 0 });
        version.Transitions.Add(new WorkflowTransition { Version = version, FromStage = stage, ToStage = null, ActionType = "Complete", Priority = 0 });
        version.Transitions.Add(new WorkflowTransition { Version = version, FromStage = stage, ToStage = null, ActionType = "Cancel", Priority = 0 });

        definition.Versions.Add(version);
        db.WorkflowDefinitions.Add(definition);
        await db.SaveChangesAsync();
    }
}
