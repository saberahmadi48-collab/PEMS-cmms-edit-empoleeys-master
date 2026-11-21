namespace PEMS.Application.Interfaces;

/// <summary>One incoming receipt (IRN) of the list.</summary>
public sealed record IrnReceiptRow(int IrnReceiptId, string IrnNumber, string Status, string Vendor, string Warehouse, string ReceivedBy, DateTime ReceivingDate, int LineCount);

/// <summary>The head of one IRN (one row).</summary>
public sealed record IrnReceiptDetailRow(int IrnReceiptId, string IrnNumber, string Status, string Vendor, string Warehouse, string PurchaseOrder, string ReceivedBy, DateTime ReceivingDate, string Notes);

/// <summary>One line of an IRN with its inspection result.</summary>
public sealed record IrnReceiptLineRow(int IrnReceiptLineId, string Material, decimal Received, decimal? Accepted, decimal? Rejected, decimal? Hold, string Unit, string RejectionAction, string HoldResolution, string Notes);

public sealed record IrnReceiptHistoryRow(int Id, DateTime Date, string From, string To, string By, string Reason);

public sealed record IrnDispositionRow(int Id, DateTime Date, string By, string Result, string Notes, string WorkflowOutcome);

/// <summary>One line of the OS&amp;D report (over, short and damaged) of an IRN.</summary>
public sealed record IrnOsdRow(int IrnReceiptLineId, int? PoLineNo, string Material, decimal Ordered, decimal ReceivedBefore, decimal ThisReceipt, decimal Over, decimal Short, decimal? Accepted, decimal Rejected, decimal Hold, string Action, string Notes);

/// <summary>
/// The incoming receipt (IRN, SCRUM-655) with its QC / disposition gate as operations. The reads are what IrnReceiptController showed; the commands are the controller's own logic moved into a service: create, add a line,
/// the disposition (it starts the independent QA approval workflow, with the compensation when the workflow cannot start), the QA approval (it executes the workflow step and posts the accepted quantity to stock, with the recovery of a
/// posting that failed after the step), and the receiving against PO lines. Submit and cancel are marked on IIrnReceiptService, the rejection decision and the hold resolution on IPurchaseOrderReceivingService.
/// The disposition is typed as a mode for every line (AcceptAll, RejectAll, HoldAll) or as lines "lineId:accepted:rejected:hold" separated by semicolons.
/// </summary>
public interface IIrnReceiptOperations
{
    [PEMS.Application.Common.PemsOperation("Irn", "List", "IRN.View")]
    Task<List<IrnReceiptRow>> ListAsync(string? status = null);

    [PEMS.Application.Common.PemsOperation("Irn", "Detail", "IRN.View")]
    Task<List<IrnReceiptDetailRow>> DetailAsync(int id);

    [PEMS.Application.Common.PemsOperation("Irn", "Lines", "IRN.View")]
    Task<List<IrnReceiptLineRow>> LinesAsync(int id);

    [PEMS.Application.Common.PemsOperation("Irn", "History", "IRN.View")]
    Task<List<IrnReceiptHistoryRow>> HistoryAsync(int id);

    [PEMS.Application.Common.PemsOperation("Irn", "Dispositions", "IRN.View")]
    Task<List<IrnDispositionRow>> DispositionsAsync(int id);

    /// <summary>The lines of the order the receipt is raised against, with what is received and still open.</summary>
    [PEMS.Application.Common.PemsOperation("Irn", "PoLines", "IRN.View")]
    Task<List<PurchaseOrderReceivingRow>> PoLinesAsync(int id);

    [PEMS.Application.Common.PemsOperation("Irn", "Osd", "IRN.View")]
    Task<List<IrnOsdRow>> OsdAsync(int id);

    [PEMS.Application.Common.PemsOperation("Irn", "Vendors", "IRN.Create")]
    Task<List<ReportChoiceRow>> VendorsAsync();

    [PEMS.Application.Common.PemsOperation("Irn", "Warehouses", "IRN.Create")]
    Task<List<ReportChoiceRow>> WarehousesAsync();

    [PEMS.Application.Common.PemsOperation("Irn", "PurchaseOrders", "IRN.Create")]
    Task<List<ReportChoiceRow>> PurchaseOrdersAsync();

    [PEMS.Application.Common.PemsOperation("Irn", "StockMaterials", "IRN.Create")]
    Task<List<ReportChoiceRow>> StockMaterialsAsync();

    [PEMS.Application.Common.PemsOperation("Irn", "Create", "IRN.Create")]
    Task<(bool Success, string? Error)> CreateAsync(int vendorId, int warehouseId, DateTime? receivingDate, int? purchaseOrderId, string? notes, int? employeeId);

    [PEMS.Application.Common.PemsOperation("Irn", "AddLine", "IRN.Create")]
    Task<(bool Success, string? Error)> AddLineAsync(int irnReceiptId, int materialItemId, decimal receivedQuantity);

    /// <summary>Records the disposition of every line by a mode (AcceptAll, RejectAll, HoldAll) and starts the QA approval workflow.</summary>
    [PEMS.Application.Common.PemsOperation("Irn", "Disposition", "IRN.Edit")]
    Task<(bool Success, string? Error)> DispositionAsync(int id, string mode, string? notes, int? employeeId);

    /// <summary>Records the disposition of chosen lines as "lineId:accepted:rejected:hold;..." and starts the QA approval workflow.</summary>
    [PEMS.Application.Common.PemsOperation("Irn", "DispositionLines", "IRN.Edit")]
    Task<(bool Success, string? Error)> DispositionLinesAsync(int id, string lines, string? notes, int? employeeId);

    /// <summary>The QA decision (Approve or Reject) on a disposition awaiting approval; an approval posts the accepted quantity to stock.</summary>
    [PEMS.Application.Common.PemsOperation("Irn", "Approve", "IRN.Approve")]
    Task<(bool Success, string? Error)> ApproveAsync(List<string> userRoles, int id, string decision, string? comment, int? employeeId, string? ip, string? userAgent);

    /// <summary>Receives a quantity against one line of the order the receipt is raised against.</summary>
    [PEMS.Application.Common.PemsOperation("Irn", "ReceiveFromPo", "IRN.Create")]
    Task<PoReceivingResult> ReceiveFromPoAsync(int id, int purchaseOrderLineId, decimal quantity, PoReceivingActor actor);
}
