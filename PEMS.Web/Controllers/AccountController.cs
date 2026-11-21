using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using PEMS.Application.Common;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Identity;
using Microsoft.Extensions.Localization;
using PEMS.Web.Models.Account;
using PEMS.Web.Resources;

namespace PEMS.Web.Controllers;

// طبق CMMS Production Baseline v1 بند P0-03: قبلاً [AllowAnonymous] روی کل کلاس بود، که طبق
// ASP0026 باعث می‌شد [Authorize] روی ChangePassword عملاً بی‌اثر باشه (AllowAnonymous در سطح
// کلاس همیشه برنده‌ی Authorize در سطح Action است، نه برعکس). حالا [AllowAnonymous] فقط روی
// اکشن‌هایی است که واقعاً باید بدون ورود در دسترس باشند.
public class AccountController : Controller
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<AccountController> _logger;
    private readonly IWebAuthnService _webAuthnService;
    private readonly IStringLocalizer<SharedResource> _t;


    public AccountController(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        ILogger<AccountController> logger,
        IWebAuthnService webAuthnService,
        IStringLocalizer<SharedResource> localizer)
    {
        _t = localizer;
        _signInManager = signInManager;
        _userManager = userManager;
        _logger = logger;
        _webAuthnService = webAuthnService;
    }

    // طبق سند PEMS-SEC-PERMIT-AUTH-001 بند ۲: صفحه‌ی ثبت/مدیریت Passkey - پیش‌نیاز Step-up
    // Authentication روی اقدامات حساس Permit.
    [HttpGet]
    [Authorize]
    public async Task<IActionResult> ManagePasskeys()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return RedirectToAction(nameof(Login));

        var credentials = await _webAuthnService.GetCredentialsAsync(user.Id);
        return View(credentials);
    }



    [HttpGet]
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Index", "Home");

        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }



    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var normalizedUserName = model.UserName?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedUserName))
        {
            _logger.LogWarning("Failed login attempt with empty username.");
            ModelState.AddModelError(string.Empty, _t["Login_Err_Invalid"]);
            return View(model);
        }

        var user = await _userManager.FindByNameAsync(normalizedUserName)
            ?? await _userManager.FindByEmailAsync(normalizedUserName);

        if (user == null || !user.IsActive)
        {
            _logger.LogWarning(
                "Failed login attempt for unknown or inactive user name {UserName}",
                normalizedUserName);

            ModelState.AddModelError(
                string.Empty,
                _t["Login_Err_Invalid"]);

            return View(model);
        }

        // SCRUM-354: رمز موقتِ ریست حساب مدیر ۲۴ ساعت اعتبار دارد. بعد از آن ورود با همان پیام عمومی رد می‌شود
        // (تفاوتی بین «رمز غلط» و «رمز موقت منقضی» به بیرون نشان داده نمی‌شود) و مدیر دیگری باید دوباره ریست کند.
        if (AdminAccountRules.IsTemporaryPasswordExpired(user.MustChangePassword, user.TemporaryPasswordExpiresAt, DateTime.UtcNow))
        {
            _logger.LogWarning("Login refused for {UserName}: temporary password expired.", user.UserName);

            ModelState.AddModelError(string.Empty, _t["Login_Err_Invalid"]);

            return View(model);
        }

        var result = await _signInManager.PasswordSignInAsync(
            user.UserName!,
            model.Password,
            model.RememberMe,
            lockoutOnFailure: true);

        if (result.IsLockedOut)
        {
            var lockoutEnd = await _userManager.GetLockoutEndDateAsync(user);
            var minutesRemaining = lockoutEnd.HasValue
                ? Math.Max(1, (int)Math.Ceiling((lockoutEnd.Value - DateTimeOffset.UtcNow).TotalMinutes))
                : 0;

            _logger.LogWarning(
                "Locked-out account {UserName} attempted login. Lockout ends at {LockoutEndUtc}.",
                user.UserName,
                lockoutEnd);

            ModelState.AddModelError(
                string.Empty,
                minutesRemaining > 0
                    ? _t["Login_Err_LockedMinutes", minutesRemaining]
                    : _t["Login_Err_Locked"]);

            return View(model);
        }

        if (!result.Succeeded)
        {
            var failedCount = await _userManager.GetAccessFailedCountAsync(user);
            _logger.LogWarning(
                "Failed login attempt for {UserName}. Failed count: {FailedCount}.",
                user.UserName,
                failedCount);

            ModelState.AddModelError(
                string.Empty,
                _t["Login_Err_Invalid"]);

            return View(model);
        }

        user.LastLoginDate = DateTime.Now;

        await _userManager.UpdateAsync(user);

        _logger.LogInformation("کاربر {UserName} وارد سیستم شد", user.UserName);


        // طبق درخواست: کاربرانی که با رمز پیش‌فرض ساخته شدن (مثلاً هنگام ساخت خودکار از
        // روی Employee) باید همین اولین بار مجبور به تغییر رمز بشن، قبل از هر صفحه‌ی دیگه
        if (user.MustChangePassword)
            return RedirectToAction(nameof(ChangePassword));


        if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
            return Redirect(model.ReturnUrl);


        return RedirectToAction("Index", "Home");
    }



    [HttpGet]
    [Authorize]
    public IActionResult ChangePassword()
    {
        return View(new PEMS.Web.Models.Account.ChangePasswordViewModel());
    }


    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(PEMS.Web.Models.Account.ChangePasswordViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var user = await _userManager.GetUserAsync(User);
        if (user == null) return RedirectToAction(nameof(Login));

        // اگه مجبور به تغییر رمز هست (رمز پیش‌فرض)، رمز فعلی رو نمی‌دونه لازم باشه بپرسیم؟
        // چرا، هنوز باید رمز فعلی (همون پیش‌فرض) رو وارد کنه - امنیتی درسته حتی برای رمز پیش‌فرض
        var result = await _userManager.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword);

        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);

            return View(model);
        }

        user.MustChangePassword = false;
        user.TemporaryPasswordExpiresAt = null;
        await _userManager.UpdateAsync(user);

        await _signInManager.RefreshSignInAsync(user);

        TempData["Message"] = _t["Password changed successfully."].Value;
        return RedirectToAction("Index", "Home");
    }



    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();

        return RedirectToAction(nameof(Login));
    }



    [HttpGet]
    [AllowAnonymous]
    public IActionResult AccessDenied()
    {
        return View();
    }



    [HttpGet]
    [AllowAnonymous]
    public IActionResult ForgotPassword()
    {
        return View(new ForgotPasswordViewModel());
    }



    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public IActionResult ForgotPassword(ForgotPasswordViewModel model)
    {
        // در فاز فعلی (Framework)، ارسال ایمیل واقعی پیاده‌سازی نشده است.
        // پیام عمومی نمایش داده می‌شود تا کاربر بداند باید با مدیر سیستم تماس بگیرد؛
        // در فاز بعدی می‌توان سرویس ارسال ایمیل واقعی (SMTP/SendGrid و ...) اضافه کرد.

        ViewBag.Submitted = true;

        return View(model);
    }
}
