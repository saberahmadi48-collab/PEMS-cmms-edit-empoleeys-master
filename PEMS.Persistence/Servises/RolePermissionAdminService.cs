using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Identity;

namespace PEMS.Persistence.Services;

/// <summary>
/// ویرایش مجوز از دید کاربر (Admin/Users/Details &gt; Effective). مجوز مؤثر کاربر = اجتماع Grantهای
/// فعالِ نقش‌هایش (AccessControlService.DecideAsync) و «مجوز به‌ازای کاربر» وجود ندارد؛ پس این
/// سرویس Grant همان نقش را تغییر می‌دهد و همه‌ی کاربران آن نقش را تحت‌تأثیر قرار می‌دهد.
///
/// فقط ۱۰ عمل ثابت Permission Matrix قابل ویرایش‌اند. بقیه (مثلاً ViewAllDisciplines) در هر Startup
/// توسط Seeder دوباره فعال می‌شوند و تغییرشان بی‌صدا برمی‌گشت؛ همین سرنوشت را مجوزهایی دارند که یک
/// نقش از طریق RoleModuleAction گرفته (AccessControlSeeder.SeedFromRoleModuleActionsAsync). برای
/// SystemAdmin هم Bypass سراسری وجود دارد و Grant چیزی را تغییر نمی‌دهد.
///
/// نوشتن مستقیم روی یک Grant، سطر RolePermission (منبع Seed مسیر Matrix) را هم هم‌گام نگه می‌دارد؛
/// وگرنه SyncRoleFromRolePermissionsAsync در ذخیره‌ی بعدی Matrix یا Startup آن را برمی‌گرداند.
/// از SetPermissionsAsync/SyncRowAsync عمداً استفاده نشده: آن‌ها برای هر ماژول هر ۱۰ Permission را
/// می‌سازند، در حالی که این‌جا فقط یک مجوز تغییر می‌کند.
/// </summary>
public class RolePermissionAdminService : IRolePermissionAdminService
{
    // ستون‌های RolePermission = ۱۰ عمل ثابت Permission Matrix
    private static readonly string[] MatrixActions =
    {
        "View", "Create", "Edit", "Delete", "Approve", "ManualNumber", "Freeze", "Unfreeze", "Hold", "ReleaseHold"
    };

    private readonly PEMSDbContext _context;
    private readonly ILogger<RolePermissionAdminService>? _logger;

    public RolePermissionAdminService(PEMSDbContext context, ILogger<RolePermissionAdminService>? logger = null)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<IReadOnlyDictionary<string, string>> GetNonEditableAsync(IEnumerable<string> userRoles)
    {
        var roles = userRoles.Distinct().ToArray();

        var permissions = await _context.Permissions.AsNoTracking()
            .Where(p => p.IsActive)
            .Select(p => new { p.Name, p.ActionName })
            .ToListAsync();

        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        string? blanket = roles.Contains(SystemRoles.SystemAdmin) ? PermissionChangeReasons.SystemAdmin
            : roles.Length == 0 ? PermissionChangeReasons.NoRoles
            : null;
        if (blanket != null)
        {
            foreach (var p in permissions) result[p.Name] = blanket;
            return result;
        }

        var moduleActions = await _context.RoleModuleActions.AsNoTracking()
            .Where(x => roles.Contains(x.RoleName))
            .Select(x => new { x.Module, x.Action })
            .ToListAsync();
        var fromModuleActions = moduleActions.Select(x => $"{x.Module}.{x.Action}").ToHashSet(StringComparer.Ordinal);

        foreach (var p in permissions)
        {
            if (!MatrixActions.Contains(p.ActionName, StringComparer.Ordinal))
                result[p.Name] = PermissionChangeReasons.OutsideMatrix;
            else if (fromModuleActions.Contains(p.Name))
                result[p.Name] = PermissionChangeReasons.RoleModuleAction;
        }
        return result;
    }

