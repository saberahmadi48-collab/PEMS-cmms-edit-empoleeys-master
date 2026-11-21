using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PEMS.Persistence.Context;
using PEMS.Domain.Entities;

namespace PEMS.Persistence.Seed;

/// <summary>
/// طبق درخواست: Maintenance Master Data باید به‌صورت پیش‌فرض بر اساس استانداردهای رایج
/// صنعت نفت/گاز/پتروشیمی آماده باشد - طبقه‌بندی زیرمجموعه‌های تعمیراتی هر دیسیپلین طبق
/// PEMS_CMMS_MAINTENANCE_ORGANIZATION_BLUEPRINT.md بخش‌های ۵ تا ۸ (Rotating/Static Equipment،
/// MCC/Switchgear، PLC/DCS/SIS، Pipeline/Flange...). فقط برای دیسیپلین‌هایی seed می‌شود که
/// واقعاً در سیستم موجودند - طبق بخش ۴ سند: «این‌ها کاندید معماری‌اند، نه اجازه‌ی seed کورکورانه».
///
/// طبق ممیزی «PEMS — Enterprise Maintenance Master Data»: کدهای دیسیپلین "PIPE"/"CIVL" پایین‌تر
/// قبلاً با کدهای واقعی DisciplineSeeder ("PIP"/"CIV") یکی نبودند - یعنی FirstOrDefault همیشه
/// null برمی‌گشت و ۷ گروه (PIPE-*/CIVL-*) هرگز Seed نمی‌شدند، نه فقط یک‌بار، برای همیشه. رفع شد.
/// بند ۵ سند «Enterprise Maintenance Master Data» هم اینجا اضافه شد (LUBE/WELD/NDT/HVAC/UTIL/
/// FIRE/CORR/REL/MARINE/ENV/SAFETY/FAC) - زیر دیسیپلین‌های موجود یا ۵ دیسیپلین جدیدی که
/// DisciplineSeeder اضافه کرد.
/// </summary>
public static class MaintenanceGroupSeeder
{
    private static readonly (string DisciplineCode, string Code, string Name)[] Defaults =
    {
        ("MECH", "MECH-ROT", "Rotating Equipment"),
        ("MECH", "MECH-STA", "Static Equipment"),
        ("MECH", "MECH-VLV", "Mechanical Valves"),
        ("MECH", "MECH-WKS", "Workshop & Machining"),
        ("MECH", "MECH-FAB", "Fabrication"),
        ("MECH", "MECH-WELD", "Welding & Fabrication"),
        ("MECH", "MECH-LUBE", "Lubrication"),

        ("ELEC", "ELEC-MOT", "Motors"),
        ("ELEC", "ELEC-MCC", "MCC"),
        ("ELEC", "ELEC-SWG", "Switchgear"),
        ("ELEC", "ELEC-TRF", "Transformers"),
        ("ELEC", "ELEC-PWR", "Power Distribution"),
        ("ELEC", "ELEC-CAB", "Cables"),
        ("ELEC", "ELEC-PRO", "Protection"),
        ("ELEC", "ELEC-UPS", "UPS & Battery"),
        ("ELEC", "ELEC-INST", "Electrical & Instrumentation"),

        ("INST", "INST-FLD", "Field Instruments"),
        ("INST", "INST-CV", "Control Valves"),
        ("INST", "INST-PLC", "PLC"),
        ("INST", "INST-DCS", "DCS"),
        ("INST", "INST-SIS", "SIS"),
        ("INST", "INST-ANL", "Analyzers"),
        ("INST", "INST-CAL", "Calibration"),
        ("INST", "INST-FG", "Fire & Gas Instruments"),

        ("PIP", "PIPE-LIN", "Pipeline"),
        ("PIP", "PIPE-FLG", "Flange Management"),
        ("PIP", "PIPE-SUP", "Piping Supports"),
        ("PIP", "PIPE-RPR", "Piping Repair"),
        ("PIP", "PIPE-HW", "Hot Work"),

        ("CIV", "CIVL-STR", "Civil Structures & Buildings"),
        ("CIV", "CIVL-RD", "Roads & Drainage"),
        ("CIV", "CIVL-FAC", "Facility Maintenance"),
        ("CIV", "CIVL-MAR", "Marine / Jetty"),

        ("HSE", "HSE-ENV", "Environmental Systems"),
        ("HSE", "HSE-SAF", "Safety Systems"),

        // ==============================================================================
        // طبق «PEMS — Enterprise Maintenance Master Data & Standards Implementation» بند ۵:
        // زیر ۵ دیسیپلین جدیدی که DisciplineSeeder برای پوشش کامل Maintenance Domain اضافه کرد.
        // ==============================================================================
        ("UTIL", "UTIL-HVAC", "HVAC & Refrigeration"),
        ("UTIL", "UTIL-GEN", "Utilities (Steam/Air/Water)"),

        ("FIRE", "FIRE-PROT", "Fire Protection"),

        ("INSP", "INSP-NDT", "Non-Destructive Testing"),
        ("INSP", "INSP-MI", "Mechanical Integrity"),

        ("CORR", "CORR-MGT", "Corrosion Management"),

        ("REL", "REL-ENG", "Reliability Engineering"),
    };

    public static async Task SeedAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<PEMSDbContext>();

        var disciplines = await db.Disciplines.Where(x => x.IsActive).ToListAsync();
        if (disciplines.Count == 0) return;

        // طبق درخواست: پر کردن پیش‌فرض‌های استاندارد، بدون دست‌زدن به مواردی که کاربر خودش
        // قبلاً از همین صفحه ساخته - چک به‌ازای هر مورد، نه یک‌بار برای کل جدول
        var existingCodes = (await db.MaintenanceGroups.Select(x => x.Code).ToListAsync())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var (disciplineCode, code, name) in Defaults)
        {
            if (existingCodes.Contains(code)) continue;

            var discipline = DisciplineSeeder.Resolve(disciplines, disciplineCode);
            if (discipline == null) continue;

            db.MaintenanceGroups.Add(new MaintenanceGroup
            {
                Code = code,
                Name = name,
                DisciplineId = discipline.DisciplineId,
                IsSystemDefault = true,
                IsActive = true,
                CreatedDate = DateTime.Now
            });
        }

        await db.SaveChangesAsync();
    }
}
