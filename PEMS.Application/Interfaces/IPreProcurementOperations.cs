namespace PEMS.Application.Interfaces;

/// <summary>One demand of the procurement queue.</summary>
public sealed record PreProcurementQueueRow(int ProcurementDemandId, string Source, string SourceNumber, string Item, decimal Quantity, string Unit, string Warehouse, string Status, DateTime RaisedAt, string RaisedBy, string Requisition);

/// <summary>One demand with what the buyer needs to decide it (one row). IsMaterialRequisition is true for an engineering MR sent to procurement (accept or return) and false for a material demand (convert or dismiss).</summary>
public sealed record PreProcurementDetailRow(int ProcurementDemandId, string Source, string SourceNumber, string Item, decimal Quantity, string Unit, string Warehouse, string Status, DateTime RaisedAt, string RaisedBy,
    string Requisition, bool InCatalogue, string DecisionReason, DateTime? DecidedAt, string DecidedBy, bool IsMaterialRequisition, bool IsOpen);

/// <summary>
/// The procurement cartable (SCRUM-735) as read operations over IPreProcurementService; the decisions (convert to a requisition, accept, dismiss or return, send a replenishment recommendation) are the
/// operations marked on the service itself.
/// </summary>
public interface IPreProcurementOperations
{
    [PEMS.Application.Common.PemsOperation("PreProcurement", "Queue", "PreProcurement.View")]
    Task<List<PreProcurementQueueRow>> QueueAsync(string? status = null, string? source = null);

    [PEMS.Application.Common.PemsOperation("PreProcurement", "Detail", "PreProcurement.View")]
    Task<List<PreProcurementDetailRow>> DetailAsync(int id);

    /// <summary>The Draft requisitions the line can be added to (choices).</summary>
    [PEMS.Application.Common.PemsOperation("PreProcurement", "DraftRequisitions", "PreProcurement.View")]
    Task<List<ReportChoiceRow>> DraftRequisitionsAsync();

    [PEMS.Application.Common.PemsOperation("PreProcurement", "Projects", "PreProcurement.View")]
    Task<List<ReportChoiceRow>> ProjectsAsync();

    [PEMS.Application.Common.PemsOperation("PreProcurement", "Disciplines", "PreProcurement.View")]
    Task<List<ReportChoiceRow>> DisciplinesAsync();
}
