using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PEMS.Application.Common;
using PEMS.Application.DTOs;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Identity;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;

namespace PEMS.Tests.Security;

/// <summary>
/// SCRUM-354 (۳۵۳.۱): محافظ آخرین مدیر فعال (سمت سرور)، ریست حساب مدیر توسط مدیر دیگر (رمز موقت یک‌بارنمایش و ۲۴ ساعته، تغییر اجباری، ریست MFA)،
/// ممنوعیت ریست خود، و رد رمز موقت منقضی‌شده در ورود.
/// </summary>
public class AdminAccountServiceTests
{
    private sealed class FixedClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 25, 9, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Env
    {
        public required PEMSDbContext Ctx { get; init; }
        public required ServiceProvider Sp { get; init; }
        public required LetterTestNotifications Notes { get; init; }
        public required FixedClock Clock { get; init; }
        public UserManager<ApplicationUser> Users => Sp.GetRequiredService<UserManager<ApplicationUser>>();
        public RecoveryEventService Events => new(Ctx, Users, Notes);
        public AdminAccountService Admins => new(Ctx, Users, Events, Clock);
    }

    private static Env NewEnv()
    {
        var ctx = PemsDbContextFactory.CreateInMemory("adminacct_" + Guid.NewGuid().ToString("N"));
        var services = new ServiceCollection();
        services.AddSingleton(ctx);
        services.AddLogging();
        services.AddDataProtection();
        services.AddIdentityCore<ApplicationUser>().AddRoles<ApplicationRole>().AddEntityFrameworkStores<PEMSDbContext>().AddDefaultTokenProviders();
        return new Env { Ctx = ctx, Sp = services.BuildServiceProvider(), Notes = new LetterTestNotifications(), Clock = new FixedClock() };
    }

    private const string OldPassword = "Old@pass12345";

    private static async Task<ApplicationUser> AddUser(Env e, string name, bool admin, bool active = true)
    {
        var u = new ApplicationUser { UserName = name, Email = name + "@t.local", FullName = name, IsActive = active, LockoutEnabled = true };
        var created = await e.Users.CreateAsync(u, OldPassword);
        Assert.True(created.Succeeded, string.Join(",", created.Errors.Select(x => x.Description)));
        if (admin)
        {
            var role = await e.Ctx.Roles.FirstOrDefaultAsync(r => r.Name == SystemRoles.SystemAdmin);
            if (role == null)
            {
                role = new ApplicationRole(SystemRoles.SystemAdmin) { NormalizedName = SystemRoles.SystemAdmin.ToUpperInvariant() };
                e.Ctx.Roles.Add(role);
                await e.Ctx.SaveChangesAsync();
            }
            e.Ctx.UserRoles.Add(new IdentityUserRole<int> { UserId = u.Id, RoleId = role.Id });
            await e.Ctx.SaveChangesAsync();
        }
        return u;
    }

    private static Task<ApplicationUser> Reload(Env e, int id) => e.Ctx.Users.AsNoTracking().SingleAsync(u => u.Id == id);

    // ------------------------------------------------------------------ محافظ آخرین مدیر

    [Fact]
    public async Task LastActiveAdmin_CannotBeDeactivated_OrDemoted_ButCanStayAsIs()
    {
        var e = NewEnv();
        var only = await AddUser(e, "admin1", true);
        await AddUser(e, "worker", false);

        Assert.True(await e.Admins.WouldLeaveNoActiveAdminAsync(only.Id, willBeActive: false, willBeAdmin: true));
        Assert.True(await e.Admins.WouldLeaveNoActiveAdminAsync(only.Id, willBeActive: true, willBeAdmin: false));
        Assert.True(await e.Admins.WouldLeaveNoActiveAdminAsync(only.Id, willBeActive: false, willBeAdmin: false));
        Assert.False(await e.Admins.WouldLeaveNoActiveAdminAsync(only.Id, willBeActive: true, willBeAdmin: true));   // تغییر بی‌اثر روی مدیر بودن
    }

    [Fact]
    public async Task WithASecondActiveAdmin_TheChangeIsAllowed_ButAnInactiveSecondAdminDoesNotCount()
    {
        var e = NewEnv();
        var a1 = await AddUser(e, "admin1", true);
        var a2 = await AddUser(e, "admin2", true, active: false);
        Assert.True(await e.Admins.WouldLeaveNoActiveAdminAsync(a1.Id, false, true));                                // admin2 غیرفعال است

        a2.IsActive = true;
        await e.Users.UpdateAsync(a2);
        Assert.False(await e.Admins.WouldLeaveNoActiveAdminAsync(a1.Id, false, true));                               // حالا مدیر فعال دیگری هست
        Assert.False(await e.Admins.WouldLeaveNoActiveAdminAsync(a2.Id, true, false));
    }

