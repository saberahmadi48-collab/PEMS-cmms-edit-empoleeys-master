using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PEMS.Application.Common;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Identity;

namespace PEMS.Persistence.Services;

/// <summary>پیاده‌سازی <see cref="IAdminRecoveryCodeService"/> (SCRUM-355): کدهای بازیابی یکبارمصرف حساب مدیر.</summary>
public class AdminRecoveryCodeService : IAdminRecoveryCodeService
{
    // برای نام کاربری ناموجود هم یک هش محاسبه می‌شود تا زمان پاسخ وجود حساب را لو ندهد
    private static readonly byte[] DummySalt = new byte[RecoveryCodeRules.SaltLength];

    private readonly PEMSDbContext _ctx;
    private readonly UserManager<ApplicationUser> _users;
    private readonly IRecoveryEventService _events;
    private readonly TimeProvider _clock;
    private readonly ILogger _log;

    public AdminRecoveryCodeService(PEMSDbContext ctx, UserManager<ApplicationUser> users, IRecoveryEventService events,
        TimeProvider? clock = null, ILoggerFactory? loggerFactory = null)
    {
        _ctx = ctx;
        _users = users;
        _events = events;
        _clock = clock ?? TimeProvider.System;
        _log = loggerFactory?.CreateLogger("PEMS.Security") ?? NullLogger.Instance;
    }

    public async Task<RecoveryCodeSetResult> GenerateAsync(int userId, CancellationToken ct = default)
    {
        var user = await _users.FindByIdAsync(userId.ToString());
        if (user == null || !user.IsActive || !await _users.IsInRoleAsync(user, SystemRoles.SystemAdmin))
            return new RecoveryCodeSetResult { Succeeded = false };

        var now = Now();

        // مجموعه‌ی قبلی (هر کد مصرف‌نشده) باطل می‌شود
        var previous = await ValidCodes(userId).ToListAsync(ct);
        foreach (var old in previous) old.RevokedAtUtc = now;

        var codes = RecoveryCodeRules.GenerateSet();
        var batch = Guid.NewGuid();
        foreach (var code in codes)
        {
            var salt = RecoveryCodeRules.NewSalt();
            _ctx.AdminRecoveryCodes.Add(new AdminRecoveryCode
            {
                UserId = userId,
                BatchId = batch,
                Salt = salt,
                CodeHash = RecoveryCodeRules.Hash(RecoveryCodeRules.Normalize(code)!, salt),
                CreatedAtUtc = now
            });
        }
        await _ctx.SaveChangesAsync(ct);

        // خود کدها هرگز لاگ نمی‌شوند
        _log.LogInformation("Recovery codes generated for administrator {UserId}: batch={BatchId} count={Count} revoked={Revoked}",
            userId, batch, codes.Count, previous.Count);

        return new RecoveryCodeSetResult { Succeeded = true, Codes = codes, GeneratedAtUtc = now, UserName = user.UserName };
    }

    public Task<int> GetRemainingCountAsync(int userId, CancellationToken ct = default) => ValidCodes(userId).CountAsync(ct);

