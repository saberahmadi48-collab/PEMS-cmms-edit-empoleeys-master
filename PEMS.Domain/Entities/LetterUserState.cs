using System.ComponentModel.DataAnnotations;

namespace PEMS.Domain.Entities;

/// <summary>
/// وضعیت شخصیِ یک کاربر روی یک نامه در کارتابل: سطل زباله و آرشیو شخصی. این وضعیت فقط برای همان کاربر است و
/// هرگز خودِ نامه را تغییر نمی‌دهد (نامه‌ی رسمی تغییرناپذیر است و حذف فیزیکی ندارد)؛ نامه‌ای که یک کاربر در سطل
/// می‌اندازد برای دیگران، برای نویسنده و برای ممیزی دقیقاً همان‌طور باقی می‌ماند.
/// «حذف دائم» نیز فقط پنهان‌سازی دائمی برای همین کاربر است (IsPurged) - نه حذف رکورد نامه.
/// ردیف فقط وقتی ساخته می‌شود که کاربر یکی از این اقدامات را انجام دهد.
/// </summary>
public class LetterUserState
{
    [Key]
    public int LetterUserStateId { get; set; }

    public int LetterId { get; set; }
    public Letter? Letter { get; set; }

    /// <summary>ApplicationUser.Id صاحب این وضعیت.</summary>
    public int ApplicationUserId { get; set; }

    public bool IsTrashed { get; set; }
    public DateTime? TrashedDate { get; set; }

    /// <summary>پنهان‌سازی دائمی از سطل زباله‌ی همین کاربر (قابل بازیابی نیست؛ خود نامه دست‌نخورده می‌ماند).</summary>
    public bool IsPurged { get; set; }
    public DateTime? PurgedDate { get; set; }

    /// <summary>
    /// آرشیو شخصی. اگر بعد از آرشیو، فعالیت تازه‌ای روی نامه ثبت شود (پاسخ/ارجاع)، نامه در کارتابل «احیاشده از آرشیو»
    /// نمایش داده می‌شود؛ این وضعیت از مقایسه‌ی آخرین فعالیت با ArchivedDate محاسبه می‌شود و جداگانه ذخیره نمی‌شود.
    /// </summary>
    public bool IsArchived { get; set; }
    public DateTime? ArchivedDate { get; set; }

    public DateTime UpdatedDate { get; set; } = DateTime.Now;
}
