using Microsoft.EntityFrameworkCore;
using PEMS.Application.Common;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;

namespace PEMS.Persistence.Services;

/// <summary>هویت بیننده برای پوشه‌ها: کاربر، کارمند و دپارتمان او، و کارمندانی که کارتابلشان به او تفویض شده (فقط دیدن).</summary>
internal sealed record CartableActor(int UserId, int? EmployeeId, int? DepartmentId,
    IReadOnlyList<int> DelegatorEmployeeIds, IReadOnlyList<int> DelegatorDepartmentIds);

/// <summary>
/// پوشه‌های کارتابل روی IQueryable نامه. هیچ‌چیز این‌جا دسترسی را بیشتر نمی‌کند: پوشه همیشه بعد از
/// <see cref="LetterVisibility"/> اعمال می‌شود، و کارتابل تفویضی با محرمانگیِ خودِ تفویض‌گیرنده فیلتر می‌شود.
/// </summary>
internal static class LetterFolderQuery
{
    private static readonly string[] DraftLike = { "Draft", "ReturnedForRevision" };

    public static async Task<CartableActor> ResolveActorAsync(PEMSDbContext ctx, int userId, DateTime now)
    {
        var employeeId = await ctx.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.EmployeeId).FirstOrDefaultAsync();
        int? departmentId = employeeId.HasValue
            ? await ctx.Employees.AsNoTracking().Where(e => e.EmployeeId == employeeId).Select(e => (int?)e.DepartmentId).FirstOrDefaultAsync()
            : null;

        var today = now.Date;
        var delegators = await (from d in ctx.LetterDelegations.AsNoTracking()
                                join u in ctx.Users.AsNoTracking() on d.DelegatorUserId equals u.Id
                                join e in ctx.Employees.AsNoTracking() on u.EmployeeId equals e.EmployeeId
                                where d.DelegateUserId == userId && d.IsActive && d.FromDate <= today && d.ToDate >= today
                                select new { e.EmployeeId, e.DepartmentId }).Distinct().ToListAsync();
        return new CartableActor(userId, employeeId, departmentId,
            delegators.Select(d => d.EmployeeId).ToList(), delegators.Select(d => d.DepartmentId).Distinct().ToList());
    }

    /// <summary>نامه‌هایی که به این کارمندان رسیده‌اند: گیرنده (مستقیم یا دپارتمان)، ارجاع‌شده یا گیرنده‌ی ثبت‌شده‌ی نامه‌ی وارده.</summary>
    private static IQueryable<Letter> ReceivedBy(PEMSDbContext ctx, IQueryable<Letter> q, IReadOnlyList<int> employeeIds, IReadOnlyList<int> departmentIds)
    {
        if (employeeIds.Count == 0) return q.Where(_ => false);
        var preSend = LetterAccessRules.PreSendStatuses;
        return q.Where(x => !preSend.Contains(x.Status) && (
            (x.ReceiverEmployeeId != null && employeeIds.Contains(x.ReceiverEmployeeId.Value))
            || ctx.LetterRecipients.Any(r => r.LetterId == x.LetterId
                && ((r.EmployeeId != null && employeeIds.Contains(r.EmployeeId.Value))
                    || (r.EmployeeId == null && r.DepartmentId != null && departmentIds.Contains(r.DepartmentId.Value))))
            || ctx.LetterReferrals.Any(r => r.LetterId == x.LetterId && r.ReferredToEmployeeId != null && employeeIds.Contains(r.ReferredToEmployeeId.Value))));
    }

    /// <summary>folder == null: نمای پیش‌فرض «همه‌ی مکاتبات» بدون سطل زباله، حذف دائم و آرشیو شخصی.</summary>
    public static IQueryable<Letter> Apply(PEMSDbContext ctx, IQueryable<Letter> query, string? folder, CartableActor actor)
    {
        var uid = actor.UserId;
        var states = ctx.LetterUserStates;

        switch (folder)
        {
            case LetterFolders.All:
                return query.Where(x => !states.Any(s => s.LetterId == x.LetterId && s.ApplicationUserId == uid && s.IsPurged));

            case LetterFolders.Trash:
                return query.Where(x => states.Any(s => s.LetterId == x.LetterId && s.ApplicationUserId == uid && s.IsTrashed && !s.IsPurged));

            case LetterFolders.Archive:
                return query.Where(x => states.Any(s => s.LetterId == x.LetterId && s.ApplicationUserId == uid && s.IsArchived && !s.IsTrashed && !s.IsPurged));
        }

        // سایر پوشه‌ها: هرچه کاربر در سطل/آرشیو/حذف دائم شخصی گذاشته نمایش داده نمی‌شود
        query = query.Where(x => !states.Any(s => s.LetterId == x.LetterId && s.ApplicationUserId == uid && (s.IsTrashed || s.IsPurged || s.IsArchived)));

        switch (folder)
        {
            case LetterFolders.Inbox:
                return actor.EmployeeId.HasValue
                    ? ReceivedBy(ctx, query, new[] { actor.EmployeeId.Value }, actor.DepartmentId.HasValue ? new[] { actor.DepartmentId.Value } : Array.Empty<int>())
                    : query.Where(_ => false);

            case LetterFolders.Shared:
            {
                var me = actor.EmployeeId;
                return me.HasValue
                    ? query.Where(x => ctx.LetterReferrals.Any(r => r.LetterId == x.LetterId && r.ReferredToEmployeeId == me.Value))
                    : query.Where(_ => false);
            }

            case LetterFolders.Sent:
                return query.Where(x => x.RegisteredById == uid && !DraftLike.Contains(x.Status));

            case LetterFolders.Drafts:
                return query.Where(x => x.RegisteredById == uid && DraftLike.Contains(x.Status));

            case LetterFolders.Starred:
                return query.Where(x => ctx.LetterStars.Any(s => s.LetterId == x.LetterId && s.ApplicationUserId == uid));

            case LetterFolders.Delegated:
                return ReceivedBy(ctx, query, actor.DelegatorEmployeeIds, actor.DelegatorDepartmentIds);

            default:
                return query;
        }
    }
}