    public async Task<RecoveryCodeRedeemResult> RedeemAsync(RecoveryCodeRedeemRequest request, CancellationToken ct = default)
    {
        var userName = request.UserName?.Trim();
        var normalized = RecoveryCodeRules.Normalize(request.Code);
        var user = string.IsNullOrEmpty(userName) ? null : await _users.FindByNameAsync(userName);
        var attempt = new RecoveryAttempt(RecoveryMethods.RecoveryCode, user?.Id, user?.UserName ?? userName, null, null, request.IpAddress);

        if (user == null || !user.IsActive)
        {
            RecoveryCodeRules.Hash(normalized ?? "AAAAAAAAAAAAAAAA", DummySalt);
            return await Reject(attempt, RecoveryFailureReasons.UnknownTarget, RecoveryCodeRedeemStatus.Rejected, ct);
        }

        if (!await _users.IsInRoleAsync(user, SystemRoles.SystemAdmin))
            return await Reject(attempt, RecoveryFailureReasons.TargetNotAdmin, RecoveryCodeRedeemStatus.Rejected, ct);

        // حساب قفل‌شده حتی با کد درست باز نمی‌شود (جلوی حدس زدن کد را می‌گیرد)
        if (await _users.IsLockedOutAsync(user))
            return await Reject(attempt, RecoveryFailureReasons.RateLimited, RecoveryCodeRedeemStatus.LockedOut, ct);

        var match = normalized == null ? null : (await ValidCodes(user.Id).ToListAsync(ct))
            .FirstOrDefault(c => RecoveryCodeRules.Matches(normalized, c.Salt, c.CodeHash));
        if (match == null)
        {
            await _users.AccessFailedAsync(user);
            return await Reject(attempt, RecoveryFailureReasons.InvalidCode, RecoveryCodeRedeemStatus.Rejected, ct);
        }

        // رمز جدید پیش از مصرف کد بررسی می‌شود تا یک رمز ضعیف کد را هدر ندهد
        var passwordErrors = await ValidatePasswordAsync(user, request.NewPassword);
        if (passwordErrors.Count > 0)
        {
            await _events.RecordFailureAsync(attempt, RecoveryFailureReasons.PasswordRejected, ct);
            return new RecoveryCodeRedeemResult { Status = RecoveryCodeRedeemStatus.PasswordRejected, PasswordErrors = passwordErrors };
        }

        // مصرف کد؛ concurrency token تضمین می‌کند دو درخواست هم‌زمان با یک کد فقط یکی موفق شود
        match.UsedAtUtc = Now();
        try { await _ctx.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            _ctx.Entry(match).State = EntityState.Detached;
            return await Reject(attempt, RecoveryFailureReasons.InvalidCode, RecoveryCodeRedeemStatus.Rejected, ct);
        }

        try
        {
            var token = await _users.GeneratePasswordResetTokenAsync(user);
            AdminMfaReset.Require(await _users.ResetPasswordAsync(user, token, request.NewPassword!));

            // رمز را خودِ صاحب حساب انتخاب کرده است: تغییر اجباری و رمز موقت لازم نیست
            user.MustChangePassword = false;
            user.TemporaryPasswordExpiresAt = null;
            await AdminMfaReset.ApplyAsync(_ctx, _users, user, ct);

            await _events.CompleteSuccessAsync(attempt with { TargetUserName = user.UserName }, ct);
        }
        catch (InvalidOperationException ex)
        {
            // کد مصرف شده ولی بازیابی کامل نشد: با کد بعدی دوباره تلاش می‌شود
            _log.LogError(ex, "Recovery-code redemption for administrator {UserId} did not complete", user.Id);
            return new RecoveryCodeRedeemResult { Status = RecoveryCodeRedeemStatus.Rejected };
        }

        return new RecoveryCodeRedeemResult
        {
            Status = RecoveryCodeRedeemStatus.Success,
            RemainingCodes = await GetRemainingCountAsync(user.Id, ct)
        };
    }

    private IQueryable<AdminRecoveryCode> ValidCodes(int userId) =>
        _ctx.AdminRecoveryCodes.Where(c => c.UserId == userId && c.UsedAtUtc == null && c.RevokedAtUtc == null);

    private async Task<List<string>> ValidatePasswordAsync(ApplicationUser user, string? password)
    {
        if (string.IsNullOrEmpty(password)) return new List<string> { "A new password is required." };
        var errors = new List<string>();
        foreach (var validator in _users.PasswordValidators)
        {
            var result = await validator.ValidateAsync(_users, user, password);
            if (!result.Succeeded) errors.AddRange(result.Errors.Select(e => e.Description));
        }
        return errors;
    }

    private async Task<RecoveryCodeRedeemResult> Reject(RecoveryAttempt attempt, string reason, RecoveryCodeRedeemStatus status, CancellationToken ct)
    {
        await _events.RecordFailureAsync(attempt, reason, ct);
        return new RecoveryCodeRedeemResult { Status = status };
    }

    private DateTime Now() => _clock.GetUtcNow().UtcDateTime;
}
