using Microsoft.EntityFrameworkCore;
using PEMS.Domain.Entities;
using PEMS.Persistence.Identity;
using PEMS.Tests.TestSupport;

namespace PEMS.Tests.Security;

/// <summary>
/// E24: ممیزی عمومی (AuditLog) هرگز راز نمی‌نویسد (هش رمز قبل→بعد پیش‌تر در ChangedFieldsSummary می‌ماند) و ایجاد/حذف موجودیت‌های امنیتی (کاربر، تخصیص نقش) مقدار
/// فیلدهای غیرمحرمانه را ثبت می‌کند؛ تغییر فیلدها قبل→بعد و کاربر/زمان همیشه ثبت می‌شود.
/// </summary>
public class SecurityAuditLogTests
{
    [Fact]
    public async Task Password_hash_changes_are_masked_and_technical_stamps_are_never_logged()
    {
        await using var ctx = PemsDbContextFactory.CreateInMemory("sec-audit-" + Guid.NewGuid());
        ctx.CurrentUserName = "admin";
        var user = new ApplicationUser { UserName = "alice", NormalizedUserName = "ALICE", FullName = "Alice", Email = "a@t.local", PasswordHash = "HASH-OLD-SECRET", SecurityStamp = "stamp-1", ConcurrencyStamp = "c-1" };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        user.PasswordHash = "HASH-NEW-SECRET"; user.SecurityStamp = "stamp-2"; user.FullName = "Alice B";
        await ctx.SaveChangesAsync();

        var logs = await ctx.AuditLogs.AsNoTracking().Where(a => a.EntityName == "ApplicationUser").OrderBy(a => a.AuditLogId).ToListAsync();
        Assert.Equal(new[] { "Insert", "Update" }, logs.Select(l => l.Action).ToArray());
        Assert.All(logs, l => { Assert.Equal("admin", l.UserName); Assert.DoesNotContain("HASH-", l.ChangedFieldsSummary ?? ""); Assert.DoesNotContain("stamp", l.ChangedFieldsSummary ?? "", StringComparison.OrdinalIgnoreCase); });
        Assert.Contains("PasswordHash: [changed]", logs[1].ChangedFieldsSummary);
        Assert.Contains("FullName: Alice -> Alice B", logs[1].ChangedFieldsSummary);          // قبل → بعد
        Assert.Contains("UserName=alice", logs[0].ChangedFieldsSummary);                     // مقدار ایجادشده (بدون راز)
    }

    [Fact]
    public async Task Role_assignment_creation_and_removal_record_what_was_granted_to_whom()
    {
        await using var ctx = PemsDbContextFactory.CreateInMemory("sec-audit2-" + Guid.NewGuid());
        ctx.CurrentUserName = "admin";
        var assignment = new RoleAssignment { UserId = 5, RoleId = 9, ScopeType = "Discipline", ScopeValue = "3", AssignmentType = "Direct", IsActive = true };
        ctx.RoleAssignments.Add(assignment);
        await ctx.SaveChangesAsync();
        ctx.RoleAssignments.Remove(assignment);
        await ctx.SaveChangesAsync();

        var logs = await ctx.AuditLogs.AsNoTracking().Where(a => a.EntityName == "RoleAssignment").OrderBy(a => a.AuditLogId).ToListAsync();
        Assert.Equal(new[] { "Insert", "Delete" }, logs.Select(l => l.Action).ToArray());
        Assert.All(logs, l => { Assert.Contains("UserId=5", l.ChangedFieldsSummary); Assert.Contains("RoleId=9", l.ChangedFieldsSummary); Assert.Contains("ScopeValue=3", l.ChangedFieldsSummary); });
    }
}
