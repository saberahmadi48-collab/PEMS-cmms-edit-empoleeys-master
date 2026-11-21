using PEMS.Domain.Common;

namespace PEMS.Domain.Entities;

/// <summary>
/// Phase 2 / P5 - وضعیت‌های توصیه‌ی تأمین. «زنده» = Open یا Dismissed: برای هر (متریال، انبار) حداکثر یک توصیه‌ی زنده وجود دارد
/// (ایندکس یکتای فیلترشده). Converted/Superseded برای P6 و بعد تعریف شده‌اند و در P5 هیچ گذاری به آن‌ها نمی‌رسد.
/// </summary>
public static class ReplenishmentStatuses
{
    public const string Open = "Open";
    public const string Dismissed = "Dismissed";
    /// <summary>رزرو برای P6 (تبدیل به MR/بسته‌ی تدارکات) - در P5 هیچ کدی این وضعیت را نمی‌نویسد.</summary>
    public const string Converted = "Converted";
    /// <summary>رزرو برای نسخه‌های بعد - در P5 هیچ کدی این وضعیت را نمی‌نویسد.</summary>
    public const string Superseded = "Superseded";
    public const string Closed = "Closed";

    public static readonly string[] Live = { Open, Dismissed };
}

public static class ReplenishmentHistoryActions
{
    public const string Created = "Created";
    public const string Updated = "Updated";
    public const string Dismissed = "Dismissed";
    public const string Reopened = "Reopened";
    public const string AutoReopened = "AutoReopened";
    public const string Closed = "Closed";
    public const string NotDefinitive = "NotDefinitive";
    public const string DefinitiveAgain = "DefinitiveAgain";
    /// <summary>SCRUM-471: sent to the pre-procurement queue (status Converted).</summary>
    public const string SentToProcurement = "SentToProcurement";
}

public static class ReplenishmentCloseReasons
{
    public const string ShortageResolved = "ShortageResolved";
    public const string NoRemainingDemand = "NoRemainingDemand";
    /// <summary>SCRUM-471: the shortage is already in procurement (live demands / MRs), so nothing is left to recommend.</summary>
    public const string CoveredByProcurement = "CoveredByProcurement";
}

/// <summary>
/// یک اجرای دستی محاسبه‌ی نیاز خالص. RequestKey (اختیاری، یکتا) اجرای هم‌کلید را Idempotent می‌کند: اجرای دوم هیچ اثری ندارد و همان خلاصه را برمی‌گرداند.
/// فقط شمارنده‌ها و خلاصه‌ی محدود موارد «غیرقطعی» اینجاست؛ جزئیات هر توصیه در تاریخچه‌ی خودش.
/// </summary>
public class NetDemandRun : BaseEntity
{
    public int NetDemandRunId { get; set; }

    public string? RequestKey { get; set; }

    public int? RequestedByEmployeeId { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.Now;
    public DateTime? CompletedAt { get; set; }

    /// <summary>Started / Completed / Failed</summary>
    public string Status { get; set; } = "Started";
    public string? FailureMessage { get; set; }

    /// <summary>نسخه‌ی قواعد محاسبه/اثر انگشت؛ با تغییر قواعد، اثر انگشت همه‌ی توصیه‌ها عمداً عوض می‌شود.</summary>
    public int CalculatorVersion { get; set; }

    /// <summary>محدوده‌ی اجرا (فهرست شناسه‌ها به صورت متن)؛ خالی = همه.</summary>
    public string? ScopeMaterialItemIds { get; set; }
    public string? ScopeWarehouseIds { get; set; }

    public int ResultsEvaluated { get; set; }
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Unchanged { get; set; }
    public int Closed { get; set; }
    public int AutoReopened { get; set; }
    public int NotDefinitive { get; set; }
    public int NotComparableNoWarehouse { get; set; }

    /// <summary>حداکثر ۵۰ مورد «کلید: کدهای مسدودکننده» برای گزارش آنچه عمداً توصیه نشد (حدس زده نمی‌شود).</summary>
    public string? NotDefinitiveSummary { get; set; }
}

/// <summary>
/// توصیه‌ی تأمین برای یک (متریال، انبار): «برآورد کمبود فقط از روی موجودی» (ورودی PO نامعلوم است) - هیچ سیاست سفارش مجدد/Lead Time/تبدیل واحد
/// اختراع نمی‌شود و RecommendedQuantity دقیقاً همان کمبود محاسبه‌شده است. هر تغییر ماده‌ی اثر انگشت، یک Revision جدید می‌سازد (تاریخچه و منابع
/// هر Revision جداگانه و فقط‌افزودنی نگهداری می‌شوند). RowVersion (BaseEntity) تعارض هم‌زمان اجرای محاسبه/بستن/رد/بازگشایی را می‌گیرد.
/// </summary>
public class ReplenishmentRecommendation : BaseEntity
{
    public int ReplenishmentRecommendationId { get; set; }

    public int MaterialItemId { get; set; }
    public MaterialItem? MaterialItem { get; set; }

    public int WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }

    public string Status { get; set; } = ReplenishmentStatuses.Open;

    public int Revision { get; set; } = 1;

    public int? UomId { get; set; }

