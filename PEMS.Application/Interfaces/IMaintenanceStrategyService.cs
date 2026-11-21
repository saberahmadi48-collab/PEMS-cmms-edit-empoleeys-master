using PEMS.Application.DTOs;

namespace PEMS.Application.Interfaces;

/// <summary>
/// طبق سند «PEMS — Global Data Grid Standard»: صفحه‌بندی/فیلتر در SQL. (خواندن؛ دستورها در
/// IMaintenanceStrategyCommandService هستند.)
/// </summary>
public interface IMaintenanceStrategyService
{
    Task<PagedResult<MaintenanceStrategyListDto>> GetListAsync(int page = 1, int pageSize = 50, int? equipmentId = null);

    Task<PagedResult<MaintenanceStrategyListDto>> GetListAsync(StrategyListFilter filter);

    Task<StrategyDetailDto?> GetDetailAsync(int maintenanceStrategyId);

    Task<List<StrategyHistoryRowDto>> GetHistoryAsync(int maintenanceStrategyId);
}

/// <summary>
/// دستورهای چرخه‌عمر استراتژی. همه‌ی قوانین RCM سمت سرور اعمال می‌شود (StrategyRules)؛ خطاها
/// کد-محورند (OpResult.Errors) و هرگز استثنا نمی‌شوند.
/// </summary>
public interface IMaintenanceStrategyCommandService
{
    /// <summary>اعتبارسنجی بدون ذخیره (برای Import و پیش‌نمایش): همان قوانین CreateAsync، شامل تکراری‌بودن.</summary>
    Task<List<PEMS.Application.Common.RuleError>> ValidateAsync(StrategyInputDto input);

    Task<OpResult> CreateAsync(StrategyInputDto input, StrategyActor actor);

    /// <summary>
    /// Draft: ویرایش درجا (با کنترل RowVersion). Approved/Active: نسخه‌ی جدید Draft ساخته می‌شود و
    /// نسخه‌ی قبلی دست‌نخورده (برای ممیزی) می‌ماند. Id بازگشتی = Id ردیفی که ویرایش/ساخته شد.
    /// </summary>
    Task<OpResult> UpdateAsync(int maintenanceStrategyId, StrategyInputDto input, StrategyActor actor);

    Task<OpResult> SubmitForReviewAsync(int maintenanceStrategyId, StrategyActor actor);

    /// <summary>تایید/رد مستقل (تفکیک وظایف: نویسنده/ارسال‌کننده نمی‌تواند تاییدکننده باشد).</summary>
    Task<OpResult> ReviewAsync(int maintenanceStrategyId, bool approve, string? comment, StrategyActor actor);

    /// <summary>فعال‌سازی نسخه‌ی تاییدشده؛ نسخه‌ی قبلی Superseded و (اختیاری) تخصیص‌ها به نسخه‌ی جدید منتقل می‌شوند.</summary>
    Task<OpResult> ActivateAsync(int maintenanceStrategyId, bool moveAssignmentsFromPreviousVersion, StrategyActor actor);

    Task<OpResult> RetireAsync(int maintenanceStrategyId, string? comment, StrategyActor actor);
}
