using PEMS.Application.DTOs;

namespace PEMS.Application.Interfaces;

/// <summary>
/// کارتابل نامه‌نگاری: پوشه‌ها و شمارنده‌ها، سطل زباله/آرشیو/خوانده‌شدن شخصی، رشته‌ی گفتگو و فیلتر تاریخچه‌ی گردش بر پایه‌ی چارت سازمانی.
/// همه‌ی عملیات شخصی‌اند و خودِ نامه را تغییر نمی‌دهند؛ فقط روی نامه‌هایی کار می‌کنند که کاربر اجازه‌ی دیدنشان را دارد.
/// </summary>
public interface ILetterCartableService
{
    Task<CartableCountsDto> GetCountsAsync(int userId, bool canViewConfidential);

    Task<CartableOpResult> TrashAsync(IReadOnlyCollection<int> letterIds, int userId, bool canViewConfidential);
    Task<CartableOpResult> RestoreAsync(IReadOnlyCollection<int> letterIds, int userId);
    /// <summary>حذف دائم = پنهان‌سازی دائم برای همین کاربر؛ فقط برای نامه‌های موجود در سطل. خود نامه حذف نمی‌شود.</summary>
    Task<CartableOpResult> PurgeAsync(IReadOnlyCollection<int> letterIds, int userId);
    Task<CartableOpResult> EmptyTrashAsync(int userId);

    Task<CartableOpResult> ArchiveForMeAsync(IReadOnlyCollection<int> letterIds, int userId, bool canViewConfidential);
    Task<CartableOpResult> UnarchiveForMeAsync(IReadOnlyCollection<int> letterIds, int userId);

    /// <summary>خوانده/نخوانده‌کردن ردیف گیرنده‌ی خودِ کاربر (گیرنده‌ی نامه). نامه‌ی پیش‌ازارسال یا بدون گیرندگی کاربر بی‌اثر است.</summary>
    Task<CartableOpResult> SetReadAsync(IReadOnlyCollection<int> letterIds, int userId, bool read, bool canViewConfidential);

    /// <summary>اعضای قابل‌دیدن رشته‌ی گفتگوی یک نامه به‌ترتیب درخت.</summary>
    Task<List<LetterThreadMemberDto>> GetThreadAsync(int letterId, int? userId, bool canViewConfidential);

    /// <summary>
    /// گردش پیام‌های یک نامه با متن کامل (نامه‌ی اصلی + پاسخ‌ها + ارجاع‌ها، از قدیم به جدید). فقط نامه‌هایی که بیننده اجازه‌ی دیدنشان را دارد؛
    /// گیرنده‌ی Bcc هرگز نمایش داده نمی‌شود. نامه‌ی ارجاعی با Kind=Referral مشخص می‌شود.
    /// </summary>
    Task<List<ConversationMessageDto>> GetConversationAsync(int letterId, int? userId, bool canViewConfidential);

    /// <summary>
    /// تاریخچه‌ی گردش را مرتب و بر اساس نقش اقدام‌کننده نسبت به بیننده فیلتر می‌کند. role: all | me | manager | subordinates.
    /// مدیر و زیردستان از چارت سازمانی می‌آیند (سمت مافوق / سمت‌های فرزند)؛ بدون سمت اصلی معتبر، فقط «همه» و «من» معنا دارند.
    /// </summary>
    Task<List<LetterTimelineEventDto>> FilterHistoryAsync(List<LetterTimelineEventDto> events, string? role, bool newestFirst, int userId);
}

/// <summary>درخت برچسب‌ها (سازمانی + شخصی) و اعمال گروهی برچسب روی نامه‌ها.</summary>
public interface ILetterTagTreeService
{
    Task<List<LetterTagNodeDto>> GetTreeAsync(int userId, bool canManageOrganizational, bool withCounts, bool canViewConfidential);

    Task<TagTreeOpResult> CreateAsync(string name, string? color, int? parentTagId, bool personal, int userId, bool canManageOrganizational);
    Task<TagTreeOpResult> UpdateAsync(int tagId, string name, string? color, int? parentTagId, int sortOrder, int userId, bool canManageOrganizational);
    Task<TagTreeOpResult> DeactivateAsync(int tagId, int userId, bool canManageOrganizational);

