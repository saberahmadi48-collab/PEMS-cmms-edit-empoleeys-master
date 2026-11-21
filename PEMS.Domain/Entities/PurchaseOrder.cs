using System.ComponentModel.DataAnnotations;
using PEMS.Domain.Common;

namespace PEMS.Domain.Entities;

public static class PurchaseOrderStatuses
{
    public const string Draft = "Draft";
    /// <summary>SCRUM-470: submitted for approval by the PO authority table (approver role, or responsible not set).</summary>
    public const string Submitted = "Submitted";
    /// <summary>SCRUM-470: approved at the right authority level; only an approved PO is issued.</summary>
    public const string Approved = "Approved";
    public const string Issued = "Issued";
    public const string Closed = "Closed";
    public const string Cancelled = "Cancelled";
}

public class PurchaseOrder : BaseEntity
{
    [Key]
    public int PoId { get; set; }

    public string PoNumber { get; set; } = string.Empty;

    public int ProjectId { get; set; }
    public Project? Project { get; set; }

    public int VendorId { get; set; }
    public Vendor? Vendor { get; set; }

    // در صورت وجود قرارداد رسمی (اختیاری - در بسیاری از PO های واقعی هنوز ثبت نشده)
    public int? ContractId { get; set; }
    public Contract? Contract { get; set; }

    public string? Description { get; set; }

    public decimal? Value { get; set; }

    public string? Currency { get; set; } = "USD";

    // Draft, Issued, Closed, Cancelled
    // پیش‌فرض Issued برای سازگاری با داده‌ی قدیمی و مسیرهای Import حفظ شده؛ PO های دستی جدید
    // توسط PurchaseOrderService.CreateAsync با Draft ساخته می‌شوند و فقط با IssueAsync (مجوز
    // Approve + SoD) به Issued می‌روند.
    public string Status { get; set; } = PurchaseOrderStatuses.Issued;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedDate { get; set; } = DateTime.Now;

    public ICollection<VDRItem> VDRItems { get; set; } = new List<VDRItem>();

    // ---- SCRUM-470: lines and approval by the PO authority table
    public ICollection<PurchaseOrderLine> Lines { get; set; } = new List<PurchaseOrderLine>();

    /// <summary>The MR the lines came from (the pre-procurement path); null for imported or legacy POs.</summary>
    public int? MaterialRequisitionId { get; set; }
    public MaterialRequisition? MaterialRequisition { get; set; }

    public DateTime? SubmittedAt { get; set; }
    public string? SubmittedByUserName { get; set; }

    /// <summary>The authority rule matched at submission; null while the responsible is not set.</summary>
    public int? AuthorityRuleId { get; set; }
    public PurchaseOrderAuthorityRule? AuthorityRule { get; set; }
    public string? ApproverRole { get; set; }

    public DateTime? ApprovedAt { get; set; }
    public string? ApprovedByUserName { get; set; }

    /// <summary>Reason of the last rejection (the PO is back in Draft).</summary>
    public string? RejectionReason { get; set; }
}

/// <summary>
/// SCRUM-470: a purchase-order line. It comes from an MR item (pre-procurement path) and keeps the registered part. A line that
/// goes into stock must be catalogued; a one-time / direct-use / service line may be free text with a commodity group and a
/// reason (SAP text item, IFS no-part line).
/// </summary>
public class PurchaseOrderLine : BaseEntity
{
    public int PurchaseOrderLineId { get; set; }

    public int PoId { get; set; }
    public PurchaseOrder? PurchaseOrder { get; set; }

    public int LineNo { get; set; }

    public int? MaterialRequisitionItemId { get; set; }
    public MaterialRequisitionItem? MaterialRequisitionItem { get; set; }

    /// <summary>The registered part (Material Master); a completed catalogue request also counts as catalogued.</summary>
    public int? MaterialItemId { get; set; }
    public MaterialItem? MaterialItem { get; set; }
    public int? PartCatalogueRequestId { get; set; }

    public string Description { get; set; } = string.Empty;
    public string? PartNumber { get; set; }

    public decimal Quantity { get; set; }
    public string? Unit { get; set; }
    public decimal UnitPrice { get; set; }
    public DateTime? RequiredDate { get; set; }

    /// <summary>True when the goods go into stock (then the line must be catalogued).</summary>
    public bool IsStockLine { get; set; } = true;

    public int? CommodityGroupId { get; set; }
    public PartCatalogueCommodityGroup? CommodityGroup { get; set; }
    public string? FreeTextReason { get; set; }

    public int? TenderId { get; set; }
    public int? BidId { get; set; }

    /// <summary>SCRUM-473: the warehouse the goods are delivered to (SAP plant / IFS site). Required on a stock line at submit;
    /// open quantity counts as incoming supply of this warehouse.</summary>
    public int? WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }

    /// <summary>SCRUM-473: inspection release before shipment, where the package requires inspection (optional link to its ITP).</summary>
    public int? InspectionTestPlanId { get; set; }
    public InspectionTestPlan? InspectionTestPlan { get; set; }
    public DateTime? InspectionReleasedAt { get; set; }
    public string? InspectionReleasedByUserName { get; set; }

    public ICollection<IrnReceiptLine> ReceiptLines { get; set; } = new List<IrnReceiptLine>();

    public bool IsCatalogued => MaterialItemId != null || PartCatalogueRequestId != null;
    public decimal LineAmount => Quantity * UnitPrice;
}

/// <summary>
/// SCRUM-470 (owner decision 2026-10-29): the PO delegation-of-authority table, separate from the contract rules. The admin sets
/// every level; nothing is seeded. A submitted PO is routed to the role of the active rule whose currency matches and whose
/// amount range holds the PO value (the lowest level when several match).
/// </summary>
public class PurchaseOrderAuthorityRule : BaseEntity
{
    public int PurchaseOrderAuthorityRuleId { get; set; }
    public int Level { get; set; }
    public decimal MinAmount { get; set; }
    /// <summary>Null means no upper limit.</summary>
    public decimal? MaxAmount { get; set; }
    public string Currency { get; set; } = "USD";
    public string ApproverRole { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedDate { get; set; } = DateTime.Now;
}
