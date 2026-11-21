using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Identity;
using PEMS.Persistence.Seed;

namespace PEMS.Persistence.Services;

/// <summary>
/// ذخیره‌ی خودکار Permission Matrix. برخلاف SetPermissionsAsync (که کل ردیف ماژول را از فرم می‌گیرد و
/// کل نقش را هم‌گام می‌کند) این‌جا فقط خانه‌های تغییرکرده‌ی همین درخواست روی سطر RolePermission اعمال
/// می‌شود و فقط Grantهای همان ماژول‌ها هم‌گام می‌شوند. هر درخواست یک SaveChanges (یک تراکنش) است.
/// SystemAdmin عمداً از ماتریس بیرون است (Bypass سراسری دارد).
/// </summary>
public class PermissionMatrixEditor : IPermissionMatrixEditor
{
    private static readonly string[] Actions =
    {
        "View", "Create", "Edit", "Delete", "Approve", "ManualNumber", "Freeze", "Unfreeze", "Hold", "ReleaseHold"
    };

    private readonly PEMSDbContext _context;
    private readonly ILogger<PermissionMatrixEditor>? _logger;

    public PermissionMatrixEditor(PEMSDbContext context, ILogger<PermissionMatrixEditor>? logger = null)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<PermissionMatrixEditResult> ApplyAsync(
        string roleName,
        IReadOnlyCollection<PermissionCellChange> cells,
        IReadOnlyCollection<string> allowedModules,
        string actor)
    {
        if (string.IsNullOrWhiteSpace(roleName) || !await _context.Roles.AnyAsync(r => r.Name == roleName))
            return Fail("UnknownRole");
        if (roleName == SystemRoles.SystemAdmin)
            return Fail("SystemAdminExcluded");

        var allowed = new HashSet<string>(allowedModules, StringComparer.Ordinal);
        foreach (var cell in cells)
        {
            if (!Actions.Contains(cell.Action, StringComparer.Ordinal)) return Fail("InvalidCell");
            if (!allowed.Contains(cell.ModuleName)) return Fail("UnknownModule");
        }

        // آخرین مقدار برای هر (ماژول، عمل) برنده است
        var finalCells = cells
            .GroupBy(c => (c.ModuleName, c.Action))
            .Select(g => g.Last())
            .ToList();
        var moduleNames = finalCells.Select(c => c.ModuleName).Distinct().ToList();

        var rows = await _context.RolePermissions
            .Where(x => x.RoleName == roleName && moduleNames.Contains(x.ModuleName))
            .ToDictionaryAsync(x => x.ModuleName, StringComparer.Ordinal);

        foreach (var cell in finalCells)
        {
            if (!rows.TryGetValue(cell.ModuleName, out var row))
            {
                row = new RolePermission { RoleName = roleName, ModuleName = cell.ModuleName, CreatedDate = DateTime.Now };
                _context.RolePermissions.Add(row);
                rows[cell.ModuleName] = row;
            }
            SetFlag(row, cell.Action, cell.Enabled);
        }

        await _context.SaveChangesAsync();

        // همان مسیر Seed/Matrix: بدون این، AccessControlService (منبع واقعی تصمیم) تغییر را نمی‌بیند
        foreach (var module in moduleNames)
            await AccessControlSeeder.SyncModuleFromRolePermissionAsync(_context, roleName, module);

        _logger?.LogInformation(
            "Permission matrix change by {Actor} on role {Role}: {Changes}",
            actor, roleName, string.Join(", ", finalCells.Select(c => $"{(c.Enabled ? "+" : "-")}{c.ModuleName}.{c.Action}")));

        var result = new PermissionMatrixEditResult { Success = true };
        foreach (var module in moduleNames)
            result.Modules.Add(new PermissionMatrixModuleState { ModuleName = module, Flags = ToFlags(rows[module]) });

        result.ConfiguredModules = await _context.RolePermissions.CountAsync(x => x.RoleName == roleName
            && (x.CanView || x.CanCreate || x.CanEdit || x.CanDelete || x.CanApprove
                || x.CanManualNumber || x.CanFreeze || x.CanUnfreeze || x.CanHold || x.CanReleaseHold));
        return result;
    }

    private static PermissionMatrixEditResult Fail(string error) => new() { Success = false, Error = error };

    private static Dictionary<string, bool> ToFlags(RolePermission r) => new()
    {
        ["View"] = r.CanView, ["Create"] = r.CanCreate, ["Edit"] = r.CanEdit, ["Delete"] = r.CanDelete,
        ["Approve"] = r.CanApprove, ["ManualNumber"] = r.CanManualNumber, ["Freeze"] = r.CanFreeze,
        ["Unfreeze"] = r.CanUnfreeze, ["Hold"] = r.CanHold, ["ReleaseHold"] = r.CanReleaseHold
    };

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
