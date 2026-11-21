using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;

namespace PEMS.Web.Services;

/// <summary>SCRUM-749: the read side of ShiftHandoverController over IShiftHandoverService.</summary>
public sealed class ShiftHandoverOperations(IShiftHandoverService service, PEMSDbContext context) : IShiftHandoverOperations
{
    public async Task<List<ShiftHandoverRow>> ListAsync(string? status = null)
        => (await service.GetAllAsync(string.IsNullOrWhiteSpace(status) ? null : status)).Select(h => new ShiftHandoverRow(h.ShiftHandoverId, h.OutgoingName ?? "", h.IncomingName ?? "", h.ShiftLabel ?? "", h.HandoverDate, h.Status, h.PreparedDate, h.AcknowledgedDate, h.CompletedDate, h.ItemCount)).ToList();

    public async Task<List<ShiftHandoverHeadRow>> HeadAsync(int id)
        => await service.GetDetailsAsync(id) is { } h ? [new ShiftHandoverHeadRow(h.ShiftHandoverId, h.OutgoingName ?? "", h.IncomingName ?? "", h.ShiftLabel ?? "", h.HandoverDate, h.Status, h.Notes ?? "", h.PreparedDate, h.AcknowledgedDate, h.CompletedDate)] : [];

    public async Task<List<ShiftHandoverItemRow>> ItemsAsync(int id) => (await service.GetDetailsAsync(id))?.Items.Select(i => new ShiftHandoverItemRow(i.ItemType, i.ReferenceType ?? "", i.ReferenceId, i.Description)).ToList() ?? [];
    public async Task<List<ShiftHandoverHistoryRow>> HistoryAsync(int id) => (await service.GetDetailsAsync(id))?.StatusHistory.Select(s => new ShiftHandoverHistoryRow(s.FromStatus, s.ToStatus, s.ChangedByName ?? "", s.ChangedDate, s.Reason ?? "")).ToList() ?? [];

    public async Task<List<ReportChoiceRow>> EmployeeChoicesAsync() => (await context.Employees.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.FirstName).Select(x => new { x.EmployeeId, x.FirstName, x.LastName }).ToListAsync())
        .Select(x => new ReportChoiceRow(x.EmployeeId.ToString(CultureInfo.InvariantCulture), x.FirstName + " " + x.LastName)).ToList();
}
