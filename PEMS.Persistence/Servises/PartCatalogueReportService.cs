using Microsoft.EntityFrameworkCore;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;

namespace PEMS.Persistence.Services;

/// <summary>
/// SCRUM-478: read-only reports of the Part Catalogue. Rows follow the request list's visibility rule
/// (<see cref="PartCatalogueRequestScope"/>). The period means:
/// <list type="bullet">
/// <item>activity: the date of the action (the request's creation date for "Create");</item>
/// <item>equipment and stages: requests created in the period;</item>
/// <item>throughput (completed, registered, days to complete): the completion or action date.</item>
/// </list>
/// </summary>
public class PartCatalogueReportService : IPartCatalogueReportService
{
    public const string CreateAction = "Create";

    private static readonly string[] OpenStatuses =
    {
        PartCatalogueStatuses.Draft, PartCatalogueStatuses.InApproval, PartCatalogueStatuses.Returned, PartCatalogueStatuses.OrganizationResolutionRequired
    };

    private readonly PEMSDbContext _ctx;
    private readonly PartCatalogueRequestScope _scope;

    public PartCatalogueReportService(PEMSDbContext ctx, IRoleModuleActionService actions)
    {
        _ctx = ctx;
        _scope = new PartCatalogueRequestScope(ctx, actions);
    }

    private sealed record Req(int Id, string? Number, string Type, string Status, string? StepKey, int RequesterId, string? Mpn, string? Assigned,
        string? TechClass, DateTime Created, DateTime? Submitted, DateTime? Completed);