    public async Task<RolePermissionChangeResult> ApplyAsync(
        IReadOnlyCollection<string> userRoles,
        string? targetRole,
        IEnumerable<PermissionToggleRequest> changes,
        string actor)
    {
        var result = new RolePermissionChangeResult();

        // آخرین درخواست برای هر مجوز برنده است (چند کلیک پشت‌سرهم روی یک سطر)
        var requests = changes
            .Where(c => !string.IsNullOrWhiteSpace(c.PermissionName))
            .GroupBy(c => c.PermissionName, StringComparer.Ordinal)
            .Select(g => g.Last())
            .ToList();
        if (requests.Count == 0) return result;

        var roles = userRoles.Distinct().ToList();
        var notEditable = await GetNonEditableAsync(roles);

        var names = requests.Select(r => r.PermissionName).ToList();
        var permissions = await _context.Permissions
            .Where(p => p.IsActive && names.Contains(p.Name))
            .ToDictionaryAsync(p => p.Name, StringComparer.Ordinal);

        var roleIdByName = await _context.Roles
            .Where(r => r.Name != null && roles.Contains(r.Name))
            .ToDictionaryAsync(r => r.Name!, r => r.Id, StringComparer.Ordinal);

        var target = targetRole ?? (roles.Count == 1 ? roles[0] : null);
        var targetUsable = target != null && roleIdByName.ContainsKey(target);

        var resources = permissions.Values.Select(p => p.ResourceName).Distinct().ToList();

        // همه‌ی ۱۰ Permission ماژول‌های درگیر: برای ساخت سطر RolePermission تازه از وضعیت فعلی Grantها
        var modulePermissions = await _context.Permissions.AsNoTracking()
            .Where(p => resources.Contains(p.ResourceName) && MatrixActions.Contains(p.ActionName))
            .ToListAsync();

        var permissionIds = permissions.Values.Select(p => p.PermissionId)
            .Union(modulePermissions.Select(p => p.PermissionId))
            .ToList();
        var roleIds = roleIdByName.Values.ToList();
        var grants = await _context.RolePermissionGrants
            .Where(g => roleIds.Contains(g.RoleId) && permissionIds.Contains(g.PermissionId))
            .ToListAsync();

        var legacyRows = await _context.RolePermissions
            .Where(x => roles.Contains(x.RoleName) && resources.Contains(x.ModuleName))
            .ToListAsync();

        var affected = new HashSet<string>(StringComparer.Ordinal);

        foreach (var request in requests)
        {
            var item = new PermissionToggleResult { PermissionName = request.PermissionName };
            result.Items.Add(item);

            if (!permissions.TryGetValue(request.PermissionName, out var permission))
            {
                item.Reason = PermissionChangeReasons.Unknown;
                continue;
            }
            if (notEditable.TryGetValue(permission.Name, out var reason))
            {
                item.Reason = reason;
                continue;
            }

            var providers = roles
                .Where(r => roleIdByName.TryGetValue(r, out var id)
                            && grants.Any(g => g.RoleId == id && g.PermissionId == permission.PermissionId && g.IsActive))
                .ToList();

            if (request.Enable)
            {
                if (providers.Count > 0) { item.Reason = PermissionChangeReasons.NoChange; continue; }
                if (!targetUsable) { item.Reason = PermissionChangeReasons.TargetRoleRequired; continue; }

                SetRoleGrant(target!, permission, enabled: true);
                affected.Add(target!);
                item.Applied = true;
            }
            else
            {
                if (providers.Count == 0) { item.Reason = PermissionChangeReasons.NoChange; continue; }

                foreach (var role in providers)
                {
                    SetRoleGrant(role, permission, enabled: false);
                    affected.Add(role);
                }
                item.Applied = true;
            }
        }

        if (result.AppliedCount > 0)
        {
            // یک SaveChanges = یک تراکنش؛ یا همه‌ی تغییرها ثبت می‌شوند یا هیچ‌کدام
            await _context.SaveChangesAsync();

            _logger?.LogInformation(
                "Role permission change by {Actor}: {Applied} applied on roles [{Roles}] -> {Changes}",
                actor, result.AppliedCount, string.Join(", ", affected),
                string.Join(", ", result.Items.Where(i => i.Applied)
                    .Select(i => $"{(requests.First(r => r.PermissionName == i.PermissionName).Enable ? "+" : "-")}{i.PermissionName}")));
        }

        result.AffectedRoles = affected.OrderBy(r => r, StringComparer.Ordinal).ToList();
        return result;

        void SetRoleGrant(string role, Permission permission, bool enabled)
        {
            var roleId = roleIdByName[role];

            var grant = grants.FirstOrDefault(g => g.RoleId == roleId && g.PermissionId == permission.PermissionId);
            if (grant == null)
            {
                grant = new RolePermissionGrant { RoleId = roleId, PermissionId = permission.PermissionId, IsActive = enabled, CreatedDate = DateTime.Now };
                _context.RolePermissionGrants.Add(grant);
                grants.Add(grant);
            }
            else
            {
                grant.IsActive = enabled;
            }

            MirrorToLegacyRow(role, roleId, permission, enabled);
        }

        void MirrorToLegacyRow(string role, int roleId, Permission permission, bool enabled)
        {
            var row = legacyRows.FirstOrDefault(x => x.RoleName == role && x.ModuleName == permission.ResourceName);
            if (row == null)
            {
                // سطر تازه باید وضعیت فعلی همان ماژول را حفظ کند، وگرنه همگام‌سازی بعدی Matrix بقیه‌ی
                // Grantهای فعال این نقش/ماژول را بی‌صدا غیرفعال می‌کرد.
                row = new RolePermission { RoleName = role, ModuleName = permission.ResourceName, CreatedDate = DateTime.Now };
                foreach (var sibling in modulePermissions.Where(p => p.ResourceName == permission.ResourceName))
                {
                    var active = grants.Any(g => g.RoleId == roleId && g.PermissionId == sibling.PermissionId && g.IsActive);
                    SetFlag(row, sibling.ActionName, active);
                }
                _context.RolePermissions.Add(row);
                legacyRows.Add(row);
            }

            SetFlag(row, permission.ActionName, enabled);
        }
    }

    private static void SetFlag(RolePermission row, string action, bool value)
    {
        switch (action)
        {
            case "View": row.CanView = value; break;
            case "Create": row.CanCreate = value; break;
            case "Edit": row.CanEdit = value; break;
            case "Delete": row.CanDelete = value; break;
            case "Approve": row.CanApprove = value; break;
            case "ManualNumber": row.CanManualNumber = value; break;
            case "Freeze": row.CanFreeze = value; break;
            case "Unfreeze": row.CanUnfreeze = value; break;
            case "Hold": row.CanHold = value; break;
            case "ReleaseHold": row.CanReleaseHold = value; break;
        }
    }
}
