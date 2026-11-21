using System.Reflection;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.Common;
using PEMS.Application.DTOs;
using PEMS.Domain.Entities;
using PEMS.Persistence.Identity;
using PEMS.Persistence.Services;
using PEMS.Web.Controllers;
using PEMS.Web.Security;

namespace PEMS.Tests.Security;

/// <summary>
/// SCRUM-355 (۳۵۳.۲): کدهای بازیابی یکبارمصرف - فقط یک بار نمایش، فقط هش ذخیره، هر کد یک بار، کد مصرف‌شده و کد قدیمی رد،
/// تولید دوباره مجموعه‌ی قبلی را باطل می‌کند، تلاش‌های brute-force با قفل حساب و محدودیت نرخ IP متوقف می‌شوند.
/// </summary>
public class AdminRecoveryCodeServiceTests
{
    private static AdminRecoveryCodeService Service(RecoveryTestWorld w) => new(w.Ctx, w.Users, w.Events, w.Clock);

    private static RecoveryCodeRedeemRequest Redeem(string user, string code, string password = RecoveryTestWorld.NewPassword) =>
        new(user, code, password, "10.1.2.3");

    // ------------------------------------------------------------------ تولید و نمایش یک‌باره

    [Fact]
    public async Task Generate_ReturnsTenUniqueCodes_ButStoresOnlySaltedHashes()
    {
        var w = RecoveryTestWorld.Create("rc_gen");
        var admin = await w.AddUser("admin1", true);

        var set = await Service(w).GenerateAsync(admin.Id);

        Assert.True(set.Succeeded);
        Assert.Equal(RecoveryCodeRules.CodesPerSet, set.Codes.Count);
        Assert.Equal(set.Codes.Count, set.Codes.Distinct().Count());
        Assert.All(set.Codes, c => Assert.Matches("^[A-Z2-9]{4}-[A-Z2-9]{4}-[A-Z2-9]{4}-[A-Z2-9]{4}$", c));

        var rows = await w.Ctx.AdminRecoveryCodes.AsNoTracking().ToListAsync();
        Assert.Equal(10, rows.Count);
        Assert.Single(rows.Select(r => r.BatchId).Distinct());
        Assert.Equal(10, rows.Select(r => Convert.ToBase64String(r.Salt)).Distinct().Count());               // نمک جدا برای هر کد
        foreach (var code in set.Codes)
        {
            var raw = System.Text.Encoding.ASCII.GetBytes(RecoveryCodeRules.Normalize(code)!);
            Assert.DoesNotContain(rows, r => r.CodeHash.SequenceEqual(raw) || r.Salt.SequenceEqual(raw));    // متن خام ذخیره نشده
            Assert.Single(rows, r => RecoveryCodeRules.Matches(RecoveryCodeRules.Normalize(code)!, r.Salt, r.CodeHash));
            Assert.DoesNotContain(await w.Ctx.AuditLogs.AsNoTracking().Select(a => a.ChangedFieldsSummary ?? "").ToListAsync(), s => s.Contains(code));
        }
    }

    [Fact]
    public async Task Codes_AreShownOnlyOnce_TheServiceCanReportOnlyACount()
    {
        var w = RecoveryTestWorld.Create("rc_once");
        var admin = await w.AddUser("admin1", true);
        var svc = Service(w);
        await svc.GenerateAsync(admin.Id);

        Assert.Equal(10, await svc.GetRemainingCountAsync(admin.Id));

        // جز GenerateAsync هیچ عضو عمومی سرویس کدها (یا چیزی شبیه آن) برنمی‌گرداند
        var readers = typeof(PEMS.Application.Interfaces.IAdminRecoveryCodeService).GetMethods()
            .Where(m => m.Name != nameof(AdminRecoveryCodeService.GenerateAsync)).Select(m => m.ReturnType.ToString());
        Assert.DoesNotContain(readers, t => t.Contains(nameof(RecoveryCodeSetResult)) || t.Contains("String]"));

    }