    public async Task<PartCatalogueReportDto> GetReportAsync(PartCatalogueReportFilter filter, PcActor actor, DateTime? today = null)
    {
        filter ??= new PartCatalogueReportFilter();
        var now = today ?? DateTime.Now;
        var (start, end) = PartCatalogueReportPeriods.Resolve(filter.Period, filter.From, filter.To, now);
        bool InPeriod(DateTime d) => (start == null || d >= start) && (end == null || d < end);

        var seeAll = await _scope.CanSeeAllAsync(actor);
        var scoped = _scope.Query(seeAll, actor, await _scope.PoolKeysAsync(actor.EmployeeId));

        // filters that are not the period or the person
        var q = scoped;
        if (filter.SectionId.HasValue) q = q.Where(r => r.SectionId == filter.SectionId);
        if (!string.IsNullOrWhiteSpace(filter.RequestType)) q = q.Where(r => r.RequestType == filter.RequestType);
        if (!string.IsNullOrWhiteSpace(filter.Status)) q = q.Where(r => r.Status == filter.Status);
        if (filter.EquipmentId.HasValue) q = q.Where(r => r.Equipment.Any(e => e.EquipmentId == filter.EquipmentId));

        var requests = await q.Select(r => new Req(r.PartCatalogueRequestId, r.RequestNumber, r.RequestType, r.Status, r.CurrentStepKey,
                r.RequesterEmployeeId, r.ManufacturerPartNumber, r.AssignedPartNumber,
                r.TechnicalClass != null ? r.TechnicalClass.Code + " - " + r.TechnicalClass.Description : null,
                r.CreatedDate, r.SubmittedDate, r.CompletedDate))
            .ToListAsync();
        var byId = requests.ToDictionary(r => r.Id);
        var ids = byId.Keys.ToList();

        var approvals = await _ctx.PartCatalogueRequestApprovals.AsNoTracking()
            .Where(a => ids.Contains(a.PartCatalogueRequestId))
            .Select(a => new { a.PartCatalogueRequestId, a.Action, a.ActorEmployeeId, a.ActionDate, a.StepKey })
            .ToListAsync();

        var report = new PartCatalogueReportDto { Filter = filter, PeriodStart = start, PeriodEnd = end, SeesAll = seeAll };

        // ---------------------------------------------------------------- activity (who did what)
        var activity = new List<PcReportActivityRow>();
        foreach (var r in requests.Where(r => InPeriod(r.Created)))
            activity.Add(Row(r, r.RequesterId, CreateAction, null, r.Created));
        foreach (var a in approvals.Where(a => InPeriod(a.ActionDate) && IsReported(a.Action)))
            activity.Add(Row(byId[a.PartCatalogueRequestId], a.ActorEmployeeId, a.Action, a.StepKey, a.ActionDate));
        if (filter.EmployeeId.HasValue) activity = activity.Where(x => x.EmployeeId == filter.EmployeeId).ToList();

        var names = await NamesAsync(activity.Select(x => x.EmployeeId).Concat(requests.Select(r => r.RequesterId)));
        foreach (var row in activity) row.EmployeeName = names.GetValueOrDefault(row.EmployeeId, "#" + row.EmployeeId);

        report.Activity.People = activity.GroupBy(x => x.EmployeeId).Select(g => new PcReportPersonRow
        {
            EmployeeId = g.Key,
            EmployeeName = names.GetValueOrDefault(g.Key, "#" + g.Key),
            Created = g.Count(x => x.Action == CreateAction),
            Submitted = g.Count(x => x.Action is PartCatalogueActions.Submit or PartCatalogueActions.Resubmit),
            Approved = g.Count(x => x.Action == PartCatalogueActions.Approve),
            Returned = g.Count(x => x.Action == PartCatalogueActions.Return),
            Cancelled = g.Count(x => x.Action == PartCatalogueActions.Cancel),
            PartNumbersRegistered = g.Count(x => x.Action == PartCatalogueActions.RegisterPartNumber)
        }).OrderByDescending(p => p.PartNumbersRegistered).ThenByDescending(p => p.Total).ThenBy(p => p.EmployeeName).ToList();
        report.Activity.Rows = Cap(activity.OrderByDescending(x => x.Date).ThenByDescending(x => x.RequestId).ToList(), report);

        // equipment and stages: with a person filter, the requests that person raised
        var own = filter.EmployeeId.HasValue ? requests.Where(r => r.RequesterId == filter.EmployeeId).ToList() : requests;

        // ---------------------------------------------------------------- equipment coverage
        var createdIds = own.Where(r => InPeriod(r.Created)).Select(r => r.Id).ToList();
        var lines = await _ctx.PartCatalogueRequestEquipment.AsNoTracking()
            .Where(e => createdIds.Contains(e.PartCatalogueRequestId))
            .Select(e => new
            {
                e.PartCatalogueRequestId, e.EquipmentId, e.EquipmentText, e.InstalledQuantity,
                Tag = e.Equipment != null ? e.Equipment.TagNumber : null,
                Name = e.Equipment != null ? e.Equipment.Name : null,
                Manufacturer = e.Request!.Manufacturer != null ? e.Request.Manufacturer.Name : e.Request.NewManufacturerName,
                Supplier = e.Request.Supplier != null ? e.Request.Supplier.Name : null,
                e.Request.SupplierPartNumber,
                Commodity = e.Request.CommodityGroup1 != null ? e.Request.CommodityGroup1.Code + " - " + e.Request.CommodityGroup1.Description : null,
                Uom = e.Request.UnitOfMeasure != null ? e.Request.UnitOfMeasure.Code : null,
                e.Request.IsCritical, e.Request.IsCapital
            })
            .ToListAsync();
        if (filter.EquipmentId.HasValue) lines = lines.Where(l => l.EquipmentId == filter.EquipmentId).ToList();

        var lineIds = lines.Select(l => l.PartCatalogueRequestId).Distinct().ToList();
        var attributes = (await _ctx.PartCatalogueRequestAttributes.AsNoTracking()
                .Where(a => lineIds.Contains(a.PartCatalogueRequestId) && a.Value != null && a.Value != "")
                .Select(a => new { a.PartCatalogueRequestId, a.Name, a.Value, a.SortOrder }).ToListAsync())
            .GroupBy(a => a.PartCatalogueRequestId)
            .ToDictionary(g => g.Key, g => string.Join("; ", g.OrderBy(a => a.SortOrder).Select(a =>
                a.Name == PartCatalogueRequestAttribute.FreeFormatText ? a.Value! : a.Name + ": " + a.Value)));

        var parts = lines.Select(l =>
        {
            var r = byId[l.PartCatalogueRequestId];
            var registered = r.Status == PartCatalogueStatuses.Completed && !string.IsNullOrWhiteSpace(r.Assigned);
            return new PcReportEquipmentPartRow
            {
                EquipmentKey = EquipmentKey(l.EquipmentId, l.EquipmentText),
                EquipmentTag = l.Tag ?? l.EquipmentText?.Trim(),
                EquipmentName = l.Name,
                InstalledQuantity = l.InstalledQuantity,
                RequestId = r.Id, RequestNumber = r.Number, RequestType = r.Type, Status = r.Status,
                AssignedPartNumber = r.Assigned, ManufacturerName = l.Manufacturer, ManufacturerPartNumber = r.Mpn,
                SupplierName = l.Supplier, SupplierPartNumber = l.SupplierPartNumber,
                TechnicalClass = r.TechClass, CommodityGroup = l.Commodity, UnitOfMeasure = l.Uom,
                IsCritical = l.IsCritical, IsCapital = l.IsCapital,
                Attributes = attributes.GetValueOrDefault(r.Id),
                RequesterName = names.GetValueOrDefault(r.RequesterId),
                CreatedDate = r.Created, CompletedDate = r.Completed, IsRegistered = registered
            };
        }).ToList();

        report.Equipment.Equipment = parts.GroupBy(p => p.EquipmentKey).Select(g =>
        {
            var first = g.First();
            var line = lines.First(l => EquipmentKey(l.EquipmentId, l.EquipmentText) == g.Key);
            return new PcReportEquipmentRow
            {
                EquipmentKey = g.Key, EquipmentId = line.EquipmentId, EquipmentTag = first.EquipmentTag, EquipmentName = first.EquipmentName,
                InRegister = line.EquipmentId.HasValue,
                Requests = g.Select(p => p.RequestId).Distinct().Count(),
                RegisteredPartNumbers = g.Where(p => p.IsRegistered).Select(p => p.AssignedPartNumber!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                InProgress = g.Where(p => OpenStatuses.Contains(p.Status)).Select(p => p.RequestId).Distinct().Count(),
                Critical = g.Where(p => p.IsCritical).Select(p => p.RequestId).Distinct().Count()
            };
        }).OrderByDescending(e => e.RegisteredPartNumbers).ThenByDescending(e => e.Requests).ThenBy(e => e.EquipmentTag).ToList();
        report.Equipment.Parts = Cap(parts.OrderBy(p => p.EquipmentTag).ThenBy(p => p.AssignedPartNumber ?? p.RequestNumber).ToList(), report);

        // ---------------------------------------------------------------- stages and throughput
        var created = own.Where(r => InPeriod(r.Created)).ToList();
        var p = report.Pipeline;
        p.Total = created.Count;
        p.Add = created.Count(r => r.Type == PartCatalogueRequestTypes.Add);
        p.Change = created.Count(r => r.Type == PartCatalogueRequestTypes.Change);
        p.Open = created.Count(r => OpenStatuses.Contains(r.Status));
        p.ByStatus = new[] { PartCatalogueStatuses.Draft, PartCatalogueStatuses.InApproval, PartCatalogueStatuses.Returned,
                PartCatalogueStatuses.OrganizationResolutionRequired, PartCatalogueStatuses.Completed, PartCatalogueStatuses.Cancelled }
            .Select(s => new PcReportCount { Key = s, Label = s, Count = created.Count(r => r.Status == s) })
            .Where(c => c.Count > 0 || c.Key != PartCatalogueStatuses.OrganizationResolutionRequired)
            .ToList();

        var stepNames = await _ctx.PartCatalogueWorkflowSteps.AsNoTracking()
            .Select(s => new { s.StepKey, s.NameEn, s.NameFa, s.StepOrder }).ToListAsync();
        p.ByStep = created.Where(r => r.Status == PartCatalogueStatuses.InApproval)
            .GroupBy(r => string.IsNullOrWhiteSpace(r.StepKey) ? "-" : r.StepKey!)
            .Select(g =>
            {
                var step = stepNames.FirstOrDefault(s => s.StepKey == g.Key);
                return new { Order = step?.StepOrder ?? int.MaxValue, Row = new PcReportCount { Key = g.Key, Label = step?.NameEn ?? g.Key, LabelFa = step?.NameFa ?? "", Count = g.Count() } };
            })
            .OrderBy(x => x.Order).ThenBy(x => x.Row.Key).Select(x => x.Row).ToList();

        var completed = own.Where(r => r.Status == PartCatalogueStatuses.Completed && r.Completed.HasValue && InPeriod(r.Completed.Value)).ToList();
        p.CompletedInPeriod = completed.Count;
        p.PartNumbersRegisteredInPeriod = activity.Count(a => a.Action == PartCatalogueActions.RegisterPartNumber);
        var days = completed.Where(r => r.Submitted.HasValue && r.Completed >= r.Submitted)
            .Select(r => (r.Completed!.Value - r.Submitted!.Value).TotalDays).OrderBy(d => d).ToList();
        if (days.Count > 0)
        {
            p.AverageDaysToComplete = Math.Round(days.Average(), 1);
            p.MedianDaysToComplete = Math.Round(days.Count % 2 == 1 ? days[days.Count / 2] : (days[days.Count / 2 - 1] + days[days.Count / 2]) / 2, 1);
        }

        var open = created.Where(r => OpenStatuses.Contains(r.Status)).Select(r => (now - (r.Submitted ?? r.Created)).TotalDays).ToList();
        p.OpenAge = new List<PcReportCount>
        {
            new() { Key = "0-7", Label = "0-7", Count = open.Count(d => d <= 7) },
            new() { Key = "8-30", Label = "8-30", Count = open.Count(d => d > 7 && d <= 30) },
            new() { Key = "31-90", Label = "31-90", Count = open.Count(d => d > 30 && d <= 90) },
            new() { Key = "90+", Label = "90+", Count = open.Count(d => d > 90) }
        };

        // monthly trend: the months of the period (the last 12 months when the period is open)
        var first = start ?? new DateTime(now.Year, now.Month, 1).AddMonths(-11);
        var last = (end ?? new DateTime(now.Year, now.Month, 1).AddMonths(1)).AddDays(-1);
        var month = new DateTime(first.Year, first.Month, 1);
        for (var guard = 0; month <= last && guard < 60; month = month.AddMonths(1), guard++)
        {
            var m = month;
            p.Months.Add(new PcReportMonth
            {
                Month = m.ToString("yyyy-MM"),
                Created = own.Count(r => r.Created >= m && r.Created < m.AddMonths(1) && InPeriod(r.Created)),
                Completed = own.Count(r => r.Status == PartCatalogueStatuses.Completed && r.Completed >= m && r.Completed < m.AddMonths(1) && InPeriod(r.Completed!.Value))
            });
        }

        // ---------------------------------------------------------------- filter options
        var people = requests.Select(r => r.RequesterId).Concat(approvals.Select(a => a.ActorEmployeeId)).Distinct().ToList();
        var optionNames = await NamesAsync(people);
        report.Employees = people.Select(id => new PcReportOption { Id = id, Text = optionNames.GetValueOrDefault(id, "#" + id) })
            .OrderBy(o => o.Text).ToList();
        var scopedIds = scoped.Select(r => r.PartCatalogueRequestId);
        report.EquipmentOptions = await _ctx.PartCatalogueRequestEquipment.AsNoTracking()
            .Where(e => e.EquipmentId != null && scopedIds.Contains(e.PartCatalogueRequestId))
            .Select(e => new { e.EquipmentId, e.Equipment!.TagNumber, e.Equipment.Name }).Distinct()
            .OrderBy(e => e.TagNumber)
            .Select(e => new PcReportOption { Id = e.EquipmentId!.Value, Text = e.TagNumber + " - " + e.Name })
            .ToListAsync();

        return report;
    }

    private static bool IsReported(string action) => action is PartCatalogueActions.Submit or PartCatalogueActions.Resubmit
        or PartCatalogueActions.Approve or PartCatalogueActions.Return or PartCatalogueActions.Cancel or PartCatalogueActions.RegisterPartNumber;

    private static PcReportActivityRow Row(Req r, int employeeId, string action, string? stepKey, DateTime date) => new()
    {
        Date = date, EmployeeId = employeeId, Action = action, StepKey = stepKey, RequestId = r.Id, RequestNumber = r.Number,
        RequestType = r.Type, ManufacturerPartNumber = r.Mpn, AssignedPartNumber = r.Assigned, TechnicalClass = r.TechClass, Status = r.Status
    };

    private static string EquipmentKey(int? equipmentId, string? text) =>
        equipmentId.HasValue ? "E" + equipmentId.Value : "T:" + (text ?? "").Trim().ToUpperInvariant();

    private static List<T> Cap<T>(List<T> rows, PartCatalogueReportDto report)
    {
        if (rows.Count <= PartCatalogueReportDto.MaxRows) return rows;
        report.Truncated = true;
        return rows.Take(PartCatalogueReportDto.MaxRows).ToList();
    }

    private async Task<Dictionary<int, string>> NamesAsync(IEnumerable<int> ids)
    {
        var list = ids.Distinct().ToList();
        return (await _ctx.Employees.AsNoTracking().Where(e => list.Contains(e.EmployeeId))
                .Select(e => new { e.EmployeeId, e.FirstName, e.LastName }).ToListAsync())
            .ToDictionary(e => e.EmployeeId, e => (e.FirstName + " " + e.LastName).Trim());
    }
}
