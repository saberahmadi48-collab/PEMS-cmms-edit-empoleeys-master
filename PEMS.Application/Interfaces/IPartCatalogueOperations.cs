using PEMS.Application.DTOs;

namespace PEMS.Application.Interfaces;

/// <summary>One counted line of the report (status, step, age).</summary>
public sealed record PcReportCountRow(string Key, string Label, string LabelFa, int Count);

public sealed record PcReportMonthRow(string Month, int Created, int Completed);

/// <summary>The pipeline totals of the report (one row).</summary>
public sealed record PcReportSummaryRow(int Total, int Add, int Change, int Open, int CompletedInPeriod, int PartNumbersRegisteredInPeriod, double? AverageDaysToComplete, double? MedianDaysToComplete, DateTime? PeriodStart, DateTime? PeriodEnd, bool SeesAll, bool Truncated);

/// <summary>One equipment of the report with its part catalogue counts.</summary>
public sealed record PcReportEquipmentLine(string EquipmentKey, string Tag, string Name, bool InRegister, int Requests, int RegisteredPartNumbers, int InProgress, int Critical);

/// <summary>A file of the Part Catalogue (a workbook): served by the file host.</summary>
public sealed record PcFileResult(bool Success, string? Error, byte[] Bytes, string FileName, string ContentType);

/// <summary>
/// The machine answers and the reports of the Part Catalogue (SCRUM-659, 660, 661) as operations served by the file host: the lookups and the duplicate checks that the request form scripts call (the same services, the same
/// JSON; the old addresses redirect to /x/{key}), and the report with its workbook. Each is allowed to the users the old controller allowed (any Part Catalogue action for the lookups and checks; View, Review, Approve or Admin
/// for the reports); a user without any gets a refusal.
/// </summary>
public interface IPartCatalogueOperations
{
    // ---- lookups (SCRUM-660)
    [PEMS.Application.Common.PemsOperation("PartCatalogueLookup", "TechnicalClasses")]
    Task<object?> TechnicalClassesAsync(List<string> userRoles, string? q = null);

    [PEMS.Application.Common.PemsOperation("PartCatalogueLookup", "TechnicalClass")]
    Task<object?> TechnicalClassAsync(List<string> userRoles, int id);

    [PEMS.Application.Common.PemsOperation("PartCatalogueLookup", "ResolveTechnicalClass")]
    Task<object?> ResolveTechnicalClassAsync(List<string> userRoles, string? text = null);

    [PEMS.Application.Common.PemsOperation("PartCatalogueLookup", "Manufacturers")]
    Task<object?> ManufacturersAsync(List<string> userRoles, string? q = null);

    [PEMS.Application.Common.PemsOperation("PartCatalogueLookup", "Suppliers")]
    Task<object?> SuppliersAsync(List<string> userRoles, string? q = null);

    [PEMS.Application.Common.PemsOperation("PartCatalogueLookup", "Equipment")]
    Task<object?> EquipmentAsync(List<string> userRoles, string? q = null);

    [PEMS.Application.Common.PemsOperation("PartCatalogueLookup", "Group1")]
    Task<object?> Group1Async(List<string> userRoles, int? sectionId = null);

    [PEMS.Application.Common.PemsOperation("PartCatalogueLookup", "Group2")]
    Task<object?> Group2Async(List<string> userRoles, int? group1Id = null);

    [PEMS.Application.Common.PemsOperation("PartCatalogueLookup", "CheckManufacturer")]
    Task<object?> CheckManufacturerAsync(List<string> userRoles, string? name = null);

    // ---- duplicate checks (SCRUM-659)
    [PEMS.Application.Common.PemsOperation("PartCatalogueDedup", "ExistingParts")]
    Task<object?> ExistingPartsAsync(List<string> userRoles, string? q = null);

    [PEMS.Application.Common.PemsOperation("PartCatalogueDedup", "DuplicateCandidates")]
    Task<object?> DuplicateCandidatesAsync(List<string> userRoles, string? manufacturerPartNumber = null, string? supplierPartNumber = null, int? manufacturerId = null, string? newManufacturerName = null, int? excludeRequestId = null);

