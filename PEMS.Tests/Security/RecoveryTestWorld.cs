using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using PEMS.Web.Resources;
using PEMS.Persistence.Context;
using PEMS.Persistence.Identity;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;

namespace PEMS.Tests.Security;

/// <summary>محیط مشترک تست‌های بازیابی حساب مدیر (SCRUM-355/356): Identity روی InMemory، ساعت ثابت، اعلان‌های ضبط‌شده.</summary>
internal sealed class RecoveryTestWorld
{
    public const string OldPassword = "Old@pass12345";
    public const string NewPassword = "New@pass67890";
    public const int MaxFailedAttempts = 3;

    public sealed class FixedClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    public required string DbName { get; init; }
    public required PEMSDbContext Ctx { get; init; }
    public required ServiceProvider Sp { get; init; }
    public LetterTestNotifications Notes { get; } = new();
    public FixedClock Clock { get; } = new();

    public UserManager<ApplicationUser> Users => Sp.GetRequiredService<UserManager<ApplicationUser>>();
    public RecoveryEventService Events => new(Ctx, Users, Notes);

    public static RecoveryTestWorld Create(string prefix)
    {
        var name = prefix + "_" + Guid.NewGuid().ToString("N");
        var ctx = PemsDbContextFactory.CreateInMemory(name);
        var services = new ServiceCollection();
        services.AddSingleton(ctx);
        services.AddLogging();
        services.AddDataProtection();
        services.AddIdentityCore<ApplicationUser>(o =>
            {
                o.Lockout.AllowedForNewUsers = true;
                o.Lockout.MaxFailedAccessAttempts = MaxFailedAttempts;
                o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(10);
                o.Password.RequiredLength = 8;
            })
            .AddRoles<ApplicationRole>().AddEntityFrameworkStores<PEMSDbContext>().AddDefaultTokenProviders();
        return new RecoveryTestWorld { DbName = name, Ctx = ctx, Sp = services.BuildServiceProvider() };
    }

    public async Task<ApplicationUser> AddUser(string name, bool admin, bool active = true)
    {
        var u = new ApplicationUser { UserName = name, Email = name + "@t.local", FullName = name, IsActive = active, LockoutEnabled = true };
        var created = await Users.CreateAsync(u, OldPassword);
        Assert.True(created.Succeeded, string.Join(",", created.Errors.Select(x => x.Description)));
        if (admin)
        {
            var role = await Ctx.Roles.FirstOrDefaultAsync(r => r.Name == SystemRoles.SystemAdmin);
            if (role == null)
            {
                role = new ApplicationRole(SystemRoles.SystemAdmin) { NormalizedName = SystemRoles.SystemAdmin.ToUpperInvariant() };
                Ctx.Roles.Add(role);
                await Ctx.SaveChangesAsync();
            }
            Ctx.UserRoles.Add(new IdentityUserRole<int> { UserId = u.Id, RoleId = role.Id });
            await Ctx.SaveChangesAsync();
        }
        return u;
    }

    public Task<ApplicationUser> Reload(int id) => Ctx.Users.AsNoTracking().SingleAsync(u => u.Id == id);

    /// <summary>کانتکست دوم روی همان پایگاه InMemory (برای شبیه‌سازی درخواست هم‌زمان).</summary>
    public PEMSDbContext SecondContext() => PemsDbContextFactory.CreateInMemory(DbName);

    public static DefaultHttpContext SignedIn(ApplicationUser user)
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.UserName!),
            new Claim(ClaimTypes.Role, SystemRoles.SystemAdmin)
        }, "test");
        return new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
    }

    public sealed class PassThroughLocalizer : IStringLocalizer<SharedResource>
    {
        public LocalizedString this[string name] => new(name, name);
        public LocalizedString this[string name, params object[] arguments] => new(name, name);
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => Array.Empty<LocalizedString>();
    }
}
