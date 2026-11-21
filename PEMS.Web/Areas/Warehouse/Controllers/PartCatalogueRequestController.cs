using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Web.Areas.Warehouse.Models;
using PEMS.Web.Helpers;
using PEMS.Web.Resources;

namespace PEMS.Web.Areas.Warehouse.Controllers;

/// <summary>
/// درخواست کاتالوگ قطعه (Add/Change) - زیر ماژول انبار. Controller نازک است: هویت (EmployeeId + نقش‌ها) را می‌سازد و
/// به IPartCatalogueService می‌دهد؛ مجوز، مالکیت، گیرنده‌ی گام و همه‌ی قواعد در سرویس اعمال می‌شود (نه این‌جا).
/// </summary>
[Area("Warehouse")]
public class PartCatalogueRequestController : Controller
{
    private readonly IPartCatalogueService _service;
    private readonly IPartCatalogueLookupService _lookups;
    private readonly IRoleModuleActionService _actions;
    private readonly ICurrentUserContext _user;
    private readonly IStringLocalizer<SharedResource> _l;

    public PartCatalogueRequestController(IPartCatalogueService service, IPartCatalogueLookupService lookups,
        IRoleModuleActionService actions, ICurrentUserContext user, IStringLocalizer<SharedResource> l)
    {
        _service = service;
        _lookups = lookups;
        _actions = actions;
        _user = user;
        _l = l;
    }

    private PcActor? Actor => _user.EmployeeId is int emp ? new PcActor(emp, _user.UserId, _user.Roles) : null;

    private async Task<bool> CanAnyAsync(params string[] actions)
    {
        foreach (var a in actions)
            if (await _actions.HasActionAsync(_user.Roles, "PartCatalogue", a)) return true;
        return false;
    }

    private IActionResult NoEmployee()
    {
        TempData["Error"] = _l["Pc_Err_NoEmployee"].Value;
        return RedirectToAction("Index", "Home", new { area = "" });
    }

    private void Flash(PcResult r, string? okKey = null)
    {
        if (r.Success) { if (okKey != null) TempData["Message"] = _l[okKey].Value; }
        else TempData["Error"] = string.Join(" | ", PcMessages.FormatAll(_l, r.Errors));
        if (r.Warnings.Count > 0) TempData["Warning"] = string.Join(" | ", PcMessages.FormatAll(_l, r.Warnings));
    }

    // ------------------------------------------------------------------ ویرایش

