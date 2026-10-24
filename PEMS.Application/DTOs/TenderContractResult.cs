namespace PEMS.Application.DTOs;

/// <summary>
/// وضعیت «رویداد ایجاد قرارداد» (Contracts.Create در AuthorizationAuditLog) که سیاست تفکیک وظایف «ایجادکننده ≠ تاییدکننده» برای
/// قراردادِ ساخته‌شده از مناقصه به آن تکیه دارد. این رویداد با Tender.Approve (رویداد گارد، با شناسه‌ی مناقصه) متمایز است.
/// </summary>
public enum ContractCreatorEventStatus
{
    /// <summary>فراخواننده شناسه‌ی کاربر نداد؛ رویدادی نوشته نشد.</summary>
    NotApplicable,

    /// <summary>رویداد در همین فراخوانی نوشته شد (ایجاد اولیه).</summary>
    Recorded,

    /// <summary>رویداد از قبل وجود داشت؛ چیزی نوشته نشد (تلاش مجدد بی‌خطر).</summary>
    AlreadyRecorded,

    /// <summary>رویداد ناقص بود و برای ایجادکننده‌ی اصلی (نه کاربر تلاش مجدد) ترمیم شد.</summary>
    Repaired,

    /// <summary>رویداد ثبت نشد (خطای ذخیره)؛ قرارداد ساخته شده و تلاش مجدد آن را ترمیم می‌کند.</summary>
    NotRecorded,

    /// <summary>ایجادکننده‌ی اصلی از Contract.CreatedBy قابل شناسایی نیست؛ رویدادی به نام کاربر دیگر نوشته نشد.</summary>
    CreatorUnidentified,

    /// <summary>قرارداد یافت نشد.</summary>
    ContractNotFound,

    /// <summary>P0.3: Commit نامطمئن بود و وجود رویداد هم قابل تأیید نشد؛ «ثبت نشد» گزارش نمی‌شود. با گزارش تشخیص (Detection Query) بررسی شود.</summary>
    OutcomeUnknown
}

/// <summary>
/// نتیجه‌ی ساخت قرارداد از پیشنهاد برنده. Success=true یعنی قرارداد و اتصال آن به مناقصه ذخیره شده‌اند (Committed=true).
/// Success=false با Committed=true یعنی قرارداد از قبل وجود دارد (ContractId پر است). Committed=false یعنی هیچ داده‌ی کسب‌وکاری ذخیره نشد.
/// GenericAuditFailed=true یعنی داده‌ی کسب‌وکار ذخیره شد ولی ذخیره‌ی جداگانه‌ی AuditLog عمومی شکست خورد (فقط در Providerهای بدون تراکنش
/// رخ می‌دهد؛ روی SQL Server کل عملیات در یک تراکنش است و این حالت Rollback می‌شود).
/// P0.3: OutcomeUnknown=true یعنی Commit نامطمئن بود و حتی بررسی بعدی هم وضعیت واقعی را روشن نکرد؛ در این حالت هرگز «ساخته نشد» گزارش
/// نمی‌شود (Success=false و Committed=false فقط یعنی «تأیید نشده»). RecoveredAfterUncertainCommit=true یعنی Commit تلاش قبلی همین فراخوانی
/// نامطمئن بود و تلاش بعدی (بدون ساخت قرارداد تکراری) وجود قرارداد و رویداد را تأیید کرد.
/// </summary>
public sealed record TenderContractResult(
    bool Success,
    string? Error,
    int? ContractId,
    bool Committed = false,
    ContractCreatorEventStatus CreatorEvent = ContractCreatorEventStatus.NotApplicable,
    bool GenericAuditFailed = false,
    bool OutcomeUnknown = false,
    bool RecoveredAfterUncertainCommit = false);
