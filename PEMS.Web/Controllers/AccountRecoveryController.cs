using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Web.Models.Account;
using PEMS.Web.Resources;
using PEMS.Web.Security;

namespace PEMS.Web.Controllers;

/// <summary>
/// SCRUM-355 / ۳۵۳.۲: بازیابی حساب مدیر با کد یکبارمصرف، بدون ورود. نرخ درخواست برای هر IP محدود است و کد نادرست
/// شمارنده‌ی قفل حساب را بالا می‌برد؛ پیام خطا عمومی است تا وجود حساب یا درستی کد لو نرود.
/// </summary>
[AllowAnonymous]
[EnableRateLimiting(RecoveryRateLimiting.PolicyName)]
public class AccountRecoveryController : Controller
{
    private readonly IAdminRecoveryCodeService _codes;
    private readonly IStringLocalizer<SharedResource> _localizer;

    public AccountRecoveryController(IAdminRecoveryCodeService codes, IStringLocalizer<SharedResource> localizer)
    {
        _codes = codes;
        _localizer = localizer;
    }

    [HttpGet]
    public IActionResult Index() => View(new RecoveryCodeRedeemViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(RecoveryCodeRedeemViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(Cleared(model));

        var result = await _codes.RedeemAsync(new RecoveryCodeRedeemRequest(
            model.UserName, model.Code, model.NewPassword, HttpContext.Connection.RemoteIpAddress?.ToString()), ct);

        switch (result.Status)
        {
            case RecoveryCodeRedeemStatus.Success:
                Response.Headers.CacheControl = "no-store";
                return View("Done", result);
            case RecoveryCodeRedeemStatus.PasswordRejected:
                foreach (var error in result.PasswordErrors) ModelState.AddModelError(nameof(model.NewPassword), error);
                break;
            case RecoveryCodeRedeemStatus.LockedOut:
                ModelState.AddModelError(string.Empty, _localizer["Too many failed attempts. The account is locked for a while; try again later."]);
                break;
            default:
                ModelState.AddModelError(string.Empty, _localizer["The user name or recovery code is not valid."]);
                break;
        }
        return View(Cleared(model));
    }

    // کد و رمز هرگز به فرم برنمی‌گردند
    private static RecoveryCodeRedeemViewModel Cleared(RecoveryCodeRedeemViewModel model) => new() { UserName = model.UserName };
}
