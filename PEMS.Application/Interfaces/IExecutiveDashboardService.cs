using PEMS.Application.DTOs;

namespace PEMS.Application.Interfaces;

/// <summary>
/// داشبورد مدیریتی (Executive Dashboard) روی داده‌ی واقعی: پیشرفت مدارک مهندسی، فشار رویژن، پاسخ کارفرما، VDR، خرید، ترنسمیتال، CMMS و مکاتبات،
/// و فهرست «نیازمند توجه». هر بخش جداگانه و فقط با مجوز همان ماژول پر می‌شود؛ خطای یک بخش (مثلاً Migration اعمال‌نشده) بقیه را نمی‌شکند.
/// </summary>
public interface IExecutiveDashboardService
{
    Task<ExecutiveDashboardDto> GetAsync(ExecutiveDashboardRequest request);
}
