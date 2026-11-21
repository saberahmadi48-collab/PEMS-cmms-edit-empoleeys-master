using PEMS.Application.DTOs;

namespace PEMS.Application.Interfaces;

/// <summary>
/// ورود دسته‌ای Excel برای الگوهای استراتژی و تخصیص تگ‌ها. دو مرحله (هم‌الگو با Pipeline ورود موجود):
/// commit=false فقط اعتبارسنجی و گزارش خطای ردیف‌به‌ردیف؛ commit=true ردیف‌های معتبر را ثبت می‌کند.
/// استراتژی‌های واردشده همیشه Draft ساخته می‌شوند و باید از گردش‌کار بازبینی/تایید بگذرند (تایید انسانی).
/// </summary>
public interface IStrategyImportService
{
    Task<StrategyImportResultDto> ImportStrategiesAsync(Stream xlsx, bool commit, StrategyActor actor);

    Task<StrategyImportResultDto> ImportAssignmentsAsync(Stream xlsx, bool commit, StrategyActor actor);

    byte[] BuildStrategyTemplate();

    byte[] BuildAssignmentTemplate();

    /// <summary>گزارش خطا (Excel) شامل ردیف، مرجع و پیام خطا برای ردیف‌های ردشده.</summary>
    byte[] BuildErrorReport(StrategyImportResultDto result, Func<PEMS.Application.Common.RuleError, string> localize);
}
