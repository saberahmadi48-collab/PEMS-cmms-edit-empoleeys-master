namespace PEMS.Application.Interfaces;

/// <summary>One store requisition of the list.</summary>
public sealed record StoreRequisitionRow(int StoreRequisitionId, string RequisitionNumber, string Status, string Warehouse, string RequestedBy, DateTime RequestedDate, string Purpose);

/// <summary>The head of one store requisition (one row); NextStatus is where the Advance command moves it (empty at the end).</summary>
public sealed record StoreRequisitionDetailRow(int StoreRequisitionId, string RequisitionNumber, string Status, string NextStatus, string Warehouse, string RequestedBy, string ApprovedBy, string WorkOrder, DateTime RequestedDate, string Purpose);

/// <summary>One line: what was asked, approved, issued and what remains of the approved quantity.</summary>
public sealed record StoreRequisitionLineRow(int StoreRequisitionLineId, string Material, decimal Requested, decimal? Approved, decimal Issued, decimal Remaining);

public sealed record StoreRequisitionHistoryRow(int Id, DateTime Date, string From, string To, string By, string Reason);

/// <summary>
/// The store requisition (internal stock request: Draft, Submitted, Approved, Reserved, Issued, Closed) as operations (SCRUM-664). The reads are what StoreRequisitionController queried; the commands call
/// IStoreRequisitionLifecycleService, which keeps the permission, identity, separation-of-duties and transition rules. The old per-line quantity forms became per-line commands.
/// </summary>
public interface IStoreRequisitionOperations
{
    [PEMS.Application.Common.PemsOperation("StoreRequisition", "List", "StoreRequisition.View")]
    Task<List<StoreRequisitionRow>> ListAsync(string? status = null);

    [PEMS.Application.Common.PemsOperation("StoreRequisition", "Detail", "StoreRequisition.View")]
    Task<List<StoreRequisitionDetailRow>> DetailAsync(int id);

    [PEMS.Application.Common.PemsOperation("StoreRequisition", "Lines", "StoreRequisition.View")]
    Task<List<StoreRequisitionLineRow>> LinesAsync(int id);

    [PEMS.Application.Common.PemsOperation("StoreRequisition", "History", "StoreRequisition.View")]
    Task<List<StoreRequisitionHistoryRow>> HistoryAsync(int id);

    [PEMS.Application.Common.PemsOperation("StoreRequisition", "Warehouses", "StoreRequisition.View")]
    Task<List<ReportChoiceRow>> WarehousesAsync();

    [PEMS.Application.Common.PemsOperation("StoreRequisition", "StockMaterials", "StoreRequisition.View")]
    Task<List<ReportChoiceRow>> StockMaterialsAsync();

    /// <summary>A new Draft requisition for the signed-in employee, numbered by the numbering service.</summary>
    [PEMS.Application.Common.PemsOperation("StoreRequisition", "Create", "StoreRequisition.Create")]
    Task<(bool Success, string? Error)> CreateAsync(int warehouseId, string? purpose, int? workOrderId, StoreRequisitionActor actor);

    [PEMS.Application.Common.PemsOperation("StoreRequisition", "AddLine", "StoreRequisition.Create")]
    Task<(bool Success, string? Error)> AddLineAsync(int storeRequisitionId, int materialItemId, decimal requestedQuantity);

    /// <summary>Moves the requisition to the next status of its flow (Submitted, Approved, Reserved, Issued, Closed).</summary>
    [PEMS.Application.Common.PemsOperation("StoreRequisition", "Advance", "StoreRequisition.View")]
    Task<(bool Success, string? Error)> AdvanceAsync(int id, string? reason, string? requestKey, StoreRequisitionActor actor);

    [PEMS.Application.Common.PemsOperation("StoreRequisition", "Reject", "StoreRequisition.View")]
    Task<(bool Success, string? Error)> RejectAsync(int id, string? reason, StoreRequisitionActor actor);

    [PEMS.Application.Common.PemsOperation("StoreRequisition", "Cancel", "StoreRequisition.View")]
    Task<(bool Success, string? Error)> CancelAsync(int id, string? reason, StoreRequisitionActor actor);

    /// <summary>Closes with a shortage after a partial issue: the rest of the reservation is released.</summary>
    [PEMS.Application.Common.PemsOperation("StoreRequisition", "CloseShort", "StoreRequisition.View")]
    Task<(bool Success, string? Error)> CloseShortAsync(int id, string? reason, StoreRequisitionActor actor);

    /// <summary>Sets the approved quantity of one line (Submitted only, Approve permission; the service checks).</summary>
    [PEMS.Application.Common.PemsOperation("StoreRequisition", "SetApprovedQuantity", "StoreRequisition.View")]
    Task<(bool Success, string? Error)> SetApprovedQuantityAsync(int id, int lineId, decimal approved, string? requestKey, StoreRequisitionActor actor);

    /// <summary>Issues the whole remaining reservation of every line.</summary>
    [PEMS.Application.Common.PemsOperation("StoreRequisition", "IssueAll", "StoreRequisition.View")]
    Task<(bool Success, string? Error)> IssueAllAsync(int id, string? reason, string? requestKey, StoreRequisitionActor actor);

    /// <summary>Issues a quantity of one line (a partial issue leaves the requisition PartiallyIssued).</summary>
    [PEMS.Application.Common.PemsOperation("StoreRequisition", "IssueLine", "StoreRequisition.View")]
    Task<(bool Success, string? Error)> IssueLineAsync(int id, int lineId, decimal quantity, string? reason, string? requestKey, StoreRequisitionActor actor);
}
