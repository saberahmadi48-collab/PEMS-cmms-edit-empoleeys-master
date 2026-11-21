using System.ComponentModel.DataAnnotations;

namespace PEMS.Domain.Entities;

/// <summary>
/// تنظیمات شخصی کارتابل هر کاربر (تراکم جدول، ستون‌های نمایان/ثابت، نمای پیش‌فرض، رفتارهای عمومی) به‌صورت JSON.
/// فقط ترجیحات نمایشی در آن است و هیچ قانون دسترسی یا کسب‌وکاری از آن خوانده نمی‌شود؛ مقدار نامعتبر = پیش‌فرض.
/// </summary>
public class LetterUserSetting
{
    [Key]
    public int LetterUserSettingId { get; set; }

    public int ApplicationUserId { get; set; }

    public string SettingsJson { get; set; } = "{}";

    public DateTime UpdatedDate { get; set; } = DateTime.Now;
}
