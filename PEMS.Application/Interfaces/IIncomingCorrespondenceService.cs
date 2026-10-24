using PEMS.Application.DTOs;

namespace PEMS.Application.Interfaces;

/// <summary>
/// ثبت مکاتبه‌ی وارده در DCC و پرس‌وجوهای فرم دو-پنلی (طرف مکاتبه، رشته‌ی مکاتبه‌ی طرف، نامه‌های صادره برای «در پاسخ به»، پیوندها، افراد).
/// ثبت یک عملیات اتمیک روی پایگاه داده است (نامه + پیوست‌ها + پیوندها + ارجاع + رونوشت + طرف جدید در یک SaveChanges)؛ فایل‌ها قبل از آن
/// در فضای ذخیره‌سازی نگهداری می‌شوند و در صورت خطا پاک می‌شوند تا فایل یتیم نماند.
/// </summary>
public interface IIncomingCorrespondenceService
{
    Task<IncomingRegistrationResult> RegisterAsync(IncomingRegistrationInput input, int? actorUserId, int? actorEmployeeId);

    /// <summary>شماره‌ی بعدیِ پیش‌نمایش (بدون رزرو) برای نمایش در فرم؛ شماره‌ی نهایی هنگام ثبت گرفته می‌شود.</summary>
    Task<string> PeekNextNumberAsync(int? projectId, DateTime receivedDate);

    Task<List<PartySearchItemDto>> SearchPartiesAsync(string? query, int? projectId, int take = 20);

    Task<PartyThreadDto?> GetPartyThreadAsync(int partyId, bool canViewConfidential, int? requestingUserId, int take = 30);

    /// <summary>نامه‌های صادره‌ی ارسال‌شده به یک طرف، برای انتخاب «در پاسخ به» (پاسخ‌خواه‌های بی‌پاسخ اول).</summary>
    Task<List<OpenOutgoingDto>> GetOutgoingForReplyAsync(int partyId, bool canViewConfidential, int? requestingUserId);

    Task<(int LetterId, string LetterNumber)?> FindDuplicateAsync(int partyId, string senderReference);

    Task<List<LinkSearchItemDto>> SearchLinksAsync(string linkType, string? query, int? projectId, int take = 15);

    Task<List<PersonSearchItemDto>> SearchPeopleAsync(string? query, int take = 15);

    /// <summary>ساخت طرف مکاتبه از روی وندورهای فعال؛ Idempotent (وندوری که قبلاً طرف دارد دوباره ساخته نمی‌شود). تعداد ساخته‌شده را برمی‌گرداند.</summary>
    Task<int> SeedPartiesFromVendorsAsync();
}
