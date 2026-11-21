using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;
using PEMS.Web.Models;
using PEMS.Web.Resources;

namespace PEMS.Web.Services;

/// <summary>SCRUM-705: what HomeController.Index computed, as read operations: the same queries and rules; a failing query is logged and shown as 0 or as a schema warning, it never breaks the page.</summary>
public sealed class HomeOperations(PEMSDbContext context, ILogger<HomeOperations> logger, IUserDashboardService dashboard, IRoleModuleActionService roleActions, IPermissionService permissions,
    ISetupService setup, IAccessControlService access, IMyWorkService myWork, IStringLocalizer<SharedResource> loc) : IHomeOperations
{
    private string T(string key) => loc[key].Value;

    private async Task<int> SafeAsync(Func<Task<int>> query, string table, List<string> failed)
    {
        try { return await query(); }
        catch (Exception ex) { logger.LogWarning(ex, "Home dashboard count of {Table} failed (a migration may be missing)", table); failed.Add(table); return 0; }
    }

    public async Task<List<HomeKpiRow>> KpisAsync()
    {
        var failed = new List<string>();
        return [new HomeKpiRow(
            await SafeAsync(() => context.Projects.CountAsync(x => x.IsActive), "Projects", failed),
            await SafeAsync(() => context.EngineeringDocuments.CountAsync(), "EngineeringDocuments", failed),
            await SafeAsync(() => context.EngineeringDocuments.CountAsync(DashboardDefinitions.PendingApproval), "EngineeringDocuments", failed),
            await SafeAsync(() => context.EngineeringDocuments.CountAsync(DashboardDefinitions.Rejected), "EngineeringDocuments", failed),
            await SafeAsync(() => context.EngineeringDocuments.CountAsync(DashboardDefinitions.ReturnedWithComments), "EngineeringDocuments", failed),
            await SafeAsync(() => context.DocumentRevisions.CountAsync(x => x.IsActive && x.Status != "Approved"), "DocumentRevisions", failed),
            await SafeAsync(() => context.MaterialRequisitions.CountAsync(x => x.IsActive && x.Status != "Closed" && x.Status != "Cancelled"), "MaterialRequisitions", failed),
            await SafeAsync(() => context.Contracts.CountAsync(DashboardDefinitions.ActiveContract), "Contracts", failed),
            await SafeAsync(() => context.WorkOrders.CountAsync(x => x.IsActive && x.Status != "Closed" && x.Status != "Completed"), "WorkOrders", failed),
            await SafeAsync(() => context.DocumentAttachments.CountAsync(x => x.AIStatus == "Failed"), "DocumentAttachments", failed),
            await SafeAsync(() => context.Equipments.CountAsync(), "Equipments", failed),
            await SafeAsync(() => context.Transmittals.CountAsync(x => x.IsActive), "Transmittals", failed))];
    }

    public async Task<List<HomeStatusRow>> DocumentsByStatusAsync()
    {
        try
        {
            return (await context.EngineeringDocuments.GroupBy(x => x.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync())
                .Select(x => new HomeStatusRow(T(x.Status), x.Count)).ToList();
        }
        catch (Exception ex) { logger.LogWarning(ex, "Home dashboard document status counts failed"); return []; }
    }

    public async Task<List<HomeDisciplineRow>> DisciplinesAsync()
    {
        try
        {
            var rows = await context.EngineeringDocuments.Where(x => x.DisciplineId != null).GroupBy(x => x.DisciplineId)
                .Select(g => new { DisciplineId = g.Key, Total = g.Count(), Approved = g.Count(x => x.Status == "Approved"), Pending = g.Count(x => x.Status == "Submitted" || x.Status == "Checked") })
                .OrderByDescending(x => x.Total).Take(8).ToListAsync();
            var ids = rows.Select(r => r.DisciplineId).ToList();
            var names = await context.Disciplines.Where(d => ids.Contains((int?)d.DisciplineId)).ToDictionaryAsync(d => d.DisciplineId, d => d.Name);
            return rows.Select(r => new DisciplinePerformanceRow(r.DisciplineId.HasValue && names.TryGetValue(r.DisciplineId.Value, out var n) ? n : "#" + r.DisciplineId, r.Total, r.Approved, r.Total - r.Approved, r.Pending))
                .Select(r => new HomeDisciplineRow(r.Discipline, r.Total, r.Approved, r.PendingApproval, r.ApprovedPercent)).ToList();
        }
        catch (Exception ex) { logger.LogWarning(ex, "Home dashboard discipline performance failed"); return []; }
    }

    private async Task<int?> UserIdAsync(string userName)
        => string.IsNullOrEmpty(userName) ? null : await context.Users.AsNoTracking().Where(u => u.UserName == userName).Select(u => (int?)u.Id).FirstOrDefaultAsync();

    public async Task<List<HomeAttentionRow>> AttentionAsync(List<string> userRoles, string userName)
    {
        var rows = new List<HomeAttentionRow>();
        static string count(int n) => n.ToString(CultureInfo.InvariantCulture);
        try
        {
            var userId = await UserIdAsync(userName);
            var employeeId = userId.HasValue ? await context.Users.Where(u => u.Id == userId.Value).Select(u => u.EmployeeId).FirstOrDefaultAsync() : null;
            if (employeeId.HasValue)
            {
                PEMS.Application.DTOs.MyApprovalsSummaryDto approvals;
                try { approvals = await myWork.GetSummaryAsync(employeeId.Value, userRoles); }
                catch (Exception ex) { logger.LogWarning(ex, "Home My Work approvals failed"); approvals = new PEMS.Application.DTOs.MyApprovalsSummaryDto(); }

                if (await roleActions.HasActionAsync(userRoles, "Permit", "Approve") || await roleActions.HasActionAsync(userRoles, "Permit", "Reject"))
                    rows.Add(new HomeAttentionRow(T("Dashboard_PermitApprovals"), count(approvals.ByModule.GetValueOrDefault("Permit")), "/Permit?pendingMyApproval=true"));
                if (await permissions.HasPermissionAsync(userRoles, "WorkOrder", "View"))
                    rows.Add(new HomeAttentionRow(T("Dashboard_AssignedWorkOrders"),
                        count(await context.WorkOrders.CountAsync(x => x.IsActive && x.AssignedEmployeeId == employeeId.Value && x.Status != "Closed" && x.Status != "Completed")), "/WorkOrder?assignedToMe=true"));
                rows.Add(new HomeAttentionRow(T("Dashboard_Referrals"), count(await context.LetterReferrals.CountAsync(r => r.Status != "Completed" && r.ReferredToEmployeeId == employeeId.Value)), "/Letter/MyReferrals"));

                var canViewAll = (await access.AuthorizeForRolesAsync(userRoles, "EngineeringDocuments", "ViewAllDisciplines")).Allowed;
                int reviews;
                if (canViewAll) reviews = await context.EngineeringDocuments.CountAsync(x => x.IsActive && (x.Status == "Submitted" || x.Status == "Checked"));
                else
                {
                    var mine = await context.Employees.Where(e => e.EmployeeId == employeeId.Value).Select(e => (int?)e.DisciplineId).FirstOrDefaultAsync();
                    reviews = await context.EngineeringDocuments.CountAsync(x => x.IsActive && x.Status == "IDC Review" && (x.DisciplineId == mine || x.DisciplineId == null));
                }
                rows.Add(new HomeAttentionRow(T("Dashboard_DocumentReviews"), count(reviews), "/EngineeringDocuments"));

                var other = approvals.ByModule.Where(kv => kv.Key != "Permit" && kv.Key != "EngineeringDocument").Sum(kv => kv.Value);
                if (other > 0) rows.Add(new HomeAttentionRow(T("MyWork_Card_Other"), count(other), "/p/my-work"));
            }
        }
        catch (Exception ex) { logger.LogWarning(ex, "Home My Work failed"); }

        try
        {
            if (await permissions.HasPermissionAsync(userRoles, "Setup", "View") && await setup.GetStatusAsync() is { IsFullyComplete: false } status)
                rows.Add(new HomeAttentionRow(T("System Setup") + ": " + string.Format(CultureInfo.CurrentCulture, T("Dashboard_SetupProgress"), status.CompletedCount, status.TotalCount), status.PercentComplete + "%", "/p/quick-setup"));
        }
        catch (Exception ex) { logger.LogWarning(ex, "Home setup status failed"); }
        return rows;
    }

    public async Task<List<HomeWidgetRow>> WidgetsAsync(string userName)
    {
        var userId = await UserIdAsync(userName);
        if (!userId.HasValue) return [];
        var rows = new List<HomeWidgetRow>();
        foreach (var w in (await dashboard.GetLayoutAsync(userId.Value)).Where(x => x.IsVisible).OrderBy(x => x.DisplayOrder))
        {
            object value;
            try
            {
                value = w.WidgetKey switch
                {
                    "PendingDocuments" => await context.EngineeringDocuments.CountAsync(x => x.IsActive && (x.Status == "Submitted" || x.Status == "Checked")),
                    "OverdueLetters" => await context.LetterReferrals.CountAsync(x => x.Status != "Completed" && x.DueDate.HasValue && x.DueDate.Value < DateTime.Now),
                    "RecentDocuments" => await context.EngineeringDocuments.Where(x => x.IsActive).OrderByDescending(x => x.CreatedDate).Take(5).Select(x => x.DocumentNo + " - " + x.Title).ToListAsync(),
                    "VDRSummary" => await context.VDRItems.CountAsync(x => x.IsActive && x.WorkflowStatus != "Closed"),
                    "IntakeSummary" => await context.DocumentAttachments.CountAsync(x => x.IntakeStage != null && x.IntakeStage != "Routed" && x.IntakeStage != "Rejected"),
                    "MyReferrals" => await context.LetterReferrals.Where(r => r.Status != "Completed" && context.Users.Any(u => u.Id == userId && u.EmployeeId == r.ReferredToEmployeeId)).CountAsync(),
                    _ => 0
                };
            }
            catch { value = 0; }
            rows.Add(new HomeWidgetRow(T(w.DisplayName), value is List<string> list ? (list.Count == 0 ? T("Dashboard_NothingToShow") : string.Join(" | ", list)) : Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""));
        }
        return rows;
    }
}
