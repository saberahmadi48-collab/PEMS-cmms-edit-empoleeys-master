namespace PEMS.Application.DTOs;

/// <summary>
/// طبق سند «PEMS — Global Data Grid Standard» فاز ۲ بند ۹: DTO سبک مخصوص فهرست VDR - برخلاف
/// VDRItemDto (که کل Submissions را برای صفحه‌ی Details بار می‌کند)، این‌جا فقط ستون‌هایی که
/// واقعاً در Views/VDR/Index.cshtml نمایش داده می‌شوند Project می‌شوند - نه Include کامل.
/// </summary>
public class VDRListItemDto
{
    public int VDRItemId { get; set; }
    public string DocumentNumber { get; set; } = "";
    public string? DocumentDescription { get; set; }
    public string? DocumentTypeCode { get; set; }
    public string VendorName { get; set; } = "";
    public string ProjectName { get; set; } = "";
    // نام دیسیپلین؛ اگر مدرک به دیسیپلین وصل نیست (ورودی Import) همان کد خام
    public string? DisciplineName { get; set; }
    public int? LatestRevisionNo { get; set; }
    public string? LatestReviewCategory { get; set; }
    public string WorkflowStatus { get; set; } = "";
    public bool IsOverdue { get; set; }
}
