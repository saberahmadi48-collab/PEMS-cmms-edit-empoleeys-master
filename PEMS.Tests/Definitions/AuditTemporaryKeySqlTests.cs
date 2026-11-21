using Microsoft.EntityFrameworkCore;
using PEMS.Domain.Entities;
using PEMS.Tests.TestSupport;

namespace PEMS.Tests.Definitions;

/// <summary>
/// SCRUM-553: a changed foreign key that points at a row inserted in the same SaveChanges held EF's temporary key when the
/// audit row was built, so the audit log said "PublishedVersionId: 2 -> -2147482645". Only SQL Server shows this (the
/// in-memory provider hands out real keys at once), so this test runs on the regression database.
/// </summary>
public class AuditTemporaryKeySqlTests
{
    [Fact]
    public async Task AForeignKeyToARowInsertedInTheSameSave_IsAuditedWithItsRealId()
    {
        await using var db = PemsDbContextFactory.CreateSqlServerOrThrow();
        var stamp = Guid.NewGuid().ToString("N")[..8];
        var child = new Tag { Name = "QA553-child-" + stamp };
        db.Tags.Add(child);
        await db.SaveChangesAsync();

        var parent = new Tag { Name = "QA553-parent-" + stamp };
        db.Tags.Add(parent);
        child.ParentTag = parent;          // the new parent gets its id only during this save
        await db.SaveChangesAsync();

        var row = await db.AuditLogs.AsNoTracking()
            .Where(a => a.EntityName == nameof(Tag) && a.EntityKeyValue == child.TagId.ToString() && a.Action == "Update")
            .OrderByDescending(a => a.AuditLogId).FirstAsync();
        Assert.Contains($"ParentTagId: null -> {parent.TagId}", row.ChangedFieldsSummary);
        Assert.DoesNotContain("-21474", row.ChangedFieldsSummary);
        Assert.True(parent.TagId > 0);   // the regression database is re-created on every test run, so the two tags stay
    }
}
