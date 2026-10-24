using PEMS.Domain.Common;

namespace PEMS.Domain.Entities;

/// <summary>
/// هدف قابل‌ویرایش یک شاخص (KPI) داشبورد مدیریتی. تعریف شاخص‌ها (کلید، واحد، جهت «بیشتر بهتر/کمتر بهتر») در کد است
/// (KpiCatalog)؛ این جدول فقط مقدار هدف و آستانه‌ی «نزدیک به هدف» را برای هر کلید نگه می‌دارد. اگر برای کلیدی ردیف
/// نباشد، پیش‌فرض کاتالوگ اعمال می‌شود. ردیف جدید با ذخیره از صفحه‌ی داشبورد ساخته/به‌روز می‌شود، هرگز حذف فیزیکی نمی‌شود
/// (IsActive=false یعنی «هدفی تعیین نشده»).
/// </summary>
public class KpiTarget : BaseEntity
{
    public int KpiTargetId { get; set; }

    /// <summary>کلید یکتای شاخص در KpiCatalog (مثلاً pm_compliance).</summary>
    public string KpiKey { get; set; } = string.Empty;

    public decimal TargetValue { get; set; }

    /// <summary>
    /// آستانه‌ی «نزدیک به هدف»؛ برای شاخص «بیشتر بهتر» کمتر از هدف و برای «کمتر بهتر» بیشتر از هدف. null = فقط محقق/نشده.
    /// </summary>
    public decimal? WarnValue { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedDate { get; set; } = DateTime.Now;
    public DateTime? ModifiedDate { get; set; }
}
