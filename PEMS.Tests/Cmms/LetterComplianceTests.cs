using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PEMS.Application.DTOs;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;
using PEMS.Web.BackgroundJobs;
using static PEMS.Tests.TestSupport.LetterTestWorld;

namespace PEMS.Tests.Cmms;

/// <summary>گزارش انطباق، گزارش «چه کسی دید» و آرشیو خودکار بی‌فعالیتی (باید فقط نامه‌ی واقعاً راکد را آرشیو کند).</summary>
public class LetterComplianceTests
{
    private static readonly DateTime Now = new(2026, 10, 21, 9, 0, 0);

    // ------------------------------------------------------------------ view log + report

    [Fact]
    public async Task ViewLog_ListsEachViewerOnceWithTheirCount_MostRecentFirst()
    {
        var (compose, letters, ctx, _) = Services(nameof(ViewLog_ListsEachViewerOnceWithTheirCount_MostRecentFirst));
        var id = (await compose.SaveDraftAsync(ValidInput(), AuthorActor())).LetterId;
        await letters.SendAsync(id, Author);

        await letters.GetDetailForViewerAsync(id, new LetterViewer(To1, 2, false, "u2"));
        await letters.GetDetailForViewerAsync(id, new LetterViewer(To1, 2, false, "u2"));
        await Task.Delay(15);
        await letters.GetDetailForViewerAsync(id, new LetterViewer(Other, 5, false, "u5"));

        var log = await new LetterComplianceService(ctx).GetViewLogAsync(id);

        Assert.Equal(new[] { Other, To1 }, log.Select(l => l.UserId));
        Assert.Equal(2, log.Single(l => l.UserId == To1).ViewCount);
        Assert.Equal("User 2", log.Single(l => l.UserId == To1).UserName);
    }

    [Fact]
    public async Task ComplianceReport_ContainsTheWholeHistory_IncludingBccAndEveryDecision_InTimeOrder()
    {
        var (compose, letters, ctx, _) = Services(nameof(ComplianceReport_ContainsTheWholeHistory_IncludingBccAndEveryDecision_InTimeOrder));
        var input = ValidInput();
        input.BccEmployeeIds = new() { 4 };
        input.ApprovalRequired = true;
        input.ApproverUserIds = new() { Approver1 };
        var id = (await compose.SaveDraftAsync(input, AuthorActor())).LetterId;
        await letters.SubmitForReviewAsync(id, Author);
        await letters.ApproveAsync(id, Approver1, "approved with thanks");
        await letters.SendAsync(id, Author);
        await letters.GetDetailForViewerAsync(id, new LetterViewer(To1, 2, false, "u2"));

        var report = await new LetterComplianceService(ctx).BuildComplianceReportAsync(id);

        Assert.NotNull(report);
        Assert.Equal("Sent", report!.Status);
        Assert.Contains(report.Rows, r => r.Category == "Created" && r.Actor == "User 1");
        Assert.Contains(report.Rows, r => r.Category == "Version" && r.Event.Contains("(Submitted)"));
        Assert.Contains(report.Rows, r => r.Category == "Version" && r.Event.Contains("(Sent)"));
        Assert.Contains(report.Rows, r => r.Category == "Review" && r.Actor == "User 6" && r.Detail == "approved with thanks");
        Assert.Contains(report.Rows, r => r.Category == "ApprovalStep" && r.Event == "Step 1: Approved");
        Assert.Contains(report.Rows, r => r.Category == "Recipient" && r.Event.Contains("(Bcc)"));         // compliance sees the Bcc
        Assert.Contains(report.Rows, r => r.Category == "Read" && r.Actor == "E2 L2");
        Assert.Contains(report.Rows, r => r.Category == "View" && r.Actor == "User 2");
        Assert.Contains(report.Rows, r => r.Category == "Audit");
        Assert.Equal(report.Rows.OrderBy(r => r.Timestamp).Select(r => r.Timestamp), report.Rows.Select(r => r.Timestamp));

        Assert.Null(await new LetterComplianceService(ctx).BuildComplianceReportAsync(999999));
    }

    // ------------------------------------------------------------------ auto-archive

    private static async Task<Letter> OldLetterAsync(PEMSDbContext ctx, string status = "Sent", int ageYears = 3, Action<Letter>? tweak = null)
    {
        var l = new Letter
        {
            LetterType = "Internal", LetterNumber = "OLD-" + Guid.NewGuid().ToString("N")[..8], Subject = "old", Status = status, IsActive = true,
            ConfidentialityLevel = "Internal", RegisteredById = Author, LetterDate = Now.AddYears(-ageYears)
        };
        ctx.Letters.Add(l);
        await ctx.SaveChangesAsync();
        // the context stamps CreatedDate on insert, so backdate with a second save
        l.CreatedDate = Now.AddYears(-ageYears);
        l.IssuedDate = Now.AddYears(-ageYears);
        tweak?.Invoke(l);
        await ctx.SaveChangesAsync();
        return l;
    }

