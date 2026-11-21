using Microsoft.EntityFrameworkCore;
using PEMS.Application.Common;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;

namespace PEMS.Persistence.Services;

/// <summary>
/// هدف مؤثر شاخص‌های داشبورد مدیریتی: ردیف KpiTargets (اگر باشد) وگرنه پیش‌فرض KpiCatalog. «بدون هدف» با ردیفی که
/// IsActive=false دارد ثبت می‌شود تا پیش‌فرض کاتالوگ را هم بتوان صریحاً خاموش کرد. حذف فیزیکی نداریم.
/// </summary>
public class KpiTargetService : IKpiTargetService
{
    private const decimal MaxValue = 1_000_000_000m;

    private readonly PEMSDbContext _ctx;

    public KpiTargetService(PEMSDbContext ctx) => _ctx = ctx;

    public async Task<List<KpiTargetDto>> GetEffectiveAsync()
    {
        var rows = await _ctx.KpiTargets.AsNoTracking().ToDictionaryAsync(x => x.KpiKey, StringComparer.Ordinal);

        return KpiCatalog.All.Select(def =>
        {
            var dto = new KpiTargetDto
            {
                Key = def.Key, Section = def.Section, Unit = def.Unit, Direction = def.Direction,
                Target = def.DefaultTarget, Warn = def.DefaultWarn
            };
            if (rows.TryGetValue(def.Key, out var row))
            {
                dto.IsCustom = true;
                dto.Target = row.IsActive ? row.TargetValue : null;
                dto.Warn = row.IsActive ? row.WarnValue : null;
            }
            return dto;
        }).ToList();
    }

    public async Task<PcResult> SaveAsync(string key, decimal? target, decimal? warn)
    {
        var def = KpiCatalog.Find(key);
        if (def == null) return PcResult.Fail("Key", "Kpi_Err_UnknownKpi");

        if (target == null) warn = null;   // آستانه بدون هدف بی‌معناست

        if (target != null && !InRange(def, target.Value)) return PcResult.Fail("Target", "Kpi_Err_TargetRange");
        if (warn != null)
        {
            if (!InRange(def, warn.Value)) return PcResult.Fail("Warn", "Kpi_Err_TargetRange");
            var wrongSide = def.Direction == KpiDirections.LowerIsBetter ? warn < target : warn > target;
            if (wrongSide) return PcResult.Fail("Warn", def.Direction == KpiDirections.LowerIsBetter ? "Kpi_Err_WarnAboveTarget" : "Kpi_Err_WarnBelowTarget");
        }

        var row = await _ctx.KpiTargets.FirstOrDefaultAsync(x => x.KpiKey == key);
        if (row == null)
        {
            row = new KpiTarget { KpiKey = key };
            _ctx.KpiTargets.Add(row);
        }

        row.IsActive = target != null;
        row.TargetValue = target.HasValue ? Math.Round(target.Value, 4) : 0m;
        row.WarnValue = warn.HasValue ? Math.Round(warn.Value, 4) : null;
        row.ModifiedDate = DateTime.Now;

        try
        {
            await _ctx.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return PcResult.Fail("Key", "Kpi_Err_SaveFailed");   // مسابقه روی ایندکس یکتا یا خطای ذخیره: هیچ تغییری ثبت نشد
        }
        return PcResult.Ok();
    }

    // درصد بین ۰ و ۱۰۰؛ بقیه‌ی واحدها غیرمنفی و با سقف امن
    private static bool InRange(KpiDefinition def, decimal v) =>
        v >= 0 && v <= (def.Unit == "%" ? 100m : MaxValue);
}
