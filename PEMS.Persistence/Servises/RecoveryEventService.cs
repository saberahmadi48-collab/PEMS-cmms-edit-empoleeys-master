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

/// <summary>پیاده‌سازی <see cref="IRecoveryEventService"/>: ممیزی + ابطال نشست + اعلان مشترک برای هر سه مسیر بازیابی حساب مدیر.</summary>
public class RecoveryEventService : IRecoveryEventService
{
    public const string SecurityEventsUrl = "/Admin/SecurityEvents";
    public const string NotificationRelatedType = "SecurityEvent";
    private const int MaxPageSize = 200;

    private readonly PEMSDbContext _ctx;
    private readonly UserManager<ApplicationUser> _users;
    private readonly INotificationService? _notifications;
    private readonly ILogger _log;

    public RecoveryEventService(PEMSDbContext ctx, UserManager<ApplicationUser> users,
        INotificationService? notifications = null, ILoggerFactory? loggerFactory = null)
    {
        _ctx = ctx;
        _users = users;
        _notifications = notifications;
        _log = loggerFactory?.CreateLogger("PEMS.Security") ?? NullLogger.Instance;
    }

    public async Task<RecoveryOutcome> CompleteSuccessAsync(RecoveryAttempt attempt, CancellationToken ct = default)
    {
        if (!RecoveryMethods.IsKnown(attempt.Method))
            throw new ArgumentException("Unknown recovery method.", nameof(attempt));
        if (attempt.TargetUserId is null)
            throw new ArgumentException("A successful recovery needs a target account.", nameof(attempt));

        var target = await _users.FindByIdAsync(attempt.TargetUserId.Value.ToString());
        if (target == null)
        {
            await RecordFailureAsync(attempt, RecoveryFailureReasons.UnknownTarget, ct);
            throw new InvalidOperationException("Recovery target account does not exist.");
        }

        if (!await _users.IsInRoleAsync(target, SystemRoles.SystemAdmin))
        {
            await RecordFailureAsync(attempt, RecoveryFailureReasons.TargetNotAdmin, ct);
            throw new InvalidOperationException("Recovery target is not an administrator account.");
        }

        // ۱) ابطال نشست‌ها پیش از ثبت موفقیت: SecurityStampValidator همه‌ی کوکی‌ها (از جمله «مرا به خاطر بسپار») را در بازه‌ی بعدی بررسی (حداکثر ۱ دقیقه) رد می‌کند.
        var revoke = await _users.UpdateSecurityStampAsync(target);
        if (!revoke.Succeeded)
        {
            await RecordFailureAsync(attempt with { TargetUserName = target.UserName }, RecoveryFailureReasons.SessionRevocationFailed, ct);
            throw new InvalidOperationException("Could not revoke the sessions of the recovered account.");
        }

        // ۲) رکورد ممیزی (اجباری: اگر ذخیره نشود، استثنا به فراخواننده می‌رسد)
        var ev = NewEvent(attempt with { TargetUserName = target.UserName }, RecoveryResults.Success, null);
        _ctx.SecurityEvents.Add(ev);
        await _ctx.SaveChangesAsync(ct);

        // ۳) اعلان به همه‌ی مدیران فعال (بهترین‌تلاش)
        var notified = await NotifyAdminsAsync(ev, ct);

        // ۴) لاگ ساخت‌یافته (مبنای ارسال به SIEM در SCRUM-212)
        _log.LogInformation(
            "SecurityEvent {EventType} {Result} method={Method} actor={Actor} target={Target} ip={Ip} id={EventId}",
            ev.EventType, ev.Result, ev.Method, ev.ActorUserName ?? "-", ev.TargetUserName ?? "-", ev.IpAddress ?? "-", ev.SecurityEventId);

        return new RecoveryOutcome { SecurityEventId = ev.SecurityEventId, AdminsNotified = notified };
    }

