namespace PEMS.Application.Interfaces;

/// <summary>SCRUM-470: <c>Procurement__*</c> settings (env files).</summary>
public class ProcurementOptions
{
    /// <summary>True = every purchase-order line must be a catalogued part (no free-text lines).</summary>
    public bool CatalogueOnlyLines { get; set; }
}

/// <summary>Who acts on a purchase order (identity from the server-side principal).</summary>
public sealed record PoActor(int? EmployeeId, IReadOnlyList<string> Roles, string? UserName);

/// <summary>Outcome of a PO command. <c>Error</c> is a resource key (<c>Po_Err_*</c>) or a message passed through from the PO service.</summary>
/// <param name="Args">SCRUM-547: values for the placeholders of the <paramref name="Error"/> text (e.g. the line number), or null.</param>
public sealed record PoResult(bool Success, string? Error, int? Id = null, object[]? Args = null)
{
    public static PoResult Fail(string error) => new(false, error);
}

/// <summary>Editable values of a PO line in Draft.</summary>
/// <param name="WarehouseId">SCRUM-473: delivery warehouse; required on a stock line at submit.</param>
public sealed record PoLineInput(decimal Quantity, decimal UnitPrice, DateTime? RequiredDate, bool IsStockLine, int? CommodityGroupId, string? FreeTextReason,
    int? WarehouseId = null);

public sealed record PoLineView(
    int PurchaseOrderLineId, int LineNo, string Description, string? PartNumber, bool IsCatalogued, decimal Quantity, string? Unit,
    decimal UnitPrice, decimal LineAmount, DateTime? RequiredDate, bool IsStockLine, int? CommodityGroupId, string? CommodityGroup,
    string? FreeTextReason, int? MaterialRequisitionItemId, int? TenderId, int? BidId, int? WarehouseId = null, string? WarehouseCode = null);

public sealed record PoApprovalView(
    int PoId, string PoNumber, string Status, string? Currency, decimal Total, int? MaterialRequisitionId, string? MaterialRequisitionNumber,
    string? ApproverRole, int? AuthorityRuleLevel, DateTime? SubmittedAt, string? SubmittedBy, DateTime? ApprovedAt, string? ApprovedBy,
    string? RejectionReason, string? CreatedBy, IReadOnlyList<PoLineView> Lines, bool RouteInvalid = false)
{
    // RouteInvalid (SCRUM-546): submitted and routed, but the rule is gone, inactive or names another role, or the role lost PurchaseOrder Approve.

    /// <summary>Submitted but no authority rule matched: the PO waits for the admin to add a rule.</summary>
    public bool ResponsibleNotSet => Status == "Submitted" && string.IsNullOrEmpty(ApproverRole);
}

public sealed record PoAuthorityRuleInput(int? Id, int Level, decimal MinAmount, decimal? MaxAmount, string Currency, string ApproverRole, string? Description, bool IsActive);

public sealed record PoAuthorityRuleView(int Id, int Level, decimal MinAmount, decimal? MaxAmount, string Currency, string ApproverRole, string? Description, bool IsActive);

/// <summary>
/// SCRUM-470: purchase-order lines from the pre-procurement path (MR items) and approval by the PO authority table
/// (owner decisions 2026-10-29). Draft → Submitted → Approved; Issue (Approved → Issued) stays in IPurchaseOrderService.
/// </summary>
public interface IPurchaseOrderApprovalService
{
    /// <summary>MRs whose items may become PO lines (issued to procurement, or raised from the procurement cartable).</summary>
    Task<IReadOnlyList<(int Id, string Number, string Description, int ProjectId)>> GetRequisitionsForPoAsync();

    Task<IReadOnlyList<(int Id, string Code, string Description)>> GetCommodityGroupsAsync();

    /// <summary>Creates a Draft PO for the MR's project and adds every MR item that is not already on a live PO.</summary>
    Task<PoResult> CreateFromRequisitionAsync(int mrId, int vendorId, string currency, string? poNumber, PoActor actor);

    /// <summary>Adds the MR's free items to a Draft PO of the same project.</summary>
    [PEMS.Application.Common.PemsOperation("PurchaseOrder", "AddLinesFromRequisition", "PurchaseOrder.View")]
    Task<PoResult> AddLinesFromRequisitionAsync(int poId, int mrId, PoActor actor);

    [PEMS.Application.Common.PemsOperation("PurchaseOrder", "UpdateLine", "PurchaseOrder.View")]
    Task<PoResult> UpdateLineAsync(int lineId, PoLineInput input, PoActor actor);

    [PEMS.Application.Common.PemsOperation("PurchaseOrder", "RemoveLine", "PurchaseOrder.View")]
    Task<PoResult> RemoveLineAsync(int lineId, PoActor actor);

    Task<PoApprovalView?> GetAsync(int poId);

    /// <summary>Checks the lines, totals the value and routes the PO to the role of the matching authority rule (or leaves it "responsible not set").</summary>
    [PEMS.Application.Common.PemsOperation("PurchaseOrder", "Submit", "PurchaseOrder.View")]
    Task<PoResult> SubmitAsync(int poId, PoActor actor);

    /// <summary>Retries the routing of a submitted PO whose responsible was not set (after the admin added a rule), or whose route is no
    /// longer valid (SCRUM-546: the open task of the old role is cancelled first).</summary>
    [PEMS.Application.Common.PemsOperation("PurchaseOrder", "Route", "PurchaseOrder.View")]
    Task<PoResult> RouteAsync(int poId, PoActor actor);

    /// <summary>SCRUM-546: the roles that may be chosen as PO approver (those with PurchaseOrder Approve).</summary>
    [PEMS.Application.Common.PemsOperation("PoAuthority", "ApproverRoles", Roles = "SystemAdmin")]
    Task<IReadOnlyList<string>> GetApproverRolesAsync();

    /// <summary>Approval by a holder of the routed role who neither created nor submitted the PO.</summary>
    [PEMS.Application.Common.PemsOperation("PurchaseOrder", "Approve", "PurchaseOrder.View")]
    Task<PoResult> ApproveAsync(int poId, PoActor actor);

    /// <summary>Back to Draft with a reason.</summary>
    [PEMS.Application.Common.PemsOperation("PurchaseOrder", "Reject", "PurchaseOrder.View")]
    Task<PoResult> RejectAsync(int poId, string reason, PoActor actor);

    [PEMS.Application.Common.PemsOperation("PoAuthority", "Rules", Roles = "SystemAdmin")]
    Task<IReadOnlyList<PoAuthorityRuleView>> GetRulesAsync();

    [PEMS.Application.Common.PemsOperation("PoAuthority", "Save", Roles = "SystemAdmin")]
    Task<PoResult> SaveRuleAsync(PoAuthorityRuleInput input, PoActor actor);
}
