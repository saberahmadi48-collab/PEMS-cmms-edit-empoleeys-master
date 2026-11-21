using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;

namespace PEMS.Web.Services;

/// <summary>The modules of the matrix are the list the old controller kept (it is also the allow-list of what can be saved).</summary>
public sealed class PermissionMatrixPageService(IPermissionService permissions) : IPermissionMatrixPageService
{
    private static readonly string[] MatrixModules =
    {
        "EngineeringDocuments", "VDR", "Contracts", "Vendors",
        "MaterialRequisition", "Equipment", "DisciplineWorkspace", "DisciplineMaster", "Correspondence",
        "WorkOrder", "StoreRequisition", "WorkRequest", "Inspection", "Transmittal",
        "Task", "FunctionalLocation", "EquipmentCriticality", "JobPlan", "MaintenancePlan",
        "ProcurementPackage",
        "MDRImport", "EquipmentImport", "MrsImport",
        "AIExtraction", "TableExtraction", "Relationship", "OCRProcessing",
        "Team", "Crew", "InventoryMasterData", "MaintenanceMasterData",
        "Setup",
        // طبق ممیزی Material Management (Gap A) + Step 2.
        "IRN",
        // طبق ممیزی Material Management Step 1/3 (یافته امنیتی).
        "MaterialItem",
        // طبق ممیزی Package Procurement Contract Finalization (§C).
        "Tender",
        // طبق ممیزی Package Procurement Contract Finalization (§H، Phase 7.5).
        "PurchaseOrder",
        // طبق «PEMS — Standards-Based Mechanical Equipment Passport & Condition Monitoring»
        // فاز ۶/۹: FindingController/ConditionReadingImportController از ابتدا از طریق
        // HasPermissionAsync گیت می‌شوند، اما تا این خط هیچ نقشی جز SystemAdmin (از طریق
        // Bypass عمومی AccessControlService) نمی‌توانست این دو ماژول را از همین صفحه ببیند
        // یا دریافت کند - عیناً همون کلاس باگی که بند ۲۰ بالا درباره‌ی Inspection/Transmittal توضیح داد.
        "CMFinding",
        "ConditionReadingImport",
        // Phase 2 / P5: توصیه‌های تأمین. فقط برای دیده‌شدن/تخصیص در ماتریس فهرست شده؛ هیچ مجوزی به هیچ نقشی خودکار داده نمی‌شود
        // (View = مشاهده، Create = اجرای محاسبه، Edit = رد/بازگشایی). تخصیص نقش‌ها در انتظار تأیید مالک است.
        "ReplenishmentRecommendation",
        // SCRUM-471: the pre-procurement queue (View = see, Edit = decide items, Approve = procurement manager).
        "PreProcurement"
    };

    private static readonly (string Column, Func<RolePermissionDto, bool> Get, Action<ModulePermissionInput> Set)[] Columns =
    {
        ("View", r => r.CanView, i => i.CanView = true), ("Create", r => r.CanCreate, i => i.CanCreate = true), ("Edit", r => r.CanEdit, i => i.CanEdit = true), ("Delete", r => r.CanDelete, i => i.CanDelete = true),
        ("Approve", r => r.CanApprove, i => i.CanApprove = true), ("ManualNumber", r => r.CanManualNumber, i => i.CanManualNumber = true), ("Freeze", r => r.CanFreeze, i => i.CanFreeze = true),
        ("Unfreeze", r => r.CanUnfreeze, i => i.CanUnfreeze = true), ("Hold", r => r.CanHold, i => i.CanHold = true), ("ReleaseHold", r => r.CanReleaseHold, i => i.CanReleaseHold = true)
    };

    public Task<List<MatrixModuleRowDto>> ModulesAsync() => Task.FromResult(MatrixModules.Select(m => new MatrixModuleRowDto { Module = m }).ToList());

    public async Task<List<MatrixCellsRowDto>> CellsOfRoleAsync(string roleName)
    {
        var rows = (await permissions.GetMatrixAsync()).Where(r => string.Equals(r.RoleName, roleName, StringComparison.Ordinal) && MatrixModules.Contains(r.ModuleName));
        var cells = rows.SelectMany(r => Columns.Where(c => c.Get(r)).Select(c => r.ModuleName + "|" + c.Column));
        return [new MatrixCellsRowDto { RoleName = roleName, Cells = string.Join(",", cells) }];
    }

    public async Task<MatrixSaveResult> SetRoleAsync(string roleName, string? cells)
    {
        if (string.IsNullOrWhiteSpace(roleName)) return new MatrixSaveResult(false, "The role is required.");
        var inputs = MatrixModules.ToDictionary(m => m, m => new ModulePermissionInput { ModuleName = m }, StringComparer.Ordinal);
        foreach (var cell in (cells ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = cell.Split('|');
            if (parts.Length != 2 || !inputs.TryGetValue(parts[0], out var input)) continue;      // an unknown module is ignored: only the modules of the matrix can be saved
            Columns.FirstOrDefault(c => c.Column == parts[1]).Set?.Invoke(input);
        }
        await permissions.SetPermissionsAsync(roleName.Trim(), inputs.Values);
        return new MatrixSaveResult(true, null);
    }
}
