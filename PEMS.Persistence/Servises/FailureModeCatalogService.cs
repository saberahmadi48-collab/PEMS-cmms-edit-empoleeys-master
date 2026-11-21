using Microsoft.EntityFrameworkCore;
using PEMS.Application.Common;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;

namespace PEMS.Persistence.Services;

/// <summary>
/// کاتالوگ کنترل‌شده‌ی حالت‌های خرابی ISO 14224 (حالت ← علل) به‌ازای کلاس تجهیز. منبع واحد برای
/// فرم استراتژی و تکمیل Work Order. جستجو با نرمال‌سازی توکنی (خط‌تیره/زیرخط/فاصله بی‌اثر)
/// است تا «pump no flow» و «PUMP_NO_FLOW» هر دو پیدا شوند.
/// </summary>
public class FailureModeCatalogService : IFailureModeCatalogService
{
    private readonly PEMSDbContext _context;

    public FailureModeCatalogService(PEMSDbContext context)
    {
        _context = context;
    }

    public async Task<List<FailureModeOptionDto>> SearchAsync(int? equipmentClassId, string? query, int take = 30)
    {
        var modes = _context.FailureModes.AsNoTracking().Where(m => m.IsActive);

        if (equipmentClassId.HasValue)
        {
            // کلاس + والدهایش + حالت‌های عمومی (بدون کلاس)
            var chain = await ClassChainAsync(equipmentClassId.Value);
            modes = modes.Where(m => m.EquipmentClassId == null || chain.Contains(m.EquipmentClassId.Value));
        }

        var list = await modes
            .Select(m => new
            {
                m.FailureModeId, m.Code, m.Name, m.NameFa, m.EquipmentClassId,
                ClassName = m.EquipmentClass != null ? m.EquipmentClass.Name : null
            })
            .OrderBy(m => m.Name)
            .ToListAsync();

        var tokens = Tokenize(query);
        var filtered = tokens.Count == 0
            ? list
            : list.Where(m =>
            {
                var hay = Normalize($"{m.Code} {m.Name} {m.NameFa} {m.ClassName}");
                return tokens.All(t => hay.Contains(t));
            }).ToList();

        var page = filtered.Take(take).ToList();
        var ids = page.Select(m => m.FailureModeId).ToList();

        var causes = await _context.Set<FailureModeCause>().AsNoTracking()
            .Where(c => ids.Contains(c.FailureModeId) && c.FailureCause!.IsActive)
            .Select(c => new { c.FailureModeId, c.FailureCause!.FailureCauseId, c.FailureCause.Code, c.FailureCause.Name, c.FailureCause.NameFa })
            .ToListAsync();

        return page.Select(m => new FailureModeOptionDto
        {
            FailureModeId = m.FailureModeId,
            Code = m.Code,
            Name = m.Name,
            NameFa = m.NameFa,
            EquipmentClassId = m.EquipmentClassId,
            EquipmentClassName = m.ClassName,
            Causes = causes.Where(c => c.FailureModeId == m.FailureModeId)
                .Select(c => new FailureCauseOptionDto { FailureCauseId = c.FailureCauseId, Code = c.Code, Name = c.Name, NameFa = c.NameFa })
                .OrderBy(c => c.Name).ToList()
        }).ToList();
    }

    public async Task<FailureModeEditDto?> GetAsync(int failureModeId)
    {
        var m = await _context.FailureModes.AsNoTracking().FirstOrDefaultAsync(x => x.FailureModeId == failureModeId);
        if (m == null) return null;
        var causeIds = await _context.Set<FailureModeCause>().AsNoTracking()
            .Where(c => c.FailureModeId == failureModeId).Select(c => c.FailureCauseId).ToListAsync();
        return new FailureModeEditDto
        {
            FailureModeId = m.FailureModeId, Code = m.Code, Name = m.Name, NameFa = m.NameFa, Description = m.Description,
            EquipmentClassId = m.EquipmentClassId, CauseIds = causeIds, IsActive = m.IsActive
        };
    }

