using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;

namespace PEMS.Persistence.Seed;

/// <summary>
/// P0-09 (D-OPEN-02، تصمیم مالک ۲۰۲۶-۱۰-۱۴): صفحات فقط-خواندنی خرید/انبار حالا مجوز View می‌خواهند. برای این‌که هیچ
/// کاربر فعلی قفل نشود، هر نقشی که روی همین ماژول‌ها مجوز دیگری (Create/Edit/Approve/Delete یا اکشن RoleModuleAction)
/// دارد، View هم می‌گیرد. قاعده: «مجوز تغییر بدون مجوز دیدن» روی این ماژول‌ها معنا ندارد.
///  * Idempotent و فقط افزایشی: هیچ مجوزی برداشته نمی‌شود؛ ردیف بدون هیچ مجوزی دست نمی‌خورد.
///  * روی RolePermissions/RoleModuleActions (منبع ویرایش‌پذیر) نوشته می‌شود، نه مستقیم روی Grant - چون
///    AccessControlSeeder.SeedFromLegacyAsync در هر Startup Grant های View با CanView=false را غیرفعال می‌کند.
///    پس باید قبل از SeedFromLegacyAsync اجرا شود.
/// </summary>
public static class ProcurementReadViewSeeder
{
    /// <summary>ماژول‌هایی که صفحات خواندنی‌شان در P0-09 به View بسته شدند (Inventory/InventoryTransaction/گزارش خرید از همین‌ها استفاده می‌کنند).</summary>
    public static readonly string[] Modules = { "Vendors", "Contracts", "IRN", "StoreRequisition", "MaterialItem", "ProcurementPackage" };

    public static async Task SeedAsync(IServiceProvider services)
        => await EnsureAsync(services.GetRequiredService<PEMSDbContext>());

    public static async Task<int> EnsureAsync(PEMSDbContext db)
    {
        var changed = 0;

        var rows = await db.RolePermissions
            .Where(r => Modules.Contains(r.ModuleName) && !r.CanView
                        && (r.CanCreate || r.CanEdit || r.CanApprove || r.CanDelete))
            .ToListAsync();
        foreach (var r in rows) { r.CanView = true; changed++; }

        // نقش‌هایی که فقط از طریق RoleModuleActions روی این ماژول‌ها مجوز دارند
        var actionRoles = await db.RoleModuleActions
            .Where(a => Modules.Contains(a.Module) && a.Action != "View")
            .Select(a => new { a.RoleName, a.Module })
            .Distinct()
            .ToListAsync();
        foreach (var a in actionRoles)
        {
            var hasViewRow = await db.RolePermissions.AnyAsync(r => r.RoleName == a.RoleName && r.ModuleName == a.Module && r.CanView)
                             || rows.Any(r => r.RoleName == a.RoleName && r.ModuleName == a.Module)
                             || await db.RoleModuleActions.AnyAsync(x => x.RoleName == a.RoleName && x.Module == a.Module && x.Action == "View");
            if (hasViewRow) continue;

            db.RoleModuleActions.Add(new RoleModuleAction { RoleName = a.RoleName, Module = a.Module, Action = "View", CreatedDate = DateTime.Now });
            changed++;
        }

        if (changed > 0) await db.SaveChangesAsync();
        return changed;
    }
}
