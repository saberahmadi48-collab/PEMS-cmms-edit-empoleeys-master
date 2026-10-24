using System.ComponentModel.DataAnnotations;

namespace PEMS.Domain.Entities;

/// <summary>
/// تفویض اختیار کارتابل: «تفویض‌کننده» در بازه‌ی زمانی مشخص، دیدن کارتابل خود را به «تفویض‌گیرنده» می‌سپارد
/// (مثلاً مرخصی یا مأموریت). این تفویض فقط دیدن (View) را می‌دهد و هرگز سطح دسترسی تفویض‌گیرنده را بالا نمی‌برد:
/// نامه‌ای که تفویض‌گیرنده به‌تنهایی اجازه‌ی دیدنش را ندارد (محرمانگی) از طریق تفویض هم دیده نمی‌شود.
/// تفویض‌گیرنده باید از چارت سازمانی انتخاب شود (کارمند فعال با حساب کاربری) و ردیف‌ها حذف نمی‌شوند، فقط لغو می‌شوند.
/// </summary>
public class LetterDelegation
{
    [Key]
    public int LetterDelegationId { get; set; }

    /// <summary>ApplicationUser.Id تفویض‌کننده (صاحب کارتابل).</summary>
    public int DelegatorUserId { get; set; }

    /// <summary>ApplicationUser.Id تفویض‌گیرنده.</summary>
    public int DelegateUserId { get; set; }

    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }

    [MaxLength(300)]
    public string? Note { get; set; }

    /// <summary>false = لغو شده (تاریخچه حفظ می‌شود).</summary>
    public bool IsActive { get; set; } = true;
    public DateTime? RevokedDate { get; set; }

    public DateTime CreatedDate { get; set; } = DateTime.Now;
}
