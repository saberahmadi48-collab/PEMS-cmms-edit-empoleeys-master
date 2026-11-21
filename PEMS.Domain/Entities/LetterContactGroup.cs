using System.ComponentModel.DataAnnotations;

namespace PEMS.Domain.Entities;

/// <summary>
/// گروه مخاطبین نامه‌نگاری (لیست توزیع): مجموعه‌ای از کارمندان چارت سازمانی و/یا طرف‌حساب‌های خارجی که با یک کلیک
/// به گیرندگان نامه اضافه می‌شوند. OwnerUserId پر = گروه شخصی؛ null = گروه سازمانی (برای همه). فهرست «مخاطبین»
/// خودش جدول جدا ندارد: کارمندان از چارت سازمانی و سازمان‌های خارجی از CorrespondentParty خوانده می‌شوند.
/// </summary>
public class LetterContactGroup
{
    [Key]
    public int LetterContactGroupId { get; set; }

    [MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    /// <summary>null = گروه سازمانی؛ در غیر این صورت ApplicationUser.Id صاحب گروه.</summary>
    public int? OwnerUserId { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedDate { get; set; } = DateTime.Now;

    public ICollection<LetterContactGroupMember> Members { get; set; } = new List<LetterContactGroupMember>();
}

/// <summary>عضو یک گروه مخاطبین: دقیقاً یکی از EmployeeId (داخلی) یا CorrespondentPartyId (خارجی) پر است.</summary>
public class LetterContactGroupMember
{
    [Key]
    public int LetterContactGroupMemberId { get; set; }

    public int LetterContactGroupId { get; set; }
    public LetterContactGroup? Group { get; set; }

    public int? EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    public int? CorrespondentPartyId { get; set; }
    public CorrespondentParty? CorrespondentParty { get; set; }

    public DateTime CreatedDate { get; set; } = DateTime.Now;
}
