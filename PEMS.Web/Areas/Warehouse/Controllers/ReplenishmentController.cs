using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.Common;
using PEMS.Application.Interfaces;
using PEMS.Application.Replenishment;
using PEMS.Persistence.Context;
using PEMS.Web.Security;

namespace PEMS.Web.Areas.Warehouse.Controllers;

/// <summary>
/// Phase 2 / P5 - توصیه‌های تأمین (اجرای دستی). Controller نازک است: مجوز و منطق در IReplenishmentRecommendationService.
/// View = مشاهده؛ Create = اجرای محاسبه؛ Edit = رد/بازگشایی. اقدام‌های تغییردهنده از ProtectedActionGuard (Fail-Closed + ثبت ردها) می‌گذرند.
/// این صفحه هیچ MR/PO/رزرو/موجودی را نمی‌سازد و تغییر نمی‌دهد؛ تبدیل به MR/بسته‌ی تدارکات (P6) وجود ندارد.
/// </summary>
[Area("Warehouse")]
[RequireReplenishmentAvailable]
public class ReplenishmentController : Controller
{
    private const string Module = IReplenishmentRecommendationService.Module;

    private readonly IReplenishmentRecommendationService _service;
    private readonly IPermissionService _permissions;
    private readonly IAccessControlService _access;
    private readonly ICurrentUserService _currentUser;
    private readonly PEMSDbContext _context;

    public ReplenishmentController(IReplenishmentRecommendationService service, IPermissionService permissions,
        IAccessControlService access, ICurrentUserService currentUser, PEMSDbContext context)
    {
        _service = service;
        _permissions = permissions;
        _access = access;
        _currentUser = currentUser;
        _context = context;
    }

    public async Task<IActionResult> Index(string? status, string? q, bool definitiveOnly = false, int page = 1, int? pageSize = null)
    {
        var actor = await BuildActorAsync();
        var list = await _service.GetPagedAsync(new ReplenishmentFilter(status, q, definitiveOnly ? true : null), page < 1 ? 1 : page, DataGridPageSize.Normalize(pageSize), actor);
        if (!list.Authorized) return Forbid();

        var last = await _service.GetLastRunAsync(actor);
        ViewData["Title"] = "Replenishment Recommendations";
        ViewData["Section"] = "Inventory & Materials";
        ViewBag.SelectedStatus = status;
        ViewBag.Search = q;
        ViewBag.DefinitiveOnly = definitiveOnly;
        ViewBag.LastRun = last.Data;
        // تقاضای مواد Work Order بدون انبار (RequiresWarehouseAttribution) جدا و برجسته نمایش داده می‌شود؛ هرگز با استنتاج به انبار نسبت داده نمی‌شود
        ViewBag.AttributionEntries = (IReadOnlyList<string>)(last.Data?.NotDefinitiveSummary.Where(IsAttributionEntry).ToList() ?? new List<string>());
        ViewBag.CanRun = await _permissions.HasPermissionAsync(_currentUser.Roles, Module, "Create");
        ViewBag.RequestKey = Guid.NewGuid().ToString("N");     // یک کلید به‌ازای هر نمایش فرم: ارسال تکراری همان فرم Idempotent است
        return View(list.Data);
    }

    public async Task<IActionResult> Details(int id)
    {
        var actor = await BuildActorAsync();
        var details = await _service.GetDetailsAsync(id, actor);
        if (!details.Authorized) return Forbid();
        if (details.Data == null) return NotFound();

        ViewData["Title"] = "Replenishment Recommendation";
        ViewData["Section"] = "Inventory & Materials";
        ViewBag.CanReview = await _permissions.HasPermissionAsync(_currentUser.Roles, Module, "Edit");
        return View(details.Data);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Run(string? requestKey)
    {
        if (await EvaluateAsync("Create", "run") != ProtectedActionOutcome.Allowed) return Forbid();

        var result = await _service.RunAsync(new ReplenishmentRunRequest(null, null, requestKey), await BuildActorAsync());
        if (result.Forbidden) return Forbid();

        if (!result.Success) TempData["Error"] = result.Error;
        else TempData["Message"] = result.Replayed
            ? $"این اجرا قبلاً انجام شده بود (اجرای {result.RunId}) و دوباره اجرا نشد."
            : $"اجرا کامل شد: {result.Created} جدید، {result.Updated} به‌روز، {result.Unchanged} بدون تغییر، {result.Closed} بسته، {result.AutoReopened} بازگشایی خودکار، {result.NotDefinitive} غیرقطعی (توصیه نشد).";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Dismiss(int id, string? rowVersion, string? reason)
    {
        if (await EvaluateAsync("Edit", id.ToString()) != ProtectedActionOutcome.Allowed) return Forbid();

        var result = await _service.DismissAsync(id, DecodeVersion(rowVersion), reason ?? "", await BuildActorAsync());
        return Finish(result, id, "توصیه رد شد.");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reopen(int id, string? rowVersion, string? note)
    {
        if (await EvaluateAsync("Edit", id.ToString()) != ProtectedActionOutcome.Allowed) return Forbid();

        var result = await _service.ReopenAsync(id, DecodeVersion(rowVersion), note, await BuildActorAsync());
        return Finish(result, id, "توصیه دوباره باز شد.");
    }

    private static bool IsAttributionEntry(string entry) => entry.Contains(ReplenishmentRules.RequiresWarehouseAttribution, StringComparison.Ordinal);

    private IActionResult Finish(ReplenishmentCommandResult result, int id, string okMessage)
    {
        if (result.Forbidden) return Forbid();
        if (result.NotFound) return NotFound();
        TempData[result.Success ? "Message" : "Error"] = result.Success ? okMessage : result.Error;
        return RedirectToAction(nameof(Details), new { id });
    }

    private Task<ProtectedActionOutcome> EvaluateAsync(string action, string resourceId) =>
        ProtectedActionGuard.EvaluateAsync(HttpContext, _permissions, _access, _currentUser, Module, action, resourceId);

    private static byte[]? DecodeVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try { return Convert.FromBase64String(value); } catch (FormatException) { return null; }
    }

    private async Task<ReplenishmentActor> BuildActorAsync()
    {
        int? employeeId = null;
        if (_currentUser.UserId.HasValue)
            employeeId = await _context.Users.Where(u => u.Id == _currentUser.UserId.Value).Select(u => u.EmployeeId).FirstOrDefaultAsync();
        return new ReplenishmentActor(_currentUser.UserId, employeeId, (_currentUser.Roles ?? new List<string>()).ToList());
    }
}