    // ---- Snapshot آخرین محاسبه (decimal(18,3))
    /// <summary>موجودی فیزیکی و رزروشده‌ی لحظه‌ی محاسبه (از UsableStockResult) - ردیابی Snapshot موجودی.</summary>
    public decimal OnHandQuantity { get; set; }
    public decimal ReservedQuantity { get; set; }
    /// <summary>OnHand - Reserved خام (می‌تواند منفی باشد و همان‌طور گزارش می‌شود)؛ از NetDemandResult.RawUsableStock.</summary>
    public decimal UsableQuantity { get; set; }
    public decimal GrossEligibleDemand { get; set; }
    public decimal CoveredByReservations { get; set; }
    public decimal UnreservedDemand { get; set; }
    public decimal ShortageQuantity { get; set; }
    public decimal RecommendedQuantity { get; set; }

    /// <summary>کدهای اطلاع‌رسانی غیرمسدودکننده‌ی آخرین محاسبه (مثلاً IncomingSupplyNotModeled) - جداشده با کاما.</summary>
    public string? NoticeCodes { get; set; }

    /// <summary>false = در آخرین محاسبه «قطعی» نبوده (مثلاً ردیف موجودی مبهم/چندمحلی) و عدد Snapshot قدیمی است؛ برای P6 تبدیل‌پذیر نیست.</summary>
    public bool IsCurrentlyDefinitive { get; set; } = true;
    public string? BlockingIssueCodes { get; set; }

    /// <summary>SHA-256 (hex) از ورودی‌های مؤثر محاسبه؛ تغییر آن یعنی تغییر مادی.</summary>
    public string InputFingerprint { get; set; } = string.Empty;

    /// <summary>
    /// تازگی ورودی‌ها (فقط آنچه واقعاً در دسترس است): آخرین به‌روزرسانی ردیف‌های موجودی (MaterialStock.LastUpdated) و آخرین تغییر وضعیت درخواست‌های انبارِ
    /// منابع تقاضا (StoreRequisitionStatusHistory). null = نامعلوم (مثلاً ردیفی بدون LastUpdated یا منبع Work Order که زمان تغییر ندارد) - هرگز «به‌روز» ادعا نمی‌شود.
    /// زمان محاسبه = NetDemandRun.StartedAt/CompletedAt (ورودی‌ها بین این دو، بدون Snapshot Isolation، خوانده شده‌اند).
    /// </summary>
    public DateTime? StockLastUpdatedAt { get; set; }
    public DateTime? DemandLastChangedAt { get; set; }

    public int FirstRunId { get; set; }
    public int LastRunId { get; set; }
    public DateTime FirstDetectedAt { get; set; } = DateTime.Now;
    public DateTime LastEvaluatedAt { get; set; } = DateTime.Now;

    // ---- رد کردن (Dismiss): مبنای مقایسه برای باز شدن خودکار فقط در صورت تغییر مادی
    public DateTime? DismissedAt { get; set; }
    public int? DismissedByEmployeeId { get; set; }
    public string? DismissedReason { get; set; }
    public decimal? DismissedShortageQuantity { get; set; }
    /// <summary>کلیدهای منبع (مثلاً «S:12,W:7») در لحظه‌ی رد کردن، مرتب‌شده.</summary>
    public string? DismissedSourceKeys { get; set; }

    public DateTime? ClosedAt { get; set; }
    public string? ClosedReason { get; set; }

    public ICollection<ReplenishmentRecommendationSource> Sources { get; set; } = new List<ReplenishmentRecommendationSource>();
    public ICollection<ReplenishmentRecommendationHistory> History { get; set; } = new List<ReplenishmentRecommendationHistory>();
}

/// <summary>
/// منبع تقاضای یک Revision از توصیه. عمداً بدون FK به StoreRequisitionLine/WorkOrderMaterial (ارجاع نرم + ایندکس): خط WorkOrderMaterial در
/// ویرایش طرح WO حذف می‌شود و FK نباید جریان‌های موجود را مسدود کند. دقیقاً یکی از دو شناسه مقدار دارد (CHECK).
/// </summary>
public class ReplenishmentRecommendationSource
{
    public int ReplenishmentRecommendationSourceId { get; set; }

    public int ReplenishmentRecommendationId { get; set; }
    public ReplenishmentRecommendation? Recommendation { get; set; }

    public int Revision { get; set; }

    /// <summary>1 = StoreRequisitionLine، 2 = WorkOrderMaterial (DemandSourceType)</summary>
    public int SourceType { get; set; }
    public int? StoreRequisitionLineId { get; set; }
    public int? WorkOrderMaterialId { get; set; }

    public decimal GrossRemainingQuantity { get; set; }
    public decimal CoveredByReservation { get; set; }
    public decimal UnreservedQuantity { get; set; }
}

/// <summary>تاریخچه‌ی فقط‌افزودنی (هرگز ویرایش/حذف نمی‌شود): هر ایجاد، تغییر مادی، رد، بازگشایی، بسته شدن و تغییر قطعی‌بودن.</summary>
public class ReplenishmentRecommendationHistory
{
    public int ReplenishmentRecommendationHistoryId { get; set; }

    public int ReplenishmentRecommendationId { get; set; }
    public ReplenishmentRecommendation? Recommendation { get; set; }

    public int Revision { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? FromStatus { get; set; }
    public string ToStatus { get; set; } = string.Empty;

    public decimal ShortageQuantity { get; set; }
    public decimal UsableQuantity { get; set; }
    public decimal UnreservedDemand { get; set; }
    public string? InputFingerprint { get; set; }

    public int? RunId { get; set; }
    /// <summary>null = اقدام سیستمی (محاسبه)</summary>
    public int? ActorEmployeeId { get; set; }
    public string? Note { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.Now;
}