    [Fact]
    public async Task NonAdmins_AndInactiveAdmins_AreNeverBlocked()
    {
        var e = NewEnv();
        await AddUser(e, "admin1", true);
        var worker = await AddUser(e, "worker", false);
        var dormant = await AddUser(e, "admin0", true, active: false);

        Assert.False(await e.Admins.WouldLeaveNoActiveAdminAsync(worker.Id, false, false));
        Assert.False(await e.Admins.WouldLeaveNoActiveAdminAsync(dormant.Id, false, false));                          // الان مدیر فعال نیست
    }

    [Fact]
    public async Task WithNoAdminRoleAtAll_NothingIsBlocked()
    {
        var e = NewEnv();
        var u = await AddUser(e, "worker", false);
        Assert.False(await e.Admins.WouldLeaveNoActiveAdminAsync(u.Id, false, false));
    }

    // ------------------------------------------------------------------ ریست مدیر دیگر

    [Fact]
    public async Task ResetOfAnotherAdmin_WorksEndToEnd()
    {
        var e = NewEnv();
        var actor = await AddUser(e, "admin1", true);
        var target = await AddUser(e, "admin2", true);
        await AddUser(e, "worker", false);

        // وضعیت قبل از ریست: قفل‌شده، دارای authenticator و passkey، با نشست فعال
        await e.Users.SetLockoutEndDateAsync(target, DateTimeOffset.UtcNow.AddHours(1));
        await e.Users.AccessFailedAsync(target);
        await e.Users.SetTwoFactorEnabledAsync(target, true);
        await e.Users.SetAuthenticationTokenAsync(target, "[AspNetUserStore]", "AuthenticatorKey", "SECRETKEY");
        e.Ctx.WebAuthnCredentials.Add(new WebAuthnCredential { UserId = target.Id, CredentialId = new byte[] { 1, 2, 3 }, PublicKey = new byte[] { 4 }, IsActive = true });
        await e.Ctx.SaveChangesAsync();
        var stampBefore = (await Reload(e, target.Id)).SecurityStamp;

        var result = await e.Admins.ResetAdminAsync(actor.Id, target.Id, "::ffff:10.0.0.9");

        Assert.True(result.Succeeded);
        Assert.Equal(AdminAccountRules.TemporaryPasswordLength, result.TemporaryPassword!.Length);
        Assert.Equal(e.Clock.Now.UtcDateTime.AddHours(24), result.ExpiresAtUtc);

        var fresh = (await e.Users.FindByIdAsync(target.Id.ToString()))!;
        Assert.True(await e.Users.CheckPasswordAsync(fresh, result.TemporaryPassword));                              // رمز موقت کار می‌کند
        Assert.False(await e.Users.CheckPasswordAsync(fresh, OldPassword));                                          // رمز قبلی دیگر نه
        Assert.True(fresh.MustChangePassword);                                                                       // تغییر اجباری
        Assert.Equal(result.ExpiresAtUtc, fresh.TemporaryPasswordExpiresAt);
        Assert.True(fresh.MfaEnrollmentRequired);                                                                    // باید MFA را دوباره ثبت کند
        Assert.False(fresh.TwoFactorEnabled);
        Assert.Null(await e.Users.GetAuthenticationTokenAsync(fresh, "[AspNetUserStore]", "AuthenticatorKey"));
        Assert.Empty(await e.Ctx.WebAuthnCredentials.Where(c => c.UserId == target.Id && c.IsActive).ToListAsync()); // passkey غیرفعال شد
        Assert.Single(await e.Ctx.WebAuthnCredentials.Where(c => c.UserId == target.Id).ToListAsync());              // ولی حذف نشد
        Assert.Null(await e.Users.GetLockoutEndDateAsync(fresh));                                                    // قفل باز شد
        Assert.Equal(0, await e.Users.GetAccessFailedCountAsync(fresh));
        Assert.NotEqual(stampBefore, fresh.SecurityStamp);                                                           // نشست‌ها باطل شد

        // ممیزی و اعلان از سرویس مشترک ۳۵۷
        var ev = await e.Ctx.SecurityEvents.AsNoTracking().SingleAsync();
        Assert.Equal((RecoveryMethods.SecondAdminReset, RecoveryResults.Success, actor.Id, target.Id, "10.0.0.9"), (ev.Method, ev.Result, ev.ActorUserId, ev.TargetUserId, ev.IpAddress));
        Assert.Equal(2, e.Notes.Sent.Count);                                                                         // هر دو مدیر فعال؛ نه کاربر عادی
    }

