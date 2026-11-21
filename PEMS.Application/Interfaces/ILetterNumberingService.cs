namespace PEMS.Application.Interfaces;

/// <summary>
/// شماره‌گذاری خودکار نامه با قالب [DEPT]-[YEAR]-[SEQ] (کد دپارتمان ثبت‌کننده، سال، شمارنده‌ی پنج‌رقمی). departmentId=null → GEN.
/// شمارنده‌ی هر (دپارتمان، سال) هم‌زمانی‌ایمن است (LetterNumberSequence + RowVersion).
/// </summary>
public interface ILetterNumberingService
{
    Task<string> GenerateNextNumberAsync(string letterType, int? projectId, int? departmentId = null);

    /// <summary>
    /// شماره‌ی نامه‌ی وارده با قالب <c>[PROJECT]-IN-[سال شمسی]-[SEQ]</c> (مثلاً JPC-IN-1405-0187). شمارنده برای هر (پروژه، سال شمسیِ تاریخ دریافت)
    /// جدا است و از ۱ فروردین از نو شروع می‌شود. projectId=null → GEN.
    /// </summary>
    Task<string> GenerateNextIncomingNumberAsync(int? projectId, DateTime receivedDate);
}
