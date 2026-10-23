using PEMS.Application.DTOs;

namespace PEMS.Application.Interfaces;

public interface ILetterService
{
    // طبق «PEMS Correspondence Workspace» بند ۸: پوشه‌ی «Starred» در Inbox سبک Gmail باید یک
    // فیلتر واقعی سمت سرور باشد - نه فقط پرچم نمایشی IsStarred روی نتایج صفحه‌ی جاری
    Task<PagedResult<LetterDto>> SearchAsync(string? letterType, string? letterNumber, string? subject,
        string? sender, string? receiver, int? projectId, int? disciplineId, string? status,
        DateTime? fromDate, DateTime? toDate, int page, int pageSize,
        bool canViewConfidential, int? requestingApplicationUserId, bool starredOnly = false,
        LetterAdvancedFilter? advanced = null);

    // خروجی همان فیلترهای Index (بدون صفحه‌بندی، سقف maxRows) با همان قانون محرمانگی
    Task<List<LetterDto>> ExportAsync(string? letterType, string? letterNumber, string? subject,
        string? sender, string? receiver, int? projectId, int? disciplineId, string? status,
        DateTime? fromDate, DateTime? toDate, bool canViewConfidential, int? requestingApplicationUserId,
        bool starredOnly, LetterAdvancedFilter? advanced, int maxRows = 5000);

    // عملیات گروهی امن: فقط نامه‌هایی که کاربر اجازه‌ی دیدنشان را دارد؛ هر نامه جداگانه ارزیابی می‌شود.
    // تایید/صدور/بستن عمداً گروهی نیست (تفکیک وظایف و گردش‌کار کنترل‌شده).
    Task<LetterBulkResult> BulkSetStarAsync(IReadOnlyCollection<int> letterIds, int applicationUserId,
        bool starred, bool canViewConfidential);

    Task<LetterBulkResult> BulkSubmitForReviewAsync(IReadOnlyCollection<int> letterIds, int? actorApplicationUserId);

    // آرشیو نرم (IsActive=false) فقط برای Draft/Rejected، فقط توسط نویسنده یا SystemAdmin، با دلیل الزامی
    // و ثبت صریح در AuditLog. هرگز حذف فیزیکی نیست (نامه‌های رسمی تغییرناپذیرند).
    Task<LetterBulkResult> BulkArchiveAsync(IReadOnlyCollection<int> letterIds, int? actorApplicationUserId,
        string? actorUserName, bool isSystemAdmin, string reason);

    Task<LetterDetailDto?> GetDetailAsync(int letterId);

    // نسخه‌ی «با کنترل دسترسی» برای صفحه‌ی جزئیات: Forbidden اگر بیننده حق دیدن ندارد؛ BCC فقط برای نویسنده/Approve/خودِ گیرنده دیده می‌شود؛
    // باز کردن نامه‌ی ارسال‌شده در گزارش «چه کسی دیده» ثبت و گیرنده‌ی همان کاربر «خوانده‌شده» می‌شود.
    Task<LetterDetailAccessResult> GetDetailForViewerAsync(int letterId, LetterViewer viewer);

    Task<bool> CanViewAsync(int letterId, LetterViewer viewer);

    Task<int> CreateAsync(string letterType, string letterNumber, DateTime letterDate, string? sender,
        string? receiver, string subject, string? body, string priority, string confidentialityLevel,
        int? projectId, int? disciplineId, int? relatedDocumentId, int? relatedMRId, int? registeredById,
        bool responseRequired = false, DateTime? responseDueDate = null);

    // تنظیم/اصلاح پاسخ‌خواهی بعد از ثبت. فقط ثبت‌کننده‌ی نامه یا کسی با اختیار مدیریت (canManageAny)؛ نه برای نامه‌ی Closed/Rejected.
    // اگر پاسخ لازم است، مهلت الزامی است و نباید قبل از تاریخ نامه باشد. تغییر در AuditLog خودکار ثبت می‌شود.
    Task<(bool Success, string? Error)> SetResponseDueAsync(int letterId, bool responseRequired, DateTime? dueDate,
        int? actorApplicationUserId, bool canManageAny);

    Task<LetterWorkspaceDashboardDto> GetWorkspaceDashboardAsync(bool canViewConfidential, int? applicationUserId, bool canReview);

    // برای نمایش دوطرفه: لیست نامه‌های مرتبط با یک مدرک مهندسی/Vendor یا یک MR مشخص
    Task<List<RelatedLetterDto>> GetRelatedToDocumentAsync(int documentId);

    Task<List<RelatedLetterDto>> GetRelatedToMRAsync(int mrId);

    // ==============================================================================
    // طبق «PEMS Letter Details — Controlled Lifecycle»: به‌جای AdvanceStatusAsync قدیمی
    // (وضعیت آزاد/بدون اعتبارسنجی گذار)، هر گذار قانونی یک اکشن صریح و مجزا با اعتبارسنجی
    // سمت‌سرور واقعی است. کاربر هرگز وضعیت دلخواه انتخاب نمی‌کند - فقط یک اقدام کسب‌وکاری.
    //
    //   Draft --(SubmitForReview)--> PendingApproval
    //   PendingApproval --(Approve)--> Approved
    //   PendingApproval --(ReturnForRevision)--> ReturnedForRevision
    //   PendingApproval --(Reject)--> Rejected
    //   ReturnedForRevision --(Resubmit، فقط توسط RegisteredById)--> PendingApproval
    //   Approved --(Issue)--> Issued
    //   Issued --(Close)--> Closed
    // ==============================================================================

