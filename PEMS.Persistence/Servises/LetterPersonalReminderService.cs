using Microsoft.EntityFrameworkCore;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;

namespace PEMS.Persistence.Services;

public class LetterPersonalReminderService : ILetterPersonalReminderService
{
    public const int MaxOpenPerUser = 200;
    private readonly PEMSDbContext _ctx;
    private readonly INotificationService? _notifications;

    public LetterPersonalReminderService(PEMSDbContext ctx, INotificationService? notifications = null)
    {
        _ctx = ctx;
        _notifications = notifications;
    }

    public async Task<List<LetterReminderDto>> GetAsync(int userId, bool includeDone = false)
    {
        var now = DateTime.Now;
        // فقط یادآورِ نامه‌ای که هنوز برای کاربر قابل‌دیدن است (محرمانگی بعد از ثبت یادآور ممکن است عوض شود)
        var visible = await LetterVisibility.ApplyAsync(_ctx, _ctx.Letters.AsNoTracking().Where(l => l.IsActive), false, userId);
        var rows = await (from r in _ctx.LetterReminders.AsNoTracking()
                          join l in visible on r.LetterId equals l.LetterId
                          where r.OwnerUserId == userId && (includeDone || !r.IsDone)
                          orderby r.RemindAt
                          select new { r.LetterReminderId, r.LetterId, l.LetterNumber, l.Subject, r.RemindAt, r.Note, r.IsDone }).Take(200).ToListAsync();
        return rows.Select(r => new LetterReminderDto
        {
            LetterReminderId = r.LetterReminderId, LetterId = r.LetterId, LetterNumber = r.LetterNumber, Subject = r.Subject,
            RemindAt = r.RemindAt, Note = r.Note, IsDone = r.IsDone, IsDue = !r.IsDone && r.RemindAt <= now
        }).ToList();
    }

    public async Task<CartableOpResult> AddAsync(int letterId, DateTime remindAt, string? note, int userId, bool canViewConfidential)
    {
        if (remindAt <= DateTime.Now.AddMinutes(-1)) return CartableOpResult.Fail("Rem_Err_Past");
        if (remindAt > DateTime.Now.AddYears(2)) return CartableOpResult.Fail("Rem_Err_TooFar");

        var visible = await LetterVisibility.ApplyAsync(_ctx, _ctx.Letters.AsNoTracking().Where(l => l.IsActive && l.LetterId == letterId), canViewConfidential, userId);
        if (!await visible.AnyAsync()) return CartableOpResult.Fail("Rem_Err_NoLetter");
        if (await _ctx.LetterReminders.CountAsync(r => r.OwnerUserId == userId && !r.IsDone) >= MaxOpenPerUser) return CartableOpResult.Fail("Rem_Err_TooMany");

        note = note?.Trim();
        var row = new LetterReminder
        {
            LetterId = letterId, OwnerUserId = userId, RemindAt = remindAt,
            Note = string.IsNullOrEmpty(note) ? null : note[..Math.Min(300, note.Length)]
        };
        _ctx.LetterReminders.Add(row);
        await _ctx.SaveChangesAsync();
        return CartableOpResult.Ok(1, row.LetterReminderId);
    }

    public async Task<CartableOpResult> CompleteAsync(int reminderId, int userId)
    {
        var row = await _ctx.LetterReminders.FirstOrDefaultAsync(r => r.LetterReminderId == reminderId && r.OwnerUserId == userId);
        if (row == null) return CartableOpResult.Fail("Rem_Err_NotFound");
        row.IsDone = true;
        await _ctx.SaveChangesAsync();
        return CartableOpResult.Ok();
    }

    public async Task<CartableOpResult> DeleteAsync(int reminderId, int userId)
    {
        var row = await _ctx.LetterReminders.FirstOrDefaultAsync(r => r.LetterReminderId == reminderId && r.OwnerUserId == userId);
        if (row == null) return CartableOpResult.Fail("Rem_Err_NotFound");
        _ctx.LetterReminders.Remove(row);   // یادآور شخصی است؛ نه سند رسمی
        await _ctx.SaveChangesAsync();
        return CartableOpResult.Ok();
    }

    public async Task<int> DispatchDueAsync(DateTime now)
    {
        if (_notifications == null) return 0;
        var due = await (from r in _ctx.LetterReminders
                         join l in _ctx.Letters on r.LetterId equals l.LetterId
                         where !r.IsDone && r.NotifiedDate == null && r.RemindAt <= now
                         select new { Reminder = r, l.LetterNumber, l.Subject }).Take(200).ToListAsync();
        var sent = 0;
        foreach (var d in due)
        {
            try
            {
                await _notifications.CreateAsync(d.Reminder.OwnerUserId, "یادآور نامه", $"{d.LetterNumber} - {d.Subject}" + (string.IsNullOrEmpty(d.Reminder.Note) ? "" : " — " + d.Reminder.Note),
                    $"/Letter/Details/{d.Reminder.LetterId}", "Letter", d.Reminder.LetterId);
                d.Reminder.NotifiedDate = now;
                sent++;
            }
            catch { /* در نوبت بعد دوباره تلاش می‌شود */ }
        }
        if (sent > 0) await _ctx.SaveChangesAsync();
        return sent;
    }
}
