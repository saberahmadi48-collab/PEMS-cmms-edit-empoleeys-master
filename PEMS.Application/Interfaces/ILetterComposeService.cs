using PEMS.Application.DTOs;

namespace PEMS.Application.Interfaces;

/// <summary>
/// نوشتن نامه‌ی رسمی (Internal/Outgoing): ذخیره‌ی پیش‌نویس/ذخیره‌ی خودکار، اعتبارسنجی، دایرکتوری کارکنان، زنجیره‌ی تایید و نسخه‌ها.
/// ارسال/تایید خودِ نامه در ILetterService است؛ این سرویس فقط «ساختن و ویرایش پیش‌نویس» را بر عهده دارد.
/// </summary>
public interface ILetterComposeService
{
    // ذخیره‌ی پیش‌نویس (autoSave=true: ذخیره‌ی خودکار؛ همان اعتبارسنجی ساختاری، و نسخه‌ی پشت‌سرهم ادغام می‌شود)
    Task<LetterComposeResult> SaveDraftAsync(LetterComposeInput input, LetterComposeActor actor, bool autoSave = false);

    // اعتبارسنجی کامل‌بودن برای ارسال به تایید/ارسال (خطاها + هشدارها)، بدون تغییر داده
    Task<LetterComposeResult> ValidateForSubmitAsync(int letterId, LetterComposeActor actor);

    // بارگذاری پیش‌نویس برای ویرایش؛ null اگر نامه نیست، متعلق به کاربر نیست یا دیگر قابل‌ویرایش نیست
    Task<LetterComposeInput?> LoadForEditAsync(int letterId, LetterComposeActor actor);

    // مقدار اولیه‌ی فرم پاسخ از نامه‌ی اصلی (گیرنده، موضوع «Re:»، دسته، اولویت، محرمانگی، پروژه...)؛ null اگر نامه‌ی اصلی نیست. دسترسی را فراخوان بررسی می‌کند
    Task<LetterComposeInput?> BuildReplyInputAsync(int parentLetterId, LetterComposeActor actor);

    /// <summary>
    /// ارجاع نامه به شخص دیگر در قالب یک نامه‌ی داخلی تازه: موضوع همان موضوع نامه‌ی اصلی (ثابت)، متن = دستورالعمل ارجاع + نقل نامه‌ی اصلی،
    /// بدون تایید و دسته‌بندی؛ فقط برچسب «ارجاع» می‌گیرد. گیرنده‌ها را کاربر انتخاب می‌کند.
    /// </summary>
    Task<LetterComposeInput?> BuildReferralInputAsync(int parentLetterId, LetterComposeActor actor);

    Task<LetterSenderBlockDto> GetSenderBlockAsync(int userId);

    // جستجوی دایرکتوری کارکنان فعال (کش‌شده؛ فیلتر در حافظه)
    Task<List<EmployeeDirectoryItem>> SearchDirectoryAsync(string? query, int take = 20);

    // نمایش گیرندگان ذخیره‌شده‌ی یک پیش‌نویس (به ترتیب شناسه‌ها؛ کارمند غیرفعال/نامعتبر نادیده گرفته می‌شود)
    Task<List<EmployeeDirectoryItem>> GetDirectoryItemsAsync(IEnumerable<int> employeeIds);

    // ورود دسته‌ای گیرندگان از متن/CSV (هر خط: کد پرسنلی، ایمیل یا نام کامل)؛ حداکثر max نفر
    Task<RecipientResolveResult> ResolveRecipientsAsync(string? text, int max = 500);

    // کاربرانی که مجوز Approve ماژول مکاتبات را دارند (برای ساخت زنجیره‌ی تایید)
    /// <summary>
    /// فهرست قابل‌انتخاب برای زنجیره‌ی تایید: همه‌ی کارکنان فعال دارای حساب کاربری (به‌علاوه‌ی دارندگان مجوز تایید که کارمند نیستند).
    /// ترتیب: مافوق‌های نویسنده در چارت سازمانی (نزدیک‌ترین اول) ← دارندگان مجوز تایید ← سایرین. خودِ نویسنده هرگز در فهرست نیست.
    /// تاییدکننده‌ی تعیین‌شده در زنجیره، برای همان مرحله اختیار تایید/برگشت/رد دارد (بدون نیاز به نقش جدا).
    /// </summary>
    Task<List<ApproverOptionDto>> GetApproverOptionsAsync(int? authorUserId = null);

    Task<List<LetterVersionDto>> GetVersionsAsync(int letterId);

    // فقط نویسنده و فقط Draft؛ نرم (IsActive=false) و با ثبت در AuditLog
    Task<(bool Success, string? Error)> DiscardDraftAsync(int letterId, LetterComposeActor actor);
}