    Task<(bool Success, string? Error)> SubmitForReviewAsync(int letterId, int? actorApplicationUserId);

    Task<(bool Success, string? Error)> ApproveAsync(int letterId, int? reviewerApplicationUserId, string? comment);

    Task<(bool Success, string? Error)> ReturnForRevisionAsync(int letterId, int? reviewerApplicationUserId, string reason);

    Task<(bool Success, string? Error)> RejectAsync(int letterId, int? reviewerApplicationUserId, string reason);

    Task<(bool Success, string? Error)> IssueAsync(int letterId, int? actorApplicationUserId);

    // ارسال نامه‌ی داخلی (Internal): Approved -> Sent، یا مستقیماً Draft -> Sent وقتی تایید لازم نیست. فقط نویسنده. نامه‌ی خارجی (Outgoing) با IssueAsync صادر می‌شود.
    // پس از ارسال: اعلان درون‌برنامه‌ای برای گیرندگان (To/Cc/Bcc)، ثبت نسخه‌ی «Sent»، و بستن خودکار نامه‌ی مادر اگر پاسخ‌خواه بوده است.
    Task<(bool Success, string? Error)> SendAsync(int letterId, int? actorApplicationUserId);

    // آرشیو دستی نامه‌ی ارسال‌شده/بسته‌شده (Status=Archived)؛ فقط نویسنده یا کسی با اختیار مدیریت. بدون حذف فیزیکی.
    Task<(bool Success, string? Error)> ArchiveAsync(int letterId, int? actorApplicationUserId, bool canArchiveAny);

    // جایگزینی تاییدکننده‌ی یک مرحله‌ی هنوز Pending (مثلاً تاییدکننده در دسترس نیست) - فقط مدیر سیستم
    Task<(bool Success, string? Error)> ReassignApprovalStepAsync(int letterId, int stepOrder, int newApproverUserId, int? actorApplicationUserId, bool isAdmin);

    Task<(bool Success, string? Error)> CloseAsync(int letterId, int? actorApplicationUserId);

    // طبق بند ۷: فقط نویسنده‌ی اصلی (RegisteredById) و فقط وقتی Status == ReturnedForRevision
    // مجاز به این اقدام است - بعد از موفقیت، نامه بلافاصله دوباره PendingApproval (غیرقابل‌ویرایش)
    // می‌شود. rowVersion برای تشخیص ویرایش هم‌زمان (بند ۲۹) الزامی است.
    Task<(bool Success, string? Error)> ResubmitAsync(int letterId, string subject, string body,
        int currentApplicationUserId, byte[]? rowVersion);

    // شمارنده‌ی تب‌های فهرست؛ با همان قانون محرمانگی SearchAsync ساخته می‌شود
    Task<LetterFolderCountsDto> GetFolderCountsAsync(bool canViewConfidential, int? requestingApplicationUserId);

    Task<LetterDashboardDto> GetDashboardAsync(int? currentApplicationUserId = null, bool canViewConfidential = false);

    Task ToggleStarAsync(int letterId, int applicationUserId);

    // طبق درخواست: پشتیبانی از چند گیرنده (نامه/رونوشت) روی یک نامه
    Task AddRecipientAsync(int letterId, string recipientType, int? employeeId, int? departmentId, string? externalName);

    Task MarkRecipientReadAsync(int letterRecipientId, int applicationUserId);

    // تایید الکترونیکی سبک (نه امضای PKI واقعی) - نام تایپ‌شده + مهر زمانی
    Task SignAsync(int letterId, string signedByName);

    // ==============================================================================
    // طبق بند ۴/۷/۱۰: جایگزین UpdateBodyAsync قدیمی (که بدون هیچ بررسی مالکیت/وضعیتی، متن هر
    // نامه‌ای را در هر وضعیتی می‌نوشت). ویرایش محتوای رسمی فقط در دو حالت مجاز است - Draft
    // (پیش‌نویس هنوز ثبت‌نشده) یا ReturnedForRevision (فقط نویسنده‌ی اصلی) - و در هر دو حالت
    // بدون تغییر Status (فقط ذخیره، نه ارسال مجدد؛ برای آن، ResubmitAsync را ببینید).
    // ==============================================================================
    Task<(bool Success, string? Error)> SaveDraftEditAsync(int letterId, string subject, string body,
        int currentApplicationUserId, byte[]? rowVersion);

    // نمایش گردش کامل پیام (ایجاد، ارجاع، خوانده‌شدن، تکمیل، امضا) به‌ترتیب زمانی یکجا
    Task<List<LetterTimelineEventDto>> GetTimelineAsync(int letterId, LetterViewer? viewer = null);

    // Thread View: زنجیره‌ی کامل پیام‌های قبلی این نامه (والدها، از قدیم به جدید)
    Task<List<ThreadMessageDto>> GetThreadAsync(int letterId);

    // پاسخ به یک نامه - نامه‌ی جدید با ParentLetterId تنظیم‌شده می‌سازد
    Task<int> ReplyAsync(int parentLetterId, string letterNumber, string subject, string? body, string? receiver,
        int? registeredById);

    // طبق بخش DCC: ثبت پیوست به‌عنوان بخشی از یک عملیات (نه فقط دکمه‌ی جدای بعد از ساخت نامه)
    Task AddAttachmentAsync(int letterId, string fileName, string fileExtension, string filePath,
        long fileSize, string? mimeType, int? uploadedById);
}
