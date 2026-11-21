using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using PEMS.Application.Interfaces;
using PEMS.Web.Resources;
using PEMS.Web.Security;

namespace PEMS.Web.Controllers;

/// <summary>
/// مدیریت برچسب‌های دلخواه (طبق نمونه‌ی کارتابل مرجع Pargar/Fonix) - قابل ساخت توسط کاربران،
/// نه فقط میانبرهای دیسیپلین ثابت.
/// </summary>
public class TagController : Controller
{
    private readonly ITagService _service;
    private readonly ICurrentUserService _currentUser;
    private readonly IStringLocalizer<SharedResource>? _localizer;

    public TagController(ITagService service, ICurrentUserService currentUser, IStringLocalizer<SharedResource>? localizer = null)
    {
        _service = service;
        _currentUser = currentUser;
        _localizer = localizer;
    }

    public async Task<IActionResult> Index()
    {
        ViewData["Title"] = "Tags";
        ViewData["Section"] = "Correspondence Management";

        var tags = await _service.GetAllAsync();
        return View(tags);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string name, string color, string? returnUrl)
    {
        await _service.CreateAsync(name, color, _currentUser.UserId);
        return string.IsNullOrEmpty(returnUrl) ? RedirectToAction(nameof(Index)) : LocalRedirect(returnUrl);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int tagId)
    {
        // SCRUM-550: a refused delete says why (it used to reload the page with the tag still there and no message)
        var outcome = await _service.DeleteAsync(tagId, _currentUser.UserId);
        var key = outcome switch
        {
            TagDeleteOutcome.Deleted => "Tg_Deleted",
            TagDeleteOutcome.HasSubTags => "Tg_Err_HasSubTags",
            TagDeleteOutcome.NotOwner => "Tg_Err_NotOwner",
            _ => "Tg_Err_NotFound"
        };
        TempData[outcome == TagDeleteOutcome.Deleted ? "Message" : "Error"] = _localizer?[key].Value ?? key;
        return RedirectToAction(nameof(Index));
    }
}
