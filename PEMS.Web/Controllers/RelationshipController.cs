using Microsoft.AspNetCore.Mvc;
using PEMS.Application.Interfaces;
using PEMS.Web.Security;

namespace PEMS.Web.Controllers;

/// <summary>
/// AI Relationship Engine UI: کاوش زنجیره‌ی روابط مهندسی از یک تجهیز، مدرک یا Vendor -
/// طبق مثال سند: Pump P-101 → Datasheet → Vendor → P&ID → ... → CMMS Asset.
/// </summary>
/// <remarks>
/// طبق «PEMS — P0 AI Pipeline Endpoint Hardening»: این Controller قبلاً هیچ بررسی مجوزی نداشت -
/// ConfirmSuggestedLink/BulkConfirm (که رابطه‌ی واقعی/FK بین Equipment و MR/VDR برقرار می‌کنند)
/// برای هر کاربر احرازهویت‌شده‌ای باز بودند. ماژول جدید "Relationship" چون هیچ ماژول موجودی این
/// قابلیت (تایید رابطه‌ی پیشنهادی بین چند نوع موجودیت مهندسی) را پوشش نمی‌دهد.
/// </remarks>
public class RelationshipController : Controller
{
    private readonly IRelationshipService _service;
    private readonly ICurrentUserService _currentUser;
    private readonly IPermissionService _permissionService;

    public RelationshipController(IRelationshipService service, ICurrentUserService currentUser,
        IPermissionService permissionService)
    {
        _service = service;
        _currentUser = currentUser;
        _permissionService = permissionService;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? q)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "Relationship", "View"))
            return Forbid();

        ViewData["Title"] = "Engineering Relationship Explorer";
        ViewData["Section"] = "AI Engineering Intelligence";

        ViewBag.Query = q;

        return View();
    }

    [HttpGet]
    public async Task<IActionResult> Search(string q)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "Relationship", "View"))
            return Forbid();

        ViewData["Title"] = "Engineering Relationship Explorer";
        ViewData["Section"] = "AI Engineering Intelligence";

        ViewBag.Query = q;

        var result = string.IsNullOrWhiteSpace(q) ? null : await _service.ExploreByFreeTextAsync(q);

        return View("Index", result);
    }

    public async Task<IActionResult> Equipment(int id)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "Relationship", "View"))
            return Forbid();

        ViewData["Title"] = "Engineering Relationship Explorer";
        ViewData["Section"] = "AI Engineering Intelligence";

        var result = await _service.ExploreFromEquipmentAsync(id);
        if (result == null) return NotFound();

        return View("Index", result);
    }

    public async Task<IActionResult> Document(int id)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "Relationship", "View"))
            return Forbid();

        ViewData["Title"] = "Engineering Relationship Explorer";
        ViewData["Section"] = "AI Engineering Intelligence";

        var result = await _service.ExploreFromDocumentAsync(id);
        if (result == null) return NotFound();

        return View("Index", result);
    }

    public async Task<IActionResult> Vendor(int id)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "Relationship", "View"))
            return Forbid();

        ViewData["Title"] = "Engineering Relationship Explorer";
        ViewData["Section"] = "AI Engineering Intelligence";

        var result = await _service.ExploreFromVendorAsync(id);
        if (result == null) return NotFound();

        return View("Index", result);
    }

    // طبق ممیزی نهایی Ownership/Traceability (§16): زنجیره‌ی Package→Tender→Bid→Contract→NTP→
    // Kickoff→ITP باید از خود Package هم قابل کاوش باشد - نه فقط از Vendor
    public async Task<IActionResult> ProcurementPackage(int id)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "Relationship", "View"))
            return Forbid();

        ViewData["Title"] = "Engineering Relationship Explorer";
        ViewData["Section"] = "AI Engineering Intelligence";

        var result = await _service.ExploreFromProcurementPackageAsync(id);
        if (result == null) return NotFound();

        return View("Index", result);
    }

    public async Task<IActionResult> Contract(int id)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "Relationship", "View"))
            return Forbid();

        ViewData["Title"] = "Engineering Relationship Explorer";
        ViewData["Section"] = "AI Engineering Intelligence";

        var result = await _service.ExploreFromContractAsync(id);
        if (result == null) return NotFound();

        return View("Index", result);
    }


    // طبق ممیزی: پرریسک‌ترین اکشن این Controller - رابطه‌ی واقعی/FK بین Equipment و MR/VDR
    // برقرار می‌کند. نیازمند "Approve".
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmSuggestedLink(int equipmentId, string targetType, int targetId, bool force = false)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "Relationship", "Approve"))
            return Forbid();

        try
        {
            await _service.ConfirmSuggestedLinkAsync(equipmentId, targetType, targetId, force);
            TempData["Message"] = "اتصال تایید و برقرار شد.";
        }
        catch (Exception ex)
        {
            // طبق ممیزی بند ۱.۴: پیام خطا شامل راهنمایی برای تایید مجدد با force=true است
            TempData["Error"] = ex.Message;
            TempData["ConflictEquipmentId"] = equipmentId;
            TempData["ConflictTargetType"] = targetType;
            TempData["ConflictTargetId"] = targetId;
        }

        return RedirectToAction(nameof(Equipment), new { id = equipmentId });
    }

    // طبق ممیزی بند ۲.۱: گزارش تحلیل شکاف برای برنامه‌ریزی
    public async Task<IActionResult> GapReport(int? projectId)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "Relationship", "View"))
            return Forbid();

        ViewData["Title"] = "Relationship Gap Report";
        ViewData["Section"] = "AI Engineering Intelligence";

        var report = await _service.GetGapReportAsync(projectId);
        return View(report);
    }

    // طبق درخواست: تایید دسته‌جمعی پیشنهادهای اتصال - چون تعداد تجهیزات زیاد است
    public async Task<IActionResult> BulkSuggestions(int? projectId)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "Relationship", "View"))
            return Forbid();

        ViewData["Title"] = "Bulk Approve Bindings";
        ViewData["Section"] = "AI Engineering Intelligence";

        var items = await _service.GetAllSuggestedLinksAsync(projectId);
        return View(items);
    }

    // طبق ممیزی بند ۱۲/۱۹: بررسی مجوز باید پیش از هر پردازش/نوشتنی رخ دهد - حتی برای Bulk.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkConfirm(List<string> selectedItems)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "Relationship", "Approve"))
            return Forbid();

        if (selectedItems == null || selectedItems.Count == 0)
        {
            TempData["Error"] = "هیچ موردی انتخاب نشده.";
            return RedirectToAction(nameof(BulkSuggestions));
        }

        // هر مورد به‌صورت "equipmentId|targetType|targetId" فرستاده می‌شه (طبق فرمت چک‌باکس‌های View)
        var parsed = new List<(int EquipmentId, string TargetType, int TargetId)>();
        foreach (var raw in selectedItems)
        {
            var parts = raw.Split('|');
            if (parts.Length == 3 && int.TryParse(parts[0], out var eqId) && int.TryParse(parts[2], out var targetId))
                parsed.Add((eqId, parts[1], targetId));
        }

        var (confirmed, errors) = await _service.BulkConfirmAsync(parsed);

        TempData["Message"] = $"{confirmed} مورد تایید و برقرار شد.";
        if (errors.Any())
            TempData["Error"] = $"{errors.Count} مورد خطا داشت: " + string.Join(" | ", errors.Take(5));

        return RedirectToAction(nameof(BulkSuggestions));
    }
}
