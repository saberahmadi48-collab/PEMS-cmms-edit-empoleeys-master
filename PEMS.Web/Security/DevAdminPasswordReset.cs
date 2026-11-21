using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using PEMS.Persistence.Identity;

namespace PEMS.Web.Security;

/// <summary>
/// طبق «PEMS Phase 6B Closure Validation»: پیش از این، هیچ راه قانونی برای بازیابی دسترسی
/// کاربر admin روی این PEMSDB اشتراکی توسعه وجود نداشت - IdentitySeeder فقط "اگر هیچ کاربری
/// نباشد" اجرا می‌شود (بند ۳)، و بازنشانی رمز موجود (Admin/UsersController.ResetPasswordAsync)
/// خودش نیاز به ورود قبلی به‌عنوان ادمین دارد (چرخه‌ی بسته). این کلاس دقیقاً همان "کوچک‌ترین
/// مکانیزم امن Development-only" است که سند درخواست کرده - نه یک راه دور زدن Authorization:
/// کاربر باز هم باید از فرم Login معمولی با رمز واقعی وارد شود.
///
/// دروازه‌های ایمنی (هر پنج مورد باید همزمان برقرار باشند):
///   ۱) فقط IHostEnvironment.IsDevelopment() - در Production/UAT هرگز فراخوانی محتوا اجرا نمی‌شود.
///   ۲) فقط با تنظیم صریح متغیر محیطی PEMS_DEV_RESET_ADMIN_PASSWORD=true - در حالت عادی حتی در
///      Development هم خاموش است (یک اجرای تصادفی dotnet run رمز کسی را عوض نمی‌کند).
///   ۳) هیچ رمز جدیدی اختراع نمی‌شود - همان seed.Password مستندشده‌ی
///      موجود (که از روز اول در همین ریپازیتوری هست) دوباره اعمال می‌شود؛ هیچ اعتبار جدیدی به
///      کد اضافه نشد.
///   ۴) MustChangePassword=true (هم‌الگوی خودِ Seeder) - رمز پیش‌فرض باید بلافاصله بعد از ورود
///      تغییر کند.
///   ۵) هر اجرا با ILogger (سطح Warning) ثبت می‌شود - قابل‌ردیابی، نه مخفی.
/// هیچ Authorization/[Authorize] ای تغییر نکرد؛ این صرفاً «فراموشی رمز» را در محیط توسعه حل
/// می‌کند، دقیقاً همان کاری که در محیط واقعی یک Admin دیگر با ResetPasswordAsync انجام می‌دهد.
/// </summary>
public static class DevAdminPasswordReset
{
    public const string EnableEnvironmentVariable = "PEMS_DEV_RESET_ADMIN_PASSWORD";

    public static async Task ResetIfRequestedAsync(IServiceProvider services, IHostEnvironment env, ILogger logger)
    {
        if (!env.IsDevelopment())
            return;

        if (Environment.GetEnvironmentVariable(EnableEnvironmentVariable) != "true")
            return;

        var seed = AdminSeedSettings.From(services.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>());
        if (seed is null)
        {
            logger.LogWarning("{EnvVar}=true but Seed:Admin:* is not configured - nothing done.", EnableEnvironmentVariable);
            return;
        }

        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var admin = await userManager.FindByNameAsync(seed.UserName);

        if (admin == null)
        {
            logger.LogWarning(
                "{EnvVar}=true بود ولی کاربر '{User}' در این پایگاه‌داده وجود ندارد - هیچ کاری انجام نشد.",
                EnableEnvironmentVariable, seed.UserName);
            return;
        }

        // its job is to put the documented seed password back; the password-history rule (SCRUM-412) must not stop it
        var token = await userManager.GeneratePasswordResetTokenAsync(admin);
        IdentityResult result;
        using (PasswordHistoryBypass.Begin())
            result = await userManager.ResetPasswordAsync(admin, token, seed.Password);

        if (!result.Succeeded)
        {
            logger.LogError(
                "بازنشانی رمز Development-only کاربر '{User}' شکست خورد: {Errors}",
                seed.UserName, string.Join(", ", result.Errors.Select(e => e.Description)));
            return;
        }

        // اگر قفل شده باشد (سیاست Lockout)، این‌جا هم باز می‌شود - وگرنه رمز درست هم وارد نمی‌شود
        await userManager.SetLockoutEndDateAsync(admin, null);
        admin.MustChangePassword = true;
        await userManager.UpdateAsync(admin);

        logger.LogWarning(
            "توسعه/تست: رمز کاربر '{User}' به مقدار پیش‌فرض مستندشده بازنشانی شد چون {EnvVar}=true بود. " +
            "این مکانیزم هرگز خارج از Development اجرا نمی‌شود و باید بلافاصله بعد از استفاده، رمز عوض شود.",
            seed.UserName, EnableEnvironmentVariable);
    }
}
