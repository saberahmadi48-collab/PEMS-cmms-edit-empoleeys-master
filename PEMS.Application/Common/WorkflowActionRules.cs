namespace PEMS.Application.Common;

/// <summary>
/// B15: تنها محل تعریف «کدام اقدام/وضعیت دلیل الزامی دارد». قبلاً هر ماژول (Work Request، Work Order، مسیر Legacy و کنترلرها) جداگانه و ناسازگار تصمیم می‌گرفت
/// (دلیل «اختیاری» بود و رد/لغو/توقف بی‌دلیل ثبت می‌شد). حالا WorkRequestService و WorkOrderLifecycleService (هم مسیر گردش‌کار، هم Legacy) از همین قاعده استفاده می‌کنند.
/// دلیل در WorkflowHistory (Comment) و در تاریخچه‌ی وضعیت کسب‌وکاری (Reason) ذخیره می‌شود.
/// </summary>
public static class WorkflowActionRules
{
    private static readonly HashSet<string> ReasonActions = new(StringComparer.Ordinal) { "Reject", "Cancel", "Hold", "Return" };
    private static readonly HashSet<string> ReasonStatuses = new(StringComparer.Ordinal) { "Rejected", "Cancelled", "OnHold" };

    /// <summary>اقدام گردش‌کار (Reject/Cancel/Hold/Return) دلیل الزامی دارد.</summary>
    public static bool ActionRequiresReason(string? actionType) => actionType != null && ReasonActions.Contains(actionType);

    /// <summary>گذار Legacy به این وضعیت‌ها (Rejected/Cancelled/OnHold) دلیل الزامی دارد.</summary>
    public static bool StatusRequiresReason(string? status) => status != null && ReasonStatuses.Contains(status);

    private static bool Persian => System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fa";

    /// <summary>null = معتبر. در غیر این صورت پیام مشخص بومی‌شده.</summary>
    public static string? ValidateAction(string? actionType, string? reason)
        => ActionRequiresReason(actionType) && string.IsNullOrWhiteSpace(reason) ? Message(actionType!) : null;

    public static string? ValidateStatus(string? nextStatus, string? reason)
        => StatusRequiresReason(nextStatus) && string.IsNullOrWhiteSpace(reason) ? Message(nextStatus!) : null;

    private static string Message(string what)
    {
        var (fa, en) = what switch
        {
            "Reject" or "Rejected" => ("رد", "rejecting"),
            "Cancel" or "Cancelled" => ("لغو", "cancelling"),
            "Hold" or "OnHold" => ("توقف", "putting on hold"),
            _ => ("بازگشت برای اصلاح", "returning for rework")
        };
        return Persian ? $"ثبت دلیل برای «{fa}» الزامی است." : $"A reason is required when {en}.";
    }

    /// <summary>دلیل تمیزشده برای ذخیره (Trim؛ تهی → null).</summary>
    public static string? Normalize(string? reason) => string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
}
