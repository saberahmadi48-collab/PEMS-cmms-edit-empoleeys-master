namespace PEMS.Domain.Entities;

/// <summary>
/// طبق سند Inventory بخش ۱۱: متریال مورد نیاز یک WorkOrder (برنامه‌ریزی مصرف) - جدا از
/// Reservation/Transaction واقعی، مثل RequiredSkill برای WorkOrder+Skill.
/// </summary>
public class WorkOrderMaterial
{
    public int WorkOrderMaterialId { get; set; }

    public int WorkOrderId { get; set; }
    public WorkOrder? WorkOrder { get; set; }

    public int MaterialItemId { get; set; }
    public MaterialItem? MaterialItem { get; set; }

    public decimal RequiredQuantity { get; set; }

    public int? UomId { get; set; }
    public MaterialUOM? Uom { get; set; }

    /// <summary>
    /// SCRUM-472: how the line is supplied (SAP stock / non-stock component, IFS supply code).
    /// <see cref="WorkOrderMaterialSupplyCodes.Stock"/> goes through the warehouse intake;
    /// <see cref="WorkOrderMaterialSupplyCodes.DirectPurchase"/> skips the warehouse and goes to procurement.
    /// </summary>
    public string SupplyCode { get; set; } = WorkOrderMaterialSupplyCodes.Stock;

    public DateTime CreatedDate { get; set; } = DateTime.Now;
}

/// <summary>SCRUM-472: supply codes of a work-order material line.</summary>
public static class WorkOrderMaterialSupplyCodes
{
    /// <summary>Issued from a warehouse the storekeeper picks; shortages are replenished.</summary>
    public const string Stock = "Stock";

    /// <summary>One-time, non-stock or service need: bought for the work order, never netted against stock.</summary>
    public const string DirectPurchase = "DirectPurchase";

    public static readonly IReadOnlyList<string> All = new[] { Stock, DirectPurchase };

    public static bool IsValid(string? code) => code != null && All.Contains(code);
}
