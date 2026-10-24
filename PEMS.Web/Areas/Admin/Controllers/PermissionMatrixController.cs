using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Identity;

namespace PEMS.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = SystemRoles.SystemAdmin)]
public class PermissionMatrixController : Controller
{
    private readonly IPermissionService _service;
    private readonly IPermissionMatrixEditor _editor;
    private readonly RoleManager<ApplicationRole> _roleManager;

    // سقف امن یک درخواست ذخیره‌ی خودکار (کل ماتریس یک نقش = ماژول × ۱۰ عمل)
    private const int MaxCellsPerRequest = 600;

    public PermissionMatrixController(IPermissionService service, IPermissionMatrixEditor editor, RoleManager<ApplicationRole> roleManager)
    {
        _service = service;
        _editor = editor;
        _roleManager = roleManager;
    }

    // طبق «PEMS Authorization Architecture Completion» فاز ۴ بند ۸/۱۱: نقش‌های واقعیِ دیتابیس
    // (System/Seeded/Custom - هرچه از طریق Admin/Roles یا یک Seeder ساخته شده)، نه لیست ثابت
    // SystemRoles.All - وگرنه نقش‌های سفارشی (مثل «maintenance planning») و نقش‌های حاکمیتیِ
    // Seed‌شده (OPS.IssueAuthority/MNT.*) هرگز روی همین صفحه دیده/ویرایش نمی‌شدند.
    //
    // طبق «PEMS — Global Engineering Management UI/UX, Navigation & RBAC Hardening» بند ۲۰:
    // Inspection/Transmittal قبلاً از این آرایه غایب بودند با اینکه هر دو واقعاً از طریق
    // HasPermissionAsync در InspectionController/TransmittalController گیت می‌شوند و در
    // SeedDefaultsAsync هم Seed دارند - یعنی SystemAdmin عملاً نمی‌توانست این دو ماژول را
    // از طریق همین صفحه ببیند یا ویرایش کند. MDRImport/EquipmentImport/MrsImport هم تازه
    // اضافه شدند (طبق بند ۹-۱۲). AIExtraction/TableExtraction/Relationship/OCRProcessing هم
    // تازه اضافه شدند (طبق «PEMS — P0 AI Pipeline Endpoint Hardening»). Team/Crew/
    // InventoryMasterData/MaintenanceMasterData هم تازه اضافه شدند (طبق «PEMS — P1
    // Organization & Master Data Authorization Hardening»).
    // همین آرایه فهرست مجاز ذخیره‌ی خودکار (SetCells) هم هست - ماژولی بیرون از آن رد می‌شود.
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
        "ReplenishmentRecommendation"
    };

    public async Task<IActionResult> Index()
    {
        ViewData["Title"] = "Permission Matrix";
        ViewData["Section"] = "Administration";
        ViewData["HelpKey"] = "Admin.PermissionMatrix.Index";

        var matrix = await _service.GetMatrixAsync();

        ViewBag.Roles = await _roleManager.Roles.Select(r => r.Name!).OrderBy(n => n).ToArrayAsync();
        ViewBag.Modules = MatrixModules;

        return View(matrix);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetRole(string roleName, List<ModulePermissionInput> permissions)
    {
        await _service.SetPermissionsAsync(roleName, permissions ?? new List<ModulePermissionInput>());
        TempData["Message"] = $"Updated permissions for {roleName}";
        return RedirectToAction(nameof(Index));
    }

    // ذخیره‌ی خودکار از صفحه‌ی ماتریس (یک خانه، یک ردیف، یک ستون یا چند خانه در یک درخواست). منطق در
    // IPermissionMatrixEditor است؛ این‌جا فقط اعتبارسنجی ورودی و بازگرداندن وضعیت نهایی ردیف‌ها.
    // SetRole بالا برای فرم بدون JavaScript دست‌نخورده می‌ماند.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetCells([FromBody] SetPermissionCellsRequest request)
    {
        if (request?.Cells == null || request.Cells.Count == 0)
            return BadRequest(new { error = "NoChanges" });
        if (request.Cells.Count > MaxCellsPerRequest)
            return BadRequest(new { error = "TooManyChanges" });

        var result = await _editor.ApplyAsync(request.RoleName ?? string.Empty, request.Cells, MatrixModules, User.Identity?.Name ?? "unknown");
        if (!result.Success)
            return BadRequest(new { error = result.Error });

        return Json(new
        {
            ok = true,
            configuredModules = result.ConfiguredModules,
            modules = result.Modules.Select(m => new { module = m.ModuleName, flags = m.Flags })
        });
    }
}

public class SetPermissionCellsRequest
{
    public string? RoleName { get; set; }
    public List<PermissionCellChange> Cells { get; set; } = new();
}
