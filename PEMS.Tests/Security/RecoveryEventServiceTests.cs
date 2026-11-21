using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
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
/// SCRUM-357 (۳۵۳.۴): سرویس مشترک بازیابی حساب مدیر - هر مسیر یک رکورد ممیزی می‌سازد، نشست‌های هدف را باطل می‌کند و به همه‌ی مدیران فعال
/// اعلان می‌دهد؛ شکست فقط ممیزی می‌شود؛ جدول فقط‌افزودنی است؛ ریست رمز مدیر از فرم ویرایش کاربر هم از همین مسیر می‌گذرد.
/// </summary>
public class RecoveryEventServiceTests
{
    private sealed class Env
    {
        public required PEMSDbContext Ctx { get; init; }
        public required ServiceProvider Sp { get; init; }
        public required LetterTestNotifications Notes { get; init; }
        public UserManager<ApplicationUser> Users => Sp.GetRequiredService<UserManager<ApplicationUser>>();
        public RecoveryEventService Service => new(Ctx, Users, Notes);
    }

    private static Env NewEnv()
    {
        var ctx = PemsDbContextFactory.CreateInMemory("recovery_" + Guid.NewGuid().ToString("N"));
        var services = new ServiceCollection();
        services.AddSingleton(ctx);
        services.AddLogging();
        services.AddDataProtection();
        services.AddIdentityCore<ApplicationUser>().AddRoles<ApplicationRole>().AddEntityFrameworkStores<PEMSDbContext>().AddDefaultTokenProviders();
        return new Env { Ctx = ctx, Sp = services.BuildServiceProvider(), Notes = new LetterTestNotifications() };
    }

