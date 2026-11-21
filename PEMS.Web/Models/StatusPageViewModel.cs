namespace PEMS.Web.Models;

/// <summary>صفحه‌ی خطای دوستانه برای پاسخ‌های 4xx/5xx بدون بدنه (مثلاً NotFound())؛ Code همان کد HTTP اصلی است.</summary>
public sealed class StatusPageViewModel
{
    public int Code { get; set; } = 500;
    public string? RequestId { get; set; }
}
