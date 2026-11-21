using Microsoft.EntityFrameworkCore;
using PEMS.Application.Interfaces;
using PEMS.Application.Replenishment;
using PEMS.Persistence.Context;
using PEMS.Web.Security;

namespace PEMS.Web.Services;

/// <summary>SCRUM-663: what ReplenishmentController showed and did, as operations; the service keeps every rule, the switch (Features:ReplenishmentRecommendations:Enabled) answers nothing while it is off.</summary>
public sealed class ReplenishmentOperations(IReplenishmentRecommendationService service, IReplenishmentAvailability availability, PEMSDbContext context, IPermissionService permissions) : IReplenishmentOperations
{
    private const string Module = IReplenishmentRecommendationService.Module;
    private const int Limit = 2000;
    private const string Off = "This module is not available.";
    private const string NoRight = "You do not have permission for this action.";

    private async Task<ReplenishmentActor> ActorAsync(List<string> roles, string userName)
    {
        var user = string.IsNullOrEmpty(userName) ? null : await context.Users.AsNoTracking().Where(u => u.UserName == userName).Select(u => new { u.Id, u.EmployeeId }).FirstOrDefaultAsync();
        return new ReplenishmentActor(user?.Id, user?.EmployeeId, roles);
    }

    public async Task<List<ReplenishmentRow>> ListAsync(List<string> userRoles, string userName, string? status = null, string? q = null, bool definitiveOnly = false)
    {
        if (!availability.IsEnabled) return [];
        var list = await service.GetPagedAsync(new ReplenishmentFilter(string.IsNullOrWhiteSpace(status) ? null : status, string.IsNullOrWhiteSpace(q) ? null : q, definitiveOnly ? true : null), 1, Limit, await ActorAsync(userRoles, userName));
        if (!list.Authorized || list.Data is null) return [];
        return list.Data.Items.Select(r => new ReplenishmentRow(r.Id, r.MaterialCode, $"{r.MaterialCode} - {r.MaterialDescription}", r.WarehouseCode, r.UsableQuantity, r.UnreservedDemand, r.ShortageQuantity, r.Status, r.IsCurrentlyDefinitive, r.LastEvaluatedAt)).ToList();
    }

    public async Task<List<ReplenishmentRunRow>> LastRunAsync(List<string> userRoles, string userName)
    {
        if (!availability.IsEnabled) return [];
        var last = await service.GetLastRunAsync(await ActorAsync(userRoles, userName));
        return last.Data is { } r ? [new ReplenishmentRunRow(r.RunId, r.Status, r.StartedAt, r.CompletedAt, r.ResultsEvaluated, r.Created, r.Updated, r.Closed, r.NotDefinitive)] : [];
    }

    public async Task<List<ReplenishmentNoteRow>> NotesAsync(List<string> userRoles, string userName)
    {
        if (!availability.IsEnabled) return [];
        var last = await service.GetLastRunAsync(await ActorAsync(userRoles, userName));
        return last.Data?.NotDefinitiveSummary.Select(s => new ReplenishmentNoteRow(s.Contains(ReplenishmentRules.RequiresWarehouseAttribution, StringComparison.Ordinal) ? "Attribution" : "NotRecommended", s)).ToList() ?? [];
    }

    public async Task<List<ReplenishmentDetailRow>> DetailAsync(List<string> userRoles, string userName, int id)
    {
        if (!availability.IsEnabled) return [];
        var details = await service.GetDetailsAsync(id, await ActorAsync(userRoles, userName));
        if (!details.Authorized || details.Data is not { } d) return [];
        var s = d.Summary;
        var canReview = await permissions.HasPermissionAsync(userRoles, Module, "Edit");
        return [new ReplenishmentDetailRow(s.Id, s.MaterialCode, $"{s.MaterialCode} - {s.MaterialDescription}", s.WarehouseCode, s.Revision, s.Status, s.IsCurrentlyDefinitive, d.FirstDetectedAt, s.LastEvaluatedAt,
            s.UsableQuantity, d.OnHandQuantity, d.ReservedQuantity, d.GrossEligibleDemand, d.CoveredByReservations, s.ShortageQuantity, d.BlockingIssueCodes ?? "", d.NoticeCodes ?? "", d.RequiresWarehouseAttribution,
            d.LastRunStartedAt, d.LastRunCompletedAt, d.StockLastUpdatedAt, d.DemandLastChangedAt, d.DismissedAt, d.DismissedReason ?? "", d.ClosedReason ?? "", canReview && s.Status == "Open" && s.IsCurrentlyDefinitive)];
    }

    public async Task<List<ReplenishmentSourceRow>> SourcesAsync(List<string> userRoles, string userName, int id)
    {
        if (!availability.IsEnabled) return [];
        var details = await service.GetDetailsAsync(id, await ActorAsync(userRoles, userName));
        if (!details.Authorized || details.Data is null) return [];
        var i = 0;
        return details.Data.Sources.Select(x => new ReplenishmentSourceRow(++i, x.SourceType == 1 ? "Store requisition" : "Work order", x.IsRestricted ? "(restricted)" : x.ParentNumber ?? "#" + x.SourceLineId,
            x.GrossRemainingQuantity, x.CoveredByReservation, x.UnreservedQuantity)).ToList();
    }

    public async Task<List<ReplenishmentHistoryRow>> HistoryAsync(List<string> userRoles, string userName, int id)
    {
        if (!availability.IsEnabled) return [];
        var details = await service.GetDetailsAsync(id, await ActorAsync(userRoles, userName));
        if (!details.Authorized || details.Data is null) return [];
        var i = 0;
        return details.Data.History.Select(h => new ReplenishmentHistoryRow(++i, h.OccurredAt, h.Revision, h.Action, (h.FromStatus is null ? "" : h.FromStatus + " → ") + h.ToStatus, h.ShortageQuantity, h.UsableQuantity, h.ActorName ?? "", h.Note ?? "")).ToList();
    }

    public async Task<(bool Success, string? Error)> RunAsync(string? requestKey, ReplenishmentActor actor)
    {
        if (!availability.IsEnabled) return (false, Off);
        var result = await service.RunAsync(new ReplenishmentRunRequest(null, null, string.IsNullOrWhiteSpace(requestKey) ? Guid.NewGuid().ToString("N") : requestKey), actor);
        return result.Forbidden ? (false, NoRight) : (result.Success, result.Error);
    }

    private async Task<(bool Success, string? Error)> CommandAsync(int id, ReplenishmentActor actor, Func<byte[]?, Task<ReplenishmentCommandResult>> run)
    {
        if (!availability.IsEnabled) return (false, Off);
        var details = await service.GetDetailsAsync(id, actor);
        if (!details.Authorized) return (false, NoRight);
        if (details.Data is null) return (false, "Not found.");
        var result = await run(details.Data.RowVersion);
        return result.Forbidden ? (false, NoRight) : result.NotFound ? (false, "Not found.") : (result.Success, result.Error);
    }

    public Task<(bool Success, string? Error)> DismissAsync(int id, string reason, ReplenishmentActor actor)
        => CommandAsync(id, actor, version => service.DismissAsync(id, version, reason ?? "", actor));

    public Task<(bool Success, string? Error)> ReopenAsync(int id, string? note, ReplenishmentActor actor)
        => CommandAsync(id, actor, version => service.ReopenAsync(id, version, note, actor));
}