    [Fact]
    public async Task ProfilePage_ShowsCodesOnlyInTheNoStoreResponse_AndNeedsTheCurrentPassword()
    {
        var w = RecoveryTestWorld.Create("rc_page");
        var admin = await w.AddUser("admin1", true);
        // هر درخواست کنترلر تازه دارد (ModelState مشترک نیست)
        RecoveryCodesController NewController() => new(Service(w), w.Users, new RecoveryTestWorld.PassThroughLocalizer())
        {
            ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext { HttpContext = RecoveryTestWorld.SignedIn(admin) }
        };

        var wrong = await NewController().Generate(new PEMS.Web.Models.Account.RecoveryCodesViewModel { CurrentPassword = "nope" }, default);
        Assert.Equal("Index", Assert.IsType<Microsoft.AspNetCore.Mvc.ViewResult>(wrong).ViewName);
        Assert.Empty(await w.Ctx.AdminRecoveryCodes.ToListAsync());

        var controller = NewController();
        var ok = Assert.IsType<Microsoft.AspNetCore.Mvc.ViewResult>(
            await controller.Generate(new PEMS.Web.Models.Account.RecoveryCodesViewModel { CurrentPassword = RecoveryTestWorld.OldPassword }, default));
        Assert.Equal("Codes", ok.ViewName);
        Assert.Equal(10, Assert.IsType<RecoveryCodeSetResult>(ok.Model).Codes.Count);
        Assert.Equal("no-store", controller.Response.Headers.CacheControl.ToString());

        // بازدید بعدی صفحه فقط تعداد را دارد
        var index = Assert.IsType<Microsoft.AspNetCore.Mvc.ViewResult>(await NewController().Index(default));
        Assert.Equal(10, Assert.IsType<PEMS.Web.Models.Account.RecoveryCodesViewModel>(index.Model).Remaining);
    }

    [Fact]
    public async Task OnlyActiveAdmins_CanHaveRecoveryCodes()
    {
        var w = RecoveryTestWorld.Create("rc_admin");
        var worker = await w.AddUser("worker", false);
        var dormant = await w.AddUser("admin0", true, active: false);

        Assert.False((await Service(w).GenerateAsync(worker.Id)).Succeeded);
        Assert.False((await Service(w).GenerateAsync(dormant.Id)).Succeeded);
        Assert.Empty(await w.Ctx.AdminRecoveryCodes.ToListAsync());
    }

    // ------------------------------------------------------------------ مصرف یک‌باره

    [Fact]
    public async Task ACode_WorksOnce_EndToEnd_AndTheSameCodeIsThenRejected()
    {
        var w = RecoveryTestWorld.Create("rc_redeem");
        var admin = await w.AddUser("admin1", true);
        await w.AddUser("admin2", true);
        var svc = Service(w);
        var code = (await svc.GenerateAsync(admin.Id)).Codes[3];
        await w.Users.SetTwoFactorEnabledAsync(admin, true);
        await w.Users.AccessFailedAsync(admin);
        var stampBefore = (await w.Reload(admin.Id)).SecurityStamp;

        var result = await svc.RedeemAsync(Redeem("admin1", code.ToLowerInvariant().Replace("-", " ")));   // حروف کوچک و فاصله هم پذیرفته می‌شود

        Assert.True(result.Succeeded);
        Assert.Equal(9, result.RemainingCodes);
        var fresh = (await w.Users.FindByIdAsync(admin.Id.ToString()))!;
        Assert.True(await w.Users.CheckPasswordAsync(fresh, RecoveryTestWorld.NewPassword));
        Assert.False(await w.Users.CheckPasswordAsync(fresh, RecoveryTestWorld.OldPassword));
        Assert.False(fresh.MustChangePassword);                                // رمز را خودش انتخاب کرد
        Assert.True(fresh.MfaEnrollmentRequired);                              // MFA باید دوباره ثبت شود
        Assert.False(fresh.TwoFactorEnabled);
        Assert.Equal(0, await w.Users.GetAccessFailedCountAsync(fresh));
        Assert.NotEqual(stampBefore, fresh.SecurityStamp);                     // نشست‌ها باطل شد

        var ev = await w.Ctx.SecurityEvents.AsNoTracking().SingleAsync(x => x.Result == RecoveryResults.Success);
        Assert.Equal((RecoveryMethods.RecoveryCode, admin.Id, (int?)null, "10.1.2.3"), (ev.Method, ev.TargetUserId.GetValueOrDefault(), ev.ActorUserId, ev.IpAddress));
        Assert.Equal(2, w.Notes.Sent.Count);                                   // همه‌ی مدیران فعال مطلع شدند
        Assert.DoesNotContain(w.Notes.Sent, n => n.Message.Contains(code));

        // همان کد دوباره => رد
        var again = await svc.RedeemAsync(Redeem("admin1", code, "Another@pass1"));
        Assert.Equal(RecoveryCodeRedeemStatus.Rejected, again.Status);
        Assert.True(await w.Users.CheckPasswordAsync((await w.Users.FindByIdAsync(admin.Id.ToString()))!, RecoveryTestWorld.NewPassword));
        Assert.Equal(RecoveryFailureReasons.InvalidCode,
            (await w.Ctx.SecurityEvents.AsNoTracking().OrderBy(x => x.SecurityEventId).LastAsync()).FailureReason);
    }

