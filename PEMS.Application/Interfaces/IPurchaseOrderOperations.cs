namespace PEMS.Application.Interfaces;

/// <summary>One purchase order of the list.</summary>
public sealed record PurchaseOrderRow(int PoId, string PoNumber, string Project, string Vendor, string Contract, string Description, decimal? Value, string Status, int VdrItems);

/// <summary>The head of one purchase order with its approval route (one row).</summary>
public sealed record PurchaseOrderDetailRow(int PoId, string PoNumber, string Project, string Vendor, string Contract, string Description, decimal? Value, string Status, string Currency, decimal Total, string Requisition,
    string ApproverRole, int? AuthorityLevel, DateTime? SubmittedAt, string SubmittedBy, DateTime? ApprovedAt, string ApprovedBy, string RejectionReason, string CreatedBy, bool RouteInvalid, bool ResponsibleNotSet);

/// <summary>One line of the order.</summary>
public sealed record PurchaseOrderLineRow(int PurchaseOrderLineId, int LineNo, string Description, string PartNumber, bool InCatalogue, decimal Quantity, string Unit, decimal UnitPrice, decimal LineAmount, DateTime? RequiredDate,
    bool IsStockLine, string CommodityGroup, string Warehouse, string FreeTextReason);

/// <summary>One line with what was received against it.</summary>
public sealed record PurchaseOrderReceivingRow(int PurchaseOrderLineId, int LineNo, string Description, string Material, string Warehouse, decimal Ordered, decimal Received, decimal InQc, decimal Accepted, decimal OnHold,
    decimal Outstanding, decimal OverReceived, bool InspectionRequired, DateTime? InspectionReleasedAt, string InspectionPlan);

/// <summary>
/// The purchase order (SCRUM-741) as operations: the reads are what PurchaseOrderController showed (list, head with the approval route, lines, receiving) and the lookups of its forms; the create commands keep the
/// access-control record behind the separation-of-duties rule (the creator may not approve or issue); the line, submit, route, approve, reject and inspection-release commands are the operations marked on
/// IPurchaseOrderApprovalService and IPurchaseOrderReceivingService; issue is marked on IPurchaseOrderService and runs under the strict access guard.
/// </summary>
public interface IPurchaseOrderOperations
{
    [PEMS.Application.Common.PemsOperation("PurchaseOrder", "List", "PurchaseOrder.View")]
    Task<List<PurchaseOrderRow>> ListAsync();

    [PEMS.Application.Common.PemsOperation("PurchaseOrder", "Detail", "PurchaseOrder.View")]
    Task<List<PurchaseOrderDetailRow>> DetailAsync(int id);

    [PEMS.Application.Common.PemsOperation("PurchaseOrder", "Lines", "PurchaseOrder.View")]
    Task<List<PurchaseOrderLineRow>> LinesAsync(int id);

    [PEMS.Application.Common.PemsOperation("PurchaseOrder", "Receiving", "PurchaseOrder.View")]
    Task<List<PurchaseOrderReceivingRow>> ReceivingAsync(int id);

    [PEMS.Application.Common.PemsOperation("PurchaseOrder", "Projects", "PurchaseOrder.Create")]
    Task<List<ReportChoiceRow>> ProjectsAsync();

    [PEMS.Application.Common.PemsOperation("PurchaseOrder", "Vendors", "PurchaseOrder.Create")]
    Task<List<ReportChoiceRow>> VendorsAsync();

    [PEMS.Application.Common.PemsOperation("PurchaseOrder", "Contracts", "PurchaseOrder.Create")]
    Task<List<ReportChoiceRow>> ContractsAsync();

    /// <summary>The requisitions that may feed a purchase order.</summary>
    [PEMS.Application.Common.PemsOperation("PurchaseOrder", "Requisitions", "PurchaseOrder.View")]
    Task<List<ReportChoiceRow>> RequisitionsAsync();

    [PEMS.Application.Common.PemsOperation("PurchaseOrder", "Create", "PurchaseOrder.Create")]
    Task<(bool Success, string? Error)> CreateAsync(string poNumber, int projectId, int vendorId, int? contractId, string? description, decimal? value, int? userId, string? ip);

    [PEMS.Application.Common.PemsOperation("PurchaseOrder", "CreateFromRequisition", "PurchaseOrder.Create")]
    Task<PoResult> CreateFromRequisitionAsync(int mrId, int vendorId, string currency, string? poNumber, PoActor actor, int? userId, string? ip);
}
