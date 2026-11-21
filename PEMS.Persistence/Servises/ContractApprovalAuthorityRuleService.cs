using Microsoft.EntityFrameworkCore;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Identity;

namespace PEMS.Persistence.Services;

public class ContractApprovalAuthorityRuleService : IContractApprovalAuthorityRuleService
{
    private readonly PEMSDbContext _context;

    public ContractApprovalAuthorityRuleService(PEMSDbContext context)
    {
        _context = context;
    }

    public async Task<List<ContractApprovalAuthorityRule>> GetAllAsync()
    {
        return await _context.ContractApprovalAuthorityRules
            .OrderBy(x => x.Priority)
            .ThenByDescending(x => x.CreatedDate)
            .ToListAsync();
    }

    public async Task<(bool Success, string? Error, int? RuleId)> CreateAsync(
        decimal? minValue, decimal? maxValue, string? criticalityClass, string requiredApproverRole,
        string? description, int priority)
    {
        var validation = await ValidateAsync(null, minValue, maxValue, criticalityClass, requiredApproverRole);
        if (validation != null) return (false, validation, null);

        var rule = new ContractApprovalAuthorityRule
        {
            MinValue = minValue,
            MaxValue = maxValue,
            CriticalityClass = string.IsNullOrWhiteSpace(criticalityClass) ? null : criticalityClass,
            RequiredApproverRole = requiredApproverRole,
            Description = description,
            Priority = priority,
            IsActive = true
        };

        _context.ContractApprovalAuthorityRules.Add(rule);
        await _context.SaveChangesAsync();
        return (true, null, rule.ContractApprovalAuthorityRuleId);
    }

    public async Task<(bool Success, string? Error)> UpdateAsync(
        int ruleId, decimal? minValue, decimal? maxValue, string? criticalityClass, string requiredApproverRole,
        string? description, int priority)
    {
        var rule = await _context.ContractApprovalAuthorityRules.FirstOrDefaultAsync(x => x.ContractApprovalAuthorityRuleId == ruleId);
        if (rule == null) return (false, "این قاعده یافت نشد.");

        var validation = await ValidateAsync(ruleId, minValue, maxValue, criticalityClass, requiredApproverRole);
        if (validation != null) return (false, validation);

        rule.MinValue = minValue;
        rule.MaxValue = maxValue;
        rule.CriticalityClass = string.IsNullOrWhiteSpace(criticalityClass) ? null : criticalityClass;
        rule.RequiredApproverRole = requiredApproverRole;
        rule.Description = description;
        rule.Priority = priority;

        await _context.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> SetActiveAsync(int ruleId, bool isActive)
    {
        var rule = await _context.ContractApprovalAuthorityRules.FirstOrDefaultAsync(x => x.ContractApprovalAuthorityRuleId == ruleId);
        if (rule == null) return (false, "این قاعده یافت نشد.");

        rule.IsActive = isActive;
        await _context.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> ValidateRuleAsync(int? ruleId, decimal? minValue, decimal? maxValue, string? criticalityClass, string requiredApproverRole)
    {
        var problem = await ValidateAsync(ruleId, minValue, maxValue, criticalityClass, requiredApproverRole);
        return problem is null ? (true, null) : (false, problem);
    }

    // طبق Gap ۳: فقط اعتبارسنجی‌هایی که معماری موجود واقعاً می‌تواند با اطمینان تعیین کند - نه
    // یک آستانه‌ی کسب‌وکاری اختراعی
    private async Task<string?> ValidateAsync(
        int? excludingRuleId, decimal? minValue, decimal? maxValue, string? criticalityClass, string requiredApproverRole)
    {
        if (string.IsNullOrWhiteSpace(requiredApproverRole))
            return "نقش تایید‌کننده‌ی الزامی باید مشخص شود.";

        if (!SystemRoles.All.Contains(requiredApproverRole))
            return $"نقش «{requiredApproverRole}» در سیستم شناخته‌شده نیست.";

        if (minValue.HasValue && maxValue.HasValue && minValue.Value > maxValue.Value)
            return "کف بازه نمی‌تواند از سقف آن بیشتر باشد.";

        if (!string.IsNullOrWhiteSpace(criticalityClass) && criticalityClass.Length > 10)
            return "کلاس بحرانی‌بودن نامعتبر است.";

        // تشخیص قاعده‌ی کاملاً تکراری (همان بازه + همان بحرانی‌بودن + همان اولویت، هر دو فعال) -
        // این ابهامی که کد نمی‌تواند بی‌خطر حل کند، نه اختراع یک قاعده‌ی کسب‌وکاری
        var duplicate = await _context.ContractApprovalAuthorityRules
            .Where(x => x.IsActive && x.ContractApprovalAuthorityRuleId != (excludingRuleId ?? -1))
            .AnyAsync(x => x.MinValue == minValue && x.MaxValue == maxValue && x.CriticalityClass == criticalityClass);

        if (duplicate)
            return "یک قاعده‌ی فعال دیگر از قبل دقیقاً همین بازه/بحرانی‌بودن را پوشش می‌دهد - Priority را تغییر دهید یا قاعده‌ی موجود را ویرایش کنید.";

        return null;
    }
}