    private static string StatusOf(PEMSDbContext ctx, int id) { ctx.ChangeTracker.Clear(); return ctx.Letters.Single(l => l.LetterId == id).Status; }

    [Fact]
    public async Task AStaleSentLetter_IsArchived_WithAnAuditRow_AndASecondRunDoesNothing()
    {
        var ctx = NewContext(nameof(AStaleSentLetter_IsArchived_WithAnAuditRow_AndASecondRunDoesNothing));
        var old = await OldLetterAsync(ctx);
        var svc = new LetterComplianceService(ctx);

        Assert.Equal(1, await svc.ArchiveInactiveAsync(Now, 2));
        Assert.Equal(0, await svc.ArchiveInactiveAsync(Now, 2));

        ctx.ChangeTracker.Clear();
        var archived = ctx.Letters.Single(l => l.LetterId == old.LetterId);
        Assert.Equal("Archived", archived.Status);
        Assert.Equal(Now, archived.ArchivedDate);
        Assert.True(archived.IsActive);                                                      // archived, never deleted
        var audit = Assert.Single(ctx.AuditLogs.Where(a => a.Action == "AutoArchive"));
        Assert.Contains("Sent -> Archived", audit.ChangedFieldsSummary);
        Assert.Equal("system", audit.UserName);
    }

    [Theory]
    [InlineData("Draft")]
    [InlineData("PendingApproval")]
    [InlineData("ReturnedForRevision")]
    [InlineData("Approved")]
    [InlineData("Rejected")]
    [InlineData("Archived")]
    public async Task PreSendOrAlreadyArchivedLetters_AreNeverTouched(string status)
    {
        var ctx = NewContext("NeverTouched_" + status);
        var l = await OldLetterAsync(ctx, status);

        Assert.Equal(0, await new LetterComplianceService(ctx).ArchiveInactiveAsync(Now, 2));
        Assert.Equal(status, StatusOf(ctx, l.LetterId));
    }

    [Fact]
    public async Task RecentLetters_AndLettersWithRecentActivity_AreKept()
    {
        var ctx = NewContext(nameof(RecentLetters_AndLettersWithRecentActivity_AreKept));
        var recent = await OldLetterAsync(ctx, ageYears: 1);
        var viewed = await OldLetterAsync(ctx);
        var reviewed = await OldLetterAsync(ctx);
        var read = await OldLetterAsync(ctx);
        var threaded = await OldLetterAsync(ctx);
        var stale = await OldLetterAsync(ctx);

        ctx.LetterViewLogs.Add(new LetterViewLog { LetterId = viewed.LetterId, UserId = To1, FirstViewedDate = Now.AddMonths(-3), LastViewedDate = Now.AddMonths(-3) });
        ctx.LetterReviews.Add(new LetterReview { LetterId = reviewed.LetterId, Decision = LetterReviewDecisions.Approved, IsActive = true });
        ctx.LetterRecipients.Add(new LetterRecipient { LetterId = read.LetterId, EmployeeId = 2, RecipientType = "To", IsRead = true, ReadDate = Now.AddMonths(-2) });
        ctx.Letters.Add(new Letter { LetterNumber = "CHILD-1", LetterType = "Internal", Subject = "re", Status = "Sent", IsActive = true, ParentLetterId = threaded.LetterId });
        await ctx.SaveChangesAsync();
        // LetterReview.CreatedDate is stamped on insert (= Now of the machine, far newer than the 2026 test clock) -> already "recent"
        ctx.ChangeTracker.Clear();

        var archivedCount = await new LetterComplianceService(ctx).ArchiveInactiveAsync(Now, 2);

        Assert.Equal(1, archivedCount);
        Assert.Equal("Archived", StatusOf(ctx, stale.LetterId));
        foreach (var kept in new[] { recent, viewed, reviewed, read, threaded })
            Assert.Equal("Sent", StatusOf(ctx, kept.LetterId));
    }