    public async Task<int> RecordFailureAsync(RecoveryAttempt attempt, string reason, CancellationToken ct = default)
    {
        var ev = NewEvent(attempt, RecoveryResults.Failure, RecoveryFailureReasons.Normalize(reason));
        _ctx.SecurityEvents.Add(ev);
        await _ctx.SaveChangesAsync(ct);

        _log.LogWarning(
            "SecurityEvent {EventType} {Result} method={Method} reason={Reason} actor={Actor} target={Target} ip={Ip} id={EventId}",
            ev.EventType, ev.Result, ev.Method, ev.FailureReason, ev.ActorUserName ?? "-", ev.TargetUserName ?? "-", ev.IpAddress ?? "-", ev.SecurityEventId);

        return ev.SecurityEventId;
    }

    public async Task<PagedResult<SecurityEventDto>> GetEventsAsync(string? method, string? result, int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var q = _ctx.SecurityEvents.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(method)) q = q.Where(x => x.Method == method);
        if (!string.IsNullOrWhiteSpace(result)) q = q.Where(x => x.Result == result);

        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(x => x.OccurredAtUtc).ThenByDescending(x => x.SecurityEventId)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new SecurityEventDto
            {
                SecurityEventId = x.SecurityEventId,
                EventType = x.EventType,
                Method = x.Method,
                Result = x.Result,
                FailureReason = x.FailureReason,
                ActorUserName = x.ActorUserName,
                TargetUserName = x.TargetUserName,
                IpAddress = x.IpAddress,
                OccurredAtUtc = x.OccurredAtUtc,
                Forwarded = x.ForwardedToSiemAt != null
            }).ToListAsync(ct);

        return new PagedResult<SecurityEventDto> { Items = rows, Page = page, PageSize = pageSize, TotalCount = total };
    }

    private static SecurityEvent NewEvent(RecoveryAttempt a, string result, string? reason) => new()
    {
        EventType = RecoveryEventTypes.AdminRecovery,
        Method = RecoveryMethods.IsKnown(a.Method) ? a.Method : "Unknown",
        Result = result,
        FailureReason = reason,
        ActorUserId = a.ActorUserId,
        ActorUserName = RecoveryEventRules.Trim(a.ActorUserName, RecoveryEventRules.MaxNameLength),
        TargetUserId = a.TargetUserId,
        TargetUserName = RecoveryEventRules.Trim(a.TargetUserName, RecoveryEventRules.MaxNameLength),
        IpAddress = RecoveryEventRules.NormalizeIp(a.IpAddress),
        OccurredAtUtc = DateTime.UtcNow
    };

    private async Task<int> NotifyAdminsAsync(SecurityEvent ev, CancellationToken ct)
    {
        if (_notifications == null) return 0;

        IList<ApplicationUser> admins;
        try { admins = await _users.GetUsersInRoleAsync(SystemRoles.SystemAdmin); }
        catch (Exception ex)
        {
            _log.LogError(ex, "Could not load administrators to notify about security event {EventId}", ev.SecurityEventId);
            return 0;
        }

        var title = "بازیابی حساب مدیر";
        var message = $"حساب مدیر «{ev.TargetUserName}» با روش «{RecoveryMethods.Label(ev.Method)}» بازیابی شد و همه‌ی نشست‌های آن بسته شد.";
        var sent = 0;
        foreach (var admin in admins.Where(a => a.IsActive).DistinctBy(a => a.Id))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await _notifications.CreateAsync(admin.Id, title, message, SecurityEventsUrl, NotificationRelatedType, ev.SecurityEventId);
                sent++;
            }
            catch (Exception ex)
            {
                // شکست اعلان نباید ریست انجام‌شده را برگرداند؛ فقط لاگ می‌شود.
                _log.LogError(ex, "Notification to administrator {AdminId} failed for security event {EventId}", admin.Id, ev.SecurityEventId);
            }
        }
        return sent;
    }
}
