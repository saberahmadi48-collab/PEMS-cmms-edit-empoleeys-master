using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;
using PEMS.Application.Common;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Web.Models.Account;
using PEMS.Web.Resources;
using PEMS.Web.Security;

namespace PEMS.Web.Controllers;

/// <summary>
/// SCRUM-356 / ۳۵۳.۳: بازیابی break-glass روی سرور. صفحه فقط وقتی پاسخ می‌دهد که فایل درخواست (کمتر از ۱۵ دقیقه) در پوشه‌ی
/// تعریف‌شده باشد - یا مسیر توکن فروشنده صریحاً روشن باشد - و درخواست از localhost یا شبکه‌ی مدیریتی بیاید؛ در غیر این صورت 404.
/// </summary>
[AllowAnonymous]
[EnableRateLimiting(RecoveryRateLimiting.PolicyName)]
[Route("break-glass")]
public class BreakGlassController : Controller
{
    private readonly IBreakGlassService _breakGlass;
    private readonly BreakGlassOptions _options;
    private readonly IStringLocalizer<SharedResource> _localizer;

    public BreakGlassController(IBreakGlassService breakGlass, BreakGlassOptions options, IStringLocalizer<SharedResource> localizer)
    {
        _breakGlass = breakGlass;
        _options = options;
        _localizer = localizer;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        if (!BreakGlassAccess.IsAllowed(HttpContext, _options)) return NotFound();
        var window = _breakGlass.GetWindow();
        if (!window.IsReachable) return NotFound();

        return View(await Model(window, null, ct));
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(BreakGlassViewModel input, CancellationToken ct)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        if (!BreakGlassAccess.IsAllowed(HttpContext, _options))
        {
            await _breakGlass.RecordBlockedNetworkAsync(ip, ct);
            return NotFound();
        }
        var window = _breakGlass.GetWindow();
        if (!window.IsReachable) return NotFound();
        if (input.TargetUserId is not int targetId)
            return View(await Model(window, _localizer["Choose the administrator account to reset."], ct));

        var result = await _breakGlass.ResetAsync(targetId, window.IsOpen ? null : input.VendorToken, ip, ct);
        if (result.Succeeded)
        {
            Response.Headers.CacheControl = "no-store";
            Response.Headers.Pragma = "no-cache";
            return View("Result", result);
        }

        // پنجره ممکن است با همین درخواست بسته شده باشد (مصرف یا انقضا)
        window = _breakGlass.GetWindow();
        if (!window.IsReachable) return NotFound();
        return View(await Model(window, ErrorText(result.Status), ct));
    }

    private async Task<BreakGlassViewModel> Model(BreakGlassWindow window, string? error, CancellationToken ct) => new()
    {
        Window = window,
        Admins = await _breakGlass.GetAdminsAsync(ct),
        Error = error
    };

    private string ErrorText(BreakGlassResetStatus status) => status switch
    {
        BreakGlassResetStatus.TokenInvalid => _localizer["The vendor recovery token is not valid for this server, has expired or was already used."],
        BreakGlassResetStatus.TargetNotFound or BreakGlassResetStatus.TargetNotAdmin => _localizer["Only an active administrator account can be reset here."],
        BreakGlassResetStatus.AuditFailed => _localizer["The password was changed but recording or session revocation failed. No temporary password is shown; place a new request file and run the reset again."],
        BreakGlassResetStatus.WindowClosed => _localizer["The recovery window is closed. Place a new request file on the server."],
        _ => _localizer["The reset failed. Nothing was shown; try again."]
    };
}
