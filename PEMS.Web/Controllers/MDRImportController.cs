using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;
using PEMS.Web.Security;

namespace PEMS.Web.Controllers;

/// <summary>
/// ویزارد Import اکسل MDR واقعی: آپلود → Staging خودکار (پارس چند-شیتی + Validate + Duplicate) →
/// بازبینی توسط کاربر → Commit یا Discard. دقیقاً همان الگوی VDRImportController.
/// </summary>
public class MDRImportController : Controller
{
    private readonly IMDRImportService _importService;
    private readonly PEMSDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly IPermissionService _permissionService;

    public MDRImportController(IMDRImportService importService, PEMSDbContext context, ICurrentUserService currentUser,
        IPermissionService permissionService)
    {
        _importService = importService;
        _context = context;
        _currentUser = currentUser;
        _permissionService = permissionService;
    }

    // طبق «PEMS — Global Engineering Management UI/UX, Navigation & RBAC Hardening» بند ۹: این
    // Pipeline قبلاً هیچ بررسی مجوزی نداشت - Commit (که EngineeringDocument/DocumentRevision/
    // DocumentWorkflow معتبر می‌ساخت) برای هر کاربر احرازهویت‌شده‌ای باز بود.
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "MDRImport", "View"))
            return Forbid();

        ViewData["Title"] = "MDR Import";
        ViewData["Section"] = "Engineering Management";
        ViewData["HelpKey"] = "Imports.StagedBatchOverview";

        ViewBag.Projects = await _context.Projects.Where(x => x.IsActive)
            .Select(x => new { x.ProjectId, x.ProjectName }).ToListAsync();

        var recentBatches = await _importService.GetRecentBatchesAsync();

        return View(recentBatches);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [PEMS.Web.Security.Uploads.UploadPolicy(PEMS.Application.Uploads.UploadProfile.Spreadsheet)]
    public async Task<IActionResult> Upload(IFormFile file, int projectId)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "MDRImport", "Create"))
            return Forbid();

        if (file == null || file.Length == 0)
        {
            TempData["Error"] = "Please select an MDR Excel file.";
            return RedirectToAction(nameof(Index));
        }

        using var stream = file.OpenReadStream();

        // طبق باگ واقعی که در Equipment Import پیدا شد (همان کلاس باگ): ImportedById باید
        // EmployeeId باشد، نه شناسه‌ی خام کاربر (UserId)
        var importedByEmployeeId = await GetCurrentEmployeeIdAsync();

        var batchId = await _importService.StageFromExcelAsync(stream, file.FileName, projectId, importedByEmployeeId);

        return RedirectToAction(nameof(Review), new { batchId });
    }

    [HttpGet]
    public async Task<IActionResult> Review(int batchId)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "MDRImport", "View"))
            return Forbid();

        ViewData["Title"] = "Review MDR Import";
        ViewData["Section"] = "Engineering Management";

        var batch = await _importService.GetBatchAsync(batchId);
        if (batch == null) return NotFound();

        return View(batch);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleRow(int rowId, bool include, int batchId)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "MDRImport", "Create"))
            return Forbid();

        await _importService.SetRowIncludeAsync(rowId, include);
        return RedirectToAction(nameof(Review), new { batchId });
    }

    // طبق بند ۹ ممیزی: پرریسک‌ترین اکشن این Pipeline - داده‌ی معتبر EngineeringDocument/
    // DocumentRevision/DocumentWorkflow می‌سازد. با همون الگوی از‌قبل‌موجود روی
    // VDRImportController.Commit (RequirePermission("VDR","Approve")) گیت شد.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Commit(int batchId)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "MDRImport", "Approve"))
            return Forbid();

        var (created, updated, skipped) = await _importService.CommitBatchAsync(batchId);

        TempData["Message"] = $"MDR Import committed: {created} created, {updated} updated, {skipped} skipped.";

        return RedirectToAction("Index", "DocumentRegister");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Discard(int batchId)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "MDRImport", "Delete"))
            return Forbid();

        await _importService.DiscardBatchAsync(batchId);
        TempData["Message"] = "MDR import batch discarded.";
        return RedirectToAction(nameof(Index));
    }

    // طبق باگ واقعی گزارش‌شده: مدارک قبلاً ایمپورت‌شده با دیسیپلین خالی (تطبیق دقیق شکست
    // خورده بود) را با الگوریتم قوی‌تر جدید دوباره تطبیق می‌دهد. این دو اکشن هم مثل Commit
    // داده‌ی معتبر قبلاً کامیت‌شده را تغییر می‌دهند - همون سطح مجوز (Approve) لازم است.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RematchDisciplines(int? projectId)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "MDRImport", "Approve"))
            return Forbid();

        var fixedCount = await _importService.RematchDisciplinesAsync(projectId);

        TempData["Message"] = fixedCount > 0
            ? $"Discipline matched for {fixedCount} previously imported document(s)."
            : "No documents needed discipline re-matching.";

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> FixLegacyStatuses(int? projectId)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "MDRImport", "Approve"))
            return Forbid();

        var fixedCount = await _importService.FixLegacyStatusValuesAsync(projectId);

        TempData["Message"] = fixedCount > 0
            ? $"Status corrected for {fixedCount} previously imported document(s)."
            : "No documents needed a status fix.";

        return RedirectToAction(nameof(Index));
    }

    private async Task<int?> GetCurrentEmployeeIdAsync()
    {
        if (!_currentUser.UserId.HasValue) return null;

        return await _context.Users
            .Where(u => u.Id == _currentUser.UserId.Value)
            .Select(u => u.EmployeeId)
            .FirstOrDefaultAsync();
    }
}