    [Fact]
    public async Task TemporaryPassword_IsNeverPersistedOrLogged_AndIsRandomEachTime()
    {
        var e = NewEnv();
        var actor = await AddUser(e, "admin1", true);
        var target = await AddUser(e, "admin2", true);

        var first = await e.Admins.ResetAdminAsync(actor.Id, target.Id, null);
        var second = await e.Admins.ResetAdminAsync(actor.Id, target.Id, null);

        Assert.NotEqual(first.TemporaryPassword, second.TemporaryPassword);
        foreach (var secret in new[] { first.TemporaryPassword!, second.TemporaryPassword! })
        {
            Assert.DoesNotContain(await e.Ctx.AuditLogs.AsNoTracking().Select(a => a.ChangedFieldsSummary ?? "").ToListAsync(), s => s.Contains(secret));
            Assert.DoesNotContain(await e.Ctx.SecurityEvents.AsNoTracking().Select(s => (s.FailureReason ?? "") + s.TargetUserName + s.ActorUserName + s.IpAddress).ToListAsync(), s => s.Contains(secret));
            Assert.DoesNotContain(e.Notes.Sent, n => n.Message.Contains(secret) || n.Title.Contains(secret));
        }
    }

    [Fact]
    public async Task SelfReset_IsRejected_AuditedAsFailure_AndChangesNothing()
    {
        var e = NewEnv();
        var admin = await AddUser(e, "admin1", true);
        await AddUser(e, "admin2", true);
        var before = await Reload(e, admin.Id);

        var result = await e.Admins.ResetAdminAsync(admin.Id, admin.Id, "10.0.0.1");

        Assert.Equal(AdminResetStatus.SelfResetNotAllowed, result.Status);
        Assert.Null(result.TemporaryPassword);
        var after = await Reload(e, admin.Id);
        Assert.Equal((before.PasswordHash, before.SecurityStamp, before.MustChangePassword), (after.PasswordHash, after.SecurityStamp, after.MustChangePassword));
        var ev = await e.Ctx.SecurityEvents.AsNoTracking().SingleAsync();
        Assert.Equal((RecoveryResults.Failure, RecoveryFailureReasons.SelfResetNotAllowed), (ev.Result, ev.FailureReason));
        Assert.Empty(e.Notes.Sent);
    }

    [Fact]
    public async Task NonAdminTarget_UnknownTarget_AndNonAdminActor_AreRejectedAndAudited()
    {
        var e = NewEnv();
        var admin = await AddUser(e, "admin1", true);
        var worker = await AddUser(e, "worker", false);
        var inactiveAdmin = await AddUser(e, "admin9", true, active: false);

        Assert.Equal(AdminResetStatus.TargetNotAdmin, (await e.Admins.ResetAdminAsync(admin.Id, worker.Id, null)).Status);
        Assert.Equal(AdminResetStatus.TargetNotFound, (await e.Admins.ResetAdminAsync(admin.Id, 9999, null)).Status);
        Assert.Equal(AdminResetStatus.ActorNotAdmin, (await e.Admins.ResetAdminAsync(worker.Id, admin.Id, null)).Status);
        Assert.Equal(AdminResetStatus.ActorNotAdmin, (await e.Admins.ResetAdminAsync(inactiveAdmin.Id, admin.Id, null)).Status);

        var reasons = (await e.Ctx.SecurityEvents.AsNoTracking().OrderBy(x => x.SecurityEventId).Select(x => x.FailureReason).ToListAsync());
        Assert.Equal(new[] { RecoveryFailureReasons.TargetNotAdmin, RecoveryFailureReasons.UnknownTarget, RecoveryFailureReasons.ActorNotAdmin, RecoveryFailureReasons.ActorNotAdmin }, reasons);
        Assert.True(await e.Users.CheckPasswordAsync((await e.Users.FindByIdAsync(admin.Id.ToString()))!, OldPassword));   // هیچ رمزی عوض نشد
        Assert.Empty(e.Notes.Sent);
    }

