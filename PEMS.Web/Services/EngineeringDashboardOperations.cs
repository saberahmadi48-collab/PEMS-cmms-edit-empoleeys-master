using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;

namespace PEMS.Web.Services;

/// <summary>SCRUM-688: what EngineeringDashboardController computed, as read operations; the scope rule, the health score, the attention queue and the discipline risk are the same arithmetic as before.</summary>
public sealed class EngineeringDashboardOperations(IEngineeringDocumentService service, PEMSDbContext context, IAccessControlService access) : IEngineeringDashboardOperations
{
    private sealed record Scope(bool Restricted, int? DisciplineId, bool EffectiveRestricted, int? EffectiveDisciplineId);

    private async Task<Scope> ScopeAsync(List<string> roles, string userName, int? disciplineId)
    {
        var userId = string.IsNullOrEmpty(userName) ? null : await context.Users.AsNoTracking().Where(u => u.UserName == userName).Select(u => (int?)u.Id).FirstOrDefaultAsync();
        var canViewAll = (await access.AuthorizeForRolesAsync(roles, "EngineeringDocuments", "ViewAllDisciplines")).Allowed;
        var restricted = userId.HasValue && !canViewAll;
        int? mine = null;
        if (restricted)
        {
            var employeeId = await context.Users.AsNoTracking().Where(u => u.Id == userId!.Value).Select(u => u.EmployeeId).FirstOrDefaultAsync();
            var employee = employeeId.HasValue ? await context.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.EmployeeId == employeeId.Value) : null;
            if (employee is null) restricted = false; else mine = employee.DisciplineId;
        }
        return new Scope(restricted, mine, restricted || disciplineId.HasValue, restricted ? mine : disciplineId);
    }

    private async Task<List<EngineeringRevisionQualityDto>> QualityAsync(List<string> roles, string userName, int? projectId, int? disciplineId, int? minRevisionCount)
    {
        var s = await ScopeAsync(roles, userName, disciplineId);
        return await service.GetRevisionQualityAsync(s.EffectiveRestricted, s.EffectiveDisciplineId, minRevisionCount, projectId);
    }

    public async Task<List<EngineeringSummaryRow>> SummaryAsync(List<string> userRoles, string userName, int? projectId = null, int? disciplineId = null, int? minRevisionCount = null)
    {
        var s = await ScopeAsync(userRoles, userName, disciplineId);
        var summary = await service.GetSummaryCountsAsync(s.Restricted, s.DisciplineId);
        var quality = await service.GetRevisionQualityAsync(s.EffectiveRestricted, s.EffectiveDisciplineId, minRevisionCount, projectId);
        var known = quality.Where(x => x.FirstPassApproved.HasValue).ToList();
        var total = Math.Max(summary.Total, 1);
        var approvedRatio = summary.Total > 0 ? (double)summary.Approved / total : 0d;
        var pendingRatio = summary.Total > 0 ? (double)summary.Pending / total : 0d;
        var overdueRatio = summary.Total > 0 ? (double)summary.Overdue / total : 0d;
        var riskRatio = quality.Count > 0 ? (double)quality.Count(x => x.RejectionCount > 0 || x.RevisionCount >= 2) / quality.Count : 0d;
        var health = Math.Clamp((int)Math.Round((approvedRatio * 0.45 + (1d - pendingRatio) * 0.2 + (1d - overdueRatio) * 0.2 + (1d - riskRatio) * 0.15) * 100d), 0, 100);
        var firstPass = known.Count > 0 ? ((double)known.Count(x => x.FirstPassApproved == true) / known.Count).ToString("P0", CultureInfo.InvariantCulture) : "N/A";
        return [new EngineeringSummaryRow(summary.Total, summary.Approved, summary.Pending, summary.Rejected, summary.Frozen, summary.Overdue, health, firstPass,
            Math.Round(approvedRatio * 100, 1).ToString("0.0", CultureInfo.InvariantCulture) + "%")];
    }

    private static EngineeringQualityRow Row(EngineeringRevisionQualityDto x)
        => new(x.DocumentId, x.DocumentNo, x.DisciplineName ?? "-", x.VendorName ?? "-", x.Status, x.RejectionCount, x.RevisionCount, x.FirstPassApproved == true ? "Yes" : x.FirstPassApproved == false ? "No" : "-");

    public async Task<List<EngineeringQualityRow>> MostRejectedAsync(List<string> userRoles, string userName, int? projectId = null, int? disciplineId = null, int? minRevisionCount = null)
        => (await QualityAsync(userRoles, userName, projectId, disciplineId, minRevisionCount)).Where(x => x.RejectionCount > 0).OrderByDescending(x => x.RejectionCount).Take(10).Select(Row).ToList();

    public async Task<List<EngineeringQualityRow>> RevisionPressureAsync(List<string> userRoles, string userName, int? projectId = null, int? disciplineId = null, int? minRevisionCount = null)
        => (await QualityAsync(userRoles, userName, projectId, disciplineId, minRevisionCount)).Where(x => x.RevisionCount > 0).OrderByDescending(x => x.RevisionCount).Take(10).Select(Row).ToList();

    public async Task<List<EngineeringAttentionRow>> AttentionQueueAsync(List<string> userRoles, string userName, int? projectId = null, int? disciplineId = null, int? minRevisionCount = null)
        => (await QualityAsync(userRoles, userName, projectId, disciplineId, minRevisionCount))
            .Where(x => x.RejectionCount > 0 || x.RevisionCount >= 2).OrderByDescending(x => x.RejectionCount).ThenByDescending(x => x.RevisionCount).Take(6)
            .Select(x => new EngineeringAttentionRow(x.DocumentId, x.DocumentNo, string.IsNullOrWhiteSpace(x.DisciplineName) ? "-" : x.DisciplineName,
                x.RejectionCount > 0 ? $"{x.RejectionCount} rejection(s)" : "Revision pressure",
                x.RejectionCount > 2 || x.RevisionCount >= 4 ? "Critical" : x.RejectionCount > 0 ? "High" : "Medium", x.RevisionCount, x.RejectionCount)).ToList();

    public async Task<List<EngineeringDisciplineRow>> DisciplineRiskAsync(List<string> userRoles, string userName, int? projectId = null, int? disciplineId = null, int? minRevisionCount = null)
        => (await QualityAsync(userRoles, userName, projectId, disciplineId, minRevisionCount))
            .Where(x => !string.IsNullOrWhiteSpace(x.DisciplineName)).GroupBy(x => x.DisciplineName!)
            .Select(g => new EngineeringDisciplineRow(g.Key, g.Count(), g.Sum(x => x.RejectionCount),
                (g.Count() > 0 ? (double)g.Sum(x => x.RevisionCount) / g.Count() : 0d).ToString("0.0", CultureInfo.InvariantCulture),
                Math.Clamp((int)Math.Round((double)g.Count(x => x.RejectionCount > 0) / Math.Max(g.Count(), 1) * 55d + (double)g.Sum(x => x.RevisionCount) / Math.Max(g.Count(), 1) * 10d), 0, 100)))
            .OrderByDescending(x => x.RiskScore).ThenByDescending(x => x.Documents).Take(8).ToList();

    public async Task<List<ReportChoiceRow>> ProjectsAsync()
        => (await context.Projects.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.ProjectName).Select(x => new { x.ProjectId, x.ProjectName }).ToListAsync())
            .Select(x => new ReportChoiceRow(x.ProjectId.ToString(CultureInfo.InvariantCulture), x.ProjectName)).ToList();

    public async Task<List<ReportChoiceRow>> DisciplinesAsync()
        => (await context.Disciplines.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).Select(x => new { x.DisciplineId, x.Name }).ToListAsync())
            .Select(x => new ReportChoiceRow(x.DisciplineId.ToString(CultureInfo.InvariantCulture), x.Name)).ToList();
}
