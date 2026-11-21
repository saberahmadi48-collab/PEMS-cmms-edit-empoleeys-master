using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PEMS.Application.Common;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;
using PEMS.Persistence.Identity;

namespace PEMS.Persistence.Services;

/// <summary>پیاده‌سازی <see cref="IBreakGlassService"/> (SCRUM-356): پنجره‌ی ۱۵ دقیقه‌ای فایل درخواست + توکن اختیاری فروشنده.</summary>
public class BreakGlassService : IBreakGlassService
{
    /// <summary>نام نمایشی انجام‌دهنده در ممیزی: اپراتوری که به سیستم فایل سرور دسترسی دارد (کاربر واردشده‌ای وجود ندارد).</summary>
    public const string ServerOperatorActor = "server-operator";
    public const string VendorTokenActor = "vendor-token";

    private readonly PEMSDbContext _ctx;
    private readonly UserManager<ApplicationUser> _users;
    private readonly IRecoveryEventService _events;
    private readonly BreakGlassOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger _log;

    public BreakGlassService(PEMSDbContext ctx, UserManager<ApplicationUser> users, IRecoveryEventService events, BreakGlassOptions options,
        TimeProvider? clock = null, ILoggerFactory? loggerFactory = null)
    {
        _ctx = ctx;
        _users = users;
        _events = events;
        _options = options;
        _clock = clock ?? TimeProvider.System;
        _log = loggerFactory?.CreateLogger("PEMS.Security") ?? NullLogger.Instance;
    }

    private string RequestFilePath => Path.Combine(_options.RequestFolder, BreakGlassRules.RequestFileName);

    private bool VendorTokenAccepted => _options.VendorRecoveryEnabled
        && !string.IsNullOrWhiteSpace(_options.VendorPublicKey) && !string.IsNullOrWhiteSpace(_options.MachineFingerprint);

    public BreakGlassWindow GetWindow()
    {
        var file = new FileInfo(RequestFilePath);
        if (!file.Exists) return new BreakGlassWindow { VendorTokenAccepted = VendorTokenAccepted };

        var start = BreakGlassRules.WindowStart(file.CreationTimeUtc, file.LastWriteTimeUtc);
        if (BreakGlassRules.IsWithinWindow(start, Now(), _options.WindowMinutes))
            return new BreakGlassWindow { IsOpen = true, ExpiresAtUtc = start.AddMinutes(_options.WindowMinutes), VendorTokenAccepted = VendorTokenAccepted };

        // منقضی: فایل حذف می‌شود تا پنجره با همان فایل دوباره باز نشود
        DeleteRequestFile("expired");
        return new BreakGlassWindow { VendorTokenAccepted = VendorTokenAccepted };
    }

    public async Task<IReadOnlyList<BreakGlassAdminDto>> GetAdminsAsync(CancellationToken ct = default)
    {
        var roleId = await _ctx.Roles.Where(r => r.Name == SystemRoles.SystemAdmin).Select(r => (int?)r.Id).FirstOrDefaultAsync(ct);
        if (roleId == null) return Array.Empty<BreakGlassAdminDto>();

        var inRole = _ctx.UserRoles.Where(ur => ur.RoleId == roleId.Value).Select(ur => ur.UserId);
        return await _ctx.Users.AsNoTracking().Where(u => u.IsActive && inRole.Contains(u.Id)).OrderBy(u => u.UserName)
            .Select(u => new BreakGlassAdminDto { UserId = u.Id, UserName = u.UserName ?? string.Empty, FullName = u.FullName })
            .ToListAsync(ct);
    }

