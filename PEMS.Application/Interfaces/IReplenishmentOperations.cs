using PEMS.Application.Replenishment;

namespace PEMS.Application.Interfaces;

/// <summary>One recommendation of the list.</summary>
public sealed record ReplenishmentRow(int Id, string MaterialCode, string Material, string Warehouse, decimal Usable, decimal UnreservedDemand, decimal Shortage, string Status, bool Definitive, DateTime LastEvaluated);

/// <summary>The last calculation run (one row; none before the first run).</summary>
public sealed record ReplenishmentRunRow(int RunId, string Status, DateTime StartedAt, DateTime? CompletedAt, int Evaluated, int Created, int Updated, int Closed, int NotDefinitive);

/// <summary>A line of the notes of the last run: Kind is Attribution (a work-order demand without a warehouse, never assigned by inference) or NotRecommended.</summary>
public sealed record ReplenishmentNoteRow(string Kind, string Text);

/// <summary>One recommendation with its figures and freshness (one row).</summary>
public sealed record ReplenishmentDetailRow(int Id, string MaterialCode, string Material, string Warehouse, int Revision, string Status, bool Definitive, DateTime FirstDetected, DateTime LastEvaluated,
    decimal Usable, decimal OnHand, decimal Reserved, decimal GrossDemand, decimal Covered, decimal Shortage, string BlockingIssues, string Notices, bool RequiresWarehouseAttribution,
    DateTime? LastRunStarted, DateTime? LastRunCompleted, DateTime? StockUpdated, DateTime? DemandChanged, DateTime? DismissedAt, string DismissedReason, string ClosedReason, bool CanSend);

public sealed record ReplenishmentSourceRow(int Index, string SourceType, string Number, decimal Gross, decimal Covered, decimal Unreserved);

public sealed record ReplenishmentHistoryRow(int Index, DateTime Date, int Revision, string Action, string Status, decimal Shortage, decimal Usable, string By, string Note);

/// <summary>
/// The replenishment recommendations (SCRUM-663) as operations over IReplenishmentRecommendationService, behind the same feature switch as the controller (a switched-off module answers nothing).
/// The reads take the signed-in user from the context and resolve the actor the way the controller did; the commands take the actor record built from the context, and the definitions
/// run them under the strict access guard (denials are recorded) like ProtectedActionGuard.
/// </summary>
public interface IReplenishmentOperations
{
    [PEMS.Application.Common.PemsOperation("Replenishment", "List", "ReplenishmentRecommendation.View")]
    Task<List<ReplenishmentRow>> ListAsync(List<string> userRoles, string userName, string? status = null, string? q = null, bool definitiveOnly = false);

    [PEMS.Application.Common.PemsOperation("Replenishment", "LastRun", "ReplenishmentRecommendation.View")]
    Task<List<ReplenishmentRunRow>> LastRunAsync(List<string> userRoles, string userName);

    [PEMS.Application.Common.PemsOperation("Replenishment", "Notes", "ReplenishmentRecommendation.View")]
    Task<List<ReplenishmentNoteRow>> NotesAsync(List<string> userRoles, string userName);

    [PEMS.Application.Common.PemsOperation("Replenishment", "Detail", "ReplenishmentRecommendation.View")]
    Task<List<ReplenishmentDetailRow>> DetailAsync(List<string> userRoles, string userName, int id);

    [PEMS.Application.Common.PemsOperation("Replenishment", "Sources", "ReplenishmentRecommendation.View")]
    Task<List<ReplenishmentSourceRow>> SourcesAsync(List<string> userRoles, string userName, int id);

    [PEMS.Application.Common.PemsOperation("Replenishment", "History", "ReplenishmentRecommendation.View")]
    Task<List<ReplenishmentHistoryRow>> HistoryAsync(List<string> userRoles, string userName, int id);

    /// <summary>Runs the calculation; the same request key never runs twice (the definition posts a fresh key per form).</summary>
    [PEMS.Application.Common.PemsOperation("Replenishment", "Run", "ReplenishmentRecommendation.Create")]
    Task<(bool Success, string? Error)> RunAsync(string? requestKey, ReplenishmentActor actor);

    [PEMS.Application.Common.PemsOperation("Replenishment", "Dismiss", "ReplenishmentRecommendation.Edit")]
    Task<(bool Success, string? Error)> DismissAsync(int id, string reason, ReplenishmentActor actor);

    [PEMS.Application.Common.PemsOperation("Replenishment", "Reopen", "ReplenishmentRecommendation.Edit")]
    Task<(bool Success, string? Error)> ReopenAsync(int id, string? note, ReplenishmentActor actor);
}
