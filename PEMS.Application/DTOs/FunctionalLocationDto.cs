namespace PEMS.Application.DTOs;

/// <summary>
/// طبق CMMS Production Baseline v1 بند ۶: FunctionalLocation از قبل در مدل دیتابیس بود ولی
/// هیچ Controller/View‌ای نداشت. طبق الگوی MaterialCategory (لیست تخت + ستون Parent، نه
/// یک Tree Widget جدا) - سازگار با نگاه فعلی پروژه به لیست‌های سلسله‌مراتبی.
/// </summary>
public class FunctionalLocationListDto
{
    public int FunctionalLocationId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Plant { get; set; }
    public string? Area { get; set; }
    public string? Unit { get; set; }
    public int? ParentId { get; set; }
    public string? ParentCode { get; set; }
    public string PathBreadcrumb { get; set; } = "";
    public int EquipmentCount { get; set; }
    public int AssetCount { get; set; }
    public bool IsActive { get; set; }
}

/// <summary>One node of the tree picker (SCRUM-513): the label, a second line (plant / area / unit) and whether it has children to open.</summary>
public sealed record TreeNodeDto(int Id, string Label, string? Sub, bool HasChildren);

/// <summary>A page of tree nodes; <see cref="HasMore"/> says there is a next page.</summary>
public sealed record TreeNodePage(IReadOnlyList<TreeNodeDto> Items, bool HasMore);

public class FunctionalLocationPickerItemDto
{
    public int FunctionalLocationId { get; set; }
    public string Code { get; set; } = "";
    public string PathBreadcrumb { get; set; } = "";
}
