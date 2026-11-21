using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PEMS.Persistence.Context;
using PEMS.Persistence.Workflow;
using PEMS.WorkflowPlatform.Entities;
using PEMS.WorkflowPlatform.Interfaces;
using PEMS.WorkflowPlatform.Services;

namespace PEMS.Persistence.Seed;

/// <summary>
/// SCRUM-103: gives every module workflow (PemsWorkflowContracts) an explicit binding, so Workflow Management can edit it.
/// Same choice the legacy Module+Category path makes today (the single active definition, its newest Published version),
/// pinned like the Work Request binding: publishing an edited version changes nothing until an administrator moves the
/// binding to it. Idempotent and conservative:
///  - a key that already has a binding (active or deliberately deactivated) is left alone;
///  - no active definition, several active definitions (legacy ambiguity) or no Published version: skipped and logged;
///  - instances are never touched.
/// Runs after all workflow seeders.
/// </summary>
public static class WorkflowBindingBackfillSeeder
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<PEMSDbContext>();
        var logger = services.GetService<ILoggerFactory>()?.CreateLogger(typeof(WorkflowBindingBackfillSeeder).FullName!);
        var bindings = new WorkflowBindingService(db);
        var actor = new WorkflowActor(null, "system-seed");

        foreach (var c in PemsWorkflowContracts.All)
        {
            if (await db.WorkflowBindings.AnyAsync(b => b.Module == c.Module && b.EntityType == c.EntityType && b.Context == c.Category))
                continue;

            var defs = await db.WorkflowDefinitions
                .Where(d => d.IsActive && d.Module == c.Module && d.Category == c.Category).ToListAsync();
            if (defs.Count != 1)
            {
                logger?.LogWarning("Workflow binding for ({Module}, {EntityType}, {Context}) not created: {Count} active definitions.",
                    c.Module, c.EntityType, c.Category, defs.Count);
                continue;
            }

            var published = await db.WorkflowVersions
                .Where(v => v.WorkflowDefinitionId == defs[0].WorkflowDefinitionId && v.Status == WorkflowVersionStatus.Published)
                .OrderByDescending(v => v.VersionNumber).FirstOrDefaultAsync();
            if (published == null)
            {
                logger?.LogWarning("Workflow binding for ({Module}, {EntityType}, {Context}) not created: no Published version.",
                    c.Module, c.EntityType, c.Category);
                continue;
            }

            await bindings.CreateAsync(c.Module, c.EntityType, c.Category, defs[0].WorkflowDefinitionId, published.WorkflowVersionId, null, null, actor);
        }
    }
}
