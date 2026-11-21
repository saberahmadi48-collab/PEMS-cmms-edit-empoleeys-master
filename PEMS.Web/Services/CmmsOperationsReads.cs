using System.Globalization;
using Microsoft.Extensions.Localization;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Web.Resources;

namespace PEMS.Web.Services;

/// <summary>SCRUM-673: the CMMS operations dashboard and the backlog as rows; the numbers come from ICmmsOperationsService, the labels use the shared resources in the language of the user.</summary>
public sealed class CmmsOperationsReads(ICmmsOperationsService operations, IStringLocalizer<SharedResource> localizer) : ICmmsOperationsReads
{
    public async Task<CmmsTotalsRow> TotalsAsync()
    {
        var d = await operations.GetDashboardAsync();
        return new CmmsTotalsRow(1, d.WorkRequestTotal, d.WorkRequestPendingAction, d.WorkOrderTotal, d.WorkOrderOverdue);
    }

    public async Task<List<CmmsCountRow>> WorkRequestsByStatusAsync(string? culture = null)
    {
        var d = await operations.GetDashboardAsync();
        return
        [
            new(Label("Draft", culture), d.WorkRequestDraft), new(Label("Submitted", culture), d.WorkRequestSubmitted), new(Label("Under Review", culture), d.WorkRequestUnderReview),
            new(Label("Approved", culture), d.WorkRequestApproved), new(Label("Planned", culture), d.WorkRequestPlanned), new(Label("Converted", culture), d.WorkRequestConverted),
            new(Label("Rejected", culture), d.WorkRequestRejected), new(Label("Cancelled", culture), d.WorkRequestCancelled),
        ];
    }

    public async Task<List<CmmsCountRow>> WorkOrdersByStatusAsync(string? culture = null)
    {
        var d = await operations.GetDashboardAsync();
        return
        [
            new(Label("Open", culture), d.WorkOrderOpen), new(Label("Assigned", culture), d.WorkOrderAssigned), new(Label("In Progress", culture), d.WorkOrderInProgress),
            new(Label("On Hold", culture), d.WorkOrderOnHold), new(Label("Completed", culture), d.WorkOrderCompleted), new(Label("Closed", culture), d.WorkOrderClosed),
            new(Label("Cancelled", culture), d.WorkOrderCancelled), new(Label("Rejected", culture), d.WorkOrderRejected), new(Label("Critical & Open", culture), d.WorkOrderCriticalOpen),
        ];
    }

    public async Task<CmmsPmComplianceRow> PmComplianceAsync()
    {
        var pm = (await operations.GetDashboardAsync()).PmCompliance;
        return pm is null ? new CmmsPmComplianceRow(1, "NOT_READY", "N/A", 0, 0, 0, 0)
            : new CmmsPmComplianceRow(1, pm.DataStatus, pm.CompliancePercent.HasValue ? pm.CompliancePercent.Value.ToString("N1", CultureInfo.InvariantCulture) + "%" : "N/A", pm.CompliantCount, pm.CompletedLateCount, pm.OverdueCount, pm.NotYetDueCount);
    }

    public async Task<List<CmmsCountRow>> TypeBreakdownAsync()
        => (await operations.GetDashboardAsync()).MaintenanceTypeBreakdown?.Items.Select(i => new CmmsCountRow(i.TypeName, i.Count)).ToList() ?? [];

    public async Task<CmmsTypeSummaryRow> TypeSummaryAsync()
    {
        var tb = (await operations.GetDashboardAsync()).MaintenanceTypeBreakdown;
        return tb is null ? new CmmsTypeSummaryRow(1, "NOT_READY", "N/A")
            : new CmmsTypeSummaryRow(1, tb.DataStatus, tb.EmergencyWorkRatioPercent.HasValue ? tb.EmergencyWorkRatioPercent.Value.ToString("N1", CultureInfo.InvariantCulture) + "%" : "N/A");
    }

    public async Task<List<MaintenanceBacklogItemDto>> BacklogAsync(string? status = null, string? priority = null)
    {
        var all = new List<MaintenanceBacklogItemDto>();
        for (var page = 1; page <= 50; page++)
        {
            var result = await operations.GetBacklogAsync(status, priority, null, false, page, 100);
            all.AddRange(result.Items);
            if (result.Items.Count == 0 || all.Count >= result.TotalCount) break;
        }
        return all;
    }

    private string Label(string key, string? culture)
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            if (!string.IsNullOrWhiteSpace(culture)) CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            var s = localizer[key];
            return s.ResourceNotFound ? key : s.Value;
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }
}