    [Fact]
    public async Task OpenWork_BlocksArchiving_ButFinishedWorkDoesNot()
    {
        var ctx = NewContext(nameof(OpenWork_BlocksArchiving_ButFinishedWorkDoesNot));
        var openReferral = await OldLetterAsync(ctx);
        var doneReferral = await OldLetterAsync(ctx);
        var waiting = await OldLetterAsync(ctx, tweak: l => { l.ResponseRequired = true; l.ResponseDueDate = Now.AddYears(-2); });
        var answered = await OldLetterAsync(ctx, tweak: l => { l.ResponseRequired = true; l.ResponseDueDate = Now.AddYears(-2); });
        var closedAndRequired = await OldLetterAsync(ctx, "Closed", tweak: l => { l.ResponseRequired = true; l.ResponseDueDate = Now.AddYears(-2); });

        ctx.LetterReferrals.Add(new LetterReferral { LetterId = openReferral.LetterId, Status = "Pending", ReferredDate = Now.AddYears(-3) });
        ctx.LetterReferrals.Add(new LetterReferral { LetterId = doneReferral.LetterId, Status = "Completed", ReferredDate = Now.AddYears(-3), CompletedDate = Now.AddYears(-3) });
        var reply = new Letter { LetterNumber = "ANS-1", LetterType = "Outgoing", Subject = "answer", Status = "Issued", IsActive = true, ParentLetterId = answered.LetterId };
        ctx.Letters.Add(reply);
        await ctx.SaveChangesAsync();
        reply.CreatedDate = Now.AddYears(-3);                                    // an old answer, so the thread itself is not "recent"
        await ctx.SaveChangesAsync();

        await new LetterComplianceService(ctx).ArchiveInactiveAsync(Now, 2);

        Assert.Equal("Sent", StatusOf(ctx, openReferral.LetterId));               // an open referral is open work
        Assert.Equal("Archived", StatusOf(ctx, doneReferral.LetterId));
        Assert.Equal("Sent", StatusOf(ctx, waiting.LetterId));                    // still waiting for an answer
        Assert.Equal("Archived", StatusOf(ctx, answered.LetterId));
        Assert.Equal("Archived", StatusOf(ctx, closedAndRequired.LetterId));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ANonPositiveWindow_ArchivesNothing(int years)
    {
        var ctx = NewContext("Window_" + years);
        await OldLetterAsync(ctx);

        Assert.Equal(0, await new LetterComplianceService(ctx).ArchiveInactiveAsync(Now, years));
    }

    [Fact]
    public async Task InactiveRowsAreIgnored()
    {
        var ctx = NewContext(nameof(InactiveRowsAreIgnored));
        var gone = await OldLetterAsync(ctx, tweak: l => l.IsActive = false);

        Assert.Equal(0, await new LetterComplianceService(ctx).ArchiveInactiveAsync(Now, 2));
        Assert.Equal("Sent", StatusOf(ctx, gone.LetterId));
    }

    // ------------------------------------------------------------------ job configuration

    private sealed class ExplodingScopeFactory : IServiceScopeFactory
    {
        public int Created { get; private set; }
        public IServiceScope CreateScope() { Created++; throw new InvalidOperationException("must not run yet"); }
    }

    [Fact]
    public void AutoArchive_IsOffByDefault_InTheOptionsAndInTheShippedConfiguration()
    {
        Assert.Equal(0, new LetterReminderOptions().AutoArchiveAfterYears);

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        string? envDir = null;
        while (dir != null && envDir == null)
        {
            var c = Path.Combine(dir.FullName, "env");
            if (File.Exists(Path.Combine(c, "local.env"))) envDir = c;
            dir = dir.Parent;
        }
        envDir ??= Path.Combine(Environment.GetEnvironmentVariable("PEMS_REPO_ROOT") ?? "", "env");
        var shipped = Directory.EnumerateFiles(envDir, "*.env").Where(f => !f.EndsWith(".secrets.env")).ToList();
        Assert.NotEmpty(shipped);
        foreach (var file in shipped)
            Assert.Equal("0", PEMS.Shared.Configuration.EnvFiles.Parse(File.ReadAllLines(file))["LetterReminders:AutoArchiveAfterYears"]);
    }

    [Fact]
    public async Task TheDailyJob_RunsWhenOnlyAutoArchiveIsEnabled_ButNeverAtStartup()
    {
        var factory = new ExplodingScopeFactory();
        var job = new LetterReminderBackgroundService(factory, Options.Create(new LetterReminderOptions { Enabled = false, AutoArchiveAfterYears = 2 }),
            NullLogger<LetterReminderBackgroundService>.Instance);

        await job.StartAsync(CancellationToken.None);
        await Task.Delay(100);
        Assert.False(job.ExecuteTask!.IsCompleted);              // waiting for the scheduled time, not exiting like the fully-disabled job
        await job.StopAsync(CancellationToken.None);

        Assert.Equal(0, factory.Created);
    }
}
