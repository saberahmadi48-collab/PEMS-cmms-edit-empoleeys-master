using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PEMS.Persistence.Context;
using PEMS.Persistence.Services;

namespace PEMS.Persistence.Seed;

/// <summary>
/// SCRUM-40: ensures every procurement document type has a numbering scheme, active with the default pattern
/// (owner decision 2026-10-27: procurement numbers are assigned automatically). It only adds missing rows; a scheme the
/// administrator edited or switched off is never touched.
/// </summary>
public static class NumberingSchemeSeeder
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<PEMSDbContext>();
        var existing = await db.NumberingSchemes.Select(x => x.DocumentType).ToListAsync();
        var added = false;
        foreach (var type in NumberingSchemeService.ProcurementDocumentTypes.Where(t => !existing.Contains(t)))
        {
            var scheme = NumberingSchemeService.Default(type, isActive: true);
            scheme.CreatedBy = "system";
            db.NumberingSchemes.Add(scheme);
            added = true;
        }
        if (added) await db.SaveChangesAsync();
    }
}
