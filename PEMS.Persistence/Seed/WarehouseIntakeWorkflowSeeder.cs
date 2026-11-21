using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PEMS.Persistence.Context;
using PEMS.Persistence.Identity;
using PEMS.WorkflowPlatform.Entities;

namespace PEMS.Persistence.Seed;

/// <summary>
/// SCRUM-472: the warehouse intake workflow. When a planner requests the stock materials of a work order, one task lands in
/// the warehouse role queue of My Work (one stage, Sequential, any warehouse role may act). The storekeeper sources the
/// lines from a warehouse they pick (WarehouseIntakeService creates the linked store requisition); when every stock line is
/// sourced the service completes the task. Cancel closes it when the work order no longer needs the materials.
/// Roles are the existing warehouse roles from RoleCatalog; nothing is granted here.
/// </summary>
public static class WarehouseIntakeWorkflowSeeder
{
    public const string Module = "WarehouseIntake";
    public const string Category = "WarehouseIntake";
    /// <summary>The business object is the work order (its id); the type keeps it apart from the work-order lifecycle instance.</summary>
    public const string BusinessObjectType = "WorkOrderMaterialIntake";
    public const string StageName = "WarehouseIntake";

    public static async Task SeedAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<PEMSDbContext>();
        if (await db.WorkflowDefinitions.AnyAsync(x => x.Module == Module && x.Category == Category)) return;

        var definition = new WorkflowDefinition
        {
            Name = "Warehouse intake of work-order materials",
            Category = Category,
            Module = Module,
            Description = "Stock lines of a work order wait for the storekeeper to pick the supplying warehouse (SCRUM-472).",
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
            NameFa = "تأمین از انبار",
            StageOrder = 1,
            StageType = "Sequential",
            ResponsibleRole = string.Join(",", RoleCatalog.WarehouseManager, RoleCatalog.WarehouseStorekeeper),
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
