using PEMS.Application.DTOs;

namespace PEMS.Application.Interfaces;

public interface ILetterTemplateService
{
    // letterType != null: قالب‌های همان نوع + قالب‌های «همه‌ی انواع»
    Task<List<LetterTemplateDto>> ListAsync(string? letterType, bool includeInactive);

    Task<LetterTemplateDto?> GetAsync(int templateId);

    // LetterTemplateId == 0: ایجاد؛ در غیر این‌صورت ویرایش. متن پاک‌سازی می‌شود. نام تکراری (برای همان نوع) پذیرفته نمی‌شود.
    // CreatedBy/ModifiedBy را DbContext خودکار از کاربر جاری پر می‌کند.
    Task<(bool Success, string? Error, int TemplateId)> SaveAsync(LetterTemplateDto template);

    // حذف فیزیکی ندارد؛ نامه‌های ثبت‌شده از قالب مستقل‌اند
    Task<bool> SetActiveAsync(int templateId, bool active);
}
