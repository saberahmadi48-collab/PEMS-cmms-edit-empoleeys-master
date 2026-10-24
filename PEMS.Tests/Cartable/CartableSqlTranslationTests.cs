using System.Reflection;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.Common;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;

namespace PEMS.Tests.Cartable;

/// <summary>
/// کارتابل روی InMemory تست می‌شود که ترجمه‌ی SQL را نمی‌سنجد؛ این تست‌ها همان پرس‌وجوها را با Provider سرور SQL فقط «کامپایل» می‌کنند (ToQueryString):
/// هیچ اتصالی باز نمی‌شود و هیچ دیتابیسی لمس نمی‌شود، ولی اگر عبارتی به SQL ترجمه نشود (مثلاً متد محلی داخل Where) تست می‌شکند.
/// </summary>
public class CartableSqlTranslationTests
{
    private static PEMSDbContext SqlContext()
        => new(new DbContextOptionsBuilder<PEMSDbContext>().UseSqlServer("Server=(local)\\NOPE;Database=NOPE;Trusted_Connection=True;TrustServerCertificate=True").Options);

    [Fact]
    public void EveryFolderSortAndThreadRootQuery_TranslatesToSqlServerSql()
    {
        using var ctx = SqlContext();
        var asm = typeof(PEMSDbContext).Assembly;
        var folderQuery = asm.GetType("PEMS.Persistence.Services.LetterFolderQuery")!;
        var actorType = asm.GetType("PEMS.Persistence.Services.CartableActor")!;
        var actor = Activator.CreateInstance(actorType, 7, (int?)3, (int?)2, new List<int> { 5, 6 }, new List<int> { 2, 4 })!;
        var apply = folderQuery.GetMethod("Apply", BindingFlags.Public | BindingFlags.Static)!;
        var order = typeof(PEMS.Persistence.Services.LetterService).GetMethod("OrderLetters", BindingFlags.NonPublic | BindingFlags.Static)!;

        foreach (var folder in new string?[] { null }.Concat(LetterFolders.Ordered))
        {
            var q = (IQueryable<Letter>)apply.Invoke(null, new object?[] { ctx, ctx.Letters.AsNoTracking().Where(l => l.IsActive), folder, actor })!;
            var scope = q;
            var roots = scope.Where(x => x.ParentLetterId == null || !scope.Any(p => p.LetterId == x.ParentLetterId));
            foreach (var sort in new string?[] { null, "subject", "number", "status", "priority", "sender", "received" })
            foreach (var desc in new[] { true, false })
            {
                var adv = new PEMS.Application.DTOs.LetterAdvancedFilter { SortBy = sort, SortDescending = desc };
                var ordered = (IQueryable<Letter>)order.Invoke(null, new object?[] { roots, adv })!;
                var sql = ordered.Skip(20).Take(50).ToQueryString();
                Assert.Contains("LetterUserStates", sql);   // every folder, including the default view, consults the personal trash / archive state
            }
        }
    }

