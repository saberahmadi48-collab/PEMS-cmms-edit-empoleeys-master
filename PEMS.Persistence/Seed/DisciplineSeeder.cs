using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;

namespace PEMS.Persistence.Seed;

public static class DisciplineSeeder
{
    // طبق «PEMS Phase 6» ممیزی پایداری تست: قبلاً این‌جا یک آرایه‌ی static از خودِ Entity های
    // Discipline بود (نمونه‌های Object مشترک و Mutable بین همه‌ی فراخوانی‌ها/تست‌ها در یک
    // پروسه). وقتی اولین DbContext این نمونه‌ها رو SaveChanges می‌کرد، EF مقدار کلید تولیدشده
    // (DisciplineId) رو مستقیم روی همون Object مشترک می‌نوشت؛ فراخوانی بعدی (حتی روی یک
    // DbContext کاملاً متفاوت/InMemory جدا) دوباره همون Object آلوده‌شده رو اضافه می‌کرد و باعث
    // خطای IdentityMap («cannot be tracked... already being tracked») می‌شد - دقیقاً مثل باگی
    // که در سایر Seeder ها (که این‌جا الگوی Tuple+ساخت‌در-حلقه رو درست پیاده کرده بودند) وجود
    // نداشت. حالا همون الگوی امنِ بقیه‌ی Seeder ها اینجا هم اعمال شد.
    private static readonly (string Code, string Name, string Description, string ImportAliases)[] Defaults =
    {
        ("MECH", "Mechanical", "Mechanical engineering discipline", "ME, MEC, MECH ENG, Mechanical Engineering"),
        ("ELEC", "Electrical", "Electrical engineering discipline", "EL, ELEC, E&I, Electrical Engineering"),
        ("INST", "Instrumentation", "Instrumentation and controls discipline", "IN, INS, I&C, Instrumentation"),
        ("PROC", "Process", "Process engineering discipline", "PR, PRO, PROCESS, Process Engineering"),
        ("PIP", "Piping", "Piping engineering discipline", "PI, PIPING, Pipe, Piping Engineering"),
        ("CIV", "Civil", "Civil and structural engineering discipline", "CV, CIVIL, Structural, Civil Engineering"),
        ("STR", "Structural", "Structural engineering discipline", "ST, STRUCT, Structural Engineering"),
        ("HSE", "HSE", "Health, safety and environment discipline", "HS, SHE, SAFETY, HSE Engineering"),
        ("ARCH", "Architecture", "Architectural engineering discipline", "AR, ARCHITECTURE, Architectural"),

        // ==============================================================================
        // طبق «PEMS — Enterprise Maintenance Master Data & Standards Implementation» بند ۴:
        // Maintenance Domain از Discipline موجود دوباره استفاده می‌کند (نه یک Entity جدید) -
        // این ۵ مورد در لیست اصلی نبودند و برای پوشش کامل Maintenance Domain اضافه شدند.
        // ==============================================================================
        ("UTIL", "Utilities", "Utilities maintenance domain (steam, air, water, HVAC)", "UTIL, Utility, Utilities Engineering"),
        ("FIRE", "Fire & Gas", "Fire protection and fire & gas detection domain", "FIRE, F&G, Fire Protection, Fire and Gas"),
        ("INSP", "Inspection", "Inspection / Mechanical Integrity domain", "INSP, MI, Mechanical Integrity, Inspection Engineering"),
        ("CORR", "Corrosion", "Corrosion management domain", "CORR, Corrosion Management, Corrosion Engineering"),
        ("REL", "Reliability", "Reliability engineering domain", "REL, RCM, Reliability Engineering")
    };

    // ==============================================================================
    // طبق ممیزی «PEMS — Enterprise Maintenance Master Data»: گارد قبلی («اگر هر ردیفی هست کلاً
    // رد شو») روی دیتابیس‌های از قبل Seed‌شده (که تقریباً همه‌ی محیط‌های واقعی همینند) این متد
    // را برای همیشه بی‌اثر می‌کرد - همون کلاس باگی که قبلاً در PermissionService.SeedDefaultsAsync
    // پیدا و رفع شد. حالا مثل MaintenanceGroupSeeder، بررسی به‌ازای هر Code جداست - Additive،
    // Idempotent، بدون دست‌زدن به دیسیپلین‌هایی که کاربر خودش قبلاً ساخته/ویرایش کرده.
    // ==============================================================================
    private static string NormalizeName(string? name)
        => new string((name ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

    /// <summary>برای Seederهای وابسته (MaintenanceGroupSeeder): دیسیپلین با همین کد، یا با همین کد در ImportAliases.</summary>
    public static PEMS.Domain.Entities.Discipline? Resolve(IEnumerable<PEMS.Domain.Entities.Discipline> disciplines, string code)
        => disciplines.FirstOrDefault(d => string.Equals(d.Code, code, StringComparison.OrdinalIgnoreCase))
           ?? disciplines.FirstOrDefault(d => (d.ImportAliases ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                  .Contains(code, StringComparer.OrdinalIgnoreCase));

    public static async Task SeedAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<PEMSDbContext>();

        // C18: پیش‌تر فقط «کد» مقایسه می‌شد؛ ردیف‌های HasData قدیمی کد دیگری داشتند (EL/PR/PI/CV/AR/...) و Seeder یک «Electrical»/«Process»/«Piping»/«Civil»
        // دوم می‌ساخت (تکرار در فهرست‌ها). حالا اگر دیسیپلین موجودی با همان کد، یا با همان کد در ImportAliases، یا با همان نام وجود داشته باشد، چیزی ساخته نمی‌شود.
        var existing = await db.Disciplines.Select(x => new { x.Code, x.Name, x.ImportAliases }).ToListAsync();
        var existingTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in existing)
        {
            existingTokens.Add(e.Code ?? "");
            existingTokens.Add(NormalizeName(e.Name));
            foreach (var alias in (e.ImportAliases ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) existingTokens.Add(alias);
        }

        var missing = Defaults.Where(d => !existingTokens.Contains(d.Code) && !existingTokens.Contains(NormalizeName(d.Name))).ToList();
        if (missing.Count == 0) return;

        foreach (var (code, name, description, importAliases) in missing)
        {
            db.Disciplines.Add(new Discipline
            {
                Code = code,
                Name = name,
                Description = description,
                ImportAliases = importAliases,
                IsActive = true,
                CreatedDate = DateTime.Now
            });
        }

        await db.SaveChangesAsync();
    }
}
