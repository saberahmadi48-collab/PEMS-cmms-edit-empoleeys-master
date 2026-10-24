using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Web.Areas.Warehouse.Models;
using PEMS.Web.Resources;

namespace PEMS.Web.Areas.Warehouse.Controllers;

/// <summary>
/// مدیریت ماژول کاتالوگ قطعه: Import مرجع‌ها، نگاشت Section→Commodity Group، گام‌های مسیر، گیرنده‌ها (Pool/CEO) و
/// زنجیره‌ی مدیر مستقیم. هر عمل در سرویس نیز با مجوز PartCatalogue.Admin بررسی می‌شود (این‌جا فقط لایه‌ی اول).
/// </summary>
[Area("Warehouse")]
public class PartCatalogueAdminController : Controller
{
    private const long MaxImportBytes = 15L * 1024 * 1024;

    private readonly IPartCatalogueAdminService _admin;
    private readonly IPartCatalogueLookupService _lookups;
    private readonly IPartCatalogueSettingsService _settings;
    private readonly IRoleModuleActionService _actions;
    private readonly ICurrentUserContext _user;
    private readonly IStringLocalizer<SharedResource> _l;

    public PartCatalogueAdminController(IPartCatalogueAdminService admin, IPartCatalogueLookupService lookups, IRoleModuleActionService actions,
        ICurrentUserContext user, IStringLocalizer<SharedResource> l, IPartCatalogueSettingsService settings)
    {
        _admin = admin;
        _lookups = lookups;
        _settings = settings;
        _actions = actions;
        _user = user;
        _l = l;
    }

    // مدیریت به Employee متصل‌بودن نیاز ندارد (مثلاً SystemAdmin بدون پرونده‌ی کارمندی)؛ مجوز Admin ملاک است.
    private PcActor Actor => new(_user.EmployeeId ?? 0, _user.UserId, _user.Roles);

    private async Task<PcActor?> AdminActorAsync()
    {
        var a = Actor;
        if (!await _actions.HasActionAsync(a.Roles, "PartCatalogue", "Admin")) return null;
        return a;
    }

    private IActionResult Back(PcResult r, string okKey = "Pc_Msg_Saved")
    {
        if (r.Success) TempData["Message"] = _l[okKey].Value;
        else TempData["Error"] = string.Join(" | ", PcMessages.FormatAll(_l, r.Errors));
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var actor = await AdminActorAsync();
        if (actor == null) return Forbid();

        var overview = await _admin.GetOverviewAsync(actor);
        if (!overview.Allowed || overview.Overview == null) return Forbid();

        ViewData["Title"] = "Part Catalogue Administration";
        ViewData["Section"] = "Warehouse";
        return View(new PcAdminViewModel { Overview = overview.Overview, Settings = await _settings.GetAsync() });
    }

