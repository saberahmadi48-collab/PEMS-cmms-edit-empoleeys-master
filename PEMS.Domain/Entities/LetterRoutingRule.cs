using System.ComponentModel.DataAnnotations;

namespace PEMS.Domain.Entities;

/// <summary>
/// قانون مسیریابی خودکار شخصی کارتابل: وقتی نامه‌ای برای صاحب قانون می‌رسد (گیرنده‌ی To/Cc شده یا نامه‌ی وارده‌ای
/// که به او ثبت شده) و با شرط‌ها (همه‌ی شرط‌های پرشده با هم، AND) بخواند، اقدام‌ها اجرا می‌شود: زدن برچسب، ستاره،
/// و ارجاع خودکار به یکی از زیردستان صاحب قانون در چارت سازمانی. ارجاع خودکار هرگز به کسی خارج از زیرمجموعه‌ی
/// سازمانی صاحب قانون نمی‌رود و برای نامه‌ای که صاحب قانون اجازه‌ی دیدنش را ندارد اجرا نمی‌شود.
/// اجرای قانون هیچ‌وقت ارسال/ثبت نامه را متوقف نمی‌کند (خطا در قانون = نادیده گرفته و ثبت می‌شود).
/// </summary>
public class LetterRoutingRule
{
    [Key]
    public int LetterRoutingRuleId { get; set; }

    /// <summary>ApplicationUser.Id صاحب قانون.</summary>
    public int OwnerUserId { get; set; }

    [MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    /// <summary>ترتیب ارزیابی (کوچک‌تر زودتر).</summary>
    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;

    // ---------------- شرط‌ها (خالی = بدون محدودیت) ----------------
    [MaxLength(20)]
    public string? MatchLetterType { get; set; }

    [MaxLength(20)]
    public string? MatchPriority { get; set; }

    [MaxLength(60)]
    public string? MatchCategory { get; set; }

    public int? MatchProjectId { get; set; }
    public int? MatchDisciplineId { get; set; }

    /// <summary>بخشی از نام فرستنده (بدون حساسیت به حروف).</summary>
    [MaxLength(120)]
    public string? MatchSenderContains { get; set; }

    /// <summary>بخشی از موضوع نامه (بدون حساسیت به حروف).</summary>
    [MaxLength(120)]
    public string? MatchSubjectContains { get; set; }

    // ---------------- اقدام‌ها ----------------
    public int? ApplyTagId { get; set; }
    public bool StarLetter { get; set; }

    /// <summary>ارجاع خودکار به این کارمند (باید زیردست مستقیم/غیرمستقیم صاحب قانون در چارت سازمانی باشد).</summary>
    public int? ReferToEmployeeId { get; set; }

    [MaxLength(300)]
    public string? ReferInstruction { get; set; }

    public int MatchCount { get; set; }
    public DateTime? LastMatchedDate { get; set; }

    public DateTime CreatedDate { get; set; } = DateTime.Now;
}
