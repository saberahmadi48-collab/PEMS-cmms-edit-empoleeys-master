using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using PEMS.Application.Common;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Identity;
using PEMS.Web.Models.Account;
using PEMS.Web.Resources;

namespace PEMS.Web.Controllers;

/// <summary>
/// SCRUM-355: کدهای بازیابی یکبارمصرف در پروفایل مدیر - تعداد باقی‌مانده و تولید مجموعه‌ی جدید (با رمز فعلی).
/// کدها فقط در پاسخ همان POST نمایش داده می‌شوند (no-store) و پس از آن قابل دیدن نیستند.
/// </summary>
[Authorize(Roles = SystemRoles.SystemAdmin)]
public class RecoveryCodesController : Controller
{
    private readonly IAdminRecoveryCodeService _codes;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IStringLocalizer<SharedResource> _localizer;

    public RecoveryCodesController(IAdminRecoveryCodeService codes, UserManager<ApplicationUser> userManager, IStringLocalizer<SharedResource> localizer)
    {
        _codes = codes;
        _userManager = userManager;
        _localizer = localizer;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        if (!int.TryParse(_userManager.GetUserId(User), out var userId)) return Forbid();
        ViewData["Title"] = "Recovery codes";
        return View(new RecoveryCodesViewModel { Remaining = await _codes.GetRemainingCountAsync(userId, ct) });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Generate(RecoveryCodesViewModel model, CancellationToken ct)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Forbid();
        ViewData["Title"] = "Recovery codes";

        // تولید مجموعه‌ی جدید کدهای قبلی را باطل می‌کند؛ با نشست رهاشده نباید ممکن باشد
        if (!ModelState.IsValid || !await _userManager.CheckPasswordAsync(user, model.CurrentPassword))
        {
            ModelState.AddModelError(nameof(model.CurrentPassword), _localizer["The current password is not correct."]);
            return View("Index", new RecoveryCodesViewModel { Remaining = await _codes.GetRemainingCountAsync(user.Id, ct) });
        }

        var result = await _codes.GenerateAsync(user.Id, ct);
        if (!result.Succeeded) return Forbid();

        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";
        return View("Codes", result);
    }
}
