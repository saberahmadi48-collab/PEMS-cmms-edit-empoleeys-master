namespace PEMS.Application.Interfaces;

/// <summary>The twelve counts of the home dashboard tiles (one row); a count that could not be read (a table that is not migrated yet) is 0 and the table is named in the attention rows.</summary>
public sealed record HomeKpiRow(int Projects, int Documents, int PendingApproval, int Rejected, int ReturnedWithComments, int OpenRevisions, int OpenMr, int ActiveContracts, int OpenWorkOrders, int AiAlerts, int Equipment, int Transmittals);

/// <summary>One slice of the documents-by-status chart (Name is the translated status).</summary>
public sealed record HomeStatusRow(string Name, int Number);

/// <summary>One discipline of the performance table.</summary>
public sealed record HomeDisciplineRow(string Discipline, int Total, int Approved, int Pending, int ApprovedPercent);

/// <summary>One thing that needs the attention of the user (my work, the setup progress, a schema warning); Url empty = no link.</summary>
public sealed record HomeAttentionRow(string Name, string Count, string Url);

/// <summary>One widget of the personal dashboard (the value of a list widget is its lines joined with a line separator).</summary>
public sealed record HomeWidgetRow(string Title, string Value);

/// <summary>The home dashboard as read operations (SCRUM-705): the same counts, the same My Work rules (permits, work orders, referrals, document reviews with the discipline restriction, other approvals), the setup progress for users with Setup.View and the personal widgets.</summary>
public interface IHomeOperations
{
    [PEMS.Application.Common.PemsOperation("Home", "Kpis")]
    Task<List<HomeKpiRow>> KpisAsync();

    [PEMS.Application.Common.PemsOperation("Home", "DocumentsByStatus")]
    Task<List<HomeStatusRow>> DocumentsByStatusAsync();

    [PEMS.Application.Common.PemsOperation("Home", "Disciplines")]
    Task<List<HomeDisciplineRow>> DisciplinesAsync();

    [PEMS.Application.Common.PemsOperation("Home", "Attention")]
    Task<List<HomeAttentionRow>> AttentionAsync(List<string> userRoles, string userName);

    [PEMS.Application.Common.PemsOperation("Home", "Widgets")]
    Task<List<HomeWidgetRow>> WidgetsAsync(string userName);
}
