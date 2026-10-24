using Microsoft.EntityFrameworkCore;
using PEMS.Application.Common;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Application.Organization;
using PEMS.Persistence.Context;

namespace PEMS.Persistence.Services;

/// <summary>
/// گزارش شخصی کارتابل + SLA پاسخ‌خواهی + عملکرد زیردستان. همه‌چیز از داده‌ی موجود محاسبه می‌شود (بدون ستون ذخیره‌شده):
/// گیرندگان نامه، ارجاع‌ها و نامه‌های پاسخ (ParentLetterId). زیردستان از چارت سازمانی می‌آیند و فقط شمارش‌ها نمایش داده می‌شود.
/// </summary>
public class LetterReportService : ILetterReportService
{
    private readonly PEMSDbContext _ctx;
    private readonly IOrganizationSubordinateResolver _org;

    public LetterReportService(PEMSDbContext ctx, IOrganizationSubordinateResolver org)
    {
        _ctx = ctx;
        _org = org;
    }

    public async Task<CartableReportDto> GetAsync(int userId, DateTime fromDate, DateTime toDate, bool canViewConfidential)
    {
        if (toDate < fromDate) (fromDate, toDate) = (toDate, fromDate);
        var rangeStart = fromDate.Date;
        var rangeEnd = toDate.Date.AddDays(1).AddTicks(-1);
        var now = DateTime.Now;
        var report = new CartableReportDto { FromDate = rangeStart, ToDate = rangeEnd.Date };

        var actor = await LetterFolderQuery.ResolveActorAsync(_ctx, userId, now);
        var visible = await LetterVisibility.ApplyAsync(_ctx, _ctx.Letters.AsNoTracking().Where(l => l.IsActive), canViewConfidential, userId);

        var received = LetterFolderQuery.Apply(_ctx, visible, LetterFolders.All, actor)
            .Where(l => l.LetterDate >= rangeStart && l.LetterDate <= rangeEnd);
        var inboxLetters = actor.EmployeeId.HasValue
            ? await LetterFolderQuery.Apply(_ctx, visible, LetterFolders.Inbox, actor).Where(l => l.LetterDate >= rangeStart && l.LetterDate <= rangeEnd)
                .Select(l => new { l.LetterId, l.LetterDate, l.ResponseRequired, l.ResponseDueDate, l.Status, l.DisciplineId }).ToListAsync()
            : null;

        report.Received = inboxLetters?.Count ?? 0;
        report.Sent = await LetterFolderQuery.Apply(_ctx, visible, LetterFolders.Sent, actor).CountAsync(l => l.LetterDate >= rangeStart && l.LetterDate <= rangeEnd);

        if (actor.EmployeeId.HasValue && inboxLetters != null)
        {
            var me = actor.EmployeeId.Value;
            var ids = inboxLetters.Select(l => l.LetterId).ToList();
            report.Unread = await _ctx.LetterRecipients.Where(r => r.EmployeeId == me && !r.IsRead && ids.Contains(r.LetterId)).Select(r => r.LetterId).Distinct().CountAsync();

            // ---- SLA پاسخ‌خواهی
            var required = inboxLetters.Where(l => l.ResponseRequired && l.ResponseDueDate.HasValue).ToList();
            report.ResponseRequired = required.Count;
            var requiredIds = required.Select(l => l.LetterId).ToList();
            var answers = (await _ctx.Letters.AsNoTracking()
                .Where(c => c.IsActive && c.ParentLetterId != null && requiredIds.Contains(c.ParentLetterId.Value) && LetterResponseRules.AnsweredChildStatuses.Contains(c.Status))
                .Select(c => new { Parent = c.ParentLetterId!.Value, c.LetterDate, c.CreatedDate }).ToListAsync())
                .GroupBy(c => c.Parent).ToDictionary(g => g.Key, g => g.Min(c => c.CreatedDate));
            var responseDays = new List<double>();
            foreach (var l in required)
            {
                if (answers.TryGetValue(l.LetterId, out var answeredAt))
                {
                    responseDays.Add(Math.Max(0, (answeredAt - l.LetterDate).TotalDays));
                    if (answeredAt.Date <= l.ResponseDueDate!.Value.Date) report.AnsweredOnTime++; else report.AnsweredLate++;
                }
                else if (!LetterResponseRules.FinishedStatuses.Contains(l.Status))
                {
                    if (l.ResponseDueDate!.Value.Date < now.Date) report.OpenOverdue++; else report.OpenWithinSla++;
                }
            }
            report.AvgResponseDays = responseDays.Count == 0 ? null : Math.Round(responseDays.Average(), 1);

            // ---- ارجاع‌ها
            var referrals = await _ctx.LetterReferrals.AsNoTracking()
                .Where(r => r.ReferredToEmployeeId == me && r.ReferredDate >= rangeStart && r.ReferredDate <= rangeEnd)
                .Select(r => new { r.Status, r.DueDate, r.ReferredDate, r.CompletedDate }).ToListAsync();
            report.OpenReferrals = referrals.Count(r => r.Status != "Completed");
            report.OverdueReferrals = referrals.Count(r => r.Status != "Completed" && r.DueDate.HasValue && r.DueDate.Value < now);
            report.CompletedReferrals = referrals.Count(r => r.Status == "Completed");
            var doneDays = referrals.Where(r => r.Status == "Completed" && r.CompletedDate.HasValue).Select(r => Math.Max(0, (r.CompletedDate!.Value - r.ReferredDate).TotalDays)).ToList();
            report.AvgReferralDays = doneDays.Count == 0 ? null : Math.Round(doneDays.Average(), 1);

            // ---- تفکیک
            var discIds = inboxLetters.Where(l => l.DisciplineId.HasValue).Select(l => l.DisciplineId!.Value).Distinct().ToList();
            var discNames = await _ctx.Disciplines.AsNoTracking().Where(d => discIds.Contains(d.DisciplineId)).ToDictionaryAsync(d => d.DisciplineId, d => d.Name);
            report.ByDiscipline = inboxLetters.GroupBy(l => l.DisciplineId.HasValue ? discNames.GetValueOrDefault(l.DisciplineId.Value) ?? "—" : "—")
                .Select(g => new CartableBucketDto(g.Key, g.Count())).OrderByDescending(b => b.Count).Take(8).ToList();
            report.ByMonth = inboxLetters.GroupBy(l => l.LetterDate.ToString("yyyy-MM")).OrderBy(g => g.Key)
                .Select(g => new CartableBucketDto(g.Key, g.Count())).ToList();

            // ---- زیردستان (چارت سازمانی)
            var subs = await _org.GetSubordinatesAsync(me);
            report.HasSubordinates = subs.Count > 0;
            if (subs.Count > 0) report.Subordinates = await SubordinateRowsAsync(subs, rangeStart, rangeEnd, now, canViewConfidential);
        }
        return report;
    }

