using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using PEMS.Application.Inventory;

namespace PEMS.Application.Replenishment;

/// <summary>
/// Phase 2 / P5 - قواعد خالص (بدون EF) تبدیل «نتیجه‌ی نیاز خالص» به «توصیه‌ی تأمین». همه‌ی تصمیم‌ها قطعی‌اند و بدون آستانه‌ی ساختگی:
///  * فقط نتیجه‌ی «قطعی» (IsDefinitive) و بدون هیچ مسئله‌ی مسدودکننده توصیه‌ی قابل‌اتکا می‌سازد؛
///  * طبق تصمیم مالک (سیاست امن فعلی - Option C) چندمحلی‌بودن موجودی (StockMultipleLocationRows) برای توصیه‌ی قطعی «مسدودکننده» است
///    (در P1 غیرمسدودکننده مانده تا رفتار آن مرحله تغییر نکند)؛
///  * تقاضای Work Order بدون انبار هرگز شمرده نمی‌شود (نتیجه‌ی بی‌انبار توصیه نمی‌سازد) و در گزارش اجرا می‌آید؛
///  * مقدار توصیه = دقیقاً کمبود محاسبه‌شده؛ بدون Reorder Point/Lead Time/تبدیل واحد/گرد کردن.
/// </summary>
public static class ReplenishmentRules
{
    /// <summary>با تغییر قواعد این نسخه را افزایش دهید؛ اثر انگشت همه‌ی توصیه‌ها عمداً یک‌بار تغییر می‌کند.</summary>
    public const int CalculatorVersion = 2;   // SCRUM-473: incoming supply from open PO lines enters the shortage

    /// <summary>کد صریح و قابل‌نمایش: تقاضای مواد Work Order بدون انبار قطعی؛ تا تعیین انبارِ تأییدشده، هرگز با استنتاج به انبار نسبت داده نمی‌شود.</summary>
    public const string RequiresWarehouseAttribution = "RequiresWarehouseAttribution";

    public sealed record Evaluation(bool IsDefinitive, decimal Shortage, IReadOnlyList<string> BlockingCodes, IReadOnlyList<string> NoticeCodes);

    public static Evaluation Evaluate(NetDemandResult r)
    {
        ArgumentNullException.ThrowIfNull(r);

        var blocking = r.Issues.Where(i => i.IsBlocking).Select(i => i.Code.ToString()).ToList();
        if (r.Issues.Any(i => i.Code == NetDemandIssueCode.StockMultipleLocationRows)) blocking.Add(nameof(NetDemandIssueCode.StockMultipleLocationRows));
        if (r.Kind is NetDemandResultKind.NotCalculable or NetDemandResultKind.NotComparableNoWarehouse) blocking.Add(r.Kind.ToString());
        if (r.Issues.Any(i => i.Code == NetDemandIssueCode.WorkOrderDemandNotAttributableToWarehouse) || r.Kind == NetDemandResultKind.NotComparableNoWarehouse)
            blocking.Add(RequiresWarehouseAttribution);
        if (!r.IsDefinitive && blocking.Count == 0) blocking.Add("NotDefinitive");

        var blockingCodes = blocking.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var notices = r.Issues.Where(i => !i.IsBlocking && i.Code != NetDemandIssueCode.StockMultipleLocationRows)
            .Select(i => i.Code.ToString()).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();

        var definitive = r.IsDefinitive && blockingCodes.Count == 0
                         && r.Kind is NetDemandResultKind.StockOnlyShortageEstimate or NetDemandResultKind.CompleteNetDemand;
        var shortage = r.Kind == NetDemandResultKind.CompleteNetDemand ? r.CommittedShortage ?? 0m : r.StockOnlyShortage ?? 0m;

        return new Evaluation(definitive, shortage < 0m ? 0m : shortage, blockingCodes, notices);
    }

    /// <summary>کلید منبع: «S:{id}» برای StoreRequisitionLine و «W:{id}» برای WorkOrderMaterial.</summary>
    public static string SourceKey(DemandSourceType type, int sourceLineId) =>
        (type == DemandSourceType.StoreRequisitionLine ? "S:" : "W:") + sourceLineId.ToString(CultureInfo.InvariantCulture);

    public static IReadOnlyList<string> SourceKeys(NetDemandResult r) =>
        r.Contributions.Select(c => SourceKey(c.Origin.Source, c.Origin.SourceLineId)).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();

    public static string JoinKeys(IEnumerable<string> keys) => string.Join(",", keys.OrderBy(x => x, StringComparer.Ordinal));

    public static ISet<string> SplitKeys(string? keys) =>
        new HashSet<string>((keys ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.Ordinal);

    private static string D(decimal? v) => (v ?? 0m).ToString("0.000", CultureInfo.InvariantCulture);

    /// <summary>اثر انگشت ورودی‌های مؤثر: شناسه‌ها، Snapshot موجودی/تقاضا، کمبود، منابع (شناسه+مقدار)، اطلاعیه‌ها و نسخه‌ی قواعد. مرتب و مستقل از ترتیب ورودی.</summary>
    public static string Fingerprint(NetDemandResult r, Evaluation e)
    {
        var sb = new StringBuilder();
        sb.Append('v').Append(CalculatorVersion).Append('|').Append(r.MaterialItemId).Append('|').Append(r.WarehouseId).Append('|')
          .Append(r.RawUsableStock.HasValue ? D(r.RawUsableStock) : "-").Append('|')
          .Append(D(r.GrossEligibleDemand)).Append('|').Append(D(r.CoveredByReservations)).Append('|').Append(D(r.UnreservedEligibleDemand)).Append('|')
          .Append(D(e.Shortage)).Append('|').Append(e.IsDefinitive ? "D" : "N").Append('|').Append(string.Join(",", e.BlockingCodes)).Append('|')
          .Append(string.Join(",", e.NoticeCodes));
        foreach (var c in r.Contributions.OrderBy(c => c.Origin.Source).ThenBy(c => c.Origin.SourceLineId))
            sb.Append('|').Append(SourceKey(c.Origin.Source, c.Origin.SourceLineId)).Append('=').Append(D(c.GrossRemaining)).Append('/').Append(D(c.CoveredByReservation));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();
    }

    /// <summary>
    /// قاعده‌ی قطعی «تغییر مادی» برای باز شدن خودکار یک توصیه‌ی Dismissed: کمبود جاری از کمبودِ لحظه‌ی رد کردن «اکیداً بیشتر» شده باشد، یا منبع
    /// تقاضایی که در لحظه‌ی رد کردن نبود اکنون در کمبود مشارکت دارد. کاهش/برابری هرگز باز نمی‌کند؛ هیچ درصد یا آستانه‌ی ساختگی وجود ندارد.
    /// </summary>
    public static bool IsMaterialChange(decimal? dismissedShortage, string? dismissedSourceKeys, decimal currentShortage, IEnumerable<string> currentSourceKeys)
    {
        if (currentShortage > (dismissedShortage ?? 0m)) return true;
        var before = SplitKeys(dismissedSourceKeys);
        return currentSourceKeys.Any(k => !before.Contains(k));
    }
}
