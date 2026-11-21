namespace PEMS.Application.Interfaces;

/// <summary>
/// نسخه‌ی اول (Heuristic) امتیازدهی ریسک Vendor، طبق معماری پیشنهادی:
/// ترکیبی از درصد به‌موقع بودن، نرخ رد/اصلاح (CO/AC/RE)، تعداد آیتم‌های معوق فعلی.
/// نسخه‌ی بعدی می‌تواند با یک مدل یادگیری‌ماشین واقعی جایگزین شود بدون تغییر این Interface.
/// </summary>
public interface IVendorRiskService
{
    [PEMS.Application.Common.PemsOperation("Vendor", "RecalculateRisk", "Vendors.Edit")]
    Task RecalculateAsync(int vendorId);

    Task RecalculateAllAsync();
}