    private async Task<List<SubordinateReportRowDto>> SubordinateRowsAsync(IReadOnlyList<SubordinateInfo> subs, DateTime rangeStart, DateTime rangeEnd, DateTime now, bool canViewConfidential)
    {
        var subIds = subs.Select(s => s.EmployeeId).ToList();

        // فقط شمارش؛ نامه‌ی محرمانه برای کسی که اجازه‌ی دیدنش را ندارد حتی در شمارش زیردستان نمی‌آید
        bool Countable(string level) => canViewConfidential || level == "Public" || level == "Internal";

        var recips = (await (from r in _ctx.LetterRecipients.AsNoTracking()
                             join l in _ctx.Letters.AsNoTracking() on r.LetterId equals l.LetterId
                             where r.EmployeeId != null && subIds.Contains(r.EmployeeId.Value) && l.IsActive
                                   && l.LetterDate >= rangeStart && l.LetterDate <= rangeEnd && !LetterAccessRules.PreSendStatuses.Contains(l.Status)
                             select new { Employee = r.EmployeeId!.Value, r.LetterId, r.IsRead, l.ConfidentialityLevel }).ToListAsync())
            .Where(x => Countable(x.ConfidentialityLevel)).ToList();

        var refs = (await (from r in _ctx.LetterReferrals.AsNoTracking()
                           join l in _ctx.Letters.AsNoTracking() on r.LetterId equals l.LetterId
                           where r.ReferredToEmployeeId != null && subIds.Contains(r.ReferredToEmployeeId.Value) && l.IsActive
                                 && r.ReferredDate >= rangeStart && r.ReferredDate <= rangeEnd
                           select new { Employee = r.ReferredToEmployeeId!.Value, r.Status, r.DueDate, r.ReferredDate, r.CompletedDate, l.ConfidentialityLevel }).ToListAsync())
            .Where(x => Countable(x.ConfidentialityLevel)).ToList();

        return subs.Select(s =>
        {
            var myRecips = recips.Where(r => r.Employee == s.EmployeeId).ToList();
            var myRefs = refs.Where(r => r.Employee == s.EmployeeId).ToList();
            var doneDays = myRefs.Where(r => r.Status == "Completed" && r.CompletedDate.HasValue).Select(r => Math.Max(0, (r.CompletedDate!.Value - r.ReferredDate).TotalDays)).ToList();
            return new SubordinateReportRowDto
            {
                EmployeeId = s.EmployeeId, Name = s.Name, Position = s.PositionTitle, Depth = s.Depth,
                Received = myRecips.Select(r => r.LetterId).Distinct().Count(),
                Unread = myRecips.Where(r => !r.IsRead).Select(r => r.LetterId).Distinct().Count(),
                OpenReferrals = myRefs.Count(r => r.Status != "Completed"),
                OverdueReferrals = myRefs.Count(r => r.Status != "Completed" && r.DueDate.HasValue && r.DueDate.Value < now),
                CompletedReferrals = myRefs.Count(r => r.Status == "Completed"),
                AvgReferralDays = doneDays.Count == 0 ? null : Math.Round(doneDays.Average(), 1)
            };
        }).ToList();
    }
}
