using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;
using PEMS.Web.Areas.Warehouse.Models;
using PEMS.Web.Helpers;
using PEMS.Web.Resources;

namespace PEMS.Web.Services;

/// <summary>SCRUM-659, 660, 661: what the Part Catalogue lookup, dedup and report controllers answered, as operations; the services and the workbook builder are the same.</summary>
public sealed class PartCatalogueOperations(IPartCatalogueLookupService lookups, IPartCatalogueDuplicateService duplicates, IPartCatalogueReportService reports, IRoleModuleActionService actions,
    IStringLocalizer<SharedResource> localizer, PEMSDbContext context) : IPartCatalogueOperations
{
    private static readonly string[] AnyAction = ["Create", "View", "Review", "Approve", "Admin"];
    private static readonly string[] ReportActions = ["View", "Review", "Approve", "Admin"];
    private static readonly string[] Tabs = ["summary", "people", "activity", "equipment", "parts", "stages"];

    private async Task RequireAsync(List<string> roles, string[] allowed)
    {
        foreach (var a in allowed) if (await actions.HasActionAsync(roles, "PartCatalogue", a)) return;
        throw new UnauthorizedAccessException();
    }

    // ---- lookups
    public async Task<object?> TechnicalClassesAsync(List<string> userRoles, string? q = null) { await RequireAsync(userRoles, AnyAction); return await lookups.SearchTechnicalClassesAsync(q); }
    public async Task<object?> TechnicalClassAsync(List<string> userRoles, int id) { await RequireAsync(userRoles, AnyAction); return await lookups.GetTechnicalClassAsync(id); }

    public async Task<object?> ResolveTechnicalClassAsync(List<string> userRoles, string? text = null)
    {
        await RequireAsync(userRoles, AnyAction);
        return await lookups.ResolveTechnicalClassAsync(text != null && text.Length > PEMS.Application.PartCatalogue.TechnicalClassMatcher.MaxTextLength ? text[..PEMS.Application.PartCatalogue.TechnicalClassMatcher.MaxTextLength] : text);
    }

    public async Task<object?> ManufacturersAsync(List<string> userRoles, string? q = null) { await RequireAsync(userRoles, AnyAction); return await lookups.SearchManufacturersAsync(q); }
    public async Task<object?> SuppliersAsync(List<string> userRoles, string? q = null) { await RequireAsync(userRoles, AnyAction); return await lookups.SearchSuppliersAsync(q); }
    public async Task<object?> EquipmentAsync(List<string> userRoles, string? q = null) { await RequireAsync(userRoles, AnyAction); return await lookups.SearchEquipmentAsync(q); }
    public async Task<object?> Group1Async(List<string> userRoles, int? sectionId = null) { await RequireAsync(userRoles, AnyAction); return await lookups.GetCommodityGroup1Async(sectionId); }
    public async Task<object?> Group2Async(List<string> userRoles, int? group1Id = null) { await RequireAsync(userRoles, AnyAction); return await lookups.GetCommodityGroup2Async(group1Id); }

    public async Task<object?> CheckManufacturerAsync(List<string> userRoles, string? name = null)
    {
        await RequireAsync(userRoles, AnyAction);
        var (exact, similar) = await lookups.CheckManufacturerNameAsync(name ?? string.Empty);
        return new { exact, similar };
    }

    // ---- duplicate checks
    public async Task<object?> ExistingPartsAsync(List<string> userRoles, string? q = null) { await RequireAsync(userRoles, AnyAction); return await duplicates.SearchExistingPartsAsync(q); }

    public async Task<object?> DuplicateCandidatesAsync(List<string> userRoles, string? manufacturerPartNumber = null, string? supplierPartNumber = null, int? manufacturerId = null, string? newManufacturerName = null, int? excludeRequestId = null)
    {
        await RequireAsync(userRoles, AnyAction);
        return await duplicates.FindDuplicateCandidatesAsync(new PcDuplicateProbe { ManufacturerPartNumber = manufacturerPartNumber, SupplierPartNumber = supplierPartNumber, ManufacturerId = manufacturerId, NewManufacturerName = newManufacturerName, ExcludeRequestId = excludeRequestId });
    }

    // ---- the report
    private async Task<PartCatalogueReportDto> ReportAsync(List<string> roles, string userName, string? period, DateTime? from, DateTime? to, int? sectionId, string? requestType, string? status, int? employeeId, int? equipmentId, string? tab = null)
    {
        await RequireAsync(roles, ReportActions);
        var user = string.IsNullOrEmpty(userName) ? null : await context.Users.AsNoTracking().Where(u => u.UserName == userName).Select(u => new { u.Id, u.EmployeeId }).FirstOrDefaultAsync();
        // reports only read: a user without an employee record still sees what the permission allows (employee 0 owns and approves no request)
        var actor = new PcActor(user?.EmployeeId ?? 0, user?.Id, roles);
        var filter = new PartCatalogueReportFilter
        {
            Period = !string.IsNullOrWhiteSpace(period) && PartCatalogueReportPeriods.Values.Contains(period) ? period : PartCatalogueReportPeriods.ThisYear, From = from, To = to, SectionId = sectionId,
            RequestType = string.IsNullOrWhiteSpace(requestType) ? null : requestType, Status = string.IsNullOrWhiteSpace(status) ? null : status, EmployeeId = employeeId, EquipmentId = equipmentId, Tab = tab != null && Tabs.Contains(tab) ? tab : "summary"
        };
        return await reports.GetReportAsync(filter, actor);
    }

    public async Task<List<PcReportSummaryRow>> ReportSummaryAsync(List<string> userRoles, string userName, string? period = null, DateTime? from = null, DateTime? to = null, int? sectionId = null, string? requestType = null, string? status = null, int? employeeId = null, int? equipmentId = null)
    {
        var r = await ReportAsync(userRoles, userName, period, from, to, sectionId, requestType, status, employeeId, equipmentId);
        var p = r.Pipeline;
        return [new PcReportSummaryRow(p.Total, p.Add, p.Change, p.Open, p.CompletedInPeriod, p.PartNumbersRegisteredInPeriod, p.AverageDaysToComplete, p.MedianDaysToComplete, r.PeriodStart, r.PeriodEnd, r.SeesAll, r.Truncated)];
    }

    private static List<PcReportCountRow> Counts(IEnumerable<PcReportCount> c) => c.Select(x => new PcReportCountRow(x.Key, x.Label, x.LabelFa, x.Count)).ToList();

    public async Task<List<PcReportCountRow>> ReportByStatusAsync(List<string> userRoles, string userName, string? period = null, DateTime? from = null, DateTime? to = null, int? sectionId = null, string? requestType = null, string? status = null, int? employeeId = null, int? equipmentId = null)
        => Counts((await ReportAsync(userRoles, userName, period, from, to, sectionId, requestType, status, employeeId, equipmentId)).Pipeline.ByStatus);

    public async Task<List<PcReportCountRow>> ReportByStepAsync(List<string> userRoles, string userName, string? period = null, DateTime? from = null, DateTime? to = null, int? sectionId = null, string? requestType = null, string? status = null, int? employeeId = null, int? equipmentId = null)
        => Counts((await ReportAsync(userRoles, userName, period, from, to, sectionId, requestType, status, employeeId, equipmentId)).Pipeline.ByStep);

    public async Task<List<PcReportCountRow>> ReportOpenAgeAsync(List<string> userRoles, string userName, string? period = null, DateTime? from = null, DateTime? to = null, int? sectionId = null, string? requestType = null, string? status = null, int? employeeId = null, int? equipmentId = null)
        => Counts((await ReportAsync(userRoles, userName, period, from, to, sectionId, requestType, status, employeeId, equipmentId)).Pipeline.OpenAge);

    public async Task<List<PcReportMonthRow>> ReportMonthsAsync(List<string> userRoles, string userName, string? period = null, DateTime? from = null, DateTime? to = null, int? sectionId = null, string? requestType = null, string? status = null, int? employeeId = null, int? equipmentId = null)
        => (await ReportAsync(userRoles, userName, period, from, to, sectionId, requestType, status, employeeId, equipmentId)).Pipeline.Months.Select(m => new PcReportMonthRow(m.Month, m.Created, m.Completed)).ToList();

    public async Task<List<PcReportPersonRow>> ReportPeopleAsync(List<string> userRoles, string userName, string? period = null, DateTime? from = null, DateTime? to = null, int? sectionId = null, string? requestType = null, string? status = null, int? employeeId = null, int? equipmentId = null)
        => (await ReportAsync(userRoles, userName, period, from, to, sectionId, requestType, status, employeeId, equipmentId)).Activity.People;

    public async Task<List<PcReportActivityRow>> ReportActivityAsync(List<string> userRoles, string userName, string? period = null, DateTime? from = null, DateTime? to = null, int? sectionId = null, string? requestType = null, string? status = null, int? employeeId = null, int? equipmentId = null)
        => (await ReportAsync(userRoles, userName, period, from, to, sectionId, requestType, status, employeeId, equipmentId)).Activity.Rows.Take(PcReportViewModel.PageRows).ToList();

    public async Task<List<PcReportEquipmentLine>> ReportEquipmentAsync(List<string> userRoles, string userName, string? period = null, DateTime? from = null, DateTime? to = null, int? sectionId = null, string? requestType = null, string? status = null, int? employeeId = null, int? equipmentId = null)
        => (await ReportAsync(userRoles, userName, period, from, to, sectionId, requestType, status, employeeId, equipmentId)).Equipment.Equipment
            .Select(e => new PcReportEquipmentLine(e.EquipmentKey, e.EquipmentTag ?? "", e.EquipmentName ?? "", e.InRegister, e.Requests, e.RegisteredPartNumbers, e.InProgress, e.Critical)).ToList();

    public async Task<List<PcReportEquipmentPartRow>> ReportPartsAsync(List<string> userRoles, string userName, string? period = null, DateTime? from = null, DateTime? to = null, int? sectionId = null, string? requestType = null, string? status = null, int? employeeId = null, int? equipmentId = null)
        => (await ReportAsync(userRoles, userName, period, from, to, sectionId, requestType, status, employeeId, equipmentId)).Equipment.Parts.Take(PcReportViewModel.PageRows).ToList();

    public async Task<List<ReportChoiceRow>> ReportSectionsAsync(List<string> userRoles)
    {
        await RequireAsync(userRoles, ReportActions);
        return (await lookups.GetSectionsAsync()).Select(s => new ReportChoiceRow(s.Id.ToString(CultureInfo.InvariantCulture), s.Text)).ToList();
    }

    public async Task<List<ReportChoiceRow>> ReportEmployeesAsync(List<string> userRoles, string userName)
        => (await ReportAsync(userRoles, userName, null, null, null, null, null, null, null, null)).Employees.Select(e => new ReportChoiceRow(e.Id.ToString(CultureInfo.InvariantCulture), e.Text)).ToList();

    public async Task<PcFileResult> ReportExportAsync(List<string> userRoles, string userName, string culture, string? sheet = null, string? period = null, DateTime? from = null, DateTime? to = null, int? sectionId = null, string? requestType = null, string? status = null, int? employeeId = null, int? equipmentId = null)
    {
        var report = await ReportAsync(userRoles, userName, period, from, to, sectionId, requestType, status, employeeId, equipmentId);
        var bytes = PartCatalogueReportExport.Build(report, new PartCatalogueReportTexts(localizer, culture == "fa"), sheet);
        return new PcFileResult(true, null, bytes, $"PartCatalogueReport-{(string.IsNullOrEmpty(sheet) ? "all" : sheet)}-{DateTime.Now:yyyyMMdd}.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
    }
}
