namespace PEMS.Application.Interfaces;

/// <summary>
/// یادآوری معوقات مکاتبات (Phase 3). هر اجرا برای هر (کاربر، نامه، نوع یادآوری) حداکثر یک اعلان در همان روز می‌سازد
/// (اجرای مجدد یا دو نمونه‌ی هم‌زمان اعلان تکراری نمی‌سازد).
/// </summary>
public interface ILetterReminderService
{
    Task<LetterReminderResult> SendDueRemindersAsync(DateTime? now = null);
}

public class LetterReminderResult
{
    public int ReferralReminders { get; set; }
    public int ResponseReminders { get; set; }
    // به‌دلیل وجود یادآوری همان روز ساخته نشد
    public int SkippedDuplicates { get; set; }
    public int Failed { get; set; }

    public int Total => ReferralReminders + ResponseReminders;
}
