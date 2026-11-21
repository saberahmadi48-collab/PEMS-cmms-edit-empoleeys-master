using Microsoft.EntityFrameworkCore;
using PEMS.Application.Common;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;

namespace PEMS.Persistence.Services;

public class LetterComplianceService : ILetterComplianceService
{
    // نامه‌ی پیش‌ازارسال (پیش‌نویس/در انتظار تایید/برگشتی/ردشده) هرگز خودکار آرشیو نمی‌شود
    private static readonly string[] AutoArchivableStatuses = { "Registered", "UnderReview", "Referred", "Sent", "Acknowledged", "Issued", "Closed" };
    private const int BatchSize = 500;

    private readonly PEMSDbContext _context;

    public LetterComplianceService(PEMSDbContext context) => _context = context;

    public async Task<List<LetterViewLogEntryDto>> GetViewLogAsync(int letterId)
    {
        var logs = await _context.LetterViewLogs.AsNoTracking().Where(v => v.LetterId == letterId).OrderByDescending(v => v.LastViewedDate).ToListAsync();
        var ids = logs.Select(l => l.UserId).Distinct().ToList();
        var names = await _context.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName ?? u.UserName ?? "-");

        return logs.Select(l => new LetterViewLogEntryDto
        {
            UserId = l.UserId,
            UserName = names.TryGetValue(l.UserId, out var n) ? n : (l.UserName ?? "-"),
            FirstViewedDate = l.FirstViewedDate,
            LastViewedDate = l.LastViewedDate,
            ViewCount = l.ViewCount
        }).ToList();
    }

    public async Task<LetterComplianceReportDto?> BuildComplianceReportAsync(int letterId)
    {
        var letter = await _context.Letters.AsNoTracking().FirstOrDefaultAsync(l => l.LetterId == letterId);
        if (letter == null) return null;

        var userIds = new HashSet<int>();
        void Track(int? id) { if (id.HasValue) userIds.Add(id.Value); }

        var versions = await _context.LetterVersions.AsNoTracking().Where(v => v.LetterId == letterId).ToListAsync();
        var reviews = await _context.LetterReviews.AsNoTracking().Where(r => r.LetterId == letterId).ToListAsync();
        var steps = await _context.LetterApprovalSteps.AsNoTracking().Where(s => s.LetterId == letterId).OrderBy(s => s.StepOrder).ToListAsync();
        var recipients = await _context.LetterRecipients.AsNoTracking().Include(r => r.Employee).Include(r => r.Department).Where(r => r.LetterId == letterId).ToListAsync();
        var referrals = await _context.LetterReferrals.AsNoTracking().Include(r => r.ReferredToEmployee).Where(r => r.LetterId == letterId).ToListAsync();
        var views = await _context.LetterViewLogs.AsNoTracking().Where(v => v.LetterId == letterId).ToListAsync();
        var key = letterId.ToString();
        var audits = await _context.AuditLogs.AsNoTracking().Where(a => a.EntityName == nameof(Letter) && a.EntityKeyValue == key).ToListAsync();

        Track(letter.RegisteredById);
        foreach (var v in versions) Track(v.SavedByUserId);
        foreach (var r in reviews) Track(r.ReviewerUserId);
        foreach (var s in steps) Track(s.ApproverUserId);
        foreach (var v in views) Track(v.UserId);

        var ids = userIds.ToList();
        var names = await _context.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName ?? u.UserName ?? "-");
        string Who(int? id) => id.HasValue && names.TryGetValue(id.Value, out var n) ? n : "-";
        string Person(LetterRecipient r) => r.Employee != null ? (r.Employee.FirstName + " " + r.Employee.LastName).Trim() : (r.Department?.Name ?? r.ExternalName ?? "-");

        var rows = new List<LetterComplianceRowDto>
        {
            new() { Timestamp = letter.CreatedDate, Actor = Who(letter.RegisteredById), Category = "Created", Event = "Letter created", Detail = $"{letter.LetterType} / {letter.Category ?? "-"} / {letter.LetterNumber}" }
        };

        rows.AddRange(versions.Select(v => new LetterComplianceRowDto { Timestamp = v.SavedDate, Actor = Who(v.SavedByUserId), Category = "Version", Event = $"Version {v.VersionNo} ({v.Reason})", Detail = v.Subject }));
        rows.AddRange(reviews.Select(r => new LetterComplianceRowDto { Timestamp = r.CreatedDate, Actor = Who(r.ReviewerUserId), Category = "Review", Event = r.Decision, Detail = r.Comment }));
        rows.AddRange(steps.Where(s => s.DecidedDate.HasValue).Select(s => new LetterComplianceRowDto
        {
            Timestamp = s.DecidedDate!.Value, Actor = Who(s.ApproverUserId), Category = "ApprovalStep", Event = $"Step {s.StepOrder}: {s.Status}", Detail = s.Comment
        }));

        foreach (var r in recipients)
        {
            rows.Add(new LetterComplianceRowDto { Timestamp = r.CreatedDate, Actor = Who(letter.RegisteredById), Category = "Recipient", Event = $"Recipient added ({r.RecipientType})", Detail = Person(r) });
            if (r.IsRead && r.ReadDate.HasValue)
                rows.Add(new LetterComplianceRowDto { Timestamp = r.ReadDate.Value, Actor = Person(r), Category = "Read", Event = "Read by recipient", Detail = r.RecipientType });
        }

        foreach (var r in referrals)
        {
            var target = r.ReferredToEmployee != null ? r.ReferredToEmployee.FirstName + " " + r.ReferredToEmployee.LastName : "-";
            rows.Add(new LetterComplianceRowDto { Timestamp = r.ReferredDate, Actor = r.ReferredByName ?? "-", Category = "Referral", Event = "Referred", Detail = target + (string.IsNullOrWhiteSpace(r.Instructions) ? "" : " - " + r.Instructions) });
            if (r.CompletedDate.HasValue)
                rows.Add(new LetterComplianceRowDto { Timestamp = r.CompletedDate.Value, Actor = target, Category = "Referral", Event = "Referral completed", Detail = r.ResponseNote });
        }

        rows.AddRange(views.Select(v => new LetterComplianceRowDto
        {
            Timestamp = v.LastViewedDate, Actor = names.TryGetValue(v.UserId, out var n) ? n : (v.UserName ?? "-"), Category = "View",
            Event = $"Viewed ({v.ViewCount}x)", Detail = $"first {v.FirstViewedDate:yyyy-MM-dd HH:mm}"
        }));

        // ممیزی خودکار سیستم (Update/Archive/Discard/ReassignApprover): ChangedFieldsSummary همان‌طور که ثبت شده
        rows.AddRange(audits.Select(a => new LetterComplianceRowDto
        {
            Timestamp = a.Timestamp, Actor = a.UserName ?? (a.UserId.HasValue ? Who(a.UserId) : "system"), Category = "Audit", Event = a.Action, Detail = a.ChangedFieldsSummary
        }));

        return new LetterComplianceReportDto
        {
            LetterId = letter.LetterId, LetterNumber = letter.LetterNumber, Subject = letter.Subject, Status = letter.Status,
            ConfidentialityLevel = letter.ConfidentialityLevel, GeneratedAt = DateTime.Now,
            Rows = rows.OrderBy(r => r.Timestamp).ThenBy(r => r.Category).ToList()
        };
    }

    public async Task<int> ArchiveInactiveAsync(DateTime? now = null, int years = 2)
    {
        if (years < 1) return 0;
        var moment = now ?? DateTime.Now;
        var cutoff = moment.AddYears(-years);
        var archived = 0;

        // ۱) کاندیداها در SQL: فعال، وضعیت قابل‌آرشیو، و خودِ نامه حداقل years سال قدمت دارد
        var candidates = await _context.Letters
            .Where(l => l.IsActive && AutoArchivableStatuses.Contains(l.Status) && l.LetterDate < cutoff && l.CreatedDate < cutoff)
            .OrderBy(l => l.LetterId).Take(BatchSize * 4).ToListAsync();

        foreach (var chunk in candidates.Chunk(BatchSize))
        {
            var ids = chunk.Select(l => l.LetterId).ToList();

            // ۲) آخرین فعالیت هر نامه = بیشینه‌ی همه‌ی رویدادهایش (ارجاع، مرور، گیرنده، بازدید، ارسال/بستن/تایید)
            var lastReferral = (await _context.LetterReferrals.Where(r => ids.Contains(r.LetterId))
                .Select(r => new { r.LetterId, r.ReferredDate, r.ReadDate, r.CompletedDate, r.Status, r.DueDate }).ToListAsync());
            var lastReview = await _context.LetterReviews.Where(r => ids.Contains(r.LetterId)).GroupBy(r => r.LetterId).Select(g => new { g.Key, Max = g.Max(r => r.CreatedDate) }).ToListAsync();
            var lastView = await _context.LetterViewLogs.Where(v => ids.Contains(v.LetterId)).GroupBy(v => v.LetterId).Select(g => new { g.Key, Max = g.Max(v => v.LastViewedDate) }).ToListAsync();
            var lastRead = await _context.LetterRecipients.Where(r => ids.Contains(r.LetterId) && r.ReadDate != null).GroupBy(r => r.LetterId).Select(g => new { g.Key, Max = g.Max(r => r.ReadDate!.Value) }).ToListAsync();
            var answered = (await _context.Letters.Where(c => c.IsActive && c.ParentLetterId != null && ids.Contains(c.ParentLetterId.Value)
                && LetterResponseRules.AnsweredChildStatuses.Contains(c.Status)).Select(c => c.ParentLetterId!.Value).Distinct().ToListAsync()).ToHashSet();
            var hasNewerChild = (await _context.Letters.Where(c => c.IsActive && c.ParentLetterId != null && ids.Contains(c.ParentLetterId.Value) && c.CreatedDate >= cutoff)
                .Select(c => c.ParentLetterId!.Value).Distinct().ToListAsync()).ToHashSet();

            foreach (var l in chunk)
            {
                if (hasNewerChild.Contains(l.LetterId)) continue;                                       // a recent reply/thread = still alive
                if (l.ResponseRequired && !answered.Contains(l.LetterId) && l.Status != "Closed") continue;   // still waiting for an answer

                var refs = lastReferral.Where(r => r.LetterId == l.LetterId).ToList();
                if (refs.Any(r => r.Status != "Completed")) continue;                                    // open referral = open work

                var dates = new List<DateTime?> { l.LetterDate, l.CreatedDate, l.IssuedDate, l.ClosedDate, l.ApprovedDate, l.SignedDate, l.ReturnedDate, l.RejectedDate };
                foreach (var r in refs) { dates.Add(r.ReferredDate); dates.Add(r.ReadDate); dates.Add(r.CompletedDate); }
                dates.Add(lastReview.FirstOrDefault(x => x.Key == l.LetterId)?.Max);
                dates.Add(lastView.FirstOrDefault(x => x.Key == l.LetterId)?.Max);
                dates.Add(lastRead.FirstOrDefault(x => x.Key == l.LetterId)?.Max);

                var last = dates.Where(d => d.HasValue).Max()!.Value;
                if (last >= cutoff) continue;

                var previous = l.Status;
                l.Status = "Archived";
                l.ArchivedDate = moment;
                await _context.AuditLogs.AddAsync(new AuditLog
                {
                    EntityName = nameof(Letter), EntityKeyValue = l.LetterId.ToString(), Action = "AutoArchive",
                    ChangedFieldsSummary = $"Status: {previous} -> Archived | last activity {last:yyyy-MM-dd} (> {years} year(s) ago)", UserName = "system", Timestamp = moment
                });
                archived++;
            }

            await _context.SaveChangesAsync();
        }

        return archived;
    }
}
