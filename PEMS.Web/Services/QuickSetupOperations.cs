using Microsoft.AspNetCore.Routing;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Identity;
using PEMS.Web.Security;

namespace PEMS.Web.Services;

/// <summary>SCRUM-743: what QuickSetupController showed, as rows: the same ISetupService status, the permission-dependent links; PermissionMatrix and AI routing stay SystemAdmin-only (no dead link for others).</summary>
public sealed class QuickSetupOperations(ISetupService setup, LinkGenerator links) : IQuickSetupOperations
{
    /// <summary>The pages that were controllers and are definition pages now (the link generator knows no action for them any more).</summary>
    private static string ConvertedPage(string? controller) => controller switch
    {
        "Employee" => "/p/employees", "Projects" => "/p/projects", "Discipline" => "/p/discipline-workspaces", "PermissionMatrix" => "/p/permission-matrix", "Team" => "/p/teams", "InventoryMasterData" => "/p/inventory-master-data", _ => ""
    };

    public async Task<List<QuickSetupRow>> StepsAsync(List<string> userRoles)
    {
        var status = await setup.GetStatusAsync();
        var isAdmin = userRoles.Contains(SystemRoles.SystemAdmin);
        var rows = new List<QuickSetupRow>
        {
            new("progress", "System setup", "How ready is PEMS for operational use? This page reads real data - nothing here is estimated.",
                status.IsFullyComplete ? "Complete" : $"{status.CompletedCount} of {status.TotalCount} areas ready ({status.PercentComplete}%)", "", "", "")
        };
        foreach (var step in status.Steps)
        {
            var adminOnly = step.Key is "Permissions" or "AIConfiguration";
            var url = step.ActionController != null && step.ActionAction != null && (!adminOnly || isAdmin)
                ? links.GetPathByAction(step.ActionAction, step.ActionController) ?? ConvertedPage(step.ActionController) : "";
            rows.Add(new QuickSetupRow(step.Key, step.Title, step.Description,
                step.Status switch { SetupStepStatus.Complete => "Ready", SetupStepStatus.Blocked => "Blocked", _ => "Not started" },
                string.Join("; ", step.MissingDependencies), adminOnly && !isAdmin ? "Requires SystemAdmin" : step.ActionLabel ?? "", url));
        }
        return rows;
    }
}
