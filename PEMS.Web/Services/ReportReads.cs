using System.Collections;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using PEMS.Application.Common;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;
using PEMS.Web.Resources;

namespace PEMS.Web.Services;

/// <summary>
/// SCRUM-747: the six report pages as rows. Each report is the same service call as the former controller; the answer is flattened by its shape: a number or text becomes a line of the section Summary, a list of tuples
/// (name, number, ...) becomes the lines of the section named after the list. Nothing is calculated here.
/// </summary>
public sealed class ReportReads(ICorrespondenceReportService correspondence, IEngineeringReportService engineering, IVDRReportService vdr, ICmmsReportService cmms,
    IProcurementReportService procurement, IOperationsReportService operations, PEMSDbContext context, IStringLocalizer<SharedResource> loc) : IReportReads
{
    public const int PipelinePageSize = 100;
    public const int MaxPipelinePages = 50;

    public async Task<List<ReportRow>> CorrespondenceAsync(int? projectId = null, int? disciplineId = null, DateTime? fromDate = null, DateTime? toDate = null)
        => Flatten(await correspondence.GetReportAsync(projectId, disciplineId, fromDate, toDate), Label);

    public async Task<List<ReportRow>> EngineeringAsync(int? projectId = null, int? disciplineId = null, DateTime? fromDate = null, DateTime? toDate = null)
        => Flatten(await engineering.GetReportAsync(projectId, disciplineId, fromDate, toDate), Label);

    public async Task<List<ReportRow>> VdrAsync(int? projectId = null, int? disciplineId = null, DateTime? fromDate = null, DateTime? toDate = null)
        => Flatten(await vdr.GetReportAsync(projectId, disciplineId, fromDate, toDate), Label);

    public async Task<List<ReportRow>> CmmsAsync(string? status = null, string? priority = null, DateTime? fromDate = null, DateTime? toDate = null)
        => Flatten(await cmms.GetReportAsync(status, priority, fromDate, toDate), Label);

    public async Task<List<ReportRow>> ProcurementAsync(int? projectId = null, int? disciplineId = null, DateTime? fromDate = null, DateTime? toDate = null)
        => Flatten(await procurement.GetReportAsync(projectId, disciplineId, fromDate, toDate), Label);

    public async Task<List<ReportPipelineRow>> ProcurementPipelineAsync(int? projectId = null, int? disciplineId = null, DateTime? fromDate = null, DateTime? toDate = null)
    {
        var all = new List<ReportPipelineRow>();
        for (var page = 1; page <= MaxPipelinePages; page++)
        {
            var result = await procurement.GetPipelineAsync(projectId, disciplineId, fromDate, toDate, page);
            all.AddRange(result.Items.Select(r => new ReportPipelineRow(r.PackageNumber, r.TenderNumber, r.TenderStatus, r.WinnerVendorName ?? "", r.BidStatus ?? "", r.TechnicalEvaluationStatus ?? "", r.CommercialEvaluationStatus ?? "",
                r.ContractNo ?? "", r.ContractStatus ?? "", r.ContractValue?.ToString("N0", CultureInfo.InvariantCulture) ?? "", r.PoNumber ?? "", r.NtpStatus ?? "", r.KickoffStatus ?? "")));
            if (result.Items.Count == 0 || all.Count >= result.TotalCount) break;
        }
        return all;
    }

    public async Task<List<ReportRow>> OperationsAsync(string? periodType = null, DateTime? anchorOrFrom = null, DateTime? toDate = null, int? productionWorkCenterId = null, int? materialItemId = null)
    {
        var normalized = periodType switch { "Month" => PeriodType.Month, "CustomRange" => PeriodType.CustomRange, _ => PeriodType.Day };
        return Flatten(await operations.GetSummaryAsync(normalized, anchorOrFrom, toDate, weekStartDay: null, productionWorkCenterId, materialItemId), Label);
    }

    public async Task<List<ReportChoiceRow>> ProjectsAsync()
        => (await context.Projects.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.ProjectName).Select(x => new { x.ProjectId, x.ProjectName }).ToListAsync())
            .Select(x => new ReportChoiceRow(x.ProjectId.ToString(CultureInfo.InvariantCulture), x.ProjectName)).ToList();

    public async Task<List<ReportChoiceRow>> DisciplinesAsync()
        => (await context.Disciplines.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).Select(x => new { x.DisciplineId, x.Name }).ToListAsync())
            .Select(x => new ReportChoiceRow(x.DisciplineId.ToString(CultureInfo.InvariantCulture), x.Name)).ToList();

    public async Task<List<ReportChoiceRow>> WorkCentersAsync()
        => (await context.ProductionWorkCenters.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Code).Select(x => new { x.ProductionWorkCenterId, x.Code, x.Name }).ToListAsync())
            .Select(x => new ReportChoiceRow(x.ProductionWorkCenterId.ToString(CultureInfo.InvariantCulture), x.Code + " " + x.Name)).ToList();

    public async Task<List<ReportChoiceRow>> MaterialItemsAsync()
        => (await context.MaterialItems.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.MaterialCode).Select(x => new { x.MaterialItemId, x.MaterialCode }).ToListAsync())
            .Select(x => new ReportChoiceRow(x.MaterialItemId.ToString(CultureInfo.InvariantCulture), x.MaterialCode)).ToList();

    /// <summary>The lines of a report DTO: scalar properties are the section Summary, a list of tuples is a section named after the property (the first element is the name, the others are joined with " / ").</summary>
    public static List<ReportRow> Flatten(object dto, Func<string, string>? label = null)
    {
        var rows = new List<ReportRow>();
        foreach (var property in dto.GetType().GetProperties())
        {
            if (property.GetIndexParameters().Length > 0 || property.GetValue(dto) is not { } value) continue;
            var words = label is null ? Words(property.Name) : label(property.Name);
            if (value is IEnumerable list && value is not string)
            {
                foreach (var item in list)
                    if (item is ITuple tuple && tuple.Length >= 2)
                        rows.Add(new ReportRow(words, Show(tuple[0]), string.Join(" / ", Enumerable.Range(1, tuple.Length - 1).Select(i => Show(tuple[i]))), property.Name,
                            Numbers(tuple).ElementAtOrDefault(0), Numbers(tuple).ElementAtOrDefault(1)));
            }
            else rows.Add(new ReportRow(label is null ? "Summary" : label("Summary"), words, Show(value), "Summary", Num(value)));
        }
        return rows;
    }

    /// <summary>The value as a number when it is a number (a count, a sum), else null.</summary>
    private static double? Num(object? value) => value switch
    {
        int or long or short or byte or double or float or decimal => Convert.ToDouble(value, CultureInfo.InvariantCulture),
        _ => null,
    };

    /// <summary>The numeric elements after the name of a tuple, in order (the colour of a tag is skipped).</summary>
    private static IEnumerable<double?> Numbers(ITuple tuple) => Enumerable.Range(1, tuple.Length - 1).Select(i => Num(tuple[i])).Where(n => n is not null);

    private string Label(string name) { var text = loc["Rpt_" + name]; return text.ResourceNotFound ? Words(name) : text.Value; }

    private static string Show(object? value) => value switch
    {
        null => "",
        string s => s,
        DateTime d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        double or float or decimal => Convert.ToDecimal(value, CultureInfo.InvariantCulture).ToString("N1", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };

    private static string Words(string name) => Regex.Replace(name, "(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", " ");
}
