namespace PEMS.Web.Models;

public class KpiCardViewModel
{
    public string Title { get; set; } = "";
    public int Count { get; set; }
    public string IconClass { get; set; } = "bi-card-list"; // آیکون پیش‌فرض
    public string ColorClass { get; set; } = "bg-icon-blue"; // رنگ پیش‌فرض
    public string? Url { get; set; } // اگر مقدار داشته باشد، کارت لینک‌دار می‌شود

    /// <summary>تعریف شاخص (Tooltip) تا عدد بدون توضیح نماند.</summary>
    public string? Hint { get; set; }

    // این متد عدد را نمایش می‌دهد، اگر 0 باشد خط تیره می‌گذارد
    public string DisplayCount => Count > 0 ? Count.ToString("N0") : "-";
}