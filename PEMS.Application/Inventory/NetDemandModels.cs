namespace PEMS.Application.Inventory;

// Phase 2 (P1-P3) - مدل‌های خالص (بدون EF/Controller/Workflow) برای «نیاز خالص» و «موجودی قابل‌استفاده».
// همه‌ی مقادیر decimal اند (هم‌نوع MaterialStock/StoreRequisitionLine/WorkOrderMaterial)؛ هیچ تبدیل واحدی انجام نمی‌شود.

public enum DemandSourceType
{
    StoreRequisitionLine = 1,
    WorkOrderMaterial = 2
}

public enum DemandUnitStatus
{
    /// <summary>واحد خط با واحد پایه‌ی متریال یکی است (یا خط واحد ندارد و پایه فرض می‌شود - نگاه کنید به NetDemandCollector).</summary>
    Compatible,
    /// <summary>واحد پایه‌ی متریال مشخص نیست؛ قابل‌مقایسه با موجودی نیست.</summary>
    Unknown,
    /// <summary>واحد خط با واحد پایه‌ی متریال فرق دارد؛ تبدیل واحد پیاده‌سازی نشده است.</summary>
    Incompatible
}

/// <summary>کد یکتای هر هشدار/استثنا. IsBlocking(code) تعیین می‌کند نتیجه «قطعی» نیست.</summary>
public enum NetDemandIssueCode
{
    // ---- استثنای ورودی تقاضا
    /// <summary>درخواست/دستور کار در وضعیت پایانی (Cancelled/Rejected/Closed/Completed...) - تقاضا نیست؛ اطلاع‌رسانی.</summary>
    CancelledOrIneligibleLifecycle,
    /// <summary>تقاضای بالقوه ولی هنوز متعهد نیست (SR در Submitted، WO در Open) - در مجموع قطعی نمی‌آید؛ اطلاع‌رسانی.</summary>
    PotentialDemandNotCommitted,
    /// <summary>همان خطِ منبع (نوع+شناسه) دو بار با مقدار یکسان آمده؛ فقط یک‌بار شمرده شد.</summary>
    DuplicateSourceLineIgnored,
    /// <summary>همان خطِ منبع با مقدارهای متفاوت تکرار شده؛ هیچ‌کدام شمرده نشد.</summary>
    ConflictingDuplicateSourceLine,
    /// <summary>نیاز WO با SR دارای همان WorkOrderId+متریال هم‌پوشان است و رابطه‌ی قطعی ندارند؛ نیاز WO از مجموع قطعی حذف شد.</summary>
    AmbiguousOverlapWithStoreRequisition,
    /// <summary>رزرو بدون StoreRequisitionLineId که به یک خط یکتا قابل انتساب نیست.</summary>
    AmbiguousReservationAttribution,
    UnitUnknown,
    UnitIncompatible,
    InvalidQuantity,
    UnknownLifecycleStatus,
    MaterialNotFound,
    InactiveMaterial,
    NotStockItem,
    /// <summary>نیاز WO انبار ندارد؛ با موجودی هیچ انبار مشخصی مقایسه نمی‌شود (pool نمی‌کنیم).</summary>
    WorkOrderDemandNotAttributableToWarehouse,

    // ---- موجودی
    StockNoRecord,
    StockAmbiguousRows,
    StockMultipleLocationRows,
    StockNegativeOnHand,
    StockNegativeReserved,
    StockReservedExceedsOnHand,
    StockInactiveRowHasQuantity,
    WarehouseNotFound,
    WarehouseInactive,
    StockMissing,

    // ---- سطح نتیجه
    ReservationExceedsRemainingDemand,
    IncomingSupplyNotModeled,
    ReorderPolicyNotEvaluated,

    // ---- SCRUM-472 (appended: existing values keep their numbers)
    /// <summary>The work-order line has supply code DirectPurchase: it is bought for the work order and never netted against stock.</summary>
    DirectPurchaseNotStockDemand,