    [Fact]
    public void ServiceJoinsAndFilters_TranslateToSqlServerSql()
    {
        using var ctx = SqlContext();
        var tagIds = new List<int> { 1, 2, 3 };
        var ids = new List<int> { 4, 5 };
        // thread walking
        _ = ctx.Letters.AsNoTracking().Where(l => l.IsActive && ids.Contains(l.LetterId)).Select(l => new { l.LetterId, l.ParentLetterId }).ToQueryString();
        _ = ctx.Letters.AsNoTracking().Where(l => l.IsActive && l.ParentLetterId != null && ids.Contains(l.ParentLetterId.Value)).Select(l => new { l.LetterId, l.ParentLetterId }).ToQueryString();
        // tag filter
        _ = ctx.Letters.Where(x => ctx.LetterTags.Any(t => t.LetterId == x.LetterId && tagIds.Contains(t.TagId))).ToQueryString();
        // counts: tag pairs joined with a folder query
        var visible = ctx.Letters.AsNoTracking().Where(l => l.IsActive);
        _ = (from lt in ctx.LetterTags.AsNoTracking() join l in visible on lt.LetterId equals l.LetterId select new { lt.TagId, lt.LetterId }).Distinct().ToQueryString();
        // unread count
        _ = visible.Where(x => ctx.LetterRecipients.Any(r => r.LetterId == x.LetterId && r.EmployeeId == 3 && !r.IsRead)).ToQueryString();
        // read toggle
        _ = (from r in ctx.LetterRecipients join l in ctx.Letters on r.LetterId equals l.LetterId where ids.Contains(r.LetterId) && r.EmployeeId == 3 && !LetterAccessRules.PreSendStatuses.Contains(l.Status) select r).ToQueryString();
        // delegation actor
        var today = DateTime.Today;
        _ = (from d in ctx.LetterDelegations.AsNoTracking() join u in ctx.Users.AsNoTracking() on d.DelegatorUserId equals u.Id join e in ctx.Employees.AsNoTracking() on u.EmployeeId equals e.EmployeeId
             where d.DelegateUserId == 7 && d.IsActive && d.FromDate <= today && d.ToDate >= today select new { e.EmployeeId, e.DepartmentId }).Distinct().ToQueryString();
        // delegation list with left join
        _ = (from u in ctx.Users.AsNoTracking() where ids.Contains(u.Id) join e in ctx.Employees.AsNoTracking() on u.EmployeeId equals e.EmployeeId into ge from e in ge.DefaultIfEmpty() select new { u.Id, u.FullName, Position = e != null ? e.Position : null }).ToQueryString();
        // reminders / report
        _ = (from r in ctx.LetterReminders.AsNoTracking() join l in visible on r.LetterId equals l.LetterId where r.OwnerUserId == 7 && !r.IsDone orderby r.RemindAt select new { r.LetterReminderId, l.LetterNumber }).ToQueryString();
        _ = (from r in ctx.LetterRecipients.AsNoTracking() join l in ctx.Letters.AsNoTracking() on r.LetterId equals l.LetterId
             where r.EmployeeId != null && ids.Contains(r.EmployeeId.Value) && l.IsActive && l.LetterDate >= today && !LetterAccessRules.PreSendStatuses.Contains(l.Status)
             select new { Employee = r.EmployeeId!.Value, r.LetterId, r.IsRead, l.ConfidentialityLevel }).ToQueryString();
        // contacts
        _ = (from e in ctx.Employees.AsNoTracking().Where(e => e.IsActive && (e.FirstName + " " + e.LastName).Contains("x")) join d in ctx.Departments.AsNoTracking() on e.DepartmentId equals d.DepartmentId into gd from d in gd.DefaultIfEmpty()
             orderby e.FirstName select new { e.EmployeeId, Department = d != null ? d.Name : null }).Take(30).ToQueryString();
        // conversation (referral / reply) queries and the approver picker
        int? sender = 3;
        _ = (from r in ctx.LetterRecipients.AsNoTracking() where ids.Contains(r.LetterId) && r.RecipientType != "Bcc"
             join e in ctx.Employees.AsNoTracking() on r.EmployeeId equals e.EmployeeId into ge from e in ge.DefaultIfEmpty()
             select new { r.LetterId, r.RecipientType, Name = e != null ? e.FirstName + " " + e.LastName : r.ExternalName }).ToQueryString();
        _ = ctx.LetterReferrals.Where(r => r.ReferredToEmployeeId == sender && r.Status != "Completed" && (r.LetterId == 5 || r.ReferralLetterId == 5)).ToQueryString();
        _ = ctx.LetterRecipients.Where(r => r.LetterId == 5 && r.RecipientType == "To" && r.EmployeeId != null).Select(r => r.EmployeeId!.Value).Distinct().ToQueryString();
        _ = (from u in ctx.Users.AsNoTracking() join e in ctx.Employees.AsNoTracking() on u.EmployeeId equals e.EmployeeId where u.IsActive && e.IsActive select u.Id).ToQueryString();
        _ = ctx.LetterApprovalSteps.AsNoTracking().Where(s => s.LetterId == 5 && s.Status == "Pending").OrderBy(s => s.StepOrder).Select(s => (int?)s.ApproverUserId).Take(1).ToQueryString();
        _ = ctx.Tags.Where(t => t.Name == "x" && t.OwnerUserId == null && t.ParentTagId == null).ToQueryString();
        // personal state lookups
        _ = ctx.LetterUserStates.Where(s => s.ApplicationUserId == 7 && ids.Contains(s.LetterId)).ToQueryString();
    }
}