    public async Task<OpResult> SaveAsync(FailureModeEditDto dto)
    {
        var errors = new List<RuleError>();
        var code = (dto.Code ?? "").Trim().ToUpperInvariant().Replace(' ', '_');
        if (code.Length < 3) errors.Add(new RuleError("Rcm_Err_CatalogCodeInvalid", "code"));
        if (string.IsNullOrWhiteSpace(dto.Name)) errors.Add(new RuleError("Rcm_Err_CatalogNameRequired", "name"));
        if (dto.EquipmentClassId.HasValue && !await _context.EquipmentClasses.AnyAsync(c => c.EquipmentClassId == dto.EquipmentClassId))
            errors.Add(new RuleError("Rcm_Err_ClassNotFound", "equipmentClassId"));
        if (await _context.FailureModes.AnyAsync(m => m.Code == code && m.FailureModeId != (dto.FailureModeId ?? 0)))
            errors.Add(new RuleError("Rcm_Err_CatalogCodeDuplicate", "code"));
        if (errors.Count > 0) return OpResult.Fail(errors);

        FailureMode mode;
        if (dto.FailureModeId.HasValue)
        {
            mode = await _context.FailureModes.FirstOrDefaultAsync(m => m.FailureModeId == dto.FailureModeId)
                   ?? throw new InvalidOperationException("FailureMode not found");
        }
        else
        {
            mode = new FailureMode { IsSystemDefault = false, CreatedDate = DateTime.Now };
            _context.FailureModes.Add(mode);
        }

        mode.Code = code;
        mode.Name = dto.Name.Trim();
        mode.NameFa = string.IsNullOrWhiteSpace(dto.NameFa) ? null : dto.NameFa.Trim();
        mode.Description = dto.Description;
        mode.EquipmentClassId = dto.EquipmentClassId;
        mode.IsActive = dto.IsActive;
        await _context.SaveChangesAsync();

        var existing = await _context.Set<FailureModeCause>().Where(c => c.FailureModeId == mode.FailureModeId).ToListAsync();
        _context.Set<FailureModeCause>().RemoveRange(existing.Where(e => !dto.CauseIds.Contains(e.FailureCauseId)));
        foreach (var causeId in dto.CauseIds.Distinct().Where(id => existing.All(e => e.FailureCauseId != id)))
            _context.Set<FailureModeCause>().Add(new FailureModeCause { FailureModeId = mode.FailureModeId, FailureCauseId = causeId });
        await _context.SaveChangesAsync();

        return OpResult.Ok(mode.FailureModeId);
    }

    public async Task<OpResult> SetActiveAsync(int failureModeId, bool isActive)
    {
        var mode = await _context.FailureModes.FirstOrDefaultAsync(m => m.FailureModeId == failureModeId);
        if (mode == null) return OpResult.Fail(new RuleError("Rcm_Err_FailureModeNotFound"));
        mode.IsActive = isActive;
        await _context.SaveChangesAsync();
        return OpResult.Ok(failureModeId);
    }

    public Task<bool> IsKnownCodeAsync(string? code)
        => string.IsNullOrWhiteSpace(code)
            ? Task.FromResult(false)
            : _context.FailureModes.AnyAsync(m => m.IsActive && m.Code == code.Trim());

    // ==================== Helpers ====================

    private async Task<HashSet<int>> ClassChainAsync(int classId)
    {
        var all = await _context.EquipmentClasses.AsNoTracking()
            .Select(c => new { c.EquipmentClassId, c.ParentEquipmentClassId }).ToListAsync();
        var chain = new HashSet<int>();
        int? current = classId;
        while (current.HasValue && chain.Add(current.Value))
            current = all.FirstOrDefault(c => c.EquipmentClassId == current.Value)?.ParentEquipmentClassId;
        return chain;
    }

    internal static string Normalize(string? text)
        => new string((text ?? "").Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    internal static List<string> Tokenize(string? query)
        => (query ?? "")
            .Split(new[] { ' ', '-', '_', '/', '.', ',', '‌' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(Normalize)
            .Where(t => t.Length > 0)
            .ToList();
}
