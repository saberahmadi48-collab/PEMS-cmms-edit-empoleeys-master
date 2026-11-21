using System.Threading.RateLimiting;
using PEMS.Application.Common;

namespace PEMS.Web.Security;

/// <summary>
/// محدودیت نرخ صفحه‌های بازیابی حساب مدیر بدون ورود (کد بازیابی SCRUM-355 و break-glass SCRUM-356): پنجره‌ی ثابت برای هر IP،
/// فقط روی درخواست‌های POST (باز کردن فرم سهمیه را مصرف نمی‌کند). قفل حساب Identity جداگانه حدس زدن کد برای یک حساب را محدود می‌کند.
/// پاسخ رد = 429 (صفحه‌ی خطای دوستانه از UseStatusCodePagesWithReExecute).
/// </summary>
public static class RecoveryRateLimiting
{
    public const string PolicyName = "admin-recovery";

    public static RateLimitPartition<string> Partition(HttpContext context, int permitLimit, TimeSpan window)
    {
        if (!HttpMethods.IsPost(context.Request.Method))
            return RateLimitPartition.GetNoLimiter("read");

        var ip = RecoveryEventRules.NormalizeIp(context.Connection.RemoteIpAddress?.ToString()) ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = window,
            QueueLimit = 0,
            AutoReplenishment = true
        });
    }
}
