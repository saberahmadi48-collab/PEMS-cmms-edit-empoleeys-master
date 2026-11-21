namespace PEMS.Application.Common;

/// <summary>
/// قواعد پاسخ‌خواهی نامه (Phase 2) در یک‌جا تا سرویس، Controller و تست‌ها یک تعریف واحد از «پاسخ‌داده‌شده» و «معوق» داشته باشند.
/// «Registered/UnderReview/Referred» وضعیت نامه‌ی واردِ ثبت‌شده در DCC به‌عنوان پاسخِ یک نامه‌ی صادره است (فقط Incoming این وضعیت‌ها را دارد).
/// پاسخ‌داده‌شده ذخیره نمی‌شود: نامه‌ی فرزند (ParentLetterId) با یکی از وضعیت‌های AnsweredChildStatuses یعنی پاسخ صادر شده است.
/// </summary>
public static class LetterResponseRules
{
    public static readonly string[] AnsweredChildStatuses = { "Issued", "Sent", "Acknowledged", "Closed", "Registered", "UnderReview", "Referred" };

    // نامه‌ی بسته یا ردشده دیگر پاسخ‌خواه نیست
    public static readonly string[] FinishedStatuses = { "Closed", "Rejected" };

    public static string? Validate(bool responseRequired, DateTime? dueDate, DateTime letterDate)
    {
        if (!responseRequired) return null;
        if (!dueDate.HasValue) return "برای نامه‌ی نیازمند پاسخ، مهلت پاسخ الزامی است.";
        if (dueDate.Value.Date < letterDate.Date) return "مهلت پاسخ نمی‌تواند قبل از تاریخ نامه باشد.";
        return null;
    }

    public static bool IsOverdue(bool responseRequired, DateTime? dueDate, bool answered, string status, DateTime today)
        => responseRequired && dueDate.HasValue && !answered
           && !FinishedStatuses.Contains(status) && dueDate.Value.Date < today.Date;
}
