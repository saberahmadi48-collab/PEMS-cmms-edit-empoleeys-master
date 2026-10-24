using System.Security.Cryptography;
using System.Text;

namespace PEMS.Persistence.Services;

/// <summary>
/// P0.3 - نام منابع قفل کاربردی (sp_getapplock، مالک = Transaction) برای مسیر «ساخت قرارداد از مناقصه‌ی برنده» و ترمیم رویداد ایجادکننده.
///
/// ترتیب ثابت گرفتن قفل‌ها (برای جلوگیری از Deadlock): ۱) قفل مناقصه ۲) قفل شماره‌ی قرارداد ۳) قفل رویداد ایجادکننده. ترمیم تنها
/// قفل رویداد را می‌گیرد؛ قفل رویداد هرگز قبل از قفل مناقصه گرفته نمی‌شود، پس چرخه‌ی انتظار ممکن نیست.
///
/// محدودیت‌ها (شفاف): این قفل‌ها فقط مسیرهایی را هم‌زمان‌سازی می‌کنند که خودشان آن را می‌گیرند. مسیرهای دیگر ساخت قرارداد
/// (مثل ContractService.CreateAsync یا اسکریپت SQL مستقیم) قفل نمی‌گیرند؛ یکتایی قطعی شماره‌ی قرارداد فقط با Index یکتا در دیتابیس
/// (نیازمند Migration و تأیید جداگانه) ممکن است. قفل شماره‌ی قرارداد بر پایه‌ی نرمال‌سازی برنامه (FormKC + Trim + حروف بزرگ) است؛
/// اگر Collation دیتابیس دو رشته را برابر بداند که این نرمال‌سازی آن‌ها را یکسان نمی‌کند، آن دو قفل متفاوت می‌گیرند.
/// </summary>
public static class ContractCreationLocks
{
    public static string TenderResource(int tenderId) => $"PEMS:Contracts.CreateFromTender:{tenderId}";

    public static string CreatorEventResource(int contractId) => $"PEMS:Contracts.CreatorEvent:{contractId}";

    /// <summary>منبع قفل برای یک شماره‌ی قرارداد؛ از هش SHA-256 مقدار نرمال‌شده ساخته می‌شود (طول ثابت، بدون نویسه‌ی خاص، مستقل از Collation قفل).</summary>
    public static string ContractNoResource(string? contractNo)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(NormalizeContractNo(contractNo)));
        return "PEMS:Contracts.No:" + Convert.ToHexString(hash);
    }

    /// <summary>Trim + نرمال‌سازی سازگاری (عرض کامل/نیم‌عرض) + حروف بزرگ؛ Collation پیش‌فرض دیتابیس (CI_AS) حساس‌به‌حروف نیست، پس «ab» و «AB» یک قفل می‌گیرند.</summary>
    public static string NormalizeContractNo(string? contractNo)
    {
        var trimmed = (contractNo ?? string.Empty).Trim();
        try { trimmed = trimmed.Normalize(NormalizationForm.FormKC); }
        catch (ArgumentException) { /* رشته‌ی یونیکد نامعتبر: همان مقدار Trim‌شده (قفل محافظه‌کارانه‌تر) */ }
        // Collation پیش‌فرض (SQL_Latin1_General_CP1_CI_AS) این سه «گسترش» را برابر می‌داند (در تست T10c روی SQL واقعی سنجیده شده): ss=ß، ae=æ، oe=œ.
        // ToUpperInvariant آن‌ها را گسترش نمی‌دهد، پس صریحاً یکسان‌سازی می‌شوند. گسترش‌های دیگرِ Collationهای دیگر شمارش نشده‌اند (ریسک باقی‌مانده).
        trimmed = trimmed.Replace("\u00DF", "ss").Replace("\u1E9E", "ss").Replace("\u00E6", "ae").Replace("\u00C6", "ae").Replace("\u0153", "oe").Replace("\u0152", "oe");
        return trimmed.ToUpperInvariant();
    }
}
