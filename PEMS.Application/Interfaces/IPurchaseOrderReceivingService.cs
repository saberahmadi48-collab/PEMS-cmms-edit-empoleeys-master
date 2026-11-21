namespace PEMS.Application.Interfaces;

// SCRUM-473: receiving against purchase-order lines (gap analysis item 7, design §5 P7).
// Owner decisions 2026-10-30: D6a in-QC counts as incoming; D6b the buyer decides replace / close short on a rejection;
// D7 the Warehouse Manager resolves Hold from My Work; an over-receipt is allowed with a warning and shows on the OS&D report.

public sealed record PoReceivingActor(int? EmployeeId, string? UserName, IReadOnlyList<string> Roles);

/// <summary>Errors and warnings are resource keys (Rcv_*); a warning never stops the action.</summary>
public sealed record PoReceivingResult(bool Success, string? Error, IReadOnlyList<string> Warnings)
{
    public static PoReceivingResult Ok(IReadOnlyList<string>? warnings = null) => new(true, null, warnings ?? Array.Empty<string>());
    public static PoReceivingResult Fail(string error) => new(false, error, Array.Empty<string>());
}

/// <summary>One PO line with its quantity buckets (see PEMS.Application.Inventory.PoLineSupply).</summary>
public sealed record PoLineReceivingView(
    int PurchaseOrderLineId,
    int LineNo,
    string Description,
    string? PartNumber,
    int? MaterialItemId,
    string? MaterialCode,
    string? Unit,
    bool IsStockLine,
    int? WarehouseId,
    string? WarehouseCode,
    decimal Ordered,
    decimal Received,
    decimal InQc,
    decimal Accepted,
    decimal OnHold,
    decimal RejectedReplaced,
    decimal RejectedClosedShort,
    decimal Outstanding,
    decimal OverReceived,
    bool InspectionRequired,
    DateTime? InspectionReleasedAt,
    string? InspectionReleasedBy,
    string? InspectionTestPlanNumber)
{
    /// <summary>Only a catalogued line can be received on an IRN (an IRN line is always a material item).</summary>
    public bool CanReceive => MaterialItemId.HasValue;
}

public sealed record IrnPoLineInput(int PurchaseOrderLineId, decimal Quantity);

/// <summary>OS&D (over, short and damaged) report of one IRN, for the vendor.</summary>
public sealed record OsdReport(
    int IrnReceiptId,
    string IrnNumber,
    string IrnStatus,
    DateTime ReceivingDate,
    string VendorName,
    string WarehouseCode,
    string? PoNumber,
    string? ReceivedBy,
    IReadOnlyList<OsdLine> Lines)
{
    public bool HasDiscrepancy => Lines.Any(l => l.Over > 0m || l.Short > 0m || l.Rejected > 0m || l.Hold > 0m || l.WithoutPoLine);
}

public sealed record OsdLine(
    int IrnReceiptLineId,
    int? PoLineNo,
    string MaterialCode,
    string Description,
    string? Unit,
    bool WithoutPoLine,
    decimal Ordered,
    decimal ReceivedBefore,
    decimal ThisReceipt,
    decimal Over,
    decimal Short,
    decimal? Accepted,
    decimal Rejected,
    decimal Hold,
    string? RejectionAction,
    string? Notes);

public interface IPurchaseOrderReceivingService
{
    /// <summary>The lines of a PO with their received / open quantities and inspection release.</summary>
    Task<IReadOnlyList<PoLineReceivingView>> GetPoLinesAsync(int poId);

    /// <summary>Adds IRN lines (Draft IRN of an issued PO) against PO lines; over-receipt, an unreleased inspection or a different
    /// warehouse is a warning, not an error.</summary>
    Task<PoReceivingResult> ReceiveAgainstPoAsync(int irnReceiptId, IReadOnlyList<IrnPoLineInput> lines, PoReceivingActor actor);

    /// <summary>D6b: the buyer chooses Replace (the PO line reopens) or CloseShort for the rejected quantity of an IRN line.</summary>
    [PEMS.Application.Common.PemsOperation("Irn", "DecideRejection", "PurchaseOrder.Edit")]
    Task<PoReceivingResult> DecideRejectionAsync(int irnReceiptLineId, string action, PoReceivingActor actor);

    /// <summary>D7: after the disposition is approved, an IRN with Hold quantity becomes a task for the Warehouse Manager.</summary>
    Task<PoReceivingResult> StartHoldResolutionAsync(int irnReceiptId, int? employeeId);

    /// <summary>D7: the Warehouse Manager splits the Hold quantity into accepted (posted to stock) and rejected (D6b).</summary>
    [PEMS.Application.Common.PemsOperation("Irn", "ResolveHold", "IRN.Edit")]
    Task<PoReceivingResult> ResolveHoldAsync(int irnReceiptLineId, decimal accepted, decimal rejected, string? notes, PoReceivingActor actor);

    /// <summary>Inspection release before shipment (optionally against an ITP), where the package requires inspection.</summary>
    [PEMS.Application.Common.PemsOperation("PurchaseOrder", "ReleaseInspection", "PurchaseOrder.View")]
    Task<PoReceivingResult> ReleaseInspectionAsync(int purchaseOrderLineId, int? inspectionTestPlanId, PoReceivingActor actor);

    Task<OsdReport?> GetOsdReportAsync(int irnReceiptId);
}