    // ---- SCRUM-473 (appended)
    /// <summary>An open PO line for this material and warehouse has a unit that differs from the material's unit; its incoming quantity
    /// cannot be counted without a conversion (D5), so the result is not definitive.</summary>
    IncomingUnitIncompatible,
    /// <summary>An open stock line of an issued PO for this material has no delivery warehouse; its incoming quantity cannot be placed.</summary>
    IncomingWarehouseUnknown
}

public static class NetDemandIssues
{
    /// <summary>
    /// true = نتیجه‌ی مربوطه «قطعی» نیست (داده مبهم/نامعتبر/غیرقابل‌مقایسه یا ورودی حذف‌شده‌ی نیازمند بررسی).
    /// اطلاع‌رسانی‌ها (مثلاً تقاضای لغوشده، تکرار قطعی یکسان، نبود ردیف موجودی = صفر) قطعی‌بودن را از بین نمی‌برند.
    /// </summary>
    public static bool IsBlocking(NetDemandIssueCode code) => code switch
    {
        NetDemandIssueCode.CancelledOrIneligibleLifecycle => false,
        NetDemandIssueCode.PotentialDemandNotCommitted => false,
        NetDemandIssueCode.DuplicateSourceLineIgnored => false,
        NetDemandIssueCode.StockNoRecord => false,
        NetDemandIssueCode.StockMultipleLocationRows => false,
        NetDemandIssueCode.ReservationExceedsRemainingDemand => false,
        NetDemandIssueCode.IncomingSupplyNotModeled => false,
        NetDemandIssueCode.ReorderPolicyNotEvaluated => false,
        NetDemandIssueCode.DirectPurchaseNotStockDemand => false,   // SCRUM-472: a deliberate choice, not bad data
        _ => true
    };
}

public sealed record NetDemandIssue(NetDemandIssueCode Code, string? Detail = null)
{
    public bool IsBlocking => NetDemandIssues.IsBlocking(Code);
}

/// <summary>مبدأ ردیابی‌پذیر یک مقدار تقاضا. وقتی کاربر به منبع دسترسی ندارد، شناسه‌ها صفر و IsRestricted=true می‌شوند (مقدار همچنان شمرده می‌شود).</summary>
public sealed record DemandOrigin(DemandSourceType Source, int SourceLineId, int ParentId, string? ParentNumber, bool IsRestricted = false);

/// <summary>
/// یک مقدار تقاضای واجد شرایط. GrossRemainingQuantity = مقدار باقی‌مانده‌ی تأییدشده/مورد نیاز (شامل بخشی که قبلاً رزرو شده)؛
/// CoveredByReservationQuantity = بخشی از همین مقدار که رزرو فعال دارد (و در MaterialStock.ReservedQuantity لحاظ شده است).
/// </summary>
public sealed record DemandLine(
    DemandOrigin Origin,
    int MaterialItemId,
    int? WarehouseId,
    decimal GrossRemainingQuantity,
    decimal CoveredByReservationQuantity,
    DemandUnitStatus UnitStatus = DemandUnitStatus.Compatible);

public sealed record ExcludedDemand(
    DemandOrigin Origin,
    int MaterialItemId,
    int? WarehouseId,
    NetDemandIssueCode Reason,
    string? Detail = null,
    decimal? Quantity = null);

public sealed record ReorderPolicy(decimal? Minimum, decimal? Maximum, decimal? ReorderPoint, decimal? ReorderQuantity);

public enum StockState
{
    /// <summary>دقیقاً یک تجمیع قابل‌اتکا (یک یا چند ردیف با محل متفاوت).</summary>
    Ok,
    /// <summary>هیچ ردیف فعالی نیست ⇒ موجودی صفر (همان رفتار سرویس رزرو: ردیف نبود = موجودی نیست).</summary>
    NoStockRecord,
    /// <summary>چند ردیف فعال با محل NULL برای یک (متریال، انبار) - انتخاب دلخواه ممنوع؛ محاسبه نمی‌شود.</summary>
    AmbiguousRows,
    /// <summary>متریال/انبار/واحد نامعتبر - محاسبه نمی‌شود.</summary>
    NotCalculable
}

