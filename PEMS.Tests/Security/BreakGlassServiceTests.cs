using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.Common;
using PEMS.Application.DTOs;
using PEMS.Persistence.Services;
using PEMS.Shared.Configuration;
using PEMS.Web.Controllers;
using PEMS.Web.Models.Account;

namespace PEMS.Tests.Security;

/// <summary>
/// SCRUM-356 (۳۵۳.۳): break-glass روی سرور - بدون فایل بسته، با فایل باز، بعد از ۱۵ دقیقه بسته، از شبکه‌ی غیرمدیریتی مسدود (404)،
/// یک‌بارمصرف؛ مسیر توکن فروشنده به‌طور پیش‌فرض خاموش.
/// </summary>
public sealed class BreakGlassServiceTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "pems-bg-" + Guid.NewGuid().ToString("N"));

    public BreakGlassServiceTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, true); } catch (IOException) { }
    }

    private string RequestFile => Path.Combine(_folder, BreakGlassRules.RequestFileName);

    private BreakGlassOptions Options(bool vendor = false, string? publicKey = null, string? fingerprint = null, string? networks = null) => new()
    {
        RequestFolder = _folder,
        WindowMinutes = 15,
        ManagementNetworks = BreakGlassRules.ParseNetworks(networks),
        VendorRecoveryEnabled = vendor,
        VendorPublicKey = publicKey,
        MachineFingerprint = fingerprint
    };

    private static BreakGlassService Service(RecoveryTestWorld w, BreakGlassOptions o) => new(w.Ctx, w.Users, w.Events, o, w.Clock);

    /// <summary>فایل درخواست با زمان ایجاد/نوشتن «چند دقیقه پیش» نسبت به ساعت ثابت تست.</summary>
    private void PlaceRequest(RecoveryTestWorld w, double minutesAgo)
    {
        File.WriteAllText(RequestFile, "reset please");
        var t = w.Clock.Now.UtcDateTime.AddMinutes(-minutesAgo);
        File.SetCreationTimeUtc(RequestFile, t);
        File.SetLastWriteTimeUtc(RequestFile, t);
    }

    // ------------------------------------------------------------------ پنجره‌ی زمانی

    [Fact]
    public async Task WithoutTheFile_ThePageIsClosed_AndAResetIsRefused()
    {
        var w = RecoveryTestWorld.Create("bg_closed");
        var admin = await w.AddUser("admin1", true);
        var svc = Service(w, Options());

        var window = svc.GetWindow();
        Assert.False(window.IsOpen);
        Assert.False(window.IsReachable);

        var result = await svc.ResetAsync(admin.Id, null, "127.0.0.1");
        Assert.Equal(BreakGlassResetStatus.WindowClosed, result.Status);
        Assert.Null(result.TemporaryPassword);
        Assert.True(await w.Users.CheckPasswordAsync((await w.Users.FindByIdAsync(admin.Id.ToString()))!, RecoveryTestWorld.OldPassword));
        var ev = await w.Ctx.SecurityEvents.AsNoTracking().SingleAsync();
        Assert.Equal((RecoveryMethods.BreakGlass, RecoveryResults.Failure, RecoveryFailureReasons.WindowClosed), (ev.Method, ev.Result, ev.FailureReason));
    }

    [Fact]
    public async Task WithAFreshFile_ThePageOpens_ForFifteenMinutes()
    {
        var w = RecoveryTestWorld.Create("bg_open");
        PlaceRequest(w, minutesAgo: 1);

        var window = Service(w, Options()).GetWindow();

        Assert.True(window.IsOpen);
        Assert.Equal(w.Clock.Now.UtcDateTime.AddMinutes(14), window.ExpiresAtUtc);
        Assert.True(File.Exists(RequestFile));
    }

    [Fact]
    public async Task AfterFifteenMinutes_ThePageCloses_AndTheFileIsDeleted()
    {
        var w = RecoveryTestWorld.Create("bg_expire");
        PlaceRequest(w, minutesAgo: 14.9);
        var svc = Service(w, Options());
        Assert.True(svc.GetWindow().IsOpen);

        w.Clock.Now = w.Clock.Now.AddMinutes(0.2);                              // ۱۵ دقیقه و کمی بعد

        Assert.False(svc.GetWindow().IsOpen);
        Assert.False(File.Exists(RequestFile));
    }

    [Fact]
    public void AnOldFileCopiedIn_KeepsItsNewCreationTime_SoTheWindowStartsWhenItArrives()
    {
        var now = new DateTime(2026, 10, 26, 9, 0, 0, DateTimeKind.Utc);
        var start = BreakGlassRules.WindowStart(creationUtc: now.AddMinutes(-1), lastWriteUtc: now.AddDays(-30));
        Assert.True(BreakGlassRules.IsWithinWindow(start, now, 15));
        Assert.False(BreakGlassRules.IsWithinWindow(now.AddMinutes(-15), now, 15));
    }

    // ------------------------------------------------------------------ ریست و یک‌بارمصرف

    [Fact]
    public async Task Reset_WorksEndToEnd_AndTheRequestIsSingleUse()
    {
        var w = RecoveryTestWorld.Create("bg_reset");
        var admin = await w.AddUser("admin1", true);
        await w.AddUser("admin2", true);
        await w.Users.SetLockoutEndDateAsync(admin, DateTimeOffset.UtcNow.AddHours(1));
        var stampBefore = (await w.Reload(admin.Id)).SecurityStamp;
        PlaceRequest(w, minutesAgo: 2);
        var svc = Service(w, Options());

        var result = await svc.ResetAsync(admin.Id, null, "127.0.0.1");

        Assert.True(result.Succeeded);
        Assert.Equal(AdminAccountRules.TemporaryPasswordLength, result.TemporaryPassword!.Length);
        var fresh = (await w.Users.FindByIdAsync(admin.Id.ToString()))!;
        Assert.True(await w.Users.CheckPasswordAsync(fresh, result.TemporaryPassword));
        Assert.True(fresh.MustChangePassword);
        Assert.Equal(w.Clock.Now.UtcDateTime.AddHours(24), fresh.TemporaryPasswordExpiresAt);
        Assert.True(fresh.MfaEnrollmentRequired);
        Assert.Null(await w.Users.GetLockoutEndDateAsync(fresh));
        Assert.NotEqual(stampBefore, fresh.SecurityStamp);                      // نشست‌ها باطل شد

        var ev = await w.Ctx.SecurityEvents.AsNoTracking().SingleAsync();
        Assert.Equal((RecoveryMethods.BreakGlass, RecoveryResults.Success, BreakGlassService.ServerOperatorActor, (int?)null, "127.0.0.1"),
            (ev.Method, ev.Result, ev.ActorUserName, ev.ActorUserId, ev.IpAddress));
        Assert.Equal(2, w.Notes.Sent.Count);

        // یک بار مصرف: فایل حذف شد و درخواست دوم رد می‌شود
        Assert.False(File.Exists(RequestFile));
        Assert.Empty(Directory.GetFiles(_folder));
        Assert.False(svc.GetWindow().IsOpen);
        Assert.Equal(BreakGlassResetStatus.WindowClosed, (await svc.ResetAsync(admin.Id, null, "127.0.0.1")).Status);
    }

    [Fact]
    public async Task OnlyActiveAdmins_AreListed_AndCanBeReset_AFailedTargetDoesNotUseUpTheFile()
    {
        var w = RecoveryTestWorld.Create("bg_target");
        var admin = await w.AddUser("admin1", true);
        var worker = await w.AddUser("worker", false);
        await w.AddUser("admin0", true, active: false);
        PlaceRequest(w, minutesAgo: 1);
        var svc = Service(w, Options());

        Assert.Equal(new[] { "admin1" }, (await svc.GetAdminsAsync()).Select(a => a.UserName));
        Assert.Equal(BreakGlassResetStatus.TargetNotAdmin, (await svc.ResetAsync(worker.Id, null, "::1")).Status);
        Assert.Equal(BreakGlassResetStatus.TargetNotFound, (await svc.ResetAsync(9999, null, "::1")).Status);
        Assert.True(File.Exists(RequestFile));
        Assert.True((await svc.ResetAsync(admin.Id, null, "::1")).Succeeded);
    }

    // ------------------------------------------------------------------ شبکه

    [Theory]
    [InlineData("127.0.0.1", null, true)]
    [InlineData("::1", null, true)]
    [InlineData("::ffff:127.0.0.1", null, true)]
    [InlineData("10.20.4.7", null, true)]                                     // شبکه‌ی مدیریتی
    [InlineData("10.21.0.1", null, false)]
    [InlineData("203.0.113.9", null, false)]
    [InlineData("127.0.0.1", "203.0.113.9", false)]                           // reverse proxy محلی جلوی کاربر اینترنتی
    [InlineData("127.0.0.1", "10.20.0.8, 127.0.0.1", true)]
    [InlineData("127.0.0.1", "unknown", false)]
    [InlineData("203.0.113.9", "127.0.0.1", false)]                           // جعل سرآیند کمکی نمی‌کند
    public void OnlyLocalhostAndTheManagementNetwork_AreAllowed(string remote, string? forwardedFor, bool allowed)
    {
        var networks = BreakGlassRules.ParseNetworks("10.20.0.0/16");
        var headers = forwardedFor == null ? Array.Empty<string>() : new[] { forwardedFor };
        Assert.Equal(allowed, BreakGlassRules.IsAddressAllowed(IPAddress.Parse(remote), headers, networks));
    }

    [Fact]
    public void TheStandardForwardedHeader_IsParsed_AndBadNetworkSettingsFailFast()
    {
        Assert.Equal(new[] { "::1", "10.0.0.1" }, BreakGlassRules.ForwardedAddresses("for=\"[::1]:443\";proto=https, for=10.0.0.1:8080"));
        Assert.True(BreakGlassRules.IsAddressAllowed(IPAddress.Loopback, new[] { "for=\"[::1]:443\"" }, Array.Empty<IPNetwork>()));
        Assert.False(BreakGlassRules.IsAddressAllowed(null, Array.Empty<string>(), Array.Empty<IPNetwork>()));
        Assert.Empty(BreakGlassRules.ParseNetworks(" "));
        Assert.Throws<InvalidOperationException>(() => BreakGlassRules.ParseNetworks("10.0.0.0/99"));
    }

    [Fact]
    public async Task FromANonManagementNetwork_ThePageIs404_EvenWhileTheWindowIsOpen()
    {
        var w = RecoveryTestWorld.Create("bg_net");
        var admin = await w.AddUser("admin1", true);
        PlaceRequest(w, minutesAgo: 1);
        var options = Options(networks: "10.20.0.0/16");

        BreakGlassController Controller(string ip, string? xff = null)
        {
            var http = new DefaultHttpContext();
            http.Connection.RemoteIpAddress = IPAddress.Parse(ip);
            if (xff != null) http.Request.Headers["X-Forwarded-For"] = xff;
            return new BreakGlassController(Service(w, options), options, new RecoveryTestWorld.PassThroughLocalizer())
            {
                ControllerContext = new ControllerContext { HttpContext = http }
            };
        }

        Assert.IsType<NotFoundResult>(await Controller("203.0.113.9").Index(default));
        Assert.IsType<NotFoundResult>(await Controller("127.0.0.1", "203.0.113.9").Index(default));
        Assert.IsType<NotFoundResult>(await Controller("203.0.113.9").Index(new BreakGlassViewModel { TargetUserId = admin.Id }, default));
        Assert.True(File.Exists(RequestFile));                                    // تلاش مسدودشده درخواست را مصرف نکرد
        Assert.Equal(RecoveryFailureReasons.NetworkNotAllowed, (await w.Ctx.SecurityEvents.AsNoTracking().SingleAsync()).FailureReason);

        var page = Assert.IsType<ViewResult>(await Controller("10.20.1.1").Index(default));
        Assert.Single(Assert.IsType<BreakGlassViewModel>(page.Model).Admins);

        var done = Assert.IsType<ViewResult>(await Controller("127.0.0.1").Index(new BreakGlassViewModel { TargetUserId = admin.Id }, default));
        Assert.Equal("Result", done.ViewName);

        // پس از مصرف، صفحه حتی از localhost هم 404 است
        Assert.IsType<NotFoundResult>(await Controller("127.0.0.1").Index(default));
    }

    [Fact]
    public async Task WithoutTheFile_ThePageIs404_FromLocalhostToo()
    {
        var w = RecoveryTestWorld.Create("bg_404");
        var options = Options();
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = IPAddress.Loopback;
        var controller = new BreakGlassController(Service(w, options), options, new RecoveryTestWorld.PassThroughLocalizer())
        {
            ControllerContext = new ControllerContext { HttpContext = http }
        };

        Assert.IsType<NotFoundResult>(await controller.Index(default));
    }

    // ------------------------------------------------------------------ توکن فروشنده (اختیاری)

    [Fact]
    public void VendorTokenPath_IsDisabledByDefault_InEveryEnvironmentFile()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PEMS.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);

        var files = Directory.GetFiles(Path.Combine(dir!.FullName, "env"), "*.env").Where(f => !f.EndsWith(".secrets.env")).ToList();
        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            var settings = EnvFiles.Parse(File.ReadAllLines(file));
            Assert.Equal("false", settings["Security:BreakGlass:VendorRecovery:Enabled"]);
            Assert.Equal("15", settings["Security:BreakGlass:WindowMinutes"]);
        }

        // و در کد هم پیش‌فرض خاموش است
        Assert.False(new BreakGlassOptions().VendorRecoveryEnabled);
        Assert.Equal(VendorTokenStatus.Disabled, VendorRecoveryTokenRules.Validate("x.y", new BreakGlassOptions(), DateTime.UtcNow, out _));
    }

    [Fact]
    public async Task WhileDisabled_EvenAValidlySignedToken_DoesNotOpenAnything()
    {
        using var vendor = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var w = RecoveryTestWorld.Create("bg_vendor_off");
        var admin = await w.AddUser("admin1", true);
        var options = Options(vendor: false, publicKey: PublicKey(vendor), fingerprint: "FP-1");
        var token = Sign(vendor, "jti-1", "FP-1", w.Clock.Now.UtcDateTime, hours: 24);
        var svc = Service(w, options);

        Assert.False(svc.GetWindow().IsReachable);
        Assert.Equal(BreakGlassResetStatus.WindowClosed, (await svc.ResetAsync(admin.Id, token, "127.0.0.1")).Status);
    }

    [Fact]
    public async Task WhenEnabled_AValidToken_ResetsOnce_AndBadTokensAreRejected()
    {
        using var vendor = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var attacker = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var w = RecoveryTestWorld.Create("bg_vendor_on");
        var admin = await w.AddUser("admin1", true);
        var options = Options(vendor: true, publicKey: PublicKey(vendor), fingerprint: "FP-1");
        var now = w.Clock.Now.UtcDateTime;
        var svc = Service(w, options);

        Assert.True(svc.GetWindow().VendorTokenAccepted);
        Assert.False(svc.GetWindow().IsOpen);

        Assert.Equal(VendorTokenStatus.BadSignature, VendorRecoveryTokenRules.Validate(Sign(attacker, "j", "FP-1", now, 1), options, now, out _));
        Assert.Equal(VendorTokenStatus.WrongMachine, VendorRecoveryTokenRules.Validate(Sign(vendor, "j", "FP-2", now, 1), options, now, out _));
        Assert.Equal(VendorTokenStatus.Expired, VendorRecoveryTokenRules.Validate(Sign(vendor, "j", "FP-1", now.AddHours(-25), 24), options, now, out _));
        Assert.Equal(VendorTokenStatus.LifetimeTooLong, VendorRecoveryTokenRules.Validate(Sign(vendor, "j", "FP-1", now, 48), options, now, out _));
        Assert.Equal(VendorTokenStatus.Malformed, VendorRecoveryTokenRules.Validate("garbage", options, now, out _));

        var token = Sign(vendor, "jti-42", "FP-1", now.AddHours(-1), 24);
        var first = await svc.ResetAsync(admin.Id, token, "127.0.0.1");
        Assert.True(first.Succeeded);
        Assert.Equal(RecoveryMethods.VendorToken, (await w.Ctx.SecurityEvents.AsNoTracking().SingleAsync(e => e.Result == RecoveryResults.Success)).Method);

        var reused = await svc.ResetAsync(admin.Id, token, "127.0.0.1");
        Assert.Equal(BreakGlassResetStatus.TokenInvalid, reused.Status);
        Assert.Null(reused.TemporaryPassword);
    }

    private static string PublicKey(ECDsa key) => Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());

    private static string Sign(ECDsa key, string jti, string fingerprint, DateTime issuedUtc, double hours)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            v = 1,
            jti,
            fp = fingerprint,
            iat = new DateTimeOffset(issuedUtc).ToUnixTimeSeconds(),
            exp = new DateTimeOffset(issuedUtc.AddHours(hours)).ToUnixTimeSeconds()
        });
        var signature = key.SignData(payload, HashAlgorithmName.SHA256);
        return VendorRecoveryTokenRules.ToBase64Url(payload) + "." + VendorRecoveryTokenRules.ToBase64Url(signature);
    }
}