    private static async Task<ApplicationUser> AddUser(Env e, string name, bool admin, bool active = true)
    {
        var u = new ApplicationUser { UserName = name, NormalizedUserName = name.ToUpperInvariant(), Email = name + "@t.local", FullName = name, IsActive = active, SecurityStamp = Guid.NewGuid().ToString(), ConcurrencyStamp = Guid.NewGuid().ToString() };
        e.Ctx.Users.Add(u);
        await e.Ctx.SaveChangesAsync();
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

    private static Task<string?> StampOf(Env e, int id) => e.Ctx.Users.AsNoTracking().Where(u => u.Id == id).Select(u => u.SecurityStamp).SingleAsync();

    [Fact]
    public async Task Success_WritesEvent_RevokesSessions_AndNotifiesOnlyActiveAdmins()
    {
        var e = NewEnv();
        var actor = await AddUser(e, "admin1", true);
        var target = await AddUser(e, "admin2", true);
        var inactive = await AddUser(e, "admin3", true, active: false);
        var plain = await AddUser(e, "worker", false);
        var before = await StampOf(e, target.Id);

        var outcome = await e.Service.CompleteSuccessAsync(new RecoveryAttempt(RecoveryMethods.SecondAdminReset, target.Id, target.UserName, actor.Id, actor.UserName, "::ffff:10.0.0.7"));

        Assert.NotEqual(before, await StampOf(e, target.Id));                                       // نشست‌ها/کوکی‌های ماندگار باطل شد
        var ev = await e.Ctx.SecurityEvents.AsNoTracking().SingleAsync();
        Assert.Equal(outcome.SecurityEventId, ev.SecurityEventId);
        Assert.Equal((RecoveryEventTypes.AdminRecovery, RecoveryMethods.SecondAdminReset, RecoveryResults.Success), (ev.EventType, ev.Method, ev.Result));
        Assert.Equal(("admin1", "admin2", actor.Id, target.Id), (ev.ActorUserName, ev.TargetUserName, ev.ActorUserId, ev.TargetUserId));
        Assert.Equal("10.0.0.7", ev.IpAddress);                                                      // IPv4-mapped نرمال شد
        Assert.Null(ev.FailureReason);
        Assert.Null(ev.ForwardedToSiemAt);                                                           // در صف SIEM
        Assert.Equal(DateTimeKind.Utc, ev.OccurredAtUtc.Kind);

        Assert.Equal(2, outcome.AdminsNotified);
        Assert.Equal(new[] { actor.Id, target.Id }.OrderBy(x => x), e.Notes.Sent.Select(s => s.UserId).OrderBy(x => x));
        Assert.DoesNotContain(e.Notes.Sent, s => s.UserId == inactive.Id || s.UserId == plain.Id);
        Assert.All(e.Notes.Sent, s => { Assert.Equal(RecoveryEventService.SecurityEventsUrl, s.Link); Assert.Equal(ev.SecurityEventId, s.RelatedId); Assert.Contains("admin2", s.Message); });
    }

    [Theory]
    [InlineData(RecoveryMethods.SecondAdminReset)]
    [InlineData(RecoveryMethods.RecoveryCode)]
    [InlineData(RecoveryMethods.BreakGlass)]
    [InlineData(RecoveryMethods.VendorToken)]
    [InlineData(RecoveryMethods.AdminConsole)]
    public async Task EveryRecoveryMethod_LeavesAuditRecord_RevokesSessions_AndNotifies(string method)
    {
        var e = NewEnv();
        var target = await AddUser(e, "admin2", true);
        var before = await StampOf(e, target.Id);

        // مسیرهای کد بازیابی و break-glass کاربر وارد‌شده ندارند: actor = null
        await e.Service.CompleteSuccessAsync(new RecoveryAttempt(method, target.Id, target.UserName, IpAddress: "127.0.0.1"));

        var ev = await e.Ctx.SecurityEvents.AsNoTracking().SingleAsync();
        Assert.Equal(method, ev.Method);
        Assert.Null(ev.ActorUserId);
        Assert.NotEqual(before, await StampOf(e, target.Id));
        Assert.Single(e.Notes.Sent);
    }

    [Fact]
    public async Task Failure_OnlyAudits_NoNotification_NoSessionRevocation_AndReasonIsAKnownCode()
    {
        var e = NewEnv();
        var target = await AddUser(e, "admin2", true);
        var before = await StampOf(e, target.Id);

        await e.Service.RecordFailureAsync(new RecoveryAttempt(RecoveryMethods.RecoveryCode, target.Id, target.UserName, IpAddress: "10.1.1.1"), RecoveryFailureReasons.InvalidCode);
        // متن آزاد (مثلاً چیزی شبیه رمز/کد) هرگز ذخیره نمی‌شود
        await e.Service.RecordFailureAsync(new RecoveryAttempt(RecoveryMethods.RecoveryCode, null, "ghost"), "code ABCD-1234 was wrong");

        var rows = await e.Ctx.SecurityEvents.AsNoTracking().OrderBy(x => x.SecurityEventId).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(RecoveryResults.Failure, r.Result));
        Assert.Equal(RecoveryFailureReasons.InvalidCode, rows[0].FailureReason);
        Assert.Equal(RecoveryFailureReasons.Other, rows[1].FailureReason);
        Assert.DoesNotContain(rows, r => (r.FailureReason ?? "").Contains("ABCD"));
        Assert.Empty(e.Notes.Sent);
        Assert.Equal(before, await StampOf(e, target.Id));
    }

    [Fact]
    public async Task Target_ThatIsNotAnAdmin_IsRejected_WithFailureEvent_AndNothingElseHappens()
    {
        var e = NewEnv();
        var worker = await AddUser(e, "worker", false);
        await AddUser(e, "admin1", true);
        var before = await StampOf(e, worker.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            e.Service.CompleteSuccessAsync(new RecoveryAttempt(RecoveryMethods.SecondAdminReset, worker.Id, worker.UserName, IpAddress: "10.0.0.1")));

        var ev = await e.Ctx.SecurityEvents.AsNoTracking().SingleAsync();
        Assert.Equal((RecoveryResults.Failure, RecoveryFailureReasons.TargetNotAdmin), (ev.Result, ev.FailureReason));
        Assert.Empty(e.Notes.Sent);
        Assert.Equal(before, await StampOf(e, worker.Id));
    }

