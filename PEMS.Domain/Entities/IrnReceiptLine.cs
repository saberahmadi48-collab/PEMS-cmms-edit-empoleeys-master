using PEMS.Domain.Common;

namespace PEMS.Domain.Entities;

public class IrnReceiptLine : BaseEntity
{
    public int IrnReceiptLineId { get; set; }

    public int IrnReceiptId { get; set; }
    public IrnReceipt? IrnReceipt { get; set; }

    public int MaterialItemId { get; set; }
    public MaterialItem? MaterialItem { get; set; }

    public decimal ReceivedQuantity { get; set; }

    // طبق ممیزی Material Management (Gap B) + Step 3: نتیجه‌ی Disposition به‌ازای هر ردیف -
    // عمداً nullable (نه یک Enum سراسری) چون تا وقتی Disposition ثبت نشده، این سه فیلد باید
    // null بمانند (نه صفر) تا "هنوز بررسی نشده" با "بررسی شد و مقدار صفر تایید شد" اشتباه گرفته
    // نشود. AcceptedQuantity+RejectedQuantity+HoldQuantity باید دقیقاً برابر ReceivedQuantity شود
    // (اعتبارسنجی در IrnReceiptService، نه این‌جا). فقط AcceptedQuantity به InventoryTransaction/
    // MaterialStock تبدیل می‌شود - Rejected/Hold هرگز به موجودی Post نمی‌شوند (طبق اصل «Received
    // ≠ Available» - نه با یک ستون Condition جدید روی MaterialStock، بلکه با این‌که اصلاً چیزی
    // برای مقدار رد/بلوکه‌شده Post نمی‌شود).
    public decimal? AcceptedQuantity { get; set; }
    public decimal? RejectedQuantity { get; set; }
    public decimal? HoldQuantity { get; set; }

    public int? UomId { get; set; }
    public MaterialUOM? Uom { get; set; }

    // اختیاری - محل فیزیکی دریافت در همین انبار، اگر شناخته‌شده باشد.
    public int? StorageLocationId { get; set; }
    public StorageLocation? StorageLocation { get; set; }

    public string? Notes { get; set; }

    // ---- SCRUM-473: receiving against the PO line (null for a receipt without a PO line, e.g. an old or a manual IRN)
    public int? PurchaseOrderLineId { get; set; }
    public PurchaseOrderLine? PurchaseOrderLine { get; set; }

    /// <summary>
    /// D6b (owner 2026-10-30): what happens to the rejected quantity (RejectedQuantity + HoldRejectedQuantity) on the PO line.
    /// Null = not decided yet, treated as <see cref="IrnRejectionActions.Replace"/> (the line stays open for a replacement).
    /// </summary>
    public string? RejectionAction { get; set; }
    public DateTime? RejectionDecidedAt { get; set; }
    public string? RejectionDecidedByUserName { get; set; }

    /// <summary>D7 (owner 2026-10-30): the Warehouse Manager resolves the Hold quantity; the two parts add up to HoldQuantity.</summary>
    public decimal? HoldAcceptedQuantity { get; set; }
    public decimal? HoldRejectedQuantity { get; set; }
    public DateTime? HoldResolvedAt { get; set; }
    public string? HoldResolvedByUserName { get; set; }
    public string? HoldResolutionNotes { get; set; }

    public bool HasOpenHold => (HoldQuantity ?? 0m) > 0m && HoldResolvedAt == null;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedDate { get; set; } = DateTime.Now;
}

/// <summary>SCRUM-473 / D6b: the buyer's decision on a rejected quantity.</summary>
public static class IrnRejectionActions
{
    /// <summary>The vendor replaces the goods: the quantity reopens on the PO line (the default).</summary>
    public const string Replace = "Replace";
    /// <summary>The PO line is closed short by the rejected quantity.</summary>
    public const string CloseShort = "CloseShort";

    public static readonly string[] All = { Replace, CloseShort };
}
