using PEMS.Domain.Entities;

namespace PEMS.Application.Interfaces;

/// <summary>
/// طبق گزارش سخت‌افزاری نهایی Package Procurement (Gap ۳): لایه‌ی مدیریتی روی
/// ContractApprovalAuthorityRule (که در Phase 4 فقط معماری/گیت آن ساخته شد، بدون هیچ راه
/// مدیریتی). هیچ آستانه‌ی پولی خاص شرکتی این‌جا اختراع/Seed نمی‌شود - فقط CRUD روی چیزی که یک
/// مدیر مجاز صریحاً تعریف می‌کند.
/// </summary>
public interface IContractApprovalAuthorityRuleService
{
    Task<List<ContractApprovalAuthorityRule>> GetAllAsync();

    Task<(bool Success, string? Error, int? RuleId)> CreateAsync(
        decimal? minValue, decimal? maxValue, string? criticalityClass, string requiredApproverRole,
        string? description, int priority);

    Task<(bool Success, string? Error)> UpdateAsync(
        int ruleId, decimal? minValue, decimal? maxValue, string? criticalityClass, string requiredApproverRole,
        string? description, int priority);

    Task<(bool Success, string? Error)> SetActiveAsync(int ruleId, bool isActive);

    /// <summary>The rules of a rule (known role, range order, class length, no identical active rule) without writing; the definition pages run it before the entity write (SCRUM-632, 774).</summary>
    [PEMS.Application.Common.PemsOperation("ContractApprovalRule", "Validate", Roles = "SystemAdmin")]
    Task<(bool Success, string? Error)> ValidateRuleAsync(int? ruleId, decimal? minValue, decimal? maxValue, string? criticalityClass, string requiredApproverRole);
}