    // تنظیم روش تولید شماره‌ی قطعه (MANUAL/API) و قالب آن؛ فقط ذخیره‌ی پرچم - مصرف‌کننده‌اش در فاز ۲ است.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveSettings(string? partNoGenerationMethod, string? partNoFormat, byte[]? rowVersion)
    {
        var a = await AdminActorAsync(); if (a == null) return Forbid();
        return Back(await _settings.SaveAsync(partNoGenerationMethod, partNoFormat, rowVersion, a));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(16_000_000)]
    public async Task<IActionResult> Import(string kind, IFormFile? file)
    {
        var actor = await AdminActorAsync();
        if (actor == null) return Forbid();

        if (file == null || file.Length == 0 || file.Length > MaxImportBytes
            || !string.Equals(Path.GetExtension(file.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            TempData["Error"] = _l["Pc_Err_ImportFile"].Value;
            return RedirectToAction(nameof(Index));
        }

        PcImportResult result;
        await using (var stream = file.OpenReadStream())
        {
            result = kind switch
            {
                "TechnicalClass" => await _admin.ImportTechnicalClassesAsync(stream, actor),
                "CommodityGroup" => await _admin.ImportCommodityGroupsAsync(stream, actor),
                "ProductFamily" => await _admin.ImportProductFamiliesAsync(stream, actor),
                "Manufacturer" or "Supplier" => await _admin.ImportNameListAsync(kind, stream, actor),
                _ => new PcImportResult { Kind = kind, Errors = { "Pc_Err_InvalidReference" } }
            };
        }

        if (result.Errors.Count > 0 && result.Added + result.Updated == 0)
            TempData["Error"] = string.Join(" | ", result.Errors.Take(5).Select(e => e.StartsWith("Pc_", StringComparison.Ordinal) ? _l[e].Value : e));
        else
            TempData["Message"] = string.Format(_l["Pc_Msg_Imported"].Value, result.Kind, result.Added, result.Updated, result.Unchanged, result.Skipped);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetSection(int sectionId, bool requiresSparePartsReview, bool isActive)
    {
        var a = await AdminActorAsync(); if (a == null) return Forbid();
        return Back(await _admin.SetSectionAsync(sectionId, requiresSparePartsReview, isActive, a));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddSectionGroup(int sectionId, int commodityGroupId)
    {
        var a = await AdminActorAsync(); if (a == null) return Forbid();
        return Back(await _admin.AddSectionGroupAsync(sectionId, commodityGroupId, a));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveSectionGroup(int mapId)
    {
        var a = await AdminActorAsync(); if (a == null) return Forbid();
        return Back(await _admin.RemoveSectionGroupAsync(mapId, a));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddGroupChild(int parentGroupId, int childGroupId)
    {
        var a = await AdminActorAsync(); if (a == null) return Forbid();
        return Back(await _admin.AddGroupChildAsync(parentGroupId, childGroupId, a));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveGroupChild(int mapId)
    {
        var a = await AdminActorAsync(); if (a == null) return Forbid();
        return Back(await _admin.RemoveGroupChildAsync(mapId, a));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateStep(int stepId, int order, bool isActive, string conditionKey)
    {
        var a = await AdminActorAsync(); if (a == null) return Forbid();
        return Back(await _admin.UpdateStepAsync(stepId, order, isActive, conditionKey, a));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddApprover(string stepKey, int employeeId)
    {
        var a = await AdminActorAsync(); if (a == null) return Forbid();
        return Back(await _admin.AddApproverAsync(stepKey, employeeId, a));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveApprover(int assignmentId)
    {
        var a = await AdminActorAsync(); if (a == null) return Forbid();
        return Back(await _admin.RemoveApproverAsync(assignmentId, a));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveResponsibility(PcResponsibilityInput input)
    {
        var a = await AdminActorAsync(); if (a == null) return Forbid();
        return Back(await _admin.SaveResponsibilityAsync(input, a));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddCommodityResponsible(string responsibilityKey, int commodityGroupId, int? positionId, int? employeeId, int priority = 1)
    {
        var a = await AdminActorAsync(); if (a == null) return Forbid();
        return Back(await _admin.AddCommodityResponsibleAsync(responsibilityKey, commodityGroupId, positionId, employeeId, priority, a));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveCommodityResponsible(int id)
    {
        var a = await AdminActorAsync(); if (a == null) return Forbid();
        return Back(await _admin.RemoveCommodityResponsibleAsync(id, a));
    }

    /// <summary>تشخیص: چه کسی برای این مسئولیت (و این درخواست‌دهنده/Commodity) واجد شرایط است و چه کسی/چرا حذف شده.</summary>
    [HttpGet]
    public async Task<IActionResult> Preview(string key, int requesterEmployeeId, int? commodityGroupId)
    {
        var a = await AdminActorAsync(); if (a == null) return Forbid();
        var result = await _admin.PreviewResolutionAsync(key, requesterEmployeeId, commodityGroupId, a);
        return result == null ? NotFound() : PartialView("_ResolutionPreview", result);
    }

    [HttpGet]
    public async Task<IActionResult> CommodityGroups(string? q)
    {
        if (await AdminActorAsync() == null) return Forbid();
        return Json(await _lookups.SearchCommodityGroupsAsync(q));
    }

    [HttpGet]
    public async Task<IActionResult> Employees(string? q)
    {
        if (await AdminActorAsync() == null) return Forbid();
        return Json(await _admin.SearchEmployeesAsync(q));
    }
}
