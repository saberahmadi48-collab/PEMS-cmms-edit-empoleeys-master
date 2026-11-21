using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PEMS.Persistence.Identity;

namespace PEMS.Web.Security;

/// <summary>
/// در زمان استارت برنامه اجرا می‌شود:
///   1) نقش‌های استاندارد سیستم (SystemRoles) را در صورت نبودن می‌سازد
///   2) یک کاربر System Admin پیش‌فرض می‌سازد (فقط اگر هیچ کاربری در سیستم نباشد)
///
/// هشدار امنیتی: رمز عبور پیش‌فرض باید بلافاصله بعد از اولین ورود تغییر کند.
/// </summary>
public static class IdentitySeeder
{
    // The administrator comes from settings (Seed:Admin:UserName / Email in env/<name>.env, Seed:Admin:Password in
    // env/<name>.secrets.env); nothing is hard-coded here.


    public static async Task SeedAsync(IServiceProvider services)
    {
        var roleManager =
            services.GetRequiredService<RoleManager<ApplicationRole>>();

        var userManager =
            services.GetRequiredService<UserManager<ApplicationUser>>();

        var logger =
            services.GetRequiredService<ILoggerFactory>()
                .CreateLogger("IdentitySeeder");


        // =========================
        // 1) Seed Roles
        // =========================

        foreach (var roleName in SystemRoles.All)
        {
            var exists = await roleManager.RoleExistsAsync(roleName);

            if (exists)
                continue;

            var role = new ApplicationRole(roleName)
            {
                DisplayName = SystemRoles.DisplayNames.GetValueOrDefault(roleName, roleName)
            };

            var result = await roleManager.CreateAsync(role);

            if (!result.Succeeded)
            {
                logger.LogWarning(
                    "خطا در ساخت نقش {RoleName}: {Errors}",
                    roleName,
                    string.Join(", ", result.Errors.Select(e => e.Description)));
            }
        }


        // =========================
        // 2) Seed Default Admin (فقط اگر هیچ کاربری وجود ندارد)
        // =========================

        if (userManager.Users.Any())
            return;

        var seedAdmin = AdminSeedSettings.From(services.GetRequiredService<IConfiguration>());
        if (seedAdmin is null)
        {
            logger.LogWarning("No administrator was seeded: define Seed:Admin:UserName, Seed:Admin:Email and Seed:Admin:Password " +
                              "(Seed__Admin__* in env/<name>.env and env/<name>.secrets.env).");
            return;
        }


        var adminUser = new ApplicationUser
        {
            UserName = seedAdmin.UserName,
            Email = seedAdmin.Email,
            EmailConfirmed = true,
            FullName = "System Administrator",
            IsActive = true,
            // طبق ممیزی بند ۲۶: رمز پیش‌فرض مستندشده باید اجبار به تغییر داشته باشه - دقیقاً
            // همون الگویی که برای کاربران ساخته‌شده از Employee داریم
            MustChangePassword = true,
            CreatedDate = DateTime.Now
        };

        var createResult = await userManager.CreateAsync(
            adminUser,
            seedAdmin.Password);

        if (!createResult.Succeeded)
        {
            logger.LogError(
                "خطا در ساخت کاربر ادمین پیش‌فرض: {Errors}",
                string.Join(", ", createResult.Errors.Select(e => e.Description)));

            return;
        }

        await userManager.AddToRoleAsync(adminUser, SystemRoles.SystemAdmin);

        logger.LogWarning(
            "کاربر System Admin پیش‌فرض ساخته شد. Username: {UserName} — لطفاً بلافاصله بعد از اولین ورود رمز عبور پیش‌فرض را تغییر دهید.",
            seedAdmin.UserName);
    }
}

/// <summary>Administrator account to seed on an empty database; null when any of the three settings is missing.</summary>
public sealed record AdminSeedSettings(string UserName, string Email, string Password)
{
    public static AdminSeedSettings? From(IConfiguration configuration)
    {
        var userName = configuration["Seed:Admin:UserName"];
        var email = configuration["Seed:Admin:Email"];
        var password = configuration["Seed:Admin:Password"];
        return string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password)
            ? null
            : new AdminSeedSettings(userName, email, password);
    }
}
