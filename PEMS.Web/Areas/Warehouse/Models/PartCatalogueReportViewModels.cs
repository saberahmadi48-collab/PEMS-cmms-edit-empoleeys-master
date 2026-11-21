using Microsoft.Extensions.Localization;
using PEMS.Application.DTOs;
using PEMS.Web.Resources;

namespace PEMS.Web.Areas.Warehouse.Models;

/// <summary>SCRUM-478: the Part Catalogue report page.</summary>
public sealed class PcReportViewModel
{
    public PartCatalogueReportDto Report { get; set; } = new();
    public List<PcLookupItem> Sections { get; set; } = new();
    public bool IsPersian { get; set; }

    /// <summary>The page shows at most this many detail rows; the workbook has them all.</summary>
    public const int PageRows = 500;
}

/// <summary>
/// SCRUM-478: the localized labels of the Part Catalogue reports, shared by the page and the workbook so both say the same.
/// </summary>
public sealed class PartCatalogueReportTexts
{
    private readonly IStringLocalizer<SharedResource> _l;

    public PartCatalogueReportTexts(IStringLocalizer<SharedResource> l, bool rightToLeft)
    {
        _l = l;
        RightToLeft = rightToLeft;
    }

    public bool RightToLeft { get; }

    public string this[string key] => _l[key].Value;
    public string Format(string key, params object[] args) => _l[key, args].Value;

    public string Status(string? status) => string.IsNullOrEmpty(status) ? "" : _l["Pc_Status_" + status].Value;
    public string Type(string? type) => string.IsNullOrEmpty(type) ? "" : _l["Pc_Type_" + type].Value;

    public string Action(string action) =>
        action == "Create" ? _l["PcRpt_Action_Create"].Value : _l["Pc_Action_" + action].Value;

    public string Step(PcReportCount step) => RightToLeft && !string.IsNullOrWhiteSpace(step.LabelFa) ? step.LabelFa : step.Label;

    /// <summary>"0-7 days": the number range is isolated left-to-right so it does not read "7-0" in Persian.</summary>
    public string Days(string range) => _l["PcRpt_Days", LeftToRightIsolate + range + PopIsolate].Value;

    private static readonly string LeftToRightIsolate = ((char)0x2066).ToString(), PopIsolate = ((char)0x2069).ToString();

    public string YesNo(bool value) => value ? _l["Yes"].Value : "";

    public string Period(PartCatalogueReportDto r) => r.PeriodStart == null && r.PeriodEnd == null
        ? _l["PcRpt_Period_All"].Value
        : $"{r.PeriodStart:yyyy-MM-dd} – {r.PeriodEnd?.AddDays(-1):yyyy-MM-dd}";

    public string PeriodName(string period) => _l["PcRpt_Period_" + period].Value;

    public string PeriodDisplay(PartCatalogueReportDto r) => r.PeriodStart == null && r.PeriodEnd == null
        ? PeriodName(r.Filter.Period)
        : $"{PeriodName(r.Filter.Period)} ({Period(r)})";
}