    // ------------------------------------------------------------------ رمز موقت: تولید و انقضا

    [Fact]
    public void GeneratedTemporaryPasswords_AreRandom_AndMeetEveryCommonPolicy()
    {
        var set = new HashSet<string>();
        for (var i = 0; i < 300; i++)
        {
            var p = AdminAccountRules.GenerateTemporaryPassword();
            Assert.Equal(AdminAccountRules.TemporaryPasswordLength, p.Length);
            Assert.Contains(p, ch => char.IsUpper(ch));
            Assert.Contains(p, ch => char.IsLower(ch));
            Assert.Contains(p, ch => char.IsDigit(ch));
            Assert.Contains(p, ch => !char.IsLetterOrDigit(ch));
            Assert.DoesNotContain(p, ch => "Il10O".Contains(ch));                                                    // حروف مبهم نیست
            set.Add(p);
        }
        Assert.Equal(300, set.Count);
    }

    [Fact]
    public void TemporaryPasswordExpiry_Rule()
    {
        var now = new DateTime(2026, 10, 25, 9, 0, 0, DateTimeKind.Utc);
        Assert.False(AdminAccountRules.IsTemporaryPasswordExpired(true, now.AddMinutes(1), now));
        Assert.True(AdminAccountRules.IsTemporaryPasswordExpired(true, now.AddMinutes(-1), now));
        Assert.True(AdminAccountRules.IsTemporaryPasswordExpired(true, now, now));                                   // لحظه‌ی انقضا یعنی منقضی
        Assert.False(AdminAccountRules.IsTemporaryPasswordExpired(true, null, now));                                 // رمز پیش‌فرض کاربران عادی انقضا ندارد
        Assert.False(AdminAccountRules.IsTemporaryPasswordExpired(false, now.AddDays(-5), now));                     // بعد از تغییر رمز بی‌اثر است
        Assert.Equal(now.AddHours(24), AdminAccountRules.ExpiryFrom(now));
    }

