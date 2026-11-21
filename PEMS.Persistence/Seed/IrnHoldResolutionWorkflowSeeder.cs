using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PEMS.Persistence.Context;
using PEMS.Persistence.Identity;
using PEMS.WorkflowPlatform.Entities;

namespace PEMS.Persistence.Seed;

/// <summary>
/// SCRUM-473 (D7, owner 2026-10-30): Hold resolution of a goods receipt. When QA approves an IRN disposition that leaves Hold
/// quantity, one task per IRN lands in the Warehouse Manager queue of My Work. The manager splits each Hold into accepted (posted
/// to stock) and rejected (the buyer's replace / close-short decision, D6b); PurchaseOrderReceivingService completes the task when
/// no Hold is left. Roles come from RoleCatalog; nothing is granted here.
/// </summary>
public static class IrnHoldResolutionWorkflowSeeder
{
    public const string Module = "IRN";
    public const string Category = "HoldResolution";
    /// <summary>The business object is the IRN (its id); the type keeps it apart from the disposition approval instance.</summary>
    public const string BusinessObjectType = "IrnHold";
    public const string StageName = "HoldResolution";

    public static async Task SeedAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<PEMSDbContext>();
        if (await db.WorkflowDefinitions.AnyAsync(x => x.Module == Module && x.Category == Category)) return;

        var definition = new WorkflowDefinition
        {
            Name = "Goods receipt - Hold resolution",
            Category = Category,
            Module = Module,
            Description = "Hold quantity of an approved goods receipt waits for the Warehouse Manager to accept or reject it (SCRUM-473).",
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
            NameFa = "تعیین تکلیف کالای نگه‌داشته",
            StageOrder = 1,
            StageType = "Sequential",
            ResponsibleRole = RoleCatalog.WarehouseManager,
            ApprovalStrategy = "AllApprovalsRequired",
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
