using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;

namespace PEMS.Persistence.Seed;

/// <summary>
/// طبق سند RBAC-ABAC Authorization Architecture (فاز ۱): داده‌ی امروزِ RolePermission/
/// RoleModuleAction/AspNetUserRoles را به مدل عمومی جدید (Permission/RolePermissionGrant/
/// RoleAssignment) کپی می‌کند - جدول‌های منبع دست‌نخورده باقی می‌مانند (فقط خوانده می‌شوند).
/// سه گام مستقل، هرکدام Idempotent به‌ازای خودش - دقیقاً همون درسی که در PermissionService
/// (بند WorkOrder/StoreRequisition/...) و RoleModuleActionService (SeedModuleAsync) قبلاً اعمال
/// شده: یک گارد سراسری «اگه هر ردیفی هست کلاً رد شو» باعث می‌شود داده‌ی بعداً اضافه‌شده به منبع
/// (مثلاً یک ماژول جدید در RolePermission) هیچ‌وقت به این‌جا هم‌گام نشود.
/// </summary>
public static class AccessControlSeeder
{
    // طبق بند ۷ سند: این اکشن‌ها همیشه باید صریح باشند - علامت‌گذاری IsDestructive برای
    // مصرف فازهای بعدی (Simulator/Effective Permissions)؛ در فاز ۱ چیزی را مسدود نمی‌کند.
    private static readonly HashSet<string> DestructiveActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "Delete", "HardDelete", "Cancel", "Reject", "Approve", "Issue", "Unissue", "Close",
        "Reopen", "Freeze", "Unfreeze", "Override", "EmergencyOverride", "Hold", "ReleaseHold",
        "Revoke", "Isolate", "Deisolate", "Suspend", "Resume"
    };

    public static async Task SeedFromLegacyAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<PEMSDbContext>();
        var permissionCache = new Dictionary<string, Permission>(StringComparer.Ordinal);

        await SeedFromRolePermissionsAsync(db, permissionCache);
        await SeedFromRoleModuleActionsAsync(db, permissionCache);
        await SeedRoleAssignmentsFromUserRolesAsync(db);
    }

    /// <summary>
    /// طبق سند RBAC-ABAC Authorization Architecture (فاز ۳ - جایگزینی مکانیزم D): این دو مجوز
    /// از هیچ جدول Legacy مشتق نشده‌اند (بر خلاف SeedFromLegacyAsync) - معادل داده‌محور دقیق دو
    /// آرایه‌ی ثابت (Hard-Coded) که تا امروز مستقیماً در کد تکرار شده بودند:
    /// «EngineeringDocuments.ViewAllDisciplines» = دقیقاً همان UnrestrictedRoles که در سه فایل
    /// (EngineeringDocumentsController, EngineeringDashboardController, DocumentRegisterController)
    /// عیناً کپی شده بود؛ «Document.RecordClientResponse» = دقیقاً همان ClientResponseRoles در
    /// DocumentWorkflowService. SystemAdmin عمداً این‌جا صریح Grant نمی‌شود - طبق الگوی موجود
    /// پروژه (RolePermission/RoleModuleAction هم هرگز SystemAdmin را صریح لیست نمی‌کنند)، از
    /// Bypass عمومی AccessControlService.DecideAsync/AuthorizeForRolesAsync استفاده می‌کند.
    /// </summary>
    public static async Task SeedPhase3PermissionsAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<PEMSDbContext>();
        var cache = new Dictionary<string, Permission>(StringComparer.Ordinal);

        foreach (var roleName in new[]
        {
            PEMS.Persistence.Identity.SystemRoles.EngineeringManager,
            PEMS.Persistence.Identity.SystemRoles.EngineeringSupervisor,
            PEMS.Persistence.Identity.SystemRoles.DocumentController
        })
            await SyncGrantAsync(db, cache, roleName, "EngineeringDocuments", "ViewAllDisciplines", true);

        foreach (var roleName in new[]
        {
            PEMS.Persistence.Identity.SystemRoles.EngineeringManager,
            PEMS.Persistence.Identity.SystemRoles.DocumentController
        })
            await SyncGrantAsync(db, cache, roleName, "Document", "RecordClientResponse", true);

        // طبق «PEMS Authorization Architecture Completion» فاز ۳: جایگزین دقیق بخش تایید-دیسیپلین
        // در EngineeringDocumentsController.CanApproveDocumentDisciplineAsync - «مدیر مهندسی می‌تواند
        // هر دیسیپلینی را تایید کند؛ سرپرست فقط دیسیپلین خودش را» (طبق کامنت موجود همان متد). عمداً
        // از ViewAllDisciplines استفاده نشد - آن مجوز به سرپرست هم داده شده و استفاده‌ی مجدد از آن
        // این‌جا دامنه‌ی تایید سرپرست را (که طبق قاعده‌ی صریح موجود باید محدود به دیسیپلین خودش بماند)
        // به اشتباه گسترش می‌داد.
        await SyncGrantAsync(db, cache, PEMS.Persistence.Identity.SystemRoles.EngineeringManager, "EngineeringDocuments", "ApproveAllDisciplines", true);
    }

    /// <summary>
    /// شروع فاز ۵ (Governance): سیاست‌های SoD پیش‌فرض برای جلوگیری از خودتاییدی و کنترل‌های
    /// حساس اجرایی. این‌ها فقط Seed هستند و در آینده از پنل Admin قابل مدیریت می‌شوند.
    /// </summary>
    public static async Task SeedPhase5GovernanceAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<PEMSDbContext>();
        var cache = new Dictionary<string, Permission>(StringComparer.OrdinalIgnoreCase);

        var defaultPolicies = new[]
        {
            new { FirstResource = "EngineeringDocuments", FirstAction = "Create", SecondResource = "EngineeringDocuments", SecondAction = "Approve", Description = "کاربر نمی‌تواند مدرک را خودش ایجاد و سپس همان رکورد را تایید کند." },
            new { FirstResource = "EngineeringDocuments", FirstAction = "Edit", SecondResource = "EngineeringDocuments", SecondAction = "Approve", Description = "کاربر نمی‌تواند همان مدرک را ویرایش و سپس تأیید کند." },
            new { FirstResource = "WorkOrder", FirstAction = "Create", SecondResource = "WorkOrder", SecondAction = "Close", Description = "ایجاد و بستن همان WorkOrder توسط یک نفر ممنوع است." },
            new { FirstResource = "WorkOrder", FirstAction = "Edit", SecondResource = "WorkOrder", SecondAction = "Close", Description = "ویرایش و بستن همان WorkOrder توسط یک نفر ممنوع است." },
            new { FirstResource = "Permit", FirstAction = "Create", SecondResource = "Permit", SecondAction = "Issue", Description = "نمی‌توان یک Permit را ایجاد و در همان لحظه صادر کرد." },
            // طبق «PEMS Operations & Production Architecture Spec v1» فاز ۱ بند AF: همون الگوی
            // WorkOrder.Create/Close - کسی که سفارش/بچ تولید را ایجاد یا ویرایش کرده، نمی‌تواند
            // همان رکورد را خودش Release/Complete/Close یا از نظر کیفیت Release/Reject کند.
            new { FirstResource = "Production", FirstAction = "Create", SecondResource = "Production", SecondAction = "Approve", Description = "کاربر نمی‌تواند سفارش/بچ تولیدی را که خودش ایجاد کرده، خودش Release/تایید کیفیت کند." },
            new { FirstResource = "Production", FirstAction = "Edit", SecondResource = "Production", SecondAction = "Approve", Description = "کاربر نمی‌تواند سفارش تولیدی را که خودش برنامه‌ریزی (Plan) کرده، خودش Release/Close کند." },
            // طبق ممیزی Package Procurement Contract Finalization (§F، قاعده‌ی کسب‌وکار ۶):
            // ContractManager هم‌زمان Create+Edit+Approve روی Contracts/ProcurementPackage دارد -
            // این دو ردیف همان شکاف زنده‌ی SoD را می‌بندد (داده کافی نیست، §F کنترلرهایی که این
            // مجوز را واقعاً چک می‌کنند هم باید مسیر AuthorizeAsync آگاه-از-کاربر را صدا بزنند).
            new { FirstResource = "Contracts", FirstAction = "Create", SecondResource = "Contracts", SecondAction = "Approve", Description = "کاربر نمی‌تواند قراردادی را که خودش ایجاد کرده، خودش تایید کند." },
            new { FirstResource = "Contracts", FirstAction = "Edit", SecondResource = "Contracts", SecondAction = "Approve", Description = "کاربر نمی‌تواند قراردادی را که خودش ویرایش کرده، خودش تایید کند." },
            new { FirstResource = "ProcurementPackage", FirstAction = "Create", SecondResource = "ProcurementPackage", SecondAction = "Approve", Description = "کاربر نمی‌تواند Procurement Package ای را که خودش ایجاد کرده، خودش تایید کند." },
            // Procurement Lifecycle WP-B: PO دستی با Draft ساخته می‌شود و صدور آن مجوز Approve می‌خواهد؛
            // سازنده‌ی PO نباید خودش آن را صادر کند.
            new { FirstResource = "PurchaseOrder", FirstAction = "Create", SecondResource = "PurchaseOrder", SecondAction = "Approve", Description = "کاربر نمی‌تواند Purchase Order ای را که خودش ایجاد کرده، خودش صادر (Issue) کند." }
        };

        foreach (var policy in defaultPolicies)
        {
            var permissionA = await GetOrCreatePermissionAsync(db, cache, policy.FirstResource, policy.FirstAction);
            var permissionB = await GetOrCreatePermissionAsync(db, cache, policy.SecondResource, policy.SecondAction);

            var exists = await db.SoDPolicies.AnyAsync(x => x.IsActive &&
                ((x.PermissionAId == permissionA.PermissionId && x.PermissionBId == permissionB.PermissionId) ||
                 (x.PermissionAId == permissionB.PermissionId && x.PermissionBId == permissionA.PermissionId)));

            if (exists)
                continue;

            db.SoDPolicies.Add(new SoDPolicy
            {
                PermissionAId = permissionA.PermissionId,
                PermissionBId = permissionB.PermissionId,
                Description = policy.Description,
                IsActive = true,
                CreatedDate = DateTime.Now
            });
        }

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// طبق «PEMS Authorization Architecture Completion» فاز ۴ بند ۱۳: هم‌گام‌سازی فوریِ یک نقش
    /// (نه هر ۲۰ نقش) بلافاصله بعد از این‌که PermissionService.SetPermissionsAsync ردیف‌های
    /// RolePermission را ذخیره کرد - از همان تابع SyncGrantAsync که SeedFromLegacyAsync در
    /// Startup مصرف می‌کند (نه یک مسیر نوشتن یا یک مکانیزم هم‌گام‌سازیِ تازه). این‌طوری تغییر
    /// مجوز از طریق Admin/PermissionMatrix همان درخواست بعدی زمان اجرا را می‌بیند - شکاف «یک
    /// چرخه‌ی Restart» که ممیزی قبلی مستند کرده بود، برای این مسیر خاص بسته می‌شود؛ Restart
    /// برای بقیه‌ی منابع (RoleModuleAction, Phase3 Permissions) هنوز idempotent/بی‌خطر است.
    /// </summary>
    public static async Task SyncRoleFromRolePermissionsAsync(PEMSDbContext db, string roleName)
    {
        var cache = new Dictionary<string, Permission>(StringComparer.Ordinal);
        var rows = await db.RolePermissions.Where(x => x.RoleName == roleName).ToListAsync();

        foreach (var row in rows)
            await SyncRowAsync(db, cache, row);
    }

    /// <summary>
    /// همان هم‌گام‌سازی SyncRoleFromRolePermissionsAsync ولی فقط برای یک ماژول - برای ذخیره‌ی خودکار
    /// تک‌خانه‌ی Permission Matrix که نباید به‌ازای هر کلیک همه‌ی ماژول‌های نقش را دوباره هم‌گام کند.
    /// </summary>
    public static async Task SyncModuleFromRolePermissionAsync(PEMSDbContext db, string roleName, string moduleName)
    {
        var cache = new Dictionary<string, Permission>(StringComparer.Ordinal);
        var row = await db.RolePermissions.FirstOrDefaultAsync(x => x.RoleName == roleName && x.ModuleName == moduleName);
        if (row != null)
            await SyncRowAsync(db, cache, row);
    }

    private static async Task SeedFromRolePermissionsAsync(PEMSDbContext db, Dictionary<string, Permission> cache)
    {
        var rows = await db.RolePermissions.ToListAsync();

        foreach (var row in rows)
            await SyncRowAsync(db, cache, row);
    }

    private static async Task SyncRowAsync(PEMSDbContext db, Dictionary<string, Permission> cache, RolePermission row)
    {
        await SyncGrantAsync(db, cache, row.RoleName, row.ModuleName, "View", row.CanView);
        await SyncGrantAsync(db, cache, row.RoleName, row.ModuleName, "Create", row.CanCreate);
        await SyncGrantAsync(db, cache, row.RoleName, row.ModuleName, "Edit", row.CanEdit);
        await SyncGrantAsync(db, cache, row.RoleName, row.ModuleName, "Delete", row.CanDelete);
        await SyncGrantAsync(db, cache, row.RoleName, row.ModuleName, "Approve", row.CanApprove);
        await SyncGrantAsync(db, cache, row.RoleName, row.ModuleName, "ManualNumber", row.CanManualNumber);
        await SyncGrantAsync(db, cache, row.RoleName, row.ModuleName, "Freeze", row.CanFreeze);
        await SyncGrantAsync(db, cache, row.RoleName, row.ModuleName, "Unfreeze", row.CanUnfreeze);
        await SyncGrantAsync(db, cache, row.RoleName, row.ModuleName, "Hold", row.CanHold);
        await SyncGrantAsync(db, cache, row.RoleName, row.ModuleName, "ReleaseHold", row.CanReleaseHold);
    }

    private static async Task SeedFromRoleModuleActionsAsync(PEMSDbContext db, Dictionary<string, Permission> cache)
    {
        var rows = await db.RoleModuleActions.ToListAsync();

        foreach (var row in rows)
            await SyncGrantAsync(db, cache, row.RoleName, row.Module, row.Action, true);
    }

    /// <summary>
    /// طبق «PEMS Authorization Architecture Completion» فاز ۴ بند ۱۳: برخلاف GrantIfTrueAsync
    /// قدیمی (که فقط اضافه می‌کرد، هرگز لغو نمی‌کرد)، این نسخه وقتی granted=false و یک Grant فعال
    /// از قبل موجود است، آن را غیرفعال می‌کند (IsActive=false، نه حذف - همان الگوی موجود
    /// WorkOrderGovernanceSeeder برای «مجوزهای قدیمی که پرچمشان False شد»، صرفاً این‌جا تعمیم داده
    /// شده). بدون این، غیرفعال‌کردن یک Checkbox در Permission Matrix هرگز به RolePermissionGrant
    /// (منبع واقعی تصمیم AccessControlService) نمی‌رسید - Grant قدیمی برای همیشه فعال می‌ماند.
    /// </summary>
    private static async Task SyncGrantAsync(
        PEMSDbContext db, Dictionary<string, Permission> cache, string roleName, string resourceName, string actionName, bool granted)
    {
        var permission = await GetOrCreatePermissionAsync(db, cache, resourceName, actionName);

        var roleId = await db.Roles.Where(r => r.Name == roleName).Select(r => (int?)r.Id).FirstOrDefaultAsync();
        if (roleId == null) return; // نقش هنوز در AspNetRoles وجود ندارد - چیزی برای هم‌گام‌سازی نیست

        var existing = await db.RolePermissionGrants
            .FirstOrDefaultAsync(g => g.RoleId == roleId.Value && g.PermissionId == permission.PermissionId);

        if (granted)
        {
            if (existing == null)
            {
                db.RolePermissionGrants.Add(new RolePermissionGrant { RoleId = roleId.Value, PermissionId = permission.PermissionId, IsActive = true, CreatedDate = DateTime.Now });
                await db.SaveChangesAsync();
            }
            else if (!existing.IsActive)
            {
                existing.IsActive = true;
                await db.SaveChangesAsync();
            }
        }
        else if (existing is { IsActive: true })
        {
            existing.IsActive = false;
            await db.SaveChangesAsync();
        }
    }

    private static async Task<Permission> GetOrCreatePermissionAsync(
        PEMSDbContext db, Dictionary<string, Permission> cache, string resourceName, string actionName)
    {
        var name = $"{resourceName}.{actionName}";
        if (cache.TryGetValue(name, out var cached)) return cached;

        var existing = await db.Permissions.FirstOrDefaultAsync(p => p.Name == name);
        if (existing != null)
        {
            cache[name] = existing;
            return existing;
        }

        var permission = new Permission
        {
            Name = name,
            ResourceName = resourceName,
            ActionName = actionName,
            IsDestructive = DestructiveActions.Contains(actionName),
            IsActive = true,
            CreatedDate = DateTime.Now
        };
        db.Permissions.Add(permission);
        await db.SaveChangesAsync();

        cache[name] = permission;
        return permission;
    }

    private static async Task SeedRoleAssignmentsFromUserRolesAsync(PEMSDbContext db)
    {
        var userRoles = await db.UserRoles.Select(ur => new { ur.UserId, ur.RoleId }).ToListAsync();

        foreach (var ur in userRoles)
        {
            var exists = await db.RoleAssignments.AnyAsync(a =>
                a.UserId == ur.UserId && a.RoleId == ur.RoleId && a.ScopeType == "Global");
            if (exists) continue;

            db.RoleAssignments.Add(new RoleAssignment
            {
                UserId = ur.UserId,
                RoleId = ur.RoleId,
                ScopeType = "Global",
                AssignmentType = "Direct",
                IsActive = true,
                CreatedDate = DateTime.Now
            });
        }

        await db.SaveChangesAsync();
    }
}
