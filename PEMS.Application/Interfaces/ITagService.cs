using PEMS.Application.DTOs;

namespace PEMS.Application.Interfaces;

/// <summary>
/// برچسب دلخواه و قابل‌ساخت توسط کاربر (طبق نمونه‌ی کارتابل مرجع) - فعلاً مخصوص نامه‌ها،
/// طراحی به‌گونه‌ای که بعداً برای هر ماژول دیگر هم قابل‌استفاده‌ی مجدد باشد.
/// </summary>
public interface ITagService
{
    Task<List<TagDto>> GetAllAsync(int? viewerUserId = null);

    Task<int> CreateAsync(string name, string color, int? createdById);

    Task DeleteAsync(int tagId, int? actorUserId = null);

    Task AttachToLetterAsync(int letterId, int tagId, int? actorUserId = null);

    Task DetachFromLetterAsync(int letterId, int tagId);

    Task<List<TagDto>> GetForLetterAsync(int letterId, int? viewerUserId = null);
}