    [Fact]
    public async Task UnknownTarget_IsRejected_WithFailureEvent()
    {
        var e = NewEnv();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            e.Service.CompleteSuccessAsync(new RecoveryAttempt(RecoveryMethods.BreakGlass, 9999, "ghost")));
        var ev = await e.Ctx.SecurityEvents.AsNoTracking().SingleAsync();
        Assert.Equal(RecoveryFailureReasons.UnknownTarget, ev.FailureReason);
        Assert.Equal(9999, ev.TargetUserId);
    }

    [Fact]
    public async Task UnknownMethod_AndMissingTarget_AreProgrammingErrors()
    {
        var e = NewEnv();
        var target = await AddUser(e, "admin2", true);
        await Assert.ThrowsAsync<ArgumentException>(() => e.Service.CompleteSuccessAsync(new RecoveryAttempt("Magic", target.Id, target.UserName)));
        await Assert.ThrowsAsync<ArgumentException>(() => e.Service.CompleteSuccessAsync(new RecoveryAttempt(RecoveryMethods.RecoveryCode, null, null)));
        Assert.Empty(e.Ctx.SecurityEvents);
    }

    [Fact]
    public async Task NotificationFailure_DoesNotUndoTheRecovery_ButTheAuditRecordStays()
    {
        var e = NewEnv();
        await AddUser(e, "admin1", true);
        var target = await AddUser(e, "admin2", true);
        e.Notes.Throw = true;
        var before = await StampOf(e, target.Id);

        var outcome = await e.Service.CompleteSuccessAsync(new RecoveryAttempt(RecoveryMethods.BreakGlass, target.Id, target.UserName));

        Assert.Equal(0, outcome.AdminsNotified);
        Assert.Single(e.Ctx.SecurityEvents);
        Assert.NotEqual(before, await StampOf(e, target.Id));
    }

    [Fact]
    public async Task SecurityEvents_AreAppendOnly_OnlyTheSiemMarkerMayChange()
    {
        var e = NewEnv();
        var target = await AddUser(e, "admin2", true);
        await e.Service.CompleteSuccessAsync(new RecoveryAttempt(RecoveryMethods.RecoveryCode, target.Id, target.UserName));

        var ev = await e.Ctx.SecurityEvents.SingleAsync();
        ev.Result = RecoveryResults.Failure;                                                         // دست‌کاری نتیجه
        await Assert.ThrowsAsync<InvalidOperationException>(() => e.Ctx.SaveChangesAsync());
        e.Ctx.Entry(ev).State = EntityState.Unchanged;

        e.Ctx.SecurityEvents.Remove(ev);                                                             // حذف
        await Assert.ThrowsAsync<InvalidOperationException>(() => e.Ctx.SaveChangesAsync());
        e.Ctx.Entry(ev).State = EntityState.Unchanged;

        ev.ForwardedToSiemAt = DateTime.UtcNow;                                                      // علامت تحویل SIEM مجاز است
        await e.Ctx.SaveChangesAsync();
        Assert.NotNull((await e.Ctx.SecurityEvents.AsNoTracking().SingleAsync()).ForwardedToSiemAt);
    }

    [Fact]
    public async Task GetEvents_FiltersByMethodAndResult_NewestFirst_AndPages()
    {
        var e = NewEnv();
        var target = await AddUser(e, "admin2", true);
        var a = new RecoveryAttempt(RecoveryMethods.RecoveryCode, target.Id, target.UserName);
        await e.Service.RecordFailureAsync(a, RecoveryFailureReasons.InvalidCode);
        await e.Service.CompleteSuccessAsync(a);
        await e.Service.CompleteSuccessAsync(a with { Method = RecoveryMethods.BreakGlass });

        var all = await e.Service.GetEventsAsync(null, null, 1, 20);
        Assert.Equal(3, all.TotalCount);
        Assert.Equal(all.Items.OrderByDescending(x => x.SecurityEventId).Select(x => x.SecurityEventId), all.Items.Select(x => x.SecurityEventId));

        Assert.Equal(1, (await e.Service.GetEventsAsync(RecoveryMethods.BreakGlass, null, 1, 20)).TotalCount);
        Assert.Equal(1, (await e.Service.GetEventsAsync(null, RecoveryResults.Failure, 1, 20)).TotalCount);
        var page2 = await e.Service.GetEventsAsync(null, null, 2, 2);
        Assert.Single(page2.Items);
        Assert.Equal(2, page2.TotalPages);
    }

    [Fact]
    public void Rules_NormalizeIp_Trim_AndReasons()
    {
        Assert.Equal("1.2.3.4", RecoveryEventRules.NormalizeIp("::ffff:1.2.3.4"));
        Assert.Equal("::1", RecoveryEventRules.NormalizeIp(" ::1 "));
        Assert.Null(RecoveryEventRules.NormalizeIp("  "));
        Assert.Equal(RecoveryEventRules.MaxNameLength, RecoveryEventRules.Trim(new string('x', 999), RecoveryEventRules.MaxNameLength)!.Length);
        Assert.Equal(RecoveryFailureReasons.Other, RecoveryFailureReasons.Normalize(null));
        Assert.Equal(RecoveryFailureReasons.RateLimited, RecoveryFailureReasons.Normalize(RecoveryFailureReasons.RateLimited));
        Assert.True(RecoveryMethods.All.All(m => RecoveryMethods.Label(m).Length > 0));
    }

    // ------------------------------------------------------------------ UsersController.Edit (مسیر قدیمی ریست رمز)

    private static UsersFacade NewUsersController(Env e) => UsersFacade.For(e.Users, e.Sp.GetRequiredService<RoleManager<ApplicationRole>>(), e.Ctx, e.Service);

    private static async Task<(Employee Emp, ApplicationUser User)> LinkedUser(Env e, string name, bool admin)
    {
        var dept = new Department { Code = "D" + name, Name = "Dept " + name };
        var disc = new Discipline { Code = "P" + name, Name = "Disc " + name, IsActive = true };
        e.Ctx.Departments.Add(dept); e.Ctx.Disciplines.Add(disc); await e.Ctx.SaveChangesAsync();
        var emp = new Employee { FirstName = name, LastName = name, DepartmentId = dept.DepartmentId, DisciplineId = disc.DisciplineId, IsActive = true };
        e.Ctx.Employees.Add(emp); await e.Ctx.SaveChangesAsync();
        var u = await AddUser(e, name, admin);
        u.EmployeeId = emp.EmployeeId;
        await e.Ctx.SaveChangesAsync();
        return (emp, u);
    }

    private static UserForm EditModel(ApplicationUser u, Employee emp, string? password, IEnumerable<string>? roles = null) => new()
    {
        Id = u.Id, UserName = u.UserName!, Email = u.Email!, FullName = u.FullName, IsActive = true,
        ExistingEmployeeId = emp.EmployeeId, Password = password, SelectedRoles = roles?.ToList() ?? new()
    };

    [Fact]
    public async Task UsersEdit_PasswordResetOfAdmin_IsAudited_RevokesSessions_AndNotifies()
    {
        var e = NewEnv();
        var (emp, admin) = await LinkedUser(e, "admin2", true);
        await AddUser(e, "admin1", true);
        var before = await StampOf(e, admin.Id);

        var res = await NewUsersController(e).Edit(EditModel(admin, emp, "Newpass@12345", new[] { SystemRoles.SystemAdmin }));

        UsersFacade.Saved(res);
        var ev = await e.Ctx.SecurityEvents.AsNoTracking().SingleAsync();
        Assert.Equal((RecoveryMethods.AdminConsole, RecoveryResults.Success, admin.Id), (ev.Method, ev.Result, ev.TargetUserId));
        Assert.NotEqual(before, await StampOf(e, admin.Id));
        Assert.Equal(2, e.Notes.Sent.Count);
        Assert.True(await e.Users.CheckPasswordAsync((await e.Users.FindByIdAsync(admin.Id.ToString()))!, "Newpass@12345"));
    }

    [Fact]
    public async Task UsersEdit_RejectedPassword_IsReportedNotSilent_AndAuditedAsFailure()
    {
        var e = NewEnv();
        var (emp, admin) = await LinkedUser(e, "admin2", true);
        var c = NewUsersController(e);

        var res = await c.Edit(EditModel(admin, emp, "abc", new[] { SystemRoles.SystemAdmin }));          // خیلی ضعیف

        UsersFacade.Refused(res);
        Assert.Equal("User updated, but the password was not changed: {0}", res.Error);                    // before, the result was ignored
        var ev = await e.Ctx.SecurityEvents.AsNoTracking().SingleAsync();
        Assert.Equal((RecoveryResults.Failure, RecoveryFailureReasons.PasswordRejected), (ev.Result, ev.FailureReason));
        Assert.Empty(e.Notes.Sent);
    }

    [Fact]
    public async Task UsersEdit_PasswordResetOfNonAdmin_IsNotAnAdminRecoveryEvent()
    {
        var e = NewEnv();
        var (emp, worker) = await LinkedUser(e, "worker", false);

        await NewUsersController(e).Edit(EditModel(worker, emp, "Newpass@12345"));

        Assert.Empty(e.Ctx.SecurityEvents);
        Assert.Empty(e.Notes.Sent);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PEMS.sln")) && !Directory.Exists(Path.Combine(dir.FullName, "PEMS.Web"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root not found");
    }
}