    [Fact]
    public async Task Regeneration_InvalidatesThePreviousSet_AndOldCodesAreRejected()
    {
        var w = RecoveryTestWorld.Create("rc_regen");
        var admin = await w.AddUser("admin1", true);
        await w.AddUser("admin2", true);
        var svc = Service(w);
        var oldSet = await svc.GenerateAsync(admin.Id);
        Assert.True((await svc.RedeemAsync(Redeem("admin1", oldSet.Codes[0]))).Succeeded);

        var newSet = await svc.GenerateAsync(admin.Id);

        Assert.Equal(10, await svc.GetRemainingCountAsync(admin.Id));
        Assert.Equal(9, await w.Ctx.AdminRecoveryCodes.CountAsync(c => c.RevokedAtUtc != null));   // ۹ کد مصرف‌نشده‌ی قبلی باطل شد
        Assert.Equal(1, await w.Ctx.AdminRecoveryCodes.CountAsync(c => c.UsedAtUtc != null));
        Assert.Empty(oldSet.Codes.Intersect(newSet.Codes));

        var withOld = await svc.RedeemAsync(Redeem("admin1", oldSet.Codes[5]));
        Assert.Equal(RecoveryCodeRedeemStatus.Rejected, withOld.Status);
        Assert.True((await svc.RedeemAsync(Redeem("admin1", newSet.Codes[5], "Third@pass123"))).Succeeded);
    }

    [Fact]
    public async Task ACode_OfOneAdmin_DoesNotWorkForAnother_AndNonAdminsOrUnknownUsersAreRejected()
    {
        var w = RecoveryTestWorld.Create("rc_cross");
        var a1 = await w.AddUser("admin1", true);
        await w.AddUser("admin2", true);
        await w.AddUser("worker", false);
        var code = (await Service(w).GenerateAsync(a1.Id)).Codes[0];

        Assert.Equal(RecoveryCodeRedeemStatus.Rejected, (await Service(w).RedeemAsync(Redeem("admin2", code))).Status);
        Assert.Equal(RecoveryCodeRedeemStatus.Rejected, (await Service(w).RedeemAsync(Redeem("worker", code))).Status);
        Assert.Equal(RecoveryCodeRedeemStatus.Rejected, (await Service(w).RedeemAsync(Redeem("nobody", code))).Status);
        Assert.Equal(RecoveryCodeRedeemStatus.Rejected, (await Service(w).RedeemAsync(Redeem("admin1", "not-a-code"))).Status);

        var reasons = await w.Ctx.SecurityEvents.AsNoTracking().OrderBy(x => x.SecurityEventId).Select(x => x.FailureReason).ToListAsync();
        Assert.Equal(new[] { RecoveryFailureReasons.InvalidCode, RecoveryFailureReasons.TargetNotAdmin, RecoveryFailureReasons.UnknownTarget, RecoveryFailureReasons.InvalidCode }, reasons);
        Assert.Equal(10, await Service(w).GetRemainingCountAsync(a1.Id));       // کد مالک هنوز سالم است
    }

    [Fact]
    public async Task AWeakNewPassword_IsRejected_WithoutConsumingTheCode()
    {
        var w = RecoveryTestWorld.Create("rc_weak");
        var admin = await w.AddUser("admin1", true);
        var code = (await Service(w).GenerateAsync(admin.Id)).Codes[0];

        var weak = await Service(w).RedeemAsync(Redeem("admin1", code, "short"));

        Assert.Equal(RecoveryCodeRedeemStatus.PasswordRejected, weak.Status);
        Assert.NotEmpty(weak.PasswordErrors);
        Assert.Equal(10, await Service(w).GetRemainingCountAsync(admin.Id));
        Assert.True((await Service(w).RedeemAsync(Redeem("admin1", code))).Succeeded);
    }