    public async Task<BreakGlassResetResult> ResetAsync(int targetUserId, string? vendorToken, string? ipAddress, CancellationToken ct = default)
    {
        var target = await _users.FindByIdAsync(targetUserId.ToString());
        var window = GetWindow();

        // مسیر: فایل درخواست باز => فایل؛ وگرنه توکن فروشنده (اگر روشن و معتبر)
        string method;
        VendorTokenPayload? token = null;
        if (window.IsOpen) method = RecoveryMethods.BreakGlass;
        else if (!string.IsNullOrWhiteSpace(vendorToken) && window.VendorTokenAccepted) method = RecoveryMethods.VendorToken;
        else
        {
            await _events.RecordFailureAsync(Attempt(RecoveryMethods.BreakGlass, targetUserId, target, ipAddress), RecoveryFailureReasons.WindowClosed, ct);
            return new BreakGlassResetResult { Status = BreakGlassResetStatus.WindowClosed };
        }

        var attempt = Attempt(method, targetUserId, target, ipAddress);

        if (method == RecoveryMethods.VendorToken)
        {
            var status = VendorRecoveryTokenRules.Validate(vendorToken, _options, Now(), out token);
            var alreadyUsed = status == VendorTokenStatus.Valid && File.Exists(UsedMarkerPath(token!.Jti));
            if (status != VendorTokenStatus.Valid || alreadyUsed)
            {
                _log.LogWarning("Vendor recovery token rejected: {Status}", alreadyUsed ? "AlreadyUsed" : status.ToString());
                await _events.RecordFailureAsync(attempt, RecoveryFailureReasons.TokenInvalid, ct);
                return new BreakGlassResetResult { Status = BreakGlassResetStatus.TokenInvalid };
            }
        }

        if (target == null || !target.IsActive)
        {
            await _events.RecordFailureAsync(attempt, RecoveryFailureReasons.UnknownTarget, ct);
            return new BreakGlassResetResult { Status = BreakGlassResetStatus.TargetNotFound };
        }

        if (!await _users.IsInRoleAsync(target, SystemRoles.SystemAdmin))
        {
            await _events.RecordFailureAsync(attempt, RecoveryFailureReasons.TargetNotAdmin, ct);
            return new BreakGlassResetResult { Status = BreakGlassResetStatus.TargetNotAdmin, TargetUserName = target.UserName };
        }

        // یک بار مصرف: پیش از تغییر رمز علامت مصرف گذاشته می‌شود تا دو درخواست هم‌زمان هر دو موفق نشوند
        if (!ConsumeOnce(method, token))
        {
            await _events.RecordFailureAsync(attempt, RecoveryFailureReasons.WindowClosed, ct);
            return new BreakGlassResetResult { Status = BreakGlassResetStatus.WindowClosed };
        }

        var temporaryPassword = AdminAccountRules.GenerateTemporaryPassword();
        var resetToken = await _users.GeneratePasswordResetTokenAsync(target);
        var reset = await _users.ResetPasswordAsync(target, resetToken, temporaryPassword);
        if (!reset.Succeeded)
        {
            await _events.RecordFailureAsync(attempt, RecoveryFailureReasons.PasswordRejected, ct);
            return new BreakGlassResetResult { Status = BreakGlassResetStatus.PasswordRejected, TargetUserName = target.UserName };
        }

        try
        {
            target.MustChangePassword = true;
            target.TemporaryPasswordExpiresAt = AdminAccountRules.ExpiryFrom(Now());
            await AdminMfaReset.ApplyAsync(_ctx, _users, target, ct);
            await _events.CompleteSuccessAsync(attempt with { TargetUserName = target.UserName }, ct);
        }
        catch (Exception ex) when (ex is InvalidOperationException or DbUpdateException)
        {
            _log.LogError(ex, "Break-glass reset of administrator {UserId} did not complete", target.Id);
            return new BreakGlassResetResult { Status = BreakGlassResetStatus.AuditFailed, TargetUserName = target.UserName };
        }

        return new BreakGlassResetResult
        {
            Status = BreakGlassResetStatus.Success,
            TemporaryPassword = temporaryPassword,
            ExpiresAtUtc = target.TemporaryPasswordExpiresAt,
            TargetUserName = target.UserName
        };
    }

    public async Task RecordBlockedNetworkAsync(string? ipAddress, CancellationToken ct = default)
    {
        if (!GetWindow().IsOpen) return;
        await _events.RecordFailureAsync(new RecoveryAttempt(RecoveryMethods.BreakGlass, null, null, null, ServerOperatorActor, ipAddress),
            RecoveryFailureReasons.NetworkNotAllowed, ct);
    }

    private bool ConsumeOnce(string method, VendorTokenPayload? token)
    {
        if (method == RecoveryMethods.VendorToken)
        {
            try
            {
                Directory.CreateDirectory(_options.RequestFolder);
                // CreateNew: اگر نشانگر هم‌زمان ساخته شده باشد شکست می‌خورد
                using var _ = new FileStream(UsedMarkerPath(token!.Jti), FileMode.CreateNew, FileAccess.Write);
                return true;
            }
            catch (IOException) { return false; }
        }

        // حذف فایل درخواست = بستن پنجره؛ فقط اولین درخواست موفق به حذف می‌رسد
        try
        {
            var claimed = RequestFilePath + "." + Guid.NewGuid().ToString("N") + ".used";
            File.Move(RequestFilePath, claimed);
            File.Delete(claimed);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void DeleteRequestFile(string reason)
    {
        try
        {
            File.Delete(RequestFilePath);
            _log.LogInformation("Break-glass request file removed ({Reason})", reason);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.LogWarning(ex, "Could not remove the break-glass request file ({Reason})", reason);
        }
    }

    private string UsedMarkerPath(string jti) => Path.Combine(_options.RequestFolder, VendorRecoveryTokenRules.UsedMarkerFileName(jti));

    private static RecoveryAttempt Attempt(string method, int targetUserId, ApplicationUser? target, string? ip) =>
        new(method, target?.Id ?? targetUserId, target?.UserName, null,
            method == RecoveryMethods.VendorToken ? VendorTokenActor : ServerOperatorActor, ip);

    private DateTime Now() => _clock.GetUtcNow().UtcDateTime;
}