    /// <summary>زدن/برداشتن یک برچسب روی چند نامه‌ی قابل‌دیدن. برچسب باید برای کاربر قابل‌استفاده باشد (سازمانی یا شخصیِ خودش).</summary>
    Task<CartableOpResult> ApplyAsync(int tagId, IReadOnlyCollection<int> letterIds, bool attach, int userId, bool canViewConfidential);
}

/// <summary>تنظیمات شخصی کارتابل (نمایشی).</summary>
public interface ILetterUserSettingsService
{
    Task<PEMS.Application.Common.LetterUserSettingsRules.Settings> GetAsync(int userId);
    Task SaveAsync(int userId, PEMS.Application.Common.LetterUserSettingsRules.Settings settings);
}

/// <summary>تفویض اختیار کارتابل (فقط دیدن) با انتخاب تفویض‌گیرنده از چارت سازمانی.</summary>
public interface ILetterDelegationService
{
    /// <summary>تفویض‌هایی که من داده‌ام و آن‌هایی که به من داده شده.</summary>
    Task<(List<LetterDelegationDto> Given, List<LetterDelegationDto> Received)> GetAsync(int userId);

    /// <summary>کارمندان فعالِ دارای حساب کاربری، با نسبت سازمانی نسبت به کاربر (مدیر/زیردست/هم‌تراز/سایر).</summary>
    Task<List<DelegateCandidateDto>> GetCandidatesAsync(int userId);

    Task<CartableOpResult> CreateAsync(int delegatorUserId, int delegateUserId, DateTime fromDate, DateTime toDate, string? note);
    Task<CartableOpResult> RevokeAsync(int delegationId, int userId);
}

/// <summary>قوانین مسیریابی خودکار شخصی.</summary>
public interface ILetterRoutingRuleService
{
    Task<List<LetterRoutingRuleDto>> GetAsync(int userId);
    Task<CartableOpResult> SaveAsync(LetterRoutingRuleDto rule, int userId);
    Task<CartableOpResult> SetActiveAsync(int ruleId, bool active, int userId);
    Task<CartableOpResult> DeleteAsync(int ruleId, int userId);

    /// <summary>
    /// قوانین گیرندگان یک نامه‌ی تازه‌رسیده را اجرا می‌کند (برچسب، ستاره، ارجاع به زیردست). هرگز استثنا نمی‌دهد
    /// و ارسال/ثبت نامه را متوقف نمی‌کند. تعداد قانون‌های اجراشده را برمی‌گرداند.
    /// </summary>
    Task<int> ApplyToArrivedLetterAsync(int letterId);
}

/// <summary>مخاطبین (چارت سازمانی + طرف‌حساب‌های خارجی) و گروه‌های توزیع.</summary>
public interface ILetterContactService
{
    Task<List<LetterContactDto>> SearchAsync(string? query, int take = 30);
    Task<List<LetterContactGroupDto>> GetGroupsAsync(int userId, bool canManageOrganizational);
    Task<CartableOpResult> SaveGroupAsync(int? groupId, string name, bool personal, IReadOnlyCollection<int> employeeIds,
        IReadOnlyCollection<int> partyIds, int userId, bool canManageOrganizational);
    Task<CartableOpResult> DeleteGroupAsync(int groupId, int userId, bool canManageOrganizational);
}

/// <summary>یادآورهای شخصی روی نامه.</summary>
public interface ILetterPersonalReminderService
{
    Task<List<LetterReminderDto>> GetAsync(int userId, bool includeDone = false);
    Task<CartableOpResult> AddAsync(int letterId, DateTime remindAt, string? note, int userId, bool canViewConfidential);
    Task<CartableOpResult> CompleteAsync(int reminderId, int userId);
    Task<CartableOpResult> DeleteAsync(int reminderId, int userId);

    /// <summary>برای یادآورهای سررسیده اعلان می‌سازد (فقط یک‌بار برای هر یادآور). تعداد اعلان‌های ساخته‌شده را برمی‌گرداند.</summary>
    Task<int> DispatchDueAsync(DateTime now);
}

/// <summary>گزارش شخصی کارتابل، SLA و عملکرد زیردستان (چارت سازمانی).</summary>
public interface ILetterReportService
{
    Task<CartableReportDto> GetAsync(int userId, DateTime fromDate, DateTime toDate, bool canViewConfidential);
}
