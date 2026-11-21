using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PEMS.Persistence.Context;
using PEMS.Persistence.Identity;

// طبق دستورالعمل P0 Authenticated Load & Concurrency Baseline بند ۲.۱۳: هیچ رمزی حدس زده
// نشد. تنها کاربر واقعی موجود (admin@pems.local) رمزش نامشخص است، پس طبق مجوز صریح همان بند
// ("create a clearly isolated test account only if that can be done safely") این ابزار
// حساب‌های کاملاً ایزوله و به‌وضوح نام‌گذاری‌شده (LoadTestUserNNN) می‌سازد - از طریق همان
// UserManager/RoleManager واقعی ASP.NET Identity (نه درج خام SQL) تا Hash رمز کاملاً سازگار
// با ورود واقعی باشد. Idempotent است - اجرای دوباره فقط ردیف‌های موجود را رد می‌کند.

const string DefaultConnection =
    "Server=localhost\\SQLEXPRESS;Database=PEMSDB;Trusted_Connection=True;TrustServerCertificate=True";
var connectionString = Environment.GetEnvironmentVariable("PEMS_TEST_CONNECTION") ?? DefaultConnection;

// طبق بند ۲.۶: تایید صریح این‌که هدف دیتابیس توسعه/تست محلی است، نه یک سرور تولیدی ناشناخته.
if (!connectionString.Contains("PEMSDB", StringComparison.OrdinalIgnoreCase) ||
    !(connectionString.Contains("localhost", StringComparison.OrdinalIgnoreCase) ||
      connectionString.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase)))
{
    Console.Error.WriteLine("ایمنی: این ابزار فقط برای دیتابیس توسعه/تست محلی طراحی شده. متوقف شد.");
    return 1;
}

// SCRUM-17: no password in source. It comes from PEMS_LOADTEST_PASSWORD or from LoadTest__Password in env/local.secrets.env
// (git-ignored, template in env/local.secrets.env.example); the same value must be given to the k6 script (tools/loadtest/pems-baseline.js).
var testUserPassword = Environment.GetEnvironmentVariable("PEMS_LOADTEST_PASSWORD") ?? ReadLocalSecret("LoadTest:Password") ?? string.Empty;
if (string.IsNullOrWhiteSpace(testUserPassword))
{
    Console.Error.WriteLine("Set PEMS_LOADTEST_PASSWORD or LoadTest__Password in env/local.secrets.env (a throw-away password for the LoadTest accounts; never commit it).");
    return 1;
}
const int GeneralUserCount = 200; // یک VU مستقل به‌ازای هر سطح همزمانی تا ۲۰۰ کاربر
const int AdminUserCount = 10;    // فقط برای سناریوی AuditLog (نیازمند نقش SystemAdmin واقعی)

var services = new ServiceCollection();
services.AddDbContext<PEMSDbContext>(o => o.UseSqlServer(connectionString));
services.AddLogging();
services.AddDataProtection();
services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.Password.RequiredLength = 8;
        options.Password.RequireDigit = true;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.User.RequireUniqueEmail = true;
    })
    .AddRoles<ApplicationRole>()
    .AddEntityFrameworkStores<PEMSDbContext>()
    .AddDefaultTokenProviders();

using var provider = services.BuildServiceProvider();
using var scope = provider.CreateScope();
var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

var created = 0;
var alreadyExisted = 0;

async Task<bool> EnsureUserAsync(string userName, string role)
{
    var existing = await userManager.FindByNameAsync(userName);
    if (existing != null)
    {
        alreadyExisted++;
        return true;
    }

    var user = new ApplicationUser
    {
        UserName = userName,
        Email = $"{userName}@pems.local",
        EmailConfirmed = true,
        FullName = $"Load Test ({userName})",
        IsActive = true,
        MustChangePassword = false,
        CreatedDate = DateTime.Now
    };

    var result = await userManager.CreateAsync(user, testUserPassword);
    if (!result.Succeeded)
    {
        Console.Error.WriteLine($"شکست در ساخت {userName}: {string.Join(", ", result.Errors.Select(e => e.Description))}");
        return false;
    }

    var roleResult = await userManager.AddToRoleAsync(user, role);
    if (!roleResult.Succeeded)
    {
        Console.Error.WriteLine($"شکست در تخصیص نقش {role} به {userName}: {string.Join(", ", roleResult.Errors.Select(e => e.Description))}");
        return false;
    }

    created++;
    return true;
}

for (var i = 1; i <= GeneralUserCount; i++)
    await EnsureUserAsync($"loadtest{i:000}", SystemRoles.EngineeringManager);

for (var i = 1; i <= AdminUserCount; i++)
    await EnsureUserAsync($"loadtestadmin{i:00}", SystemRoles.SystemAdmin);

Console.WriteLine($"تمام شد. ساخته‌شده: {created}, از قبل موجود: {alreadyExisted}");
Console.WriteLine($"کاربران عمومی: loadtest001..loadtest{GeneralUserCount:000} (نقش {SystemRoles.EngineeringManager})");
Console.WriteLine($"کاربران ادمین: loadtestadmin01..loadtestadmin{AdminUserCount:00} (نقش {SystemRoles.SystemAdmin})");

return 0;

static string? ReadLocalSecret(string key)
{
    var directory = PEMS.Shared.Configuration.EnvFiles.FindDirectory(null, AppContext.BaseDirectory, Directory.GetCurrentDirectory());
    if (directory == null) return null;
    var path = PEMS.Shared.Configuration.EnvFiles.SecretsPath(directory, "local");
    if (!File.Exists(path)) return null;
    return PEMS.Shared.Configuration.EnvFiles.Parse(File.ReadAllLines(path)).TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
}
