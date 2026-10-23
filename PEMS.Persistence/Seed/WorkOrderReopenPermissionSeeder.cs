using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;

namespace PEMS.Persistence.Seed;

/// <summary>
/// H28/H29: دو مجوز جداگانه، Idempotent و فقط افزایشی (Grantهای غیرفعال‌شده‌ی دستی مدیر بازنویسی نمی‌شوند؛ SystemAdmin طبق قاعده‌ی عمومی Bypass دارد):
///  * WorkOrder.Reopen: بازگشایی Work Order بسته‌شده → مدیر نگهداری (MNT.Manager)؛
///  * WorkOrder.CorrectActuals: اصلاح ساعت‌کار/قطعات پس از اتمام اجرا و پیش از تصویب نهایی (با دلیل و ممیزی) → سرگروه دیسیپلین (MNT.DisciplineHead).
/// </summary>
public static class WorkOrderReopenPermissionSeeder
{
    public const string PermissionName = "WorkOrder.Reopen";
    public const string GrantedRole = "MNT.Manager";
    public const string CorrectionPermissionName = "WorkOrder.CorrectActuals";
    public const string CorrectionGrantedRole = "MNT.DisciplineHead";

    public static async Task SeedAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<PEMSDbContext>();
        await EnsureAsync(db);
    }

    public static async Task EnsureAsync(PEMSDbContext db)
    {
        await EnsureGrantAsync(db, PermissionName, GrantedRole);
        await EnsureGrantAsync(db, CorrectionPermissionName, CorrectionGrantedRole);
    }

    private static async Task EnsureGrantAsync(PEMSDbContext db, string permissionName, string role)
    {
        var permission = await WorkOrderGovernanceSeeder.GetOrCreatePermissionAsync(db, permissionName);

        var roleId = await db.Roles.Where(r => r.Name == role).Select(r => (int?)r.Id).FirstOrDefaultAsync();
        if (roleId == null) return;           // نقش هنوز ساخته نشده؛ Seed بعدی آن را می‌گیرد

        var exists = await db.RolePermissionGrants.AnyAsync(g => g.RoleId == roleId && g.PermissionId == permission.PermissionId);
        if (exists) return;

        db.RolePermissionGrants.Add(new RolePermissionGrant { RoleId = roleId.Value, PermissionId = permission.PermissionId, IsActive = true, CreatedDate = DateTime.Now });
        await db.SaveChangesAsync();
    }
}