    private async Task<PcEditViewModel?> BuildEditAsync(int id, PcActor actor, PartCatalogueFormDto? posted = null)
    {
        var details = await _service.GetDetailsAsync(id, actor);
        if (details == null) return null;
        if (posted != null)
        {
            // نمایش مقادیر ارسالی کاربر (ناموفق) - فقط فیلدهای قابل‌ویرایش؛ Id/نوع/RowVersion از DB می‌مانند
            posted.Id = details.Id;
            posted.RequestType = details.RequestType;
            posted.RowVersion = details.RowVersion;
            details.Form = posted;
        }

        var f = details.Form;
        var vm = new PcEditViewModel
        {
            Details = details,
            Sections = await _lookups.GetSectionsAsync(),
            Group1 = await _lookups.GetCommodityGroup1Async(f.SectionId),
            Group2 = await _lookups.GetCommodityGroup2Async(f.CommodityGroup1Id),
            ProductFamilies = await _lookups.GetProductFamiliesAsync(),
            Units = await _lookups.GetUnitsAsync(),
            Currencies = await _lookups.GetCurrenciesAsync(),
            StockGrades = await _lookups.GetStockGradesAsync(),
            PlanningMethods = await _lookups.GetPlanningMethodsAsync(),
            TypeDesignations = await _lookups.GetTypeDesignationsAsync()
        };
        return vm;
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var actor = Actor;
        if (actor == null) return NoEmployee();
        if (!await CanAnyAsync("Create")) return Forbid();

        var vm = await BuildEditAsync(id, actor);
        if (vm == null) return NotFound();
        if (!vm.Details.Actions.CanEdit) return Redirect($"/p/pc-request-details?id={id}");

        ViewData["Title"] = "Part Catalogue Request";
        ViewData["Section"] = "Warehouse";
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(PartCatalogueFormDto form, bool submitAfterSave = false)
    {
        var actor = Actor;
        if (actor == null) return NoEmployee();
        if (!await CanAnyAsync("Create")) return Forbid();

        var saved = await _service.SaveAsync(form, actor);
        if (!saved.Success)
        {
            var back = await BuildEditAsync(form.Id, actor, form);
            if (back == null) return NotFound();
            back.Errors = saved.Result.Errors;
            back.Warnings = saved.Result.Warnings;
            ViewData["Title"] = "Part Catalogue Request";
            ViewData["Section"] = "Warehouse";
            return View(back);
        }

        if (submitAfterSave)
        {
            // RowVersion بعد از ذخیره عوض شده؛ تازه می‌خوانیم تا Submit با نسخه‌ی جدید انجام شود
            var fresh = await _service.GetDetailsAsync(form.Id, actor);
            var submit = await _service.SubmitAsync(form.Id, fresh?.RowVersion, actor);
            if (!submit.Success)
            {
                var back = await BuildEditAsync(form.Id, actor);
                if (back == null) return NotFound();
                back.Errors = submit.Errors;
                back.Warnings = submit.Warnings;
                ViewData["Title"] = "Part Catalogue Request";
                ViewData["Section"] = "Warehouse";
                return View(back);
            }
            TempData["Message"] = _l["Pc_Msg_Submitted"].Value;
            return Redirect($"/p/pc-request-details?id={form.Id}");
        }

        if (saved.Result.Warnings.Count > 0) TempData["Warning"] = string.Join(" | ", PcMessages.FormatAll(_l, saved.Result.Warnings));
        TempData["Message"] = _l["Pc_Msg_Saved"].Value;
        return RedirectToAction(nameof(Edit), new { id = form.Id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GenerateNumber(int id, byte[]? rowVersion)
    {
        var actor = Actor;
        if (actor == null) return NoEmployee();
        Flash(await _service.GenerateNumberAsync(id, rowVersion, actor), "Pc_Msg_NumberGenerated");
        return RedirectToAction(nameof(Edit), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LoadPart(int id, string? existingPartNumber)
    {
        var actor = Actor;
        if (actor == null) return NoEmployee();
        Flash(await _service.LoadExistingPartAsync(id, existingPartNumber ?? string.Empty, actor), "Pc_Msg_PartLoaded");
        return RedirectToAction(nameof(Edit), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddNote(int id, string text, string? returnTo)
    {
        var actor = Actor;
        if (actor == null) return NoEmployee();
        Flash(await _service.AddNoteAsync(id, text, actor), "Pc_Msg_NoteAdded");
        return (returnTo == "edit" ? RedirectToAction(nameof(Edit), new { id }) : Redirect($"/p/pc-request-details?id={id}"));
    }

    // ------------------------------------------------------------------ تجهیز / پیوست

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddEquipment(int id, int? equipmentId, string? equipmentText, decimal installedQuantity)
    {
        var actor = Actor;
        if (actor == null) return NoEmployee();
        Flash(await _service.AddEquipmentAsync(id, equipmentId, equipmentText, installedQuantity, actor));
        return RedirectToAction(nameof(Edit), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveEquipment(int id, int lineId)
    {
        var actor = Actor;
        if (actor == null) return NoEmployee();
        Flash(await _service.RemoveEquipmentAsync(id, lineId, actor));
        return RedirectToAction(nameof(Edit), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(25_000_000)]
    [PEMS.Web.Security.Uploads.UploadPolicy(PEMS.Application.Uploads.UploadProfile.Attachment)]
    public async Task<IActionResult> UploadAttachment(int id, IFormFile? file, string? kind)
    {
        var actor = Actor;
        if (actor == null) return NoEmployee();
        if (file == null || file.Length == 0)
        {
            TempData["Error"] = string.Join(" | ", PcMessages.FormatAll(_l, new[] { new PcMessage("File", "Pc_Err_Required") }));
            return RedirectToAction(nameof(Edit), new { id });
        }

        await using var stream = file.OpenReadStream();
        Flash(await _service.UploadAttachmentAsync(id, file.FileName, file.ContentType, kind ?? "Document", stream, file.Length, actor), "Pc_Msg_AttachmentAdded");
        return RedirectToAction(nameof(Edit), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveAttachment(int id, int attachmentId)
    {
        var actor = Actor;
        if (actor == null) return NoEmployee();
        Flash(await _service.RemoveAttachmentAsync(id, attachmentId, actor));
        return RedirectToAction(nameof(Edit), new { id });
    }

    [HttpGet]
    public async Task<IActionResult> Download(int id, int attachmentId)
    {
        var actor = Actor;
        if (actor == null) return NoEmployee();
        var file = await _service.OpenAttachmentAsync(id, attachmentId, actor);
        if (file == null) return NotFound();
        return File(file.Value.Stream, file.Value.ContentType, file.Value.FileName);
    }
}
