using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.Common;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;
using PEMS.Web.Security;

namespace PEMS.Web.Controllers;

/// <summary>
/// طبق «PEMS CMMS — Equipment Functional Location Assignment &amp; Import»: همون الگوی
/// FunctionalLocationImportController (Upload → Stage → Review/Toggle → Commit/Discard) -
/// ماژول مجوز مستقل "EquipmentFunctionalLocationAssignment" (مثل EquipmentImport/
/// FunctionalLocationImport)، نه یک زیرساخت جدید.
/// </summary>
public class EquipmentFunctionalLocationAssignmentController : Controller
{
    private const string Module = "EquipmentFunctionalLocationAssignment";

    private readonly IEquipmentFunctionalLocationAssignmentService _service;
    private readonly PEMSDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly IPermissionService _permissionService;

    public EquipmentFunctionalLocationAssignmentController(IEquipmentFunctionalLocationAssignmentService service,
        PEMSDbContext context, ICurrentUserService currentUser, IPermissionService permissionService)
    {
        _service = service;
        _context = context;
        _currentUser = currentUser;
        _permissionService = permissionService;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, Module, "View"))
            return Forbid();

        ViewData["Title"] = "Equipment ↔ Functional Location Assignment";
        ViewData["Section"] = "CMMS Integration";

        var recentBatches = await _service.GetRecentBatchesAsync();
        return View(recentBatches);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [PEMS.Web.Security.Uploads.UploadPolicy(PEMS.Application.Uploads.UploadProfile.Spreadsheet)]
    public async Task<IActionResult> Upload(IFormFile file)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, Module, "Create"))
            return Forbid();

        if (file == null || file.Length == 0)
        {
            TempData["Error"] = "Please select an Equipment ↔ Functional Location assignment Excel file.";
            return RedirectToAction(nameof(Index));
        }

        using var stream = file.OpenReadStream();

        var importedByEmployeeId = await GetCurrentEmployeeIdAsync();
        var batchId = await _service.StageFromExcelAsync(stream, file.FileName, importedByEmployeeId);

        return RedirectToAction(nameof(Review), new { batchId });
    }

    [HttpGet]
    public async Task<IActionResult> Review(int batchId, int page = 1, int? pageSize = null, string? status = null)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, Module, "View"))
            return Forbid();

        ViewData["Title"] = "Review Equipment ↔ Functional Location Assignment";
        ViewData["Section"] = "CMMS Integration";

        var normalizedPageSize = DataGridPageSize.Normalize(pageSize);
        var batch = await _service.GetBatchAsync(batchId, page, normalizedPageSize, status);
        if (batch == null) return NotFound();

        ViewBag.StatusFilter = status;

        return View(batch);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleRow(int rowId, bool include, int batchId, int page = 1, string? status = null)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, Module, "Create"))
            return Forbid();

        await _service.SetRowIncludeAsync(rowId, include);
        return RedirectToAction(nameof(Review), new { batchId, page, status });
    }

    // طبق بند «Existing Assignment Protection» سند وظیفه: تنها مسیر صریح برای اجازه‌دادن به
    // جایگزینی یک تخصیص موجود - جدا از ToggleRow معمولی تا هیچ‌وقت به‌طور تصادفی فعال نشود.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReplaceExisting(int rowId, bool replace, int batchId, int page = 1, string? status = null)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, Module, "Approve"))
            return Forbid();

        await _service.SetReplaceExistingAssignmentAsync(rowId, replace);
        return RedirectToAction(nameof(Review), new { batchId, page, status });
    }

    // پرریسک‌ترین اکشن این Pipeline - رابطه‌ی Equipment.FunctionalLocationId واقعی را تغییر می‌دهد.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Commit(int batchId)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, Module, "Approve"))
            return Forbid();

        var result = await _service.CommitBatchAsync(batchId);

        TempData["Message"] = $"Equipment ↔ Functional Location assignment committed: {result.Assigned} assigned, " +
                               $"{result.Skipped} skipped, {result.Unresolved} unresolved, {result.Conflicts} conflicts, " +
                               $"{result.ValidationErrors} validation errors.";

        return RedirectToAction(nameof(Review), new { batchId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Discard(int batchId)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, Module, "Delete"))
            return Forbid();

        await _service.DiscardBatchAsync(batchId);
        TempData["Message"] = "Equipment ↔ Functional Location assignment batch discarded.";
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