    [Fact]
    public void Login_RefusesExpiredTemporaryPassword_BeforeAnyPasswordCheck_AndChangePasswordClearsTheExpiry()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "PEMS.Web", "Controllers", "AccountController.cs"));
        var rule = src.IndexOf("IsTemporaryPasswordExpired", StringComparison.Ordinal);
        var signIn = src.IndexOf("PasswordSignInAsync", StringComparison.Ordinal);
        Assert.True(rule > 0 && signIn > 0 && rule < signIn, "the expiry check must run before the password sign-in");
        Assert.Contains("TemporaryPasswordExpiresAt = null", src);
    }

    // ------------------------------------------------------------------ UsersController

    private static UsersFacade NewUsersController(Env e, int? actorId = null) =>
        UsersFacade.For(e.Users, e.Sp.GetRequiredService<RoleManager<ApplicationRole>>(), e.Ctx, e.Events, e.Admins, actorId ?? 0);

    private static async Task<Employee> LinkEmployee(Env e, ApplicationUser u)
    {
        var dept = new Department { Code = "D" + u.UserName, Name = "Dept " + u.UserName };
        var disc = new Discipline { Code = "P" + u.UserName, Name = "Disc " + u.UserName, IsActive = true };
        e.Ctx.Departments.Add(dept); e.Ctx.Disciplines.Add(disc); await e.Ctx.SaveChangesAsync();
        var emp = new Employee { FirstName = u.UserName!, LastName = u.UserName!, DepartmentId = dept.DepartmentId, DisciplineId = disc.DisciplineId, IsActive = true };
        e.Ctx.Employees.Add(emp); await e.Ctx.SaveChangesAsync();
        var tracked = await e.Ctx.Users.SingleAsync(x => x.Id == u.Id);
        tracked.EmployeeId = emp.EmployeeId;
        await e.Ctx.SaveChangesAsync();
        return emp;
    }

    private static UserForm EditModel(ApplicationUser u, Employee emp, bool active, params string[] roles) => new()
    {
        Id = u.Id, UserName = u.UserName!, Email = u.Email!, FullName = u.FullName, IsActive = active,
        ExistingEmployeeId = emp.EmployeeId, SelectedRoles = roles.ToList()
    };

    [Fact]
    public async Task UsersEdit_BlocksDeactivatingOrDemotingTheLastAdmin_OnTheServer()
    {
        var e = NewEnv();
        var only = await AddUser(e, "admin1", true);
        var emp = await LinkEmployee(e, only);

        var deactivate = await NewUsersController(e).Edit(EditModel(only, emp, active: false, SystemRoles.SystemAdmin));
        var demote = await NewUsersController(e).Edit(EditModel(only, emp, active: true));   // تیک SystemAdmin برداشته شد

        UsersFacade.Refused(deactivate);
        UsersFacade.Refused(demote);
        Assert.StartsWith("This is the last active administrator", deactivate.Error);
        Assert.True((await Reload(e, only.Id)).IsActive);
        Assert.True(await e.Users.IsInRoleAsync((await e.Users.FindByIdAsync(only.Id.ToString()))!, SystemRoles.SystemAdmin));
    }

    [Fact]
    public async Task UsersEdit_AllowsTheSameChange_WhenAnotherActiveAdminExists()
    {
        var e = NewEnv();
        var a1 = await AddUser(e, "admin1", true);
        await AddUser(e, "admin2", true);
        var emp = await LinkEmployee(e, a1);

        var res = await NewUsersController(e).Edit(EditModel(a1, emp, active: false, SystemRoles.SystemAdmin));

        UsersFacade.Saved(res);
        Assert.False((await Reload(e, a1.Id)).IsActive);
    }

    [Fact]
    public async Task UsersToggleActive_BlocksTheLastAdmin_AndStillTogglesOthers()
    {
        var e = NewEnv();
        var only = await AddUser(e, "admin1", true);
        var worker = await AddUser(e, "worker", false);

        var c = NewUsersController(e);
        var blocked = await c.ToggleActive(only.Id);
        Assert.True((await Reload(e, only.Id)).IsActive);
        Assert.Contains("last active administrator", blocked.Error);

        await c.ToggleActive(worker.Id);
        Assert.False((await Reload(e, worker.Id)).IsActive);
    }

    [Fact]
    public async Task ResetAdmin_ReturnsTheTemporaryPasswordAsAOneTimeSecret_NotARedirectOrAMessage()
    {
        var e = NewEnv();
        var actor = await AddUser(e, "admin1", true);
        var target = await AddUser(e, "admin2", true);

        var res = await NewUsersController(e, actor.Id).ResetAdmin(target.Id);

        Assert.True(res.Success);
        Assert.False(string.IsNullOrEmpty(res.SecretValue));          // the generic secret page shows it once, with no-store headers
        Assert.NotNull(res.SecretTitle);
        Assert.Equal("/p/users", res.BackUrl);
    }

    [Fact]
    public async Task ResetAdmin_ForSelf_IsRefused_WithAMessage_AndNoSecret()
    {
        var e = NewEnv();
        var admin = await AddUser(e, "admin1", true);
        await AddUser(e, "admin2", true);

        var res = await NewUsersController(e, admin.Id).ResetAdmin(admin.Id);

        Assert.False(res.Success);
        Assert.Null(res.SecretValue);
        Assert.Contains("cannot reset your own account", res.Error);
        Assert.True(await e.Users.CheckPasswordAsync((await e.Users.FindByIdAsync(admin.Id.ToString()))!, OldPassword));
    }

    [Fact]
    public async Task ResetAdmin_OnlyForAnotherAdmin()
    {
        var e = NewEnv();
        var actor = await AddUser(e, "admin1", true);
        var other = await AddUser(e, "admin2", true);
        var worker = await AddUser(e, "worker", false);
        var c = NewUsersController(e, actor.Id);

        Assert.True((await c.ResetAdmin(other.Id)).Success);
        Assert.False((await c.ResetAdmin(actor.Id)).Success);        // themselves
        Assert.False((await c.ResetAdmin(worker.Id)).Success);       // not an administrator
        Assert.False((await c.ResetAdmin(9999)).Success);            // unknown account
    }

    [Fact]
    public void ResetAdmin_IsADefinition_ConfirmedByASwitch_AndTheListOffersItOnlyForAdministrators()
    {
        var root = RepoRoot();
        string Def(string kind, string key) => File.ReadAllText(Path.Combine(root, "PEMS.Persistence", "Definitions", "System", kind, "core", key + ".json"));
        Assert.Contains("\"require\"", Def("action", "users-reset-admin"));
        Assert.Contains("row.isAdmin == true", Def("page", "users"));
    }

    private static string RepoRoot()
    {
        var env = Environment.GetEnvironmentVariable("PEMS_REPO_ROOT");
        if (!string.IsNullOrEmpty(env)) return env;
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "PEMS.Web"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root not found");
    }
}
