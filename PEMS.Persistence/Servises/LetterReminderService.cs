using Microsoft.EntityFrameworkCore;
using PEMS.Application.Common;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;

namespace PEMS.Persistence.Services;

/// <summary>
/// یادآوری روزانه‌ی معوقات مکاتبات (Phase 3):
///   - ارجاع باز با مهلت گذشته  -> کاربر ارجاع‌گیرنده + نویسنده‌ی نامه
///   - پاسخ‌خواهی معوق (بدون نامه‌ی پاسخ صادرشده) -> نویسنده‌ی نامه
/// نامه‌ی Closed/Rejected/غیرفعال هرگز یادآوری نمی‌شود. ضد تکرار: برای هر (کاربر، نامه، عنوان) در همان روز فقط یک اعلان.
/// شکست یک اعلان بقیه را متوقف نمی‌کند.
/// </summary>
public class LetterReminderService : ILetterReminderService
{
    public const string ReferralTitle = "Reminder: referral overdue";
    public const string ResponseTitle = "Reminder: response overdue";

    private readonly PEMSDbContext _context;
    private readonly INotificationService _notifications;

    public LetterReminderService(PEMSDbContext context, INotificationService notifications)
    {
        _context = context;
        _notifications = notifications;
    }

    public async Task<LetterReminderResult> SendDueRemindersAsync(DateTime? now = null)
    {
        var moment = now ?? DateTime.Now;
        var today = moment.Date;
        var result = new LetterReminderResult();

        var already = (await _context.Notifications
            .Where(n => n.CreatedDate >= today && n.RelatedType == "Letter" && n.RelatedId != null
                && (n.Title == ReferralTitle || n.Title == ResponseTitle))
            .Select(n => new { n.UserId, LetterId = n.RelatedId!.Value, n.Title })
            .ToListAsync()).Select(x => (x.UserId, x.LetterId, x.Title)).ToHashSet();

        async Task SendAsync(int userId, int letterId, string title, string message, bool isReferral)
        {
            if (!already.Add((userId, letterId, title))) { result.SkippedDuplicates++; return; }

            try
            {
                await _notifications.CreateAsync(userId, title, message, $"/Letter/Details/{letterId}", "Letter", letterId);
                if (isReferral) result.ReferralReminders++; else result.ResponseReminders++;
            }
            catch (Exception)
            {
                result.Failed++;
            }
        }

        // ---- ارجاع‌های باز و معوق
        var overdueReferrals = await (from r in _context.LetterReferrals
                                      join l in _context.Letters on r.LetterId equals l.LetterId
                                      where l.IsActive && !LetterResponseRules.FinishedStatuses.Contains(l.Status)
                                            && r.Status != "Completed" && r.DueDate != null && r.DueDate < moment
                                      select new { r.LetterId, l.LetterNumber, l.Subject, l.RegisteredById, r.ReferredToEmployeeId, DueDate = r.DueDate!.Value })
                                     .ToListAsync();

        var employeeIds = overdueReferrals.Where(r => r.ReferredToEmployeeId.HasValue).Select(r => r.ReferredToEmployeeId!.Value).Distinct().ToList();
        var userByEmployee = (await _context.Users
            .Where(u => u.IsActive && u.EmployeeId != null && employeeIds.Contains(u.EmployeeId.Value))
            .Select(u => new { u.Id, EmployeeId = u.EmployeeId!.Value })
            .ToListAsync()).ToLookup(u => u.EmployeeId, u => u.Id);

        string Overdue(DateTime due) => $"{(int)Math.Max(1, (today - due.Date).TotalDays)} day(s) overdue since {due:yyyy-MM-dd}";

        foreach (var r in overdueReferrals)
        {
            var label = $"{r.LetterNumber} - {r.Subject}: {Overdue(r.DueDate)}";

            if (r.ReferredToEmployeeId.HasValue)
                foreach (var userId in userByEmployee[r.ReferredToEmployeeId.Value])
                    await SendAsync(userId, r.LetterId, ReferralTitle, label, isReferral: true);

            if (r.RegisteredById.HasValue)
                await SendAsync(r.RegisteredById.Value, r.LetterId, ReferralTitle, label, isReferral: true);
        }

        // ---- پاسخ‌خواهی معوق (محاسبه‌شده، مثل فهرست/داشبورد)
        var candidates = await _context.Letters
            .Where(l => l.IsActive && l.ResponseRequired && l.ResponseDueDate != null && l.ResponseDueDate < today
                && !LetterResponseRules.FinishedStatuses.Contains(l.Status) && l.RegisteredById != null)
            .Select(l => new { l.LetterId, l.LetterNumber, l.Subject, l.RegisteredById, DueDate = l.ResponseDueDate!.Value })
            .ToListAsync();

        var ids = candidates.Select(c => c.LetterId).ToList();
        var answered = (await _context.Letters
            .Where(c => c.IsActive && c.ParentLetterId != null && ids.Contains(c.ParentLetterId.Value)
                && LetterResponseRules.AnsweredChildStatuses.Contains(c.Status))
            .Select(c => c.ParentLetterId!.Value).Distinct().ToListAsync()).ToHashSet();

        foreach (var c in candidates.Where(c => !answered.Contains(c.LetterId)))
            await SendAsync(c.RegisteredById!.Value, c.LetterId, ResponseTitle, $"{c.LetterNumber} - {c.Subject}: {Overdue(c.DueDate)}", isReferral: false);

        return result;
    }
}
