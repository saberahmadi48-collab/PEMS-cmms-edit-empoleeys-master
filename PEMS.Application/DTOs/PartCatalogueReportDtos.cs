using PEMS.Application.Common;

namespace PEMS.Application.DTOs;

/// <summary>SCRUM-478: period presets of the Part Catalogue reports.</summary>
public static class PartCatalogueReportPeriods
{
    public const string All = "All";
    public const string ThisMonth = "ThisMonth";
    public const string LastMonth = "LastMonth";
    public const string ThisQuarter = "ThisQuarter";
    public const string ThisYear = "ThisYear";
    public const string Custom = "Custom";

    public static readonly string[] Values = { ThisMonth, LastMonth, ThisQuarter, ThisYear, All, Custom };

    /// <summary>
    /// The period as [Start, End) (End exclusive, like <see cref="ReportingPeriod"/>), or null for all time.
    /// A custom range needs both dates; a missing one makes it open on that side. Unknown presets mean this month.
    /// </summary>
    public static (DateTime? Start, DateTime? End) Resolve(string? period, DateTime? from, DateTime? to, DateTime today)
    {
        today = today.Date;
        switch (period)
        {
            case All:
                return (null, null);
            case LastMonth:
            {
                var r = ReportingPeriod.Resolve(PeriodType.Month, today.AddMonths(-1));
                return (r.Start, r.End);
            }
            case ThisQuarter:
            {
                var start = new DateTime(today.Year, (today.Month - 1) / 3 * 3 + 1, 1);
                return (start, start.AddMonths(3));
            }
            case ThisYear:
            {
                var start = new DateTime(today.Year, 1, 1);
                return (start, start.AddYears(1));
            }
            case Custom:
            {
                if (from.HasValue && to.HasValue)
                {
                    var (a, b) = from.Value.Date <= to.Value.Date ? (from.Value, to.Value) : (to.Value, from.Value);
                    var r = ReportingPeriod.Resolve(PeriodType.CustomRange, a, b);
                    return (r.Start, r.End);
                }
                return (from?.Date, to?.Date.AddDays(1));
            }
            default:
            {
                var r = ReportingPeriod.Resolve(PeriodType.Month, today);
                return (r.Start, r.End);
            }
        }
    }
}

/// <summary>SCRUM-478: filters shared by every Part Catalogue report and its Excel export.</summary>
public sealed class PartCatalogueReportFilter
{
    public string Period { get; set; } = PartCatalogueReportPeriods.ThisYear;
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int? SectionId { get; set; }
    public string? RequestType { get; set; }
    public string? Status { get; set; }
    public int? EmployeeId { get; set; }
    public int? EquipmentId { get; set; }

    /// <summary>summary | people | activity | equipment | parts | stages</summary>
    public string Tab { get; set; } = "summary";
}

/// <summary>One action of one person on one request (the "who did what" row).</summary>
public sealed class PcReportActivityRow
{
    public DateTime Date { get; set; }
    public int EmployeeId { get; set; }
    public string EmployeeName { get; set; } = "";
    /// <summary>Create, or a PartCatalogueActions value (Submit, Approve, RegisterPartNumber, ...).</summary>
    public string Action { get; set; } = "";
    public string? StepKey { get; set; }
    public int RequestId { get; set; }
    public string? RequestNumber { get; set; }
    public string RequestType { get; set; } = "";
    public string? ManufacturerPartNumber { get; set; }
    public string? AssignedPartNumber { get; set; }
    public string? TechnicalClass { get; set; }
    public string Status { get; set; } = "";
}

/// <summary>What one person did in the period.</summary>
public sealed class PcReportPersonRow
{
    public int EmployeeId { get; set; }
    public string EmployeeName { get; set; } = "";
    public int Created { get; set; }
    public int Submitted { get; set; }
    public int Approved { get; set; }
    public int Returned { get; set; }
    public int Cancelled { get; set; }
    public int PartNumbersRegistered { get; set; }
    public int Total => Created + Submitted + Approved + Returned + Cancelled + PartNumbersRegistered;
}

