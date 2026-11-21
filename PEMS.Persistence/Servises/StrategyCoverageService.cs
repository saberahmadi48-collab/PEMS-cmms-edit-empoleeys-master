using Microsoft.EntityFrameworkCore;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Common;
using PEMS.Persistence.Context;

namespace PEMS.Persistence.Services;

/// <summary>
/// پوشش استراتژی بر اساس «تخصیص‌ها»: یک تجهیز زمانی «پوشش‌داده‌شده» است که حداقل یک تخصیص فعال به
/// نسخه‌ی Approved/Active از یک استراتژی فعال داشته باشد. گزارش بر اساس بحرانی‌بودن و کلاس، و فهرست
/// تجهیز بحرانی (کلاس A) بدون هیچ استراتژی. تجهیز بدون ارزیابی بحرانی «Unrated» شمرده و جدا گزارش
/// می‌شود (امروز ارزیابی بحرانی ثبت نشده - گزارش به‌جای حدس‌زدن، این شکاف را صریح نشان می‌دهد).
/// </summary>
public class StrategyCoverageService : IStrategyCoverageService
{
    private readonly PEMSDbContext _context;

    public StrategyCoverageService(PEMSDbContext context)
    {
        _context = context;
    }

    public async Task<StrategyCoverageDto> GetCoverageAsync(int topUncoveredCritical = 100)
    {
        var coveredIds = _context.MaintenanceStrategyAssignments
            .Where(a => a.IsActive && a.Status == RcmCodes.AssignmentStatus.Active
                        && a.MaintenanceStrategy!.IsActive
                        && (a.MaintenanceStrategy.Status == RcmCodes.Status.Approved || a.MaintenanceStrategy.Status == RcmCodes.Status.Active))
            .Select(a => a.EquipmentId);

        var rows = await _context.Equipments.AsNoTracking()
            .Where(e => e.IsActive)
            .Select(e => new
            {
                e.EquipmentId,
                e.EquipmentClassId,
                ClassName = e.EquipmentClassRef != null ? e.EquipmentClassRef.Name : null,
                Criticality = _context.EquipmentCriticalities
                    .Where(c => c.IsActive && c.EquipmentId == e.EquipmentId)
                    .Select(c => c.CriticalityClass).FirstOrDefault(),
                Covered = coveredIds.Contains(e.EquipmentId)
            })
            .ToListAsync();

        var dto = new StrategyCoverageDto
        {
            TotalEquipment = rows.Count,
            CoveredEquipment = rows.Count(r => r.Covered),
            UnclassifiedEquipment = rows.Count(r => r.EquipmentClassId == null),
            UnratedEquipment = rows.Count(r => string.IsNullOrEmpty(r.Criticality))
        };

        string CritKey(string? c) => string.IsNullOrEmpty(c) ? RcmCodes.Criticality.Unrated : c;

        dto.ByCriticality = rows.GroupBy(r => CritKey(r.Criticality))
            .Select(g => new CoverageRowDto
            {
                Key = g.Key,
                TotalEquipment = g.Count(),
                WithApprovedStrategy = g.Count(r => r.Covered),
                WithoutStrategy = g.Count(r => !r.Covered)
            })
            .OrderBy(r => r.Key == RcmCodes.Criticality.Unrated ? "Z" : r.Key)
            .ToList();

        dto.ByClass = rows.GroupBy(r => r.ClassName ?? "")
            .Select(g => new CoverageRowDto
            {
                Key = g.Key,
                TotalEquipment = g.Count(),
                WithApprovedStrategy = g.Count(r => r.Covered),
                WithoutStrategy = g.Count(r => !r.Covered)
            })
            .OrderByDescending(r => r.WithoutStrategy)
            .ToList();

        var uncoveredCriticalIds = rows.Where(r => r.Criticality == RcmCodes.Criticality.A && !r.Covered)
            .Select(r => r.EquipmentId).ToList();
        dto.CriticalWithoutStrategyTotal = uncoveredCriticalIds.Count;

        var take = uncoveredCriticalIds.Take(topUncoveredCritical).ToList();
        var identities = await EquipmentIdentityProjection.Project(_context,
            _context.Equipments.AsNoTracking().Where(e => take.Contains(e.EquipmentId)).OrderBy(e => e.TagNumber)).ToListAsync();
        dto.CriticalWithoutStrategy = identities.Select(i => new UncoveredCriticalRowDto { Equipment = i }).ToList();

        return dto;
    }
}
