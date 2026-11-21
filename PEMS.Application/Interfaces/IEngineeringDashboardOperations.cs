namespace PEMS.Application.Interfaces;

/// <summary>The totals of the engineering dashboard (one row): counts, the health score (0-100) and the first-pass approval rate as text ("N/A" while no document finished a cycle).</summary>
public sealed record EngineeringSummaryRow(int Total, int Approved, int Pending, int Rejected, int Frozen, int Overdue, int HealthScore, string FirstPassRate, string ApprovedRate);

/// <summary>One document of the quality lists (most rejected, revision pressure).</summary>
public sealed record EngineeringQualityRow(int DocumentId, string DocumentNo, string Discipline, string Vendor, string Status, int Rejections, int Revisions, string FirstPass);

/// <summary>One document of the management attention queue; Severity is Critical, High or Medium.</summary>
public sealed record EngineeringAttentionRow(int DocumentId, string DocumentNo, string Discipline, string Reason, string Severity, int Revisions, int Rejections);

/// <summary>One discipline of the risk overview.</summary>
public sealed record EngineeringDisciplineRow(string Discipline, int Documents, int Rejections, string AverageRevisions, int RiskScore);

/// <summary>The engineering dashboard page as read operations (SCRUM-688): the same two queries of IEngineeringDocumentService and the same discipline restriction as the controller (a user without
/// EngineeringDocuments.ViewAllDisciplines sees only the own discipline; a chosen discipline never widens it).</summary>
public interface IEngineeringDashboardOperations
{
    [PEMS.Application.Common.PemsOperation("EngineeringDashboard", "Summary")]
    Task<List<EngineeringSummaryRow>> SummaryAsync(List<string> userRoles, string userName, int? projectId = null, int? disciplineId = null, int? minRevisionCount = null);

    [PEMS.Application.Common.PemsOperation("EngineeringDashboard", "MostRejected")]
    Task<List<EngineeringQualityRow>> MostRejectedAsync(List<string> userRoles, string userName, int? projectId = null, int? disciplineId = null, int? minRevisionCount = null);

    [PEMS.Application.Common.PemsOperation("EngineeringDashboard", "RevisionPressure")]
    Task<List<EngineeringQualityRow>> RevisionPressureAsync(List<string> userRoles, string userName, int? projectId = null, int? disciplineId = null, int? minRevisionCount = null);

    [PEMS.Application.Common.PemsOperation("EngineeringDashboard", "AttentionQueue")]
    Task<List<EngineeringAttentionRow>> AttentionQueueAsync(List<string> userRoles, string userName, int? projectId = null, int? disciplineId = null, int? minRevisionCount = null);

    [PEMS.Application.Common.PemsOperation("EngineeringDashboard", "DisciplineRisk")]
    Task<List<EngineeringDisciplineRow>> DisciplineRiskAsync(List<string> userRoles, string userName, int? projectId = null, int? disciplineId = null, int? minRevisionCount = null);

    [PEMS.Application.Common.PemsOperation("EngineeringDashboard", "Projects")]
    Task<List<ReportChoiceRow>> ProjectsAsync();

    [PEMS.Application.Common.PemsOperation("EngineeringDashboard", "Disciplines")]
    Task<List<ReportChoiceRow>> DisciplinesAsync();
}
