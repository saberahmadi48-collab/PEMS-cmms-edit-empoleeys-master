using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PEMS.Application.Common;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Identity;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;
using PEMS.Web.BackgroundJobs;

namespace PEMS.Tests.Cmms;

/// <summary>
/// Phase 3 مکاتبات: نشانه‌های قالب ({{Date}} ...)، تاریخچه‌ی پاسخ‌خواهی در تایم‌لاین، یادآوری روزانه‌ی معوقات
/// (ضد تکرار، فقط افراد درست، شکست یک اعلان بقیه را نمی‌اندازد) و زمان‌بندی/خاموش‌بودن پیش‌فرض سرویس پس‌زمینه.
/// </summary>
public class LetterPhase3Tests
{
    // ------------------------------------------------------------------ placeholders

    private static readonly Dictionary<string, string> Values = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Date"] = "2026-03-10", ["UserName"] = "Sara <Admin>", ["LetterType"] = "Outgoing"
    };

    [Fact]
    public void Apply_ReplacesKnownTokens_CaseInsensitively_AllowingSpaces()
    {
        Assert.Equal("On 2026-03-10 and 2026-03-10 and 2026-03-10",
            LetterTemplatePlaceholders.Apply("On {{Date}} and {{ date }} and {{DATE}}", Values, htmlEncode: false));
    }

    [Fact]
    public void Apply_HtmlEncodesValuesInBody_ButNotInPlainSubject()
    {
        Assert.Equal("Sara &lt;Admin&gt;", LetterTemplatePlaceholders.Apply("{{UserName}}", Values, htmlEncode: true));
        Assert.Equal("Sara <Admin>", LetterTemplatePlaceholders.Apply("{{UserName}}", Values, htmlEncode: false));

        var injected = new Dictionary<string, string> { ["ParentSubject"] = "<script>alert(1)</script>" };
        var body = LetterTemplatePlaceholders.Apply("<p>Re: {{ParentSubject}}</p>", injected, htmlEncode: true);
        Assert.DoesNotContain("<script>", body);
        Assert.Contains("&lt;script&gt;", body);
    }

    [Fact]
    public void Apply_LeavesTokensWithoutAValueAndUnknownTokensUntouched()
    {
        var text = "{{ParentNumber}} / {{Nonsense}} / {{Date}}";

        Assert.Equal("{{ParentNumber}} / {{Nonsense}} / 2026-03-10", LetterTemplatePlaceholders.Apply(text, Values, false));
        Assert.Equal("", LetterTemplatePlaceholders.Apply("", Values, false));
        Assert.Equal("", LetterTemplatePlaceholders.Apply(null, Values, false));
    }

    [Fact]
    public void Unknown_ListsEachUnknownTokenOnce_AndAcceptsEveryDocumentedOne()
    {
        Assert.Equal(new[] { "Foo", "Bar" }, LetterTemplatePlaceholders.Unknown("{{Foo}} {{foo}} {{Bar}} {{Date}}"));
        Assert.Empty(LetterTemplatePlaceholders.Unknown(string.Join(" ", LetterTemplatePlaceholders.All.Select(a => "{{" + a + "}}"))));
        Assert.Empty(LetterTemplatePlaceholders.Unknown(null));
        Assert.Empty(LetterTemplatePlaceholders.Unknown("plain text with { single } braces"));
    }

    [Fact]
    public async Task TemplateSave_RejectsUnknownTokensInSubjectOrBody_NamingTheAllowedOnes()
    {
        var ctx = PemsDbContextFactory.CreateInMemory("LetterP3_TplTokens");
        var svc = new LetterTemplateService(ctx, new RichTextSanitizerService());
        LetterTemplateDto Make(string subject, string body) => new()
        {
            Name = "T" + subject + body, Subject = subject, Body = body, Priority = "Normal", ConfidentialityLevel = "Internal"
        };

        var badSubject = await svc.SaveAsync(Make("About {{Typo}}", "<p>x</p>"));
        var badBody = await svc.SaveAsync(Make("Fine", "<p>{{Nope}}</p>"));
        var good = await svc.SaveAsync(Make("Re {{ParentSubject}} on {{Date}}", "<p>Dear {{Counterparty}}, {{UserName}}</p>"));

        Assert.False(badSubject.Success);
        Assert.Contains("{{Typo}}", badSubject.Error);
        Assert.Contains("{{Date}}", badSubject.Error);          // lists what is allowed
        Assert.False(badBody.Success);
        Assert.Contains("{{Nope}}", badBody.Error);
        Assert.True(good.Success, good.Error);
        Assert.Single(ctx.LetterTemplates);
    }

    // ------------------------------------------------------------------ response history

    [Theory]
    [InlineData("ResponseRequired: False -> True | ResponseDueDate: null -> 2026-03-10", "Response requirement set - due 2026-03-10")]
    [InlineData("ModifiedBy: a -> b | ResponseRequired: False -> True | ResponseDueDate: null -> 2026-03-10 00:00:00", "Response requirement set - due 2026-03-10")]
    [InlineData("ResponseRequired: True -> False | ResponseDueDate: 2026-03-10 -> null", "Response requirement removed")]
    [InlineData("ResponseDueDate: 2026-03-10 -> 2026-03-20", "Response due date changed from 2026-03-10 to 2026-03-20")]
    [InlineData("ResponseRequired: False -> True", "Response requirement set")]
    public void Describe_TurnsAnAuditSummaryIntoASentence(string summary, string expected)
        => Assert.Equal(expected, LetterResponseHistory.Describe(summary));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Status: Draft -> PendingApproval")]
    [InlineData("ResponseRequired: True -> True")]
    [InlineData("ResponseDueDate: 2026-03-10 -> 2026-03-10")]
    [InlineData("Subject: ResponseDueDate is in the title")]
    public void Describe_IgnoresRowsThatAreNotAboutTheResponse(string? summary)
        => Assert.Null(LetterResponseHistory.Describe(summary));

    [Fact]
    public async Task Timeline_ShowsEveryResponseRequirementChange_InOrder_WithoutOtherAuditNoise()
    {
        var ctx = PemsDbContextFactory.CreateInMemory("LetterP3_Timeline");
        var s = new LetterService(ctx, new RichTextSanitizerService(), knowledgeBaseService: null!);
        var letter = new Letter { LetterId = 7001, LetterType = "Incoming", LetterNumber = "H-1", Subject = "S", Status = "Registered",
            LetterDate = DateTime.Today.AddDays(-5), RegisteredById = 5, IsActive = true, ConfidentialityLevel = "Public" };
        ctx.Letters.Add(letter);
        await ctx.SaveChangesAsync();

        await s.SetResponseDueAsync(7001, true, DateTime.Today.AddDays(10), 5, false);
        await s.SetResponseDueAsync(7001, true, DateTime.Today.AddDays(20), 5, false);
        await s.SetResponseDueAsync(7001, false, null, 5, false);

        var events = (await s.GetTimelineAsync(7001)).Where(e => e.EventType == "Response").ToList();

        Assert.Equal(3, events.Count);
        Assert.StartsWith("Response requirement set - due", events[0].Description);
        Assert.StartsWith("Response due date changed from", events[1].Description);
        Assert.Equal("Response requirement removed", events[2].Description);
        Assert.All(events, e => Assert.Equal("bi-reply", e.Icon));
        Assert.Equal(events.OrderBy(e => e.EventDate).Select(e => e.Description), events.Select(e => e.Description));
    }

    [Fact]
    public async Task Timeline_HasNoResponseEvents_ForALetterThatNeverHadOne()
    {
        var ctx = PemsDbContextFactory.CreateInMemory("LetterP3_TimelineNone");
        var s = new LetterService(ctx, new RichTextSanitizerService(), knowledgeBaseService: null!);
        ctx.Letters.Add(new Letter { LetterId = 7002, LetterType = "Incoming", LetterNumber = "H-2", Subject = "S", Status = "Registered", IsActive = true, ConfidentialityLevel = "Public" });
        await ctx.SaveChangesAsync();

        Assert.DoesNotContain(await s.GetTimelineAsync(7002), e => e.EventType == "Response");
    }

    // ------------------------------------------------------------------ reminders

    private sealed class PersistingNotifications : INotificationService
    {
        private readonly PEMSDbContext _ctx;
        public HashSet<int> FailFor { get; } = new();
        public PersistingNotifications(PEMSDbContext ctx) => _ctx = ctx;

        public async Task CreateAsync(int userId, string title, string message, string? linkUrl = null, string? relatedType = null, int? relatedId = null)
        {
            if (FailFor.Contains(userId)) throw new InvalidOperationException("delivery failed");
            _ctx.Notifications.Add(new Notification { UserId = userId, Title = title, Message = message, LinkUrl = linkUrl, RelatedType = relatedType, RelatedId = relatedId, CreatedDate = DateTime.Now });
            await _ctx.SaveChangesAsync();
        }
        public Task<List<NotificationDto>> GetForUserAsync(int userId, bool unreadOnly = false) => throw new NotImplementedException();
        public Task<PagedResult<NotificationDto>> GetInboxAsync(int userId, string? filter, int page, int pageSize) => throw new NotImplementedException();
        public Task<int> GetUnreadCountAsync(int userId) => throw new NotImplementedException();
        public Task<bool> MarkAsReadAsync(int userId, int notificationId) => throw new NotImplementedException();
        public Task MarkAllAsReadAsync(int userId) => throw new NotImplementedException();
        public Task<string?> OpenAsync(int userId, int notificationId) => throw new NotImplementedException();
        public Task<bool> DismissAsync(int userId, int notificationId) => throw new NotImplementedException();
        public Task DismissAllAsync(int userId) => throw new NotImplementedException();
        public Task MarkRelatedReadAsync(int userId, string relatedType, int relatedId) => throw new NotImplementedException();
        public Task ResolveRelatedAsync(int? userId, string relatedType, int relatedId) => throw new NotImplementedException();
    }

    private const int AuthorUser = 31;
    private const int AssigneeUser = 32;
    private const int InactiveUser = 33;

    private static int _lid = 8000;

    private static Letter NewLetter(string status = "Registered", bool responseRequired = false, DateTime? due = null) => new()
    {
        LetterId = ++_lid, LetterType = "Incoming", LetterNumber = $"R-{_lid}", Subject = $"Subj {_lid}", Status = status,
        LetterDate = DateTime.Today.AddDays(-30), RegisteredById = AuthorUser, IsActive = true, ConfidentialityLevel = "Public",
        ResponseRequired = responseRequired, ResponseDueDate = due
    };

    private static (LetterReminderService Svc, PEMSDbContext Ctx, PersistingNotifications Notes) Setup(string name)
    {
        var ctx = PemsDbContextFactory.CreateInMemory($"LetterP3_{name}");
        ctx.Employees.Add(new Employee { EmployeeId = 1, FirstName = "A", LastName = "A", IsActive = true });
        ctx.Employees.Add(new Employee { EmployeeId = 2, FirstName = "B", LastName = "B", IsActive = true });
        ctx.Users.Add(new ApplicationUser { Id = AuthorUser, UserName = "author", NormalizedUserName = "AUTHOR", IsActive = true });
        ctx.Users.Add(new ApplicationUser { Id = AssigneeUser, UserName = "assignee", NormalizedUserName = "ASSIGNEE", EmployeeId = 1, IsActive = true });
        ctx.Users.Add(new ApplicationUser { Id = InactiveUser, UserName = "gone", NormalizedUserName = "GONE", EmployeeId = 2, IsActive = false });
        ctx.SaveChanges();
        var notes = new PersistingNotifications(ctx);
        return (new LetterReminderService(ctx, notes), ctx, notes);
    }

    [Fact]
    public async Task OverdueReferral_RemindsTheActiveAssigneeAndTheAuthor_ButNotInactiveUsers()
    {
        var (svc, ctx, _) = Setup(nameof(OverdueReferral_RemindsTheActiveAssigneeAndTheAuthor_ButNotInactiveUsers));
        var l1 = NewLetter(); var l2 = NewLetter();
        ctx.Letters.AddRange(l1, l2);
        ctx.LetterReferrals.Add(new LetterReferral { LetterId = l1.LetterId, ReferredToEmployeeId = 1, Status = "Pending", DueDate = DateTime.Now.AddDays(-3) });
        ctx.LetterReferrals.Add(new LetterReferral { LetterId = l2.LetterId, ReferredToEmployeeId = 2, Status = "Pending", DueDate = DateTime.Now.AddDays(-3) });
        await ctx.SaveChangesAsync();

        var result = await svc.SendDueRemindersAsync();

        // l1: assignee + author; l2: only the author (assignee's user is deactivated)
        Assert.Equal(3, result.ReferralReminders);
        var sent = ctx.Notifications.Where(n => n.Title == LetterReminderService.ReferralTitle).ToList();
        Assert.Contains(sent, n => n.UserId == AssigneeUser && n.RelatedId == l1.LetterId);
        Assert.Contains(sent, n => n.UserId == AuthorUser && n.RelatedId == l1.LetterId);
        Assert.Contains(sent, n => n.UserId == AuthorUser && n.RelatedId == l2.LetterId);
        Assert.DoesNotContain(sent, n => n.UserId == InactiveUser);
        Assert.All(sent, n => Assert.Equal($"/Letter/Details/{n.RelatedId}", n.LinkUrl));
        Assert.Contains(sent, n => n.Message.Contains("overdue since"));
    }

    [Fact]
    public async Task Referrals_NotOverdue_Completed_OrOnFinishedLetters_AreNeverReminded()
    {
        var (svc, ctx, _) = Setup(nameof(Referrals_NotOverdue_Completed_OrOnFinishedLetters_AreNeverReminded));
        var future = NewLetter(); var done = NewLetter(); var closed = NewLetter("Closed"); var rejected = NewLetter("Rejected");
        var archived = NewLetter(); archived.IsActive = false;
        ctx.Letters.AddRange(future, done, closed, rejected, archived);
        var past = DateTime.Now.AddDays(-4);
        ctx.LetterReferrals.AddRange(
            new LetterReferral { LetterId = future.LetterId, ReferredToEmployeeId = 1, Status = "Pending", DueDate = DateTime.Now.AddDays(2) },
            new LetterReferral { LetterId = done.LetterId, ReferredToEmployeeId = 1, Status = "Completed", DueDate = past },
            new LetterReferral { LetterId = closed.LetterId, ReferredToEmployeeId = 1, Status = "Pending", DueDate = past },
            new LetterReferral { LetterId = rejected.LetterId, ReferredToEmployeeId = 1, Status = "Pending", DueDate = past },
            new LetterReferral { LetterId = archived.LetterId, ReferredToEmployeeId = 1, Status = "Pending", DueDate = past },
            new LetterReferral { LetterId = future.LetterId, ReferredToEmployeeId = 1, Status = "Pending", DueDate = null });
        await ctx.SaveChangesAsync();

        var result = await svc.SendDueRemindersAsync();

        Assert.Equal(0, result.Total);
        Assert.Empty(ctx.Notifications);
    }

    [Fact]
    public async Task OverdueResponse_RemindsOnlyTheAuthor_AndSkipsAnsweredClosedAndNotRequired()
    {
        var (svc, ctx, _) = Setup(nameof(OverdueResponse_RemindsOnlyTheAuthor_AndSkipsAnsweredClosedAndNotRequired));
        var past = DateTime.Today.AddDays(-2);
        var overdue = NewLetter(responseRequired: true, due: past);
        var answered = NewLetter(responseRequired: true, due: past);
        var draftReply = NewLetter(responseRequired: true, due: past);
        var closed = NewLetter("Closed", true, past);
        var notRequired = NewLetter(responseRequired: false, due: past);
        var dueToday = NewLetter(responseRequired: true, due: DateTime.Today);
        ctx.Letters.AddRange(overdue, answered, draftReply, closed, notRequired, dueToday);
        ctx.Letters.Add(new Letter { LetterId = ++_lid, LetterType = "Outgoing", LetterNumber = "X1", Subject = "Re", Status = "Issued", ParentLetterId = answered.LetterId, IsActive = true, ConfidentialityLevel = "Public" });
        ctx.Letters.Add(new Letter { LetterId = ++_lid, LetterType = "Outgoing", LetterNumber = "X2", Subject = "Re", Status = "Draft", ParentLetterId = draftReply.LetterId, IsActive = true, ConfidentialityLevel = "Public" });
        await ctx.SaveChangesAsync();

        var result = await svc.SendDueRemindersAsync();

        Assert.Equal(2, result.ResponseReminders);       // overdue + draftReply (a draft is not an answer)
        var sent = ctx.Notifications.ToList();
        Assert.All(sent, n => { Assert.Equal(AuthorUser, n.UserId); Assert.Equal(LetterReminderService.ResponseTitle, n.Title); });
        Assert.Equal(new[] { overdue.LetterId, draftReply.LetterId }.OrderBy(x => x), sent.Select(n => n.RelatedId!.Value).OrderBy(x => x));
    }

    [Fact]
    public async Task ASecondRunTheSameDay_SendsNothingNew_ButTheNextDaySendsAgain()
    {
        var (svc, ctx, _) = Setup(nameof(ASecondRunTheSameDay_SendsNothingNew_ButTheNextDaySendsAgain));
        var l = NewLetter(responseRequired: true, due: DateTime.Today.AddDays(-3));
        ctx.Letters.Add(l);
        await ctx.SaveChangesAsync();

        var first = await svc.SendDueRemindersAsync();
        var second = await svc.SendDueRemindersAsync();
        var tomorrow = await svc.SendDueRemindersAsync(DateTime.Now.AddDays(1));

        Assert.Equal(1, first.Total);
        Assert.Equal(0, second.Total);
        Assert.Equal(1, second.SkippedDuplicates);
        Assert.Equal(1, tomorrow.Total);
        Assert.Equal(2, ctx.Notifications.Count());
    }

    [Fact]
    public async Task AFailingDelivery_IsCounted_AndDoesNotStopTheOtherReminders()
    {
        var (svc, ctx, notes) = Setup(nameof(AFailingDelivery_IsCounted_AndDoesNotStopTheOtherReminders));
        var l = NewLetter();
        ctx.Letters.Add(l);
        ctx.LetterReferrals.Add(new LetterReferral { LetterId = l.LetterId, ReferredToEmployeeId = 1, Status = "Pending", DueDate = DateTime.Now.AddDays(-2) });
        await ctx.SaveChangesAsync();
        notes.FailFor.Add(AssigneeUser);

        var result = await svc.SendDueRemindersAsync();

        Assert.Equal(1, result.Failed);
        Assert.Equal(1, result.ReferralReminders);       // the author still got theirs
        Assert.Equal(AuthorUser, Assert.Single(ctx.Notifications).UserId);
    }

    // ------------------------------------------------------------------ schedule / hosted service

    [Theory]
    [InlineData("07:30", 7, 30)]
    [InlineData(" 23:59 ", 23, 59)]
    [InlineData("00:00", 0, 0)]
    [InlineData("25:00", 7, 30)]
    [InlineData("7:30pm", 7, 30)]
    [InlineData("", 7, 30)]
    [InlineData(null, 7, 30)]
    public void ParseTime_AcceptsHHmm_AndFallsBackToTheDefaultOtherwise(string? text, int h, int m)
        => Assert.Equal(new TimeSpan(h, m, 0), LetterReminderSchedule.ParseTime(text));

    [Fact]
    public void NextRun_IsTodayIfTheTimeIsAhead_OtherwiseTomorrow()
    {
        var t = new TimeSpan(7, 30, 0);

        Assert.Equal(new DateTime(2026, 3, 10, 7, 30, 0), LetterReminderSchedule.NextRun(new DateTime(2026, 3, 10, 6, 0, 0), t));
        Assert.Equal(new DateTime(2026, 3, 11, 7, 30, 0), LetterReminderSchedule.NextRun(new DateTime(2026, 3, 10, 7, 30, 0), t));   // exactly now -> tomorrow
        Assert.Equal(new DateTime(2026, 3, 11, 7, 30, 0), LetterReminderSchedule.NextRun(new DateTime(2026, 3, 10, 22, 0, 0), t));
        Assert.Equal(new DateTime(2026, 4, 1, 7, 30, 0), LetterReminderSchedule.NextRun(new DateTime(2026, 3, 31, 12, 0, 0), t));   // month end
    }

    private sealed class ExplodingScopeFactory : IServiceScopeFactory
    {
        public int Created { get; private set; }
        public IServiceScope CreateScope() { Created++; throw new InvalidOperationException("must not run"); }
    }

    [Fact]
    public async Task TheBackgroundJob_IsOffByDefault_AndDoesNothingWhenDisabled()
    {
        Assert.False(new LetterReminderOptions().Enabled);

        var factory = new ExplodingScopeFactory();
        var job = new LetterReminderBackgroundService(factory, Options.Create(new LetterReminderOptions { Enabled = false }),
            NullLogger<LetterReminderBackgroundService>.Instance);

        await job.StartAsync(CancellationToken.None);
        await job.ExecuteTask!;
        await job.StopAsync(CancellationToken.None);

        Assert.Equal(0, factory.Created);
    }

    [Fact]
    public async Task WhenEnabled_TheJobWaitsForTheScheduledTime_AndStopsCleanlyWithoutSendingAnything()
    {
        var factory = new ExplodingScopeFactory();
        var job = new LetterReminderBackgroundService(factory, Options.Create(new LetterReminderOptions { Enabled = true, RunAtLocalTime = "07:30" }),
            NullLogger<LetterReminderBackgroundService>.Instance);

        await job.StartAsync(CancellationToken.None);
        await Task.Delay(100);
        Assert.False(job.ExecuteTask!.IsCompleted);          // sleeping until the next 07:30, not sending at startup
        await job.StopAsync(CancellationToken.None);

        Assert.Equal(0, factory.Created);
    }

    [Fact]
    public void ShippedConfiguration_KeepsRemindersOff_UntilAnAdministratorEnablesThem()
    {
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
        {
            var values = PEMS.Shared.Configuration.EnvFiles.Parse(File.ReadAllLines(file));
            Assert.Equal("false", values["LetterReminders:Enabled"]);
            Assert.Equal("07:30", values["LetterReminders:RunAtLocalTime"]);
        }
    }
}