    // ---- the report (SCRUM-661)
    [PEMS.Application.Common.PemsOperation("PartCatalogueReport", "Summary")]
    Task<List<PcReportSummaryRow>> ReportSummaryAsync(List<string> userRoles, string userName, string? period = null, DateTime? from = null, DateTime? to = null, int? sectionId = null, string? requestType = null, string? status = null, int? employeeId = null, int? equipmentId = null);

    [PEMS.Application.Common.PemsOperation("PartCatalogueReport", "ByStatus")]
    Task<List<PcReportCountRow>> ReportByStatusAsync(List<string> userRoles, string userName, string? period = null, DateTime? from = null, DateTime? to = null, int? sectionId = null, string? requestType = null, string? status = null, int? employeeId = null, int? equipmentId = null);

    [PEMS.Application.Common.PemsOperation("PartCatalogueReport", "ByStep")]
    Task<List<PcReportCountRow>> ReportByStepAsync(List<string> userRoles, string userName, string? period = null, DateTime? from = null, DateTime? to = null, int? sectionId = null, string? requestType = null, string? status = null, int? employeeId = null, int? equipmentId = null);

    [PEMS.Application.Common.PemsOperation("PartCatalogueReport", "OpenAge")]
    Task<List<PcReportCountRow>> ReportOpenAgeAsync(List<string> userRoles, string userName, string? period = null, DateTime? from = null, DateTime? to = null, int? sectionId = null, string? requestType = null, string? status = null, int? employeeId = null, int? equipmentId = null);

    [PEMS.Application.Common.PemsOperation("PartCatalogueReport", "Months")]
    Task<List<PcReportMonthRow>> ReportMonthsAsync(List<string> userRoles, string userName, string? period = null, DateTime? from = null, DateTime? to = null, int? sectionId = null, string? requestType = null, string? status = null, int? employeeId = null, int? equipmentId = null);

    [PEMS.Application.Common.PemsOperation("PartCatalogueReport", "People")]
    Task<List<PcReportPersonRow>> ReportPeopleAsync(List<string> userRoles, string userName, string? period = null, DateTime? from = null, DateTime? to = null, int? sectionId = null, string? requestType = null, string? status = null, int? employeeId = null, int? equipmentId = null);

    [PEMS.Application.Common.PemsOperation("PartCatalogueReport", "Activity")]
    Task<List<PcReportActivityRow>> ReportActivityAsync(List<string> userRoles, string userName, string? period = null, DateTime? from = null, DateTime? to = null, int? sectionId = null, string? requestType = null, string? status = null, int? employeeId = null, int? equipmentId = null);

    [PEMS.Application.Common.PemsOperation("PartCatalogueReport", "Equipment")]
    Task<List<PcReportEquipmentLine>> ReportEquipmentAsync(List<string> userRoles, string userName, string? period = null, DateTime? from = null, DateTime? to = null, int? sectionId = null, string? requestType = null, string? status = null, int? employeeId = null, int? equipmentId = null);

    [PEMS.Application.Common.PemsOperation("PartCatalogueReport", "Parts")]
    Task<List<PcReportEquipmentPartRow>> ReportPartsAsync(List<string> userRoles, string userName, string? period = null, DateTime? from = null, DateTime? to = null, int? sectionId = null, string? requestType = null, string? status = null, int? employeeId = null, int? equipmentId = null);

    [PEMS.Application.Common.PemsOperation("PartCatalogueReport", "Sections")]
    Task<List<ReportChoiceRow>> ReportSectionsAsync(List<string> userRoles);

    [PEMS.Application.Common.PemsOperation("PartCatalogueReport", "Employees")]
    Task<List<ReportChoiceRow>> ReportEmployeesAsync(List<string> userRoles, string userName);

    /// <summary>The whole workbook, or one tab's sheet (people, activity, equipment, parts, stages).</summary>
    [PEMS.Application.Common.PemsOperation("PartCatalogueReport", "Export")]
    Task<PcFileResult> ReportExportAsync(List<string> userRoles, string userName, string culture, string? sheet = null, string? period = null, DateTime? from = null, DateTime? to = null, int? sectionId = null, string? requestType = null, string? status = null, int? employeeId = null, int? equipmentId = null);
}
