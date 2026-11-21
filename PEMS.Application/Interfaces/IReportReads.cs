namespace PEMS.Application.Interfaces;

/// <summary>One line of a report: the section it belongs to (Summary, By discipline, Volume by month ...), the name and the value(s) as text. <c>Key</c> is the stable (not translated) name of the section and <c>Number</c> the value as a number when it is one (charts read these).</summary>
public sealed record ReportRow(string Section, string Name, string Value, string Key = "", double? Number = null, double? Number2 = null);

/// <summary>One choice of a report filter (project, discipline, work center, material item).</summary>
public sealed record ReportChoiceRow(string Value, string Label);

/// <summary>One line of the procurement pipeline (SCRUM-747).</summary>
public sealed record ReportPipelineRow(string PackageNumber, string TenderNumber, string TenderStatus, string WinnerVendor, string BidStatus, string TechnicalEvaluation, string CommercialEvaluation,
    string ContractNo, string ContractStatus, string ContractValue, string PoNumber, string NtpStatus, string KickoffStatus);

/// <summary>The six report pages as read operations (SCRUM-747): every report is the same service as before, flattened into sections of name and value; the permission of the procurement report is the one the controller checked.</summary>
public interface IReportReads
{
    [PEMS.Application.Common.PemsOperation("Report", "Correspondence")]
    Task<List<ReportRow>> CorrespondenceAsync(int? projectId = null, int? disciplineId = null, DateTime? fromDate = null, DateTime? toDate = null);

    [PEMS.Application.Common.PemsOperation("Report", "Engineering")]
    Task<List<ReportRow>> EngineeringAsync(int? projectId = null, int? disciplineId = null, DateTime? fromDate = null, DateTime? toDate = null);

    [PEMS.Application.Common.PemsOperation("Report", "Vdr")]
    Task<List<ReportRow>> VdrAsync(int? projectId = null, int? disciplineId = null, DateTime? fromDate = null, DateTime? toDate = null);

    [PEMS.Application.Common.PemsOperation("Report", "Cmms")]
    Task<List<ReportRow>> CmmsAsync(string? status = null, string? priority = null, DateTime? fromDate = null, DateTime? toDate = null);

    [PEMS.Application.Common.PemsOperation("Report", "Procurement", "ProcurementPackage.View")]
    Task<List<ReportRow>> ProcurementAsync(int? projectId = null, int? disciplineId = null, DateTime? fromDate = null, DateTime? toDate = null);

    [PEMS.Application.Common.PemsOperation("Report", "ProcurementPipeline", "ProcurementPackage.View")]
    Task<List<ReportPipelineRow>> ProcurementPipelineAsync(int? projectId = null, int? disciplineId = null, DateTime? fromDate = null, DateTime? toDate = null);

    [PEMS.Application.Common.PemsOperation("Report", "Operations")]
    Task<List<ReportRow>> OperationsAsync(string? periodType = null, DateTime? anchorOrFrom = null, DateTime? toDate = null, int? productionWorkCenterId = null, int? materialItemId = null);

    [PEMS.Application.Common.PemsOperation("Report", "Projects")]
    Task<List<ReportChoiceRow>> ProjectsAsync();

    [PEMS.Application.Common.PemsOperation("Report", "Disciplines")]
    Task<List<ReportChoiceRow>> DisciplinesAsync();

    [PEMS.Application.Common.PemsOperation("Report", "WorkCenters")]
    Task<List<ReportChoiceRow>> WorkCentersAsync();

    [PEMS.Application.Common.PemsOperation("Report", "MaterialItems")]
    Task<List<ReportChoiceRow>> MaterialItemsAsync();
}