    [Fact]
    public async Task TwoConcurrentRedemptions_OfOneCode_CannotBothConsumeIt()
    {
        var w = RecoveryTestWorld.Create("rc_race");
        var admin = await w.AddUser("admin1", true);
        await Service(w).GenerateAsync(admin.Id);

        var mine = await w.Ctx.AdminRecoveryCodes.FirstAsync();
        await using (var other = w.SecondContext())
        {
            var theirs = await other.AdminRecoveryCodes.SingleAsync(c => c.AdminRecoveryCodeId == mine.AdminRecoveryCodeId);
            theirs.UsedAtUtc = DateTime.UtcNow;
            await other.SaveChangesAsync();                                     // درخواست دیگر زودتر مصرف کرد
        }

        mine.UsedAtUtc = DateTime.UtcNow;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => w.Ctx.SaveChangesAsync());
    }

    [Fact]
    public async Task RecoveryCodeRows_AreAppendOnly()
    {
        var w = RecoveryTestWorld.Create("rc_append");
        var admin = await w.AddUser("admin1", true);
        await Service(w).GenerateAsync(admin.Id);
        var row = await w.Ctx.AdminRecoveryCodes.FirstAsync();

        w.Ctx.AdminRecoveryCodes.Remove(row);
        await Assert.ThrowsAsync<InvalidOperationException>(() => w.Ctx.SaveChangesAsync());
        w.Ctx.Entry(row).State = EntityState.Unchanged;

        row.CodeHash = new byte[32];
        await Assert.ThrowsAsync<InvalidOperationException>(() => w.Ctx.SaveChangesAsync());
    }

    // ------------------------------------------------------------------ brute-force

    [Fact]
    public async Task BruteForce_LocksTheAccount_AndThenEvenACorrectCodeIsRefused()
    {
        var w = RecoveryTestWorld.Create("rc_brute");
        var admin = await w.AddUser("admin1", true);
        var code = (await Service(w).GenerateAsync(admin.Id)).Codes[0];

        for (var i = 0; i < RecoveryTestWorld.MaxFailedAttempts; i++)
            Assert.Equal(RecoveryCodeRedeemStatus.Rejected, (await Service(w).RedeemAsync(Redeem("admin1", RecoveryCodeRules.GenerateCode()))).Status);

        var locked = await Service(w).RedeemAsync(Redeem("admin1", code));

        Assert.Equal(RecoveryCodeRedeemStatus.LockedOut, locked.Status);
        Assert.Equal(10, await Service(w).GetRemainingCountAsync(admin.Id));
        Assert.True(await w.Users.CheckPasswordAsync((await w.Users.FindByIdAsync(admin.Id.ToString()))!, RecoveryTestWorld.OldPassword));
        Assert.Equal(RecoveryFailureReasons.RateLimited,
            (await w.Ctx.SecurityEvents.AsNoTracking().OrderBy(x => x.SecurityEventId).LastAsync()).FailureReason);

        // بعد از پایان قفل دوباره کار می‌کند
        await w.Users.SetLockoutEndDateAsync((await w.Users.FindByIdAsync(admin.Id.ToString()))!, null);
        Assert.True((await Service(w).RedeemAsync(Redeem("admin1", code))).Succeeded);
    }

    [Fact]
    public void RecoveryPages_AreRateLimitedPerIp_OnPostOnly()
    {
        using var limiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx => RecoveryRateLimiting.Partition(ctx, 3, TimeSpan.FromMinutes(15)));

        static HttpContext Request(string method, string ip)
        {
            var ctx = new DefaultHttpContext();
            ctx.Request.Method = method;
            ctx.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(ip);
            return ctx;
        }

        for (var i = 0; i < 3; i++) Assert.True(limiter.AttemptAcquire(Request("POST", "10.0.0.5")).IsAcquired);
        Assert.False(limiter.AttemptAcquire(Request("POST", "10.0.0.5")).IsAcquired);              // چهارمین تلاش از همان IP
        Assert.False(limiter.AttemptAcquire(Request("POST", "::ffff:10.0.0.5")).IsAcquired);       // همان IP به شکل IPv6
        Assert.True(limiter.AttemptAcquire(Request("POST", "10.0.0.6")).IsAcquired);               // IP دیگر
        for (var i = 0; i < 10; i++) Assert.True(limiter.AttemptAcquire(Request("GET", "10.0.0.5")).IsAcquired);

        // سیاست واقعاً روی صفحه‌های بدون ورود اعمال شده است
        foreach (var controller in new[] { typeof(AccountRecoveryController), typeof(BreakGlassController) })
        {
            Assert.Equal(RecoveryRateLimiting.PolicyName, controller.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName);
            Assert.NotNull(controller.GetCustomAttribute<AllowAnonymousAttribute>());
        }
        Assert.Equal(SystemRoles.SystemAdmin, typeof(RecoveryCodesController).GetCustomAttribute<AuthorizeAttribute>()?.Roles);
    }

    [Theory]
    [InlineData("abcd-efgh-jkmn-pqrs", "ABCDEFGHJKMNPQRS")]
    [InlineData(" ABCD EFGH JKMN PQRS ", "ABCDEFGHJKMNPQRS")]
    [InlineData("ABCD-EFGH-JKMN-PQR", null)]       // کوتاه
    [InlineData("ABCD-EFGH-JKMN-PQR0", null)]      // صفر در الفبا نیست
    [InlineData("", null)]
    public void Normalize_AcceptsCaseAndSeparators_AndRejectsBadInput(string input, string? expected) =>
        Assert.Equal(expected, RecoveryCodeRules.Normalize(input));
}
