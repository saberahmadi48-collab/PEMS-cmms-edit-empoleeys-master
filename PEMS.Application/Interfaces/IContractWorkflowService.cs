namespace PEMS.Application.Interfaces;

/// <summary>
/// طبق ممیزی Package Procurement Contract Finalization (§D، Phase 2.1): آداپتور نازک روی
/// IWorkflowRuntime برای ماژول Contract - دقیقاً هم‌الگو با IDocumentWorkflowService. جایگزین
/// ContractService.ChangeStatusAsync (که هر رشته‌ی آزاد را بدون هیچ اعتبارسنجی می‌پذیرفت).
/// </summary>
public interface IContractWorkflowService
{
    /// <param name="editorUserId">P0-10: کاربر ارسال‌کننده؛ رویداد Contracts.Edit برای SoD «ویرایشگر ≠ تأییدکننده» با همان ذخیره ثبت می‌شود.</param>
    Task<(bool Success, string? Error)> SubmitForReviewAsync(
        int contractId, IReadOnlyList<string>? requiredDisciplines, int? actorEmployeeId, IReadOnlyList<string> actorRoles,
        int? editorUserId = null, string? ipAddress = null);

    Task<(bool Success, string? Error)> RecordDisciplineReviewAsync(
        int contractId, string disciplineRole, string decision, string? comment, int? actorEmployeeId, IReadOnlyList<string> actorRoles);

    Task<(bool Success, string? Error)> ApproveAsync(
        int contractId, string? comment, int currentUserId, int? actorEmployeeId, IReadOnlyList<string> actorRoles, string? ipAddress);

    Task<(bool Success, string? Error)> RejectAsync(
        int contractId, string? comment, int? actorEmployeeId, IReadOnlyList<string> actorRoles);

    Task<(bool Success, string? Error)> SignAsync(
        int contractId, int currentUserId, int? actorEmployeeId, IReadOnlyList<string> actorRoles, string? ipAddress);

    Task<(bool Success, string? Error)> MarkEffectiveAsync(int contractId, IReadOnlyList<string> actorRoles);

    Task<(bool Success, string? Error, int? RevisionId)> RequestRevisionAsync(
        int contractId, string changeType, decimal? newValue, string? reason, string? changeDescription, int? actorEmployeeId,
        int? editorUserId = null, string? ipAddress = null);

    /// <param name="currentUserId">P0-10: اگر داده شود، تأیید از مسیر کاربر-آگاه (Scope + SoD «ویرایشگر/ایجادکننده ≠ تأییدکننده») می‌گذرد و فقط Allow صریح عبور می‌کند.</param>
    Task<(bool Success, string? Error)> ApproveRevisionAsync(
        int contractRevisionId, int? actorEmployeeId, IReadOnlyList<string> actorRoles,
        int? currentUserId = null, string? ipAddress = null);

    // طبق §C/§I قاعده‌ی ۵ (Phase 6.2): NTP نباید پیش از رسیدن قرارداد به وضعیت اجراپذیر صادر شود
    Task<(bool Success, string? Error, int? NtpId)> IssueNtpAsync(
        int contractId, string? reference, DateTime? effectiveStartDate, int? actorEmployeeId);

    // طبق §I قاعده‌ی ۱۰ (Phase 6.5): جلسه‌ی Kick-off نباید پیش از رسیدن قرارداد به وضعیت
    // اجراپذیر برگزار شود - همان زیرساخت Meeting موجود، نه یک Entity تازه
    Task<(bool Success, string? Error, int? MeetingId)> ScheduleKickoffMeetingAsync(
        int contractId, DateTime meetingDate, string? location, string? organizerName, string? agenda);

    // طبق گزارش سخت‌افزاری نهایی (Gap ۱): Effective → Completed، مسدود اگر بازرسی الزامی کامل نشده باشد
    Task<(bool Success, string? Error)> CompleteAsync(int contractId, IReadOnlyList<string> actorRoles);

    // خروج زودهنگام از هر وضعیت غیر-نهایی - بدون گیت بازرسی (فسخ، نه تکمیل عادی)
    Task<(bool Success, string? Error)> TerminateAsync(int contractId, string? reason, IReadOnlyList<string> actorRoles);
}
