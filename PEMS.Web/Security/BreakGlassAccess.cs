using PEMS.Application.Common;

namespace PEMS.Web.Security;

/// <summary>
/// SCRUM-356: صفحه‌ی break-glass فقط از localhost یا شبکه‌ی مدیریتی پیکربندی‌شده. آدرس اتصال و همه‌ی آدرس‌های
/// X-Forwarded-For / Forwarded باید مجاز باشند (قاعده در <see cref="BreakGlassRules.IsAddressAllowed"/>).
/// </summary>
public static class BreakGlassAccess
{
    public static bool IsAllowed(HttpContext context, BreakGlassOptions options)
    {
        var forwarded = context.Request.Headers["X-Forwarded-For"].Concat(context.Request.Headers["Forwarded"])
            .Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!);
        return BreakGlassRules.IsAddressAllowed(context.Connection.RemoteIpAddress, forwarded, options.ManagementNetworks);
    }
}
