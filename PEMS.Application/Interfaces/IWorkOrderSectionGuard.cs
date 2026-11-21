using PEMS.Application.Common;

namespace PEMS.Application.Interfaces;

/// <summary>هویت و نقش فراخواننده برای ممیزی و بررسی مجوز (از نشست سمت‌سرور؛ هرگز از ورودی فرم).</summary>
public sealed record WoCaller(int? UserId, string? UserName, int? EmployeeId, IReadOnlyList<string> Roles, string? IpAddress = null);

/// <summary>نتیجه‌ی بررسی یک جهش روی بخش فرزند. HttpStatus: 403 مجوز، 404 یافت‌نشد، 400 قاعده‌ی وضعیت/دلیل.</summary>
public sealed record WoGuardResult(bool Allowed, string Code, string MessageFa, string MessageEn, WoPhase Phase, int HttpStatus)
{
    public string Message(bool persian) => persian ? MessageFa : MessageEn;
}

/// <summary>وضعیت نمایش صفحه‌ی جزئیات برای کاربر جاری: فاز، دسترسی هر بخش، و اینکه کنترل‌های جهشی هر بخش باید رندر شوند یا نه.</summary>
public sealed record WoSectionPage(WoPhase Phase, string Status, string? StageName, IReadOnlyDictionary<WoSection, SectionAccess> Access,
    IReadOnlyDictionary<WoSection, bool> ShowControls, bool CanReopen, bool CanCorrect)
{
    public bool Controls(WoSection s) => ShowControls.TryGetValue(s, out var v) && v;
    public SectionAccess AccessOf(WoSection s) => Access.TryGetValue(s, out var a) ? a : SectionAccess.Hidden;
}

public interface IWorkOrderSectionGuard
{
    /// <summary>
    /// تنها دروازه‌ی جهش روی بخش‌های فرزند. فاز را از دیتابیس می‌خواند (وضعیت + مرحله‌ی جاری گردش‌کار)، ماتریس <see cref="WorkOrderSectionPolicy"/> و مجوز را اعمال می‌کند،
    /// و هر رد را در WorkOrderChangeLog (و برای Work Order پایان‌یافته در AuthorizationAuditLog به‌عنوان رویداد امنیتی) ثبت می‌کند. استثنا نمی‌اندازد.
    /// </summary>
    Task<WoGuardResult> AuthorizeAsync(int workOrderId, WoSection section, WoSectionAction action, WoCaller caller, string? reason = null, string? recordRef = null);

    /// <summary>ثبت یک تغییر موفق (پس از ذخیره‌ی جهش): چه‌کسی/چه‌زمانی/وضعیت Work Order/مقدار قبل و بعد/دلیل.</summary>
    Task RecordChangeAsync(int workOrderId, WoSection section, WoSectionAction action, WoCaller caller, string? recordRef, string? reason, string? before, string? after);

    Task<WoSectionPage?> GetPageAsync(int workOrderId, WoCaller caller);
}