public sealed class PcReportActivityDto
{
    public List<PcReportPersonRow> People { get; set; } = new();
    public List<PcReportActivityRow> Rows { get; set; } = new();
}

/// <summary>One part (request) on one piece of equipment, with its specification.</summary>
public sealed class PcReportEquipmentPartRow
{
    public string EquipmentKey { get; set; } = "";
    public string? EquipmentTag { get; set; }
    public string? EquipmentName { get; set; }
    public decimal InstalledQuantity { get; set; }
    public int RequestId { get; set; }
    public string? RequestNumber { get; set; }
    public string RequestType { get; set; } = "";
    public string Status { get; set; } = "";
    public string? AssignedPartNumber { get; set; }
    public string? ManufacturerName { get; set; }
    public string? ManufacturerPartNumber { get; set; }
    public string? SupplierName { get; set; }
    public string? SupplierPartNumber { get; set; }
    public string? TechnicalClass { get; set; }
    public string? CommodityGroup { get; set; }
    public string? UnitOfMeasure { get; set; }
    public bool IsCritical { get; set; }
    public bool IsCapital { get; set; }
    public string? Attributes { get; set; }
    public string? RequesterName { get; set; }
    public DateTime CreatedDate { get; set; }
    public DateTime? CompletedDate { get; set; }
    public bool IsRegistered { get; set; }
}

/// <summary>Coverage of one piece of equipment (or a free-text one not yet in the equipment register).</summary>
public sealed class PcReportEquipmentRow
{
    public string EquipmentKey { get; set; } = "";
    public int? EquipmentId { get; set; }
    public string? EquipmentTag { get; set; }
    public string? EquipmentName { get; set; }
    public bool InRegister { get; set; }
    public int Requests { get; set; }
    public int RegisteredPartNumbers { get; set; }
    public int InProgress { get; set; }
    public int Critical { get; set; }
}

public sealed class PcReportEquipmentDto
{
    public List<PcReportEquipmentRow> Equipment { get; set; } = new();
    public List<PcReportEquipmentPartRow> Parts { get; set; } = new();
}

public sealed class PcReportCount
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    /// <summary>Persian label when the source has one (approval step names); otherwise empty.</summary>
    public string LabelFa { get; set; } = "";
    public int Count { get; set; }
}

public sealed class PcReportMonth
{
    /// <summary>yyyy-MM</summary>
    public string Month { get; set; } = "";
    public int Created { get; set; }
    public int Completed { get; set; }
}

/// <summary>Requests and parts by stage (status, approval step), throughput and age.</summary>
public sealed class PcReportPipelineDto
{
    public int Total { get; set; }
    public int Add { get; set; }
    public int Change { get; set; }
    public int Open { get; set; }
    public int CompletedInPeriod { get; set; }
    public int PartNumbersRegisteredInPeriod { get; set; }
    public double? AverageDaysToComplete { get; set; }
    public double? MedianDaysToComplete { get; set; }
    public List<PcReportCount> ByStatus { get; set; } = new();
    public List<PcReportCount> ByStep { get; set; } = new();
    public List<PcReportCount> OpenAge { get; set; } = new();
    public List<PcReportMonth> Months { get; set; } = new();
}

public sealed class PcReportOption
{
    public int Id { get; set; }
    public string Text { get; set; } = "";
}

/// <summary>Everything the report page and the workbook show for one filter.</summary>
public sealed class PartCatalogueReportDto
{
    public PartCatalogueReportFilter Filter { get; set; } = new();
    public DateTime? PeriodStart { get; set; }
    /// <summary>Exclusive.</summary>
    public DateTime? PeriodEnd { get; set; }
    public bool SeesAll { get; set; }
    public PcReportActivityDto Activity { get; set; } = new();
    public PcReportEquipmentDto Equipment { get; set; } = new();
    public PcReportPipelineDto Pipeline { get; set; } = new();
    public List<PcReportOption> Employees { get; set; } = new();
    public List<PcReportOption> EquipmentOptions { get; set; } = new();

    /// <summary>True when a detail list reached <see cref="MaxRows"/> and was cut.</summary>
    public bool Truncated { get; set; }
    public const int MaxRows = 50000;
}
