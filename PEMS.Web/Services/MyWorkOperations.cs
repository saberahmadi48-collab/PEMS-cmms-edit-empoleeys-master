using System.Globalization;
using Microsoft.Extensions.Localization;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Web.Helpers;
using PEMS.Web.Resources;

namespace PEMS.Web.Services;

/// <summary>SCRUM-730: the My Work approvals as rows: the same service, the same order (overdue first, then personal), the same record links as the former page, labels in the language of the user.</summary>
public sealed class MyWorkOperations(IMyWorkService myWork, IStringLocalizer<SharedResource> localizer) : IMyWorkOperations
{
    public const int PageSize = 100;
    public const int MaxPages = 50;

    public async Task<List<MyWorkRow>> ApprovalsAsync(int? employeeId = null, IReadOnlyList<string>? roles = null, string? culture = null)
    {
        var fa = string.Equals(culture, "fa", StringComparison.OrdinalIgnoreCase);
        var rows = new List<MyWorkRow>();
        for (var page = 1; page <= MaxPages; page++)
        {
            var result = await myWork.GetApprovalsAsync(employeeId ?? 0, roles ?? [], null, false, false, page, PageSize);
            foreach (var t in result.Page.Items)
                rows.Add(new MyWorkRow(t.WorkflowInstanceStageId, t.Module, Label("MyWork_Module_" + t.Module, t.Module, culture), Stage(t, fa, culture), t.IsPersonal, t.BusinessObjectId,
                    WorkflowTaskLinks.TryGetPath(t.Module, t.BusinessObjectType, t.BusinessObjectId) ?? "", t.AssignedDate, t.DueDate, t.IsOverdue));
            if (result.Page.Items.Count == 0 || rows.Count >= result.Page.TotalCount) break;
        }
        return rows;
    }

    private string Stage(MyApprovalItemDto t, bool fa, string? culture)
    {
        if (!fa) return t.StageName;
        if (!string.IsNullOrWhiteSpace(t.StageNameFa)) return t.StageNameFa!;
        return Label(WorkflowStageLabels.ResourceKey(t.StageName), t.StageName, culture);
    }

    private string Label(string key, string fallback, string? culture)
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            if (!string.IsNullOrWhiteSpace(culture)) CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            var s = localizer[key];
            return s.ResourceNotFound ? fallback : s.Value;
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }
}