/// <summary>
/// موجودی قابل‌استفاده‌ی یک (متریال، انبار). RawUsable = OnHand - Reserved (بدون Clamp، می‌تواند منفی باشد و همان‌طور گزارش می‌شود).
/// Quarantine/Blocked در مدل وجود ندارد؛ مقدار Hold/Rejected اصلاً Post نمی‌شود پس در OnHand نیست.
/// </summary>
public sealed record UsableStockResult(
    int MaterialItemId,
    int WarehouseId,
    StockState State,
    decimal? OnHand,
    decimal? Reserved,
    decimal? RawUsable,
    int ActiveRowCount,
    ReorderPolicy? Policy,
    IReadOnlyList<NetDemandIssue> Issues)
{
    public bool HasBlockingIssue => Issues.Any(i => i.IsBlocking);
}

public sealed record StockKey(int MaterialItemId, int WarehouseId);

public enum IncomingSupplyStatus
{
    /// <summary>PO-line و ارتباط رسید↔PO-line وجود ندارد ⇒ مقدار ورودی نامعلوم است (صفر نیست).</summary>
    NotModeled,
    Known
}

public enum NetDemandResultKind
{
    /// <summary>فقط موجودی در برابر تقاضای رزروناشده؛ ورودی نامعلوم. «پیش‌بینی کامل» نیست.</summary>
    StockOnlyShortageEstimate,
    /// <summary>ورودی معلوم و ورودی‌ها قابل‌مقایسه ⇒ فرمول کامل (Projected / CommittedShortage).</summary>
    CompleteNetDemand,
    /// <summary>موجودی یا واحد/متریال قابل‌اتکا نیست.</summary>
    NotCalculable,
    /// <summary>تقاضای بدون انبار (WorkOrder) - با موجودی هیچ انباری مقایسه نمی‌شود.</summary>
    NotComparableNoWarehouse
}

public enum ReorderTopUpStatus
{
    NotConfigured,
    /// <summary>ReorderPoint تعریف شده ولی معنای Min/Max/ReorderQuantity و Lead Time تصمیم‌گیری نشده ⇒ ارزیابی نمی‌شود.</summary>
    PolicyPresentNotEvaluated,
    NotCalculable
}

public sealed record ReorderTopUp(ReorderTopUpStatus Status, ReorderPolicy? Policy)
{
    /// <summary>همیشه null در نسخه‌ی ۱: مقدار پیشنهادی ساخته نمی‌شود و هرگز با CommittedShortage جمع نمی‌شود.</summary>
    public decimal? SuggestedQuantity => null;
}

public sealed record DemandContribution(DemandOrigin Origin, decimal GrossRemaining, decimal CoveredByReservation, decimal Unreserved);

public sealed record NetDemandInput(
    int MaterialItemId,
    int? WarehouseId,
    UsableStockResult? Stock,
    IReadOnlyList<DemandLine> Demand,
    IReadOnlyList<ExcludedDemand> Excluded,
    IReadOnlyList<NetDemandIssue>? ItemLevelIssues = null,
    /// <summary>Incoming supply from open PO lines (SCRUM-473); null when no incoming-supply provider is wired ⇒ IncomingSupplyStatus.NotModeled.</summary>
    decimal? KnownIncomingSupply = null);

public sealed record NetDemandResult(
    int MaterialItemId,
    int? WarehouseId,
    NetDemandResultKind Kind,
    bool IsDefinitive,
    decimal GrossEligibleDemand,
    decimal CoveredByReservations,
    decimal UnreservedEligibleDemand,
    decimal? RawUsableStock,
    decimal? UsableStockForNetting,
    IncomingSupplyStatus IncomingSupply,
    decimal? StockOnlyShortage,
    decimal? ProjectedBalance,
    decimal? CommittedShortage,
    ReorderTopUp ReorderTopUp,
    IReadOnlyList<DemandContribution> Contributions,
    IReadOnlyList<ExcludedDemand> Excluded,
    IReadOnlyList<NetDemandIssue> Issues,
    /// <summary>Snapshot موجودی که محاسبه روی آن انجام شد (Phase 2 / P5 - ردیابی)؛ null وقتی موجودی قابل‌محاسبه نبوده.</summary>
    decimal? OnHandStock = null,
    decimal? ReservedStock = null);
