using PEMS.Domain.Common;

namespace PEMS.Domain.Entities;

public class StoreRequisitionLine : BaseEntity
{
    public int StoreRequisitionLineId { get; set; }

    public int StoreRequisitionId { get; set; }
    public StoreRequisition? StoreRequisition { get; set; }

    public int MaterialItemId { get; set; }
    public MaterialItem? MaterialItem { get; set; }

    public decimal RequestedQuantity { get; set; }

    public decimal? ApprovedQuantity { get; set; }

    // طبق بخش ۱۳: از InventoryTransaction های Issue مرتبط با این خط مشتق/به‌روزرسانی می‌شه
    public decimal IssuedQuantity { get; set; }

    public int? UomId { get; set; }
    public MaterialUOM? Uom { get; set; }

    /// <summary>
    /// SCRUM-472: the work-order material line this requisition line supplies (set by the warehouse intake).
    /// The persisted link lets net demand count this line instead of the work-order line, instead of treating the
    /// pair as an ambiguous overlap. Null for requisitions raised by hand.
    /// </summary>
    public int? WorkOrderMaterialId { get; set; }
    public WorkOrderMaterial? WorkOrderMaterial { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedDate { get; set; } = DateTime.Now;
}
