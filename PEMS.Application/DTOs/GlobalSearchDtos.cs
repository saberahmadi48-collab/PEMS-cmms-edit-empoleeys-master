namespace PEMS.Application.DTOs;

/// <summary>
/// یک نتیجه‌ی جستجو - مستقل از نوع موجودیت، برای نمایش یکسان در کامپوننت JS مشترک.
/// </summary>
public class GlobalSearchResultItemDto
{
    public string EntityType { get; set; } = "";
    public int Id { get; set; }

    // متن اصلی نمایش (مثلاً TagNumber) - با <mark> دور بخش منطبق‌شده
    public string HighlightedLabel { get; set; } = "";

    // متن اصلی بدون Highlight (برای وقتی کاربر نتیجه رو انتخاب می‌کنه و باید توی Input بشینه)
    public string Label { get; set; } = "";

    // توضیح کمکی زیر Label (مثلاً "Pump - Unit 100 - Mechanical")
    public string? SubLabel { get; set; }

    // Controlled UI label, separate from record text so localization never rewrites a subject or name.
    public string? SubLabelSuffixKey { get; set; }

    // لینک به صفحه‌ی جزئیات (برای وقتی جستجو یک Navigation است، نه فقط انتخاب مقدار)
    public string? Url { get; set; }

    // امتیاز تطبیق - برای مرتب‌سازی بر اساس Relevance (Exact > StartsWith > Contains > Fuzzy)
    public int Score { get; set; }
}

public class GlobalSearchResponseDto
{
    public string EntityType { get; set; } = "";
    public string Query { get; set; } = "";
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int TotalCount { get; set; }
    public bool HasMore { get; set; }
    public List<GlobalSearchResultItemDto> Items { get; set; } = new();
}

public class SearchEntityTypeDto
{
    public string EntityType { get; set; } = "";
    public string DisplayName { get; set; } = "";
}

/// <summary>SCRUM-64: one entity type's results in a search over every provider the user may open.</summary>
public class GlobalSearchGroupDto
{
    public string EntityType { get; set; } = "";
    public int TotalCount { get; set; }
    public List<GlobalSearchResultItemDto> Items { get; set; } = new();
}
