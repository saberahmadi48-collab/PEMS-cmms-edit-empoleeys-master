using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;
using PEMS.Web.Resources;
using PEMS.Web.Security;
using PEMS.Web.ViewModels;

namespace PEMS.Web.Controllers;

/// <summary>
/// نگهداری کاتالوگ ISO 14224 (حالت خرابی ← علل) به‌ازای کلاس تجهیز. منبع واحد برای فرم استراتژی و تکمیل
/// Work Order. مجوز: ماژول MaintenanceMasterData (View/Create/Edit)؛ همه‌ی POST ها Antiforgery دارند.
/// </summary>
public class FailureModeCatalogController : Controller
{
    private const string Module = "MaintenanceMasterData";

    private readonly PEMSDbContext _context;
    private readonly IFailureModeCatalogService _catalog;
    private readonly ICurrentUserService _currentUser;
    private readonly IPermissionService _permissionService;
    private readonly IStringLocalizer<SharedResource>? _l;

    public FailureModeCatalogController(PEMSDbContext context, IFailureModeCatalogService catalog,
        ICurrentUserService currentUser, IPermissionService permissionService, IStringLocalizer<SharedResource>? localizer = null)
    {
        _context = context;
        _catalog = catalog;
        _currentUser = currentUser;
        _permissionService = permissionService;
        _l = localizer;
    }

    private Task<bool> Can(string action) => _permissionService.HasPermissionAsync(_currentUser.Roles, Module, action);

    public async Task<IActionResult> Index(int? classId, string? q)
    {
        if (!await Can("View")) return Forbid();

        ViewData["Title"] = "Failure Mode Catalog";
        ViewData["Section"] = "CMMS Integration";
        ViewBag.Classes = await ClassesAsync();
        ViewBag.ClassId = classId;
        ViewBag.Query = q;
        ViewBag.CanEdit = await Can("Edit");
        return View(await _catalog.SearchAsync(classId, q, 200));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int? id)
    {
        if (!await Can(id.HasValue ? "Edit" : "Create")) return Forbid();

        FailureModeEditDto? dto = id.HasValue ? await _catalog.GetAsync(id.Value) : new FailureModeEditDto();
        if (dto == null) return NotFound();
        return await FormAsync(dto, new List<string>());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(FailureModeEditDto dto)
    {
        if (!await Can(dto.FailureModeId.HasValue ? "Edit" : "Create")) return Forbid();

        var result = await _catalog.SaveAsync(dto);
        if (result.Success)
        {
            TempData["Message"] = T("Rcm_Info_CatalogSaved");
            return RedirectToAction(nameof(Index), new { classId = dto.EquipmentClassId });
        }

        Response.StatusCode = StatusCodes.Status400BadRequest;
        return await FormAsync(dto, _l.TranslateAll(result.Errors));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(int id, bool active)
    {
        if (!await Can("Edit")) return Forbid();

        var result = await _catalog.SetActiveAsync(id, active);
        if (!result.Success) return NotFound();
        TempData["Message"] = T("Rcm_Info_CatalogSaved");
        return RedirectToAction(nameof(Index));
    }

    private async Task<IActionResult> FormAsync(FailureModeEditDto dto, List<string> errors)
    {
        ViewData["Title"] = "Failure Mode Catalog";
        ViewData["Section"] = "CMMS Integration";
        ViewBag.Classes = await ClassesAsync();
        ViewBag.Causes = await _context.FailureCauses.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.Name)
            .Select(c => new LookupItem { Id = c.FailureCauseId, Code = c.Code, Text = c.Name }).ToListAsync();
        ViewBag.Errors = errors;
        return View("Edit", dto);
    }

    private Task<List<LookupItem>> ClassesAsync()
        => _context.EquipmentClasses.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.Name)
            .Select(c => new LookupItem { Id = c.EquipmentClassId, Text = c.Name, Code = c.Code }).ToListAsync();

    private string T(string key) => _l == null ? key : _l[key].Value;
}
