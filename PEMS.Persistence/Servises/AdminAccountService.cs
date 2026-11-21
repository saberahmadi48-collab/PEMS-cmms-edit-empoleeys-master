using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.Common;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;
using PEMS.Persistence.Identity;

namespace PEMS.Persistence.Services;

/// <summary>پیاده‌سازی <see cref="IAdminAccountService"/>: محافظ آخرین مدیر فعال + ریست حساب مدیر توسط مدیر دیگر (SCRUM-354).</summary>
public class AdminAccountService : IAdminAccountService
{
    private readonly PEMSDbContext _ctx;
    private readonly UserManager<ApplicationUser> _users;
    private readonly IRecoveryEventService _events;
    private readonly TimeProvider _clock;

    public AdminAccountService(PEMSDbContext ctx, UserManager<ApplicationUser> users, IRecoveryEventService events, TimeProvider? clock = null)
    {
        _ctx = ctx;
        _users = users;
        _events = events;
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<bool> WouldLeaveNoActiveAdminAsync(int userId, bool willBeActive, bool willBeAdmin, CancellationToken ct = default)
    {
        var activeAdminIds = await ActiveAdminIdsAsync(ct);

        // کاربر الان مدیر فعال نیست => این تغییر چیزی از مدیرهای فعال کم نمی‌کند
        if (!activeAdminIds.Contains(userId)) return false;

        // پس از تغییر همچنان مدیر فعال است => کم نمی‌شود
        if (willBeActive && willBeAdmin) return false;

        return !activeAdminIds.Any(id => id != userId);
    }

    public async Task<AdminResetResult> ResetAdminAsync(int actorUserId, int targetUserId, string? ipAddress, CancellationToken ct = default)
    {
        var actor = await _users.FindByIdAsync(actorUserId.ToString());
        var target = await _users.FindByIdAsync(targetUserId.ToString());
        var attempt = new RecoveryAttempt(RecoveryMethods.SecondAdminReset, targetUserId, target?.UserName, actorUserId, actor?.UserName, ipAddress);

        // ریست خود حتی برای مدیر ممنوع است (حتی اگر هدف ناموجود باشد، اول همین بررسی می‌شود تا نتیجه‌ی ثابتی بدهد)
        if (actorUserId == targetUserId)
            return await Reject(attempt, RecoveryFailureReasons.SelfResetNotAllowed, AdminResetStatus.SelfResetNotAllowed, ct);

        if (actor == null || !actor.IsActive || !await _users.IsInRoleAsync(actor, SystemRoles.SystemAdmin))
            return await Reject(attempt, RecoveryFailureReasons.ActorNotAdmin, AdminResetStatus.ActorNotAdmin, ct);

        if (target == null)
            return await Reject(attempt, RecoveryFailureReasons.UnknownTarget, AdminResetStatus.TargetNotFound, ct);

        if (!await _users.IsInRoleAsync(target, SystemRoles.SystemAdmin))
            return await Reject(attempt, RecoveryFailureReasons.TargetNotAdmin, AdminResetStatus.TargetNotAdmin, ct);

        // ۱) رمز موقت تصادفی (هرگز ثابت/پیش‌فرض)
        var temporaryPassword = AdminAccountRules.GenerateTemporaryPassword();
        var token = await _users.GeneratePasswordResetTokenAsync(target);
        var reset = await _users.ResetPasswordAsync(target, token, temporaryPassword);
        if (!reset.Succeeded)
            return await Reject(attempt, RecoveryFailureReasons.PasswordRejected, AdminResetStatus.PasswordRejected, ct);

        try
        {
            // ۲) تغییر اجباری رمز + انقضای ۲۴ ساعته
            var now = _clock.GetUtcNow().UtcDateTime;
            target.MustChangePassword = true;
            target.TemporaryPasswordExpiresAt = AdminAccountRules.ExpiryFrom(now);

            // ۳) ریست MFA برای ثبت‌نام مجدد + باز کردن قفل (گام مشترک هر سه مسیر)
            await AdminMfaReset.ApplyAsync(_ctx, _users, target, ct);

            // ۴) ممیزی + ابطال همه‌ی نشست‌ها + اعلان به مدیران (سرویس مشترک SCRUM-357)
            await _events.CompleteSuccessAsync(attempt with { TargetUserName = target.UserName }, ct);
        }
        catch (Exception ex) when (ex is InvalidOperationException or DbUpdateException)
        {
            // رمز عوض شده ولی ادامه‌ی کار ناقص ماند: رمز موقت را نشان نمی‌دهیم؛ مدیر باید ریست را تکرار کند (رمز قبلی بی‌صاحب می‌ماند)
            return new AdminResetResult { Status = AdminResetStatus.AuditFailed, TargetUserName = target.UserName };
        }

        return new AdminResetResult
        {
            Status = AdminResetStatus.Success,
            TemporaryPassword = temporaryPassword,
            ExpiresAtUtc = target.TemporaryPasswordExpiresAt,
            TargetUserName = target.UserName
        };
    }

    private async Task<AdminResetResult> Reject(RecoveryAttempt attempt, string reason, AdminResetStatus status, CancellationToken ct)
    {
        await _events.RecordFailureAsync(attempt, reason, ct);
        return new AdminResetResult { Status = status, TargetUserName = attempt.TargetUserName };
    }

    private async Task<List<int>> ActiveAdminIdsAsync(CancellationToken ct)
    {
        var roleId = await _ctx.Roles.Where(r => r.Name == SystemRoles.SystemAdmin).Select(r => (int?)r.Id).FirstOrDefaultAsync(ct);
        if (roleId == null) return new List<int>();

        var inRole = _ctx.UserRoles.Where(ur => ur.RoleId == roleId.Value).Select(ur => ur.UserId);
        return await _ctx.Users.Where(u => u.IsActive && inRole.Contains(u.Id)).Select(u => u.Id).ToListAsync(ct);
    }
}
