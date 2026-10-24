using Microsoft.AspNetCore.Identity;
using PEMS.Application.Common;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Identity;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;

namespace PEMS.Tests.Cmms;

/// <summary>
/// Phase 2 مکاتبات: پاسخ‌خواهی سطح نامه (پاسخ‌داده‌شده/معوق محاسبه‌شده)، اعلان‌های چرخه‌ی حیات، داشبورد با قانون محرمانگی،
/// و قالب‌های نامه. قواعد کلیدی: خود عامل اعلان نمی‌گیرد؛ شکست اعلان گذار را برنمی‌گرداند؛ قالب حذف فیزیکی ندارد.
/// </summary>
public class LetterPhase2Tests
{
    private const int Author = 21;
    private const int Reviewer = 22;
    private const int Outsider = 23;

    private sealed class RecordingNotifications : INotificationService
    {
        public bool Throw { get; set; }
        public List<(int UserId, string Title, string Message, string? Link, int? RelatedId)> Sent { get; } = new();
        public Task CreateAsync(int userId, string title, string message, string? linkUrl = null, string? relatedType = null, int? relatedId = null)
        {
            if (Throw) throw new InvalidOperationException("notification store down");
            Sent.Add((userId, title, message, linkUrl, relatedId));
            return Task.CompletedTask;
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

    private static LetterService Create(string name, out PEMSDbContext ctx, out RecordingNotifications notes)
    {
        ctx = PemsDbContextFactory.CreateInMemory($"LetterP2_{name}");
        notes = new RecordingNotifications();
        return new LetterService(ctx, new RichTextSanitizerService(), knowledgeBaseService: null!, notes);
    }

    private static int _id = 5000;

    private static Letter NewLetter(string status = "Registered", string type = "Incoming", int? registeredBy = Author,
        string confidentiality = "Public", bool responseRequired = false, DateTime? due = null, string priority = "Normal",
        DateTime? letterDate = null) => new()
    {
        LetterId = ++_id,
        LetterType = type,
        LetterNumber = $"P2-{_id}",
        LetterDate = letterDate ?? DateTime.Today.AddDays(-20),
        Subject = $"Subject {_id}",
        Priority = priority,
        ConfidentialityLevel = confidentiality,
        Status = status,
        RegisteredById = registeredBy,
        ResponseRequired = responseRequired,
        ResponseDueDate = due,
        IsActive = true
    };

    private static Task<PagedResult<LetterDto>> Search(LetterService s, LetterAdvancedFilter? advanced = null)
        => s.SearchAsync(null, null, null, null, null, null, null, null, null, null, 1, 100, true, null, false, advanced);

    // ------------------------------------------------------------------ rules

    [Theory]
    [InlineData(false, null, "2026-03-01", null)]
    [InlineData(true, "2026-03-05", "2026-03-01", null)]
    [InlineData(true, "2026-03-01", "2026-03-01", null)]
    public void Rules_Validate_AcceptsRealCombinations(bool required, string? due, string letterDate, string? expectedError)
        => Assert.Equal(expectedError, LetterResponseRules.Validate(required, due == null ? null : DateTime.Parse(due), DateTime.Parse(letterDate)));

    [Fact]
    public void Rules_Validate_RejectsMissingAndEarlierDueDates()
    {
        Assert.NotNull(LetterResponseRules.Validate(true, null, DateTime.Today));
        Assert.NotNull(LetterResponseRules.Validate(true, DateTime.Today.AddDays(-1), DateTime.Today));
    }

    [Theory]
    [InlineData(true, -1, false, "Registered", true)]
    [InlineData(true, -1, true, "Registered", false)]     // answered
    [InlineData(true, -1, false, "Closed", false)]        // closed
    [InlineData(true, -1, false, "Rejected", false)]
    [InlineData(true, 0, false, "Registered", false)]     // due today is not yet overdue
    [InlineData(true, 3, false, "Registered", false)]
    [InlineData(false, -5, false, "Registered", false)]   // not required
    public void Rules_IsOverdue_IsComputed(bool required, int dueOffsetDays, bool answered, string status, bool expected)
        => Assert.Equal(expected, LetterResponseRules.IsOverdue(required, DateTime.Today.AddDays(dueOffsetDays), answered, status, DateTime.Today));

    // ------------------------------------------------------------------ create / set response due

    [Fact]
    public async Task Create_PersistsResponseDue_OnlyWhenRequired_AndRejectsInconsistentInput()
    {
        var s = Create(nameof(Create_PersistsResponseDue_OnlyWhenRequired_AndRejectsInconsistentInput), out var ctx, out _);
        var date = DateTime.Today;

        var id1 = await s.CreateAsync("Incoming", "N-1", date, "ACME", null, "S", "<p>b</p>", "Normal", "Internal", null, null, null, null, Author,
            responseRequired: true, responseDueDate: date.AddDays(7).AddHours(5));
        var id2 = await s.CreateAsync("Incoming", "N-2", date, "ACME", null, "S", null, "Normal", "Internal", null, null, null, null, Author,
            responseRequired: false, responseDueDate: date.AddDays(7));

        var l1 = ctx.Letters.Single(x => x.LetterId == id1);
        Assert.True(l1.ResponseRequired);
        Assert.Equal(date.AddDays(7), l1.ResponseDueDate);          // time part dropped
        var l2 = ctx.Letters.Single(x => x.LetterId == id2);
        Assert.False(l2.ResponseRequired);
        Assert.Null(l2.ResponseDueDate);                            // a stray date is never stored without the flag

        await Assert.ThrowsAsync<ArgumentException>(() => s.CreateAsync("Incoming", "N-3", date, "A", null, "S", null, "Normal", "Internal",
            null, null, null, null, Author, responseRequired: true, responseDueDate: null));
        await Assert.ThrowsAsync<ArgumentException>(() => s.CreateAsync("Incoming", "N-4", date, "A", null, "S", null, "Normal", "Internal",
            null, null, null, null, Author, responseRequired: true, responseDueDate: date.AddDays(-1)));
    }

    [Fact]
    public async Task SetResponseDue_OwnerOrManagerOnly_NotForFinishedLetters_AndValidates()
    {
        var s = Create(nameof(SetResponseDue_OwnerOrManagerOnly_NotForFinishedLetters_AndValidates), out var ctx, out _);
        var open = NewLetter(); var closed = NewLetter("Closed"); var rejected = NewLetter("Rejected");
        ctx.Letters.AddRange(open, closed, rejected);
        await ctx.SaveChangesAsync();
        var due = DateTime.Today.AddDays(5);

        Assert.False((await s.SetResponseDueAsync(open.LetterId, true, due, Outsider, canManageAny: false)).Success);
        Assert.False((await s.SetResponseDueAsync(open.LetterId, true, null, Author, false)).Success);                       // due required
        Assert.False((await s.SetResponseDueAsync(open.LetterId, true, open.LetterDate.AddDays(-1), Author, false)).Success); // before letter date
        Assert.False((await s.SetResponseDueAsync(closed.LetterId, true, due, Author, false)).Success);
        Assert.False((await s.SetResponseDueAsync(rejected.LetterId, true, due, Author, true)).Success);
        Assert.False((await s.SetResponseDueAsync(99999, true, due, Author, true)).Success);

        Assert.True((await s.SetResponseDueAsync(open.LetterId, true, due, Author, false)).Success);
        Assert.Equal(due, ctx.Letters.Single(x => x.LetterId == open.LetterId).ResponseDueDate);

        Assert.True((await s.SetResponseDueAsync(open.LetterId, true, due.AddDays(2), Outsider, canManageAny: true)).Success);
        Assert.True((await s.SetResponseDueAsync(open.LetterId, false, null, Author, false)).Success);
        var cleared = ctx.Letters.Single(x => x.LetterId == open.LetterId);
        Assert.False(cleared.ResponseRequired);
        Assert.Null(cleared.ResponseDueDate);
    }

    // ------------------------------------------------------------------ computed answered / overdue in the list

    [Fact]
    public async Task AnsweredAndOverdue_AreComputedFromChildLetters_NotStored()
    {
        var s = Create(nameof(AnsweredAndOverdue_AreComputedFromChildLetters_NotStored), out var ctx, out _);
        var past = DateTime.Today.AddDays(-3);
        var unanswered = NewLetter(responseRequired: true, due: past);
        var draftReply = NewLetter(responseRequired: true, due: past);
        var issuedReply = NewLetter(responseRequired: true, due: past);
        var notRequired = NewLetter();
        ctx.Letters.AddRange(unanswered, draftReply, issuedReply, notRequired);
        ctx.Letters.Add(new Letter { LetterId = ++_id, LetterType = "Outgoing", LetterNumber = "R1", Subject = "Re", Status = "Draft", ParentLetterId = draftReply.LetterId, IsActive = true, ConfidentialityLevel = "Public" });
        ctx.Letters.Add(new Letter { LetterId = ++_id, LetterType = "Outgoing", LetterNumber = "R2", Subject = "Re", Status = "Issued", ParentLetterId = issuedReply.LetterId, IsActive = true, ConfidentialityLevel = "Public" });
        await ctx.SaveChangesAsync();

        var items = (await Search(s)).Items.ToDictionary(i => i.LetterId);

        Assert.True(items[unanswered.LetterId].ResponseOverdue);
        Assert.False(items[unanswered.LetterId].ResponseAnswered);
        Assert.True(items[draftReply.LetterId].ResponseOverdue);                 // a draft reply is not an answer yet
        Assert.True(items[issuedReply.LetterId].ResponseAnswered);
        Assert.False(items[issuedReply.LetterId].ResponseOverdue);
        Assert.False(items[notRequired.LetterId].ResponseOverdue);
        Assert.Null(items[issuedReply.LetterId].NextResponseDue);                // answered: no longer awaiting
        Assert.Equal(past, items[unanswered.LetterId].NextResponseDue);
    }

    [Fact]
    public async Task NextResponseDue_TakesTheEarlierOfLetterDueAndReferralDue()
    {
        var s = Create(nameof(NextResponseDue_TakesTheEarlierOfLetterDueAndReferralDue), out var ctx, out _);
        var l = NewLetter(responseRequired: true, due: DateTime.Today.AddDays(10));
        ctx.Letters.Add(l);
        ctx.LetterReferrals.Add(new LetterReferral { LetterId = l.LetterId, Status = "Pending", DueDate = DateTime.Today.AddDays(4) });
        await ctx.SaveChangesAsync();

        var item = Assert.Single((await Search(s)).Items);

        Assert.Equal(DateTime.Today.AddDays(4), item.NextResponseDue);
    }

    [Fact]
    public async Task OverdueResponseFilter_ReturnsOnlyUnansweredPastDueRequiredLetters()
    {
        var s = Create(nameof(OverdueResponseFilter_ReturnsOnlyUnansweredPastDueRequiredLetters), out var ctx, out _);
        var overdue = NewLetter(responseRequired: true, due: DateTime.Today.AddDays(-2));
        var future = NewLetter(responseRequired: true, due: DateTime.Today.AddDays(2));
        var answered = NewLetter(responseRequired: true, due: DateTime.Today.AddDays(-2));
        var closed = NewLetter("Closed", responseRequired: true, due: DateTime.Today.AddDays(-2));
        ctx.Letters.AddRange(overdue, future, answered, closed);
        ctx.Letters.Add(new Letter { LetterId = ++_id, LetterType = "Outgoing", LetterNumber = "R9", Subject = "Re", Status = "Issued", ParentLetterId = answered.LetterId, IsActive = true, ConfidentialityLevel = "Public" });
        await ctx.SaveChangesAsync();

        var result = await Search(s, new LetterAdvancedFilter { OverdueResponseOnly = true });

        Assert.Equal(overdue.LetterId, Assert.Single(result.Items).LetterId);
    }

    // ------------------------------------------------------------------ notifications

    private static async Task SeedApprovers(PEMSDbContext ctx)
    {
        ctx.Roles.Add(new ApplicationRole("Reviewers") { Id = 1, NormalizedName = "REVIEWERS" });
        ctx.Roles.Add(new ApplicationRole("Staff") { Id = 2, NormalizedName = "STAFF" });
        ctx.RolePermissions.Add(new RolePermission { RoleName = "Reviewers", ModuleName = "Correspondence", CanApprove = true });
        ctx.RolePermissions.Add(new RolePermission { RoleName = "Staff", ModuleName = "Correspondence", CanApprove = false });
        ctx.Users.Add(new ApplicationUser { Id = Reviewer, UserName = "rev", NormalizedUserName = "REV", IsActive = true });
        ctx.Users.Add(new ApplicationUser { Id = Author, UserName = "auth", NormalizedUserName = "AUTH", IsActive = true });
        ctx.Users.Add(new ApplicationUser { Id = Outsider, UserName = "off", NormalizedUserName = "OFF", IsActive = false });
        ctx.Users.Add(new ApplicationUser { Id = 24, UserName = "rev2", NormalizedUserName = "REV2", IsActive = true });
        ctx.UserRoles.Add(new IdentityUserRole<int> { UserId = Reviewer, RoleId = 1 });
        ctx.UserRoles.Add(new IdentityUserRole<int> { UserId = 24, RoleId = 1 });
        ctx.UserRoles.Add(new IdentityUserRole<int> { UserId = Outsider, RoleId = 1 });   // approver but deactivated
        ctx.UserRoles.Add(new IdentityUserRole<int> { UserId = Author, RoleId = 2 });
        await ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task Submit_NotifiesActiveApprovers_NotTheActorNorStaffNorDeactivatedUsers()
    {
        var s = Create(nameof(Submit_NotifiesActiveApprovers_NotTheActorNorStaffNorDeactivatedUsers), out var ctx, out var notes);
        await SeedApprovers(ctx);
        var letter = NewLetter("Draft", "Outgoing", Author);
        ctx.Letters.Add(letter);
        await ctx.SaveChangesAsync();

        var (ok, _) = await s.SubmitForReviewAsync(letter.LetterId, Author);

        Assert.True(ok);
        Assert.Equal(new[] { Reviewer, 24 }, notes.Sent.Select(x => x.UserId).OrderBy(x => x));
        Assert.All(notes.Sent, n =>
        {
            Assert.Equal($"/Letter/Details/{letter.LetterId}", n.Link);
            Assert.Equal(letter.LetterId, n.RelatedId);
            Assert.Contains(letter.LetterNumber, n.Message);
        });
    }

    [Fact]
    public async Task Approve_Return_Reject_Issue_NotifyTheAuthor_WithReasons()
    {
        var s = Create(nameof(Approve_Return_Reject_Issue_NotifyTheAuthor_WithReasons), out var ctx, out var notes);
        var toApprove = NewLetter("PendingApproval", "Outgoing", Author);
        var toReturn = NewLetter("PendingApproval", "Outgoing", Author);
        var toReject = NewLetter("PendingApproval", "Outgoing", Author);
        var toIssue = NewLetter("Approved", "Outgoing", Author);
        ctx.Letters.AddRange(toApprove, toReturn, toReject, toIssue);
        await ctx.SaveChangesAsync();

        Assert.True((await s.ApproveAsync(toApprove.LetterId, Reviewer, "ok")).Success);
        Assert.True((await s.ReturnForRevisionAsync(toReturn.LetterId, Reviewer, "fix the date")).Success);
        Assert.True((await s.RejectAsync(toReject.LetterId, Reviewer, "duplicate")).Success);
        Assert.True((await s.IssueAsync(toIssue.LetterId, Reviewer)).Success);

        Assert.Equal(4, notes.Sent.Count);
        Assert.All(notes.Sent, n => Assert.Equal(Author, n.UserId));
        Assert.Contains(notes.Sent, n => n.Title.Contains("approved") && n.RelatedId == toApprove.LetterId);
        Assert.Contains(notes.Sent, n => n.Title.Contains("returned") && n.Message.Contains("fix the date"));
        Assert.Contains(notes.Sent, n => n.Title.Contains("rejected") && n.Message.Contains("duplicate"));
        Assert.Contains(notes.Sent, n => n.Title.Contains("issued") && n.RelatedId == toIssue.LetterId);
    }

    [Fact]
    public async Task TheActorNeverNotifiesThemselves_AndFailedTransitionsNeverNotify()
    {
        var s = Create(nameof(TheActorNeverNotifiesThemselves_AndFailedTransitionsNeverNotify), out var ctx, out var notes);
        var own = NewLetter("PendingApproval", "Outgoing", Author);
        var wrongState = NewLetter("Draft", "Outgoing", Author);
        ctx.Letters.AddRange(own, wrongState);
        await ctx.SaveChangesAsync();

        Assert.True((await s.ApproveAsync(own.LetterId, Author, null)).Success);                // author == reviewer
        Assert.False((await s.ApproveAsync(wrongState.LetterId, Reviewer, null)).Success);       // not PendingApproval
        Assert.False((await s.RejectAsync(wrongState.LetterId, Reviewer, "")).Success);          // reason missing

        Assert.Empty(notes.Sent);
    }

    [Fact]
    public async Task ANotificationFailure_NeverRollsBackTheTransition()
    {
        var s = Create(nameof(ANotificationFailure_NeverRollsBackTheTransition), out var ctx, out var notes);
        await SeedApprovers(ctx);
        notes.Throw = true;
        var submit = NewLetter("Draft", "Outgoing", Author);
        var approve = NewLetter("PendingApproval", "Outgoing", Author);
        ctx.Letters.AddRange(submit, approve);
        await ctx.SaveChangesAsync();

        Assert.True((await s.SubmitForReviewAsync(submit.LetterId, Author)).Success);
        Assert.True((await s.ApproveAsync(approve.LetterId, Reviewer, null)).Success);

        Assert.Equal("PendingApproval", ctx.Letters.Single(x => x.LetterId == submit.LetterId).Status);
        Assert.Equal("Approved", ctx.Letters.Single(x => x.LetterId == approve.LetterId).Status);
    }

    [Fact]
    public async Task WithoutANotificationService_TransitionsStillWork()
    {
        var ctx = PemsDbContextFactory.CreateInMemory("LetterP2_NoNotify");
        var s = new LetterService(ctx, new RichTextSanitizerService(), knowledgeBaseService: null!);
        var l = NewLetter("Draft", "Outgoing", Author);
        ctx.Letters.Add(l);
        await ctx.SaveChangesAsync();

        Assert.True((await s.SubmitForReviewAsync(l.LetterId, Author)).Success);
    }

    [Fact]
    public async Task Resubmit_NotifiesApprovers()
    {
        var s = Create(nameof(Resubmit_NotifiesApprovers), out var ctx, out var notes);
        await SeedApprovers(ctx);
        var l = NewLetter("ReturnedForRevision", "Outgoing", Author);
        ctx.Letters.Add(l);
        await ctx.SaveChangesAsync();

        var (ok, _) = await s.ResubmitAsync(l.LetterId, "New subject", "<p>x</p>", Author, null);

        Assert.True(ok);
        Assert.Equal(2, notes.Sent.Count);
        Assert.All(notes.Sent, n => Assert.Contains("Revised", n.Title));
    }

    // ------------------------------------------------------------------ dashboards

    [Fact]
    public async Task WorkspaceDashboard_CountsOnlyWhatTheUserMaySee()
    {
        var s = Create(nameof(WorkspaceDashboard_CountsOnlyWhatTheUserMaySee), out var ctx, out _);
        ctx.Letters.AddRange(
            NewLetter("Registered", confidentiality: "Public"),
            NewLetter("Registered", confidentiality: "Confidential", registeredBy: Reviewer),    // someone else's secret
            NewLetter("Registered", confidentiality: "Confidential", registeredBy: Outsider));   // my own secret
        await ctx.SaveChangesAsync();

        var restricted = await s.GetWorkspaceDashboardAsync(canViewConfidential: false, applicationUserId: Outsider, canReview: false);
        var privileged = await s.GetWorkspaceDashboardAsync(canViewConfidential: true, applicationUserId: Reviewer, canReview: true);

        Assert.Equal(2, restricted.TotalActive);
        Assert.Equal(3, privileged.TotalActive);
        Assert.Equal(2, restricted.ByStatus["Registered"]);
    }

    [Fact]
    public async Task WorkspaceDashboard_PersonalAndReviewCounters_AreScopedCorrectly()
    {
        var s = Create(nameof(WorkspaceDashboard_PersonalAndReviewCounters_AreScopedCorrectly), out var ctx, out _);
        ctx.Letters.AddRange(
            NewLetter("Draft", "Outgoing", Author), NewLetter("Draft", "Outgoing", Author), NewLetter("Draft", "Outgoing", Reviewer),
            NewLetter("ReturnedForRevision", "Outgoing", Author),
            NewLetter("PendingApproval", "Outgoing", Author), NewLetter("PendingApproval", "Outgoing", Reviewer));
        await ctx.SaveChangesAsync();

        var author = await s.GetWorkspaceDashboardAsync(true, Author, canReview: false);
        var reviewer = await s.GetWorkspaceDashboardAsync(true, Reviewer, canReview: true);

        Assert.Equal(2, author.MyDrafts);
        Assert.Equal(1, author.MyReturnedForRevision);
        Assert.Equal(0, author.AwaitingApproval);       // hidden for non-reviewers
        Assert.Equal(2, reviewer.AwaitingApproval);
        Assert.Equal(1, reviewer.MyDrafts);
    }

    [Fact]
    public async Task WorkspaceDashboard_AgingAndAttention_AreComputedFromRealDates()
    {
        var s = Create(nameof(WorkspaceDashboard_AgingAndAttention_AreComputedFromRealDates), out var ctx, out _);
        var today = DateTime.Today;
        var a = NewLetter(letterDate: today.AddDays(-2));
        var b = NewLetter(letterDate: today.AddDays(-20));
        var c = NewLetter(letterDate: today.AddDays(-60));
        var d = NewLetter(letterDate: today.AddDays(-200));
        var closed = NewLetter("Closed", letterDate: today.AddDays(-200));
        var overdueResp = NewLetter(letterDate: today.AddDays(-15), responseRequired: true, due: today.AddDays(-9));
        var overdueRef = NewLetter(letterDate: today.AddDays(-5));
        ctx.Letters.AddRange(a, b, c, d, closed, overdueResp, overdueRef);
        ctx.LetterReferrals.Add(new LetterReferral { LetterId = overdueRef.LetterId, Status = "Pending", DueDate = DateTime.Now.AddDays(-4) });
        ctx.LetterReferrals.Add(new LetterReferral { LetterId = a.LetterId, Status = "Completed", DueDate = DateTime.Now.AddDays(-30) });
        await ctx.SaveChangesAsync();

        var dash = await s.GetWorkspaceDashboardAsync(true, Author, false);

        Assert.Equal(6, dash.OpenLetters);               // closed excluded
        var aging = dash.Aging.ToDictionary(x => x.Key, x => x.Count);
        Assert.Equal(2, aging["0-7"]);                   // a, overdueRef
        Assert.Equal(2, aging["8-30"]);                  // b, overdueResp
        Assert.Equal(1, aging["31-90"]);
        Assert.Equal(1, aging["90+"]);
        Assert.Equal(1, dash.OverdueReferrals);
        Assert.Equal(1, dash.OverdueResponses);
        Assert.Equal(2, dash.NeedsAttention.Count);
        Assert.Equal(overdueResp.LetterId, dash.NeedsAttention[0].LetterId);   // 9 days beats 4 days
        Assert.Equal("OverdueResponse", dash.NeedsAttention[0].Reason);
        Assert.Equal(9, dash.NeedsAttention[0].DaysOverdue);
        Assert.Equal("OverdueReferral", dash.NeedsAttention[1].Reason);
    }

    [Fact]
    public async Task LegacyDashboardCounters_NowRespectConfidentiality()
    {
        var s = Create(nameof(LegacyDashboardCounters_NowRespectConfidentiality), out var ctx, out _);
        ctx.Letters.AddRange(NewLetter("Registered", confidentiality: "Public"), NewLetter("Registered", confidentiality: "Confidential", registeredBy: Reviewer));
        await ctx.SaveChangesAsync();

        var restricted = await s.GetDashboardAsync(Outsider, canViewConfidential: false);
        var privileged = await s.GetDashboardAsync(Reviewer, canViewConfidential: true);

        Assert.Equal(1, restricted.PendingLetters);
        Assert.Equal(2, privileged.PendingLetters);
    }

    // ------------------------------------------------------------------ templates

    private static LetterTemplateService NewTemplates(string name, out PEMSDbContext ctx)
    {
        ctx = PemsDbContextFactory.CreateInMemory($"LetterTpl_{name}");
        return new LetterTemplateService(ctx, new RichTextSanitizerService());
    }

    private static LetterTemplateDto Tpl(string name = "Ack", string? type = "Outgoing") => new()
    {
        Name = name, LetterType = type, Subject = "Acknowledgement", Body = "<p>Dear sir</p>", Priority = "Normal", ConfidentialityLevel = "Internal"
    };

    [Fact]
    public async Task Template_CreateAndEdit_SanitizesTheBody_AndKeepsOneRow()
    {
        var svc = NewTemplates(nameof(Template_CreateAndEdit_SanitizesTheBody_AndKeepsOneRow), out var ctx);
        var dto = Tpl();
        dto.Body = "<p>ok</p><script>alert(1)</script>";

        var (ok, error, id) = await svc.SaveAsync(dto);

        Assert.True(ok, error);
        var saved = ctx.LetterTemplates.Single(x => x.LetterTemplateId == id);
        Assert.DoesNotContain("script", saved.Body);
        Assert.Contains("ok", saved.Body);

        var edit = (await svc.GetAsync(id))!;
        edit.Subject = "Updated";
        Assert.True((await svc.SaveAsync(edit)).Success);
        var after = ctx.LetterTemplates.Single(x => x.LetterTemplateId == id);
        Assert.Equal("Updated", after.Subject);
        Assert.Single(ctx.LetterTemplates);
    }

    [Theory]
    [InlineData("", "S", "Normal", "Internal", "Outgoing")]
    [InlineData("N", "", "Normal", "Internal", "Outgoing")]
    [InlineData("N", "S", "Critical", "Internal", "Outgoing")]
    [InlineData("N", "S", "Normal", "TopSecret", "Outgoing")]
    [InlineData("N", "S", "Normal", "Internal", "Memo")]
    public async Task Template_RejectsInvalidInput(string name, string subject, string priority, string conf, string type)
    {
        var svc = NewTemplates($"{nameof(Template_RejectsInvalidInput)}_{name}{subject}{priority}{conf}{type}", out var ctx);

        var (ok, error, _) = await svc.SaveAsync(new LetterTemplateDto { Name = name, Subject = subject, Priority = priority, ConfidentialityLevel = conf, LetterType = type });

        Assert.False(ok);
        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.Empty(ctx.LetterTemplates);
    }

    [Fact]
    public async Task Template_DuplicateNamePerType_IsRejected_ButSameNameForAnotherTypeIsFine()
    {
        var svc = NewTemplates(nameof(Template_DuplicateNamePerType_IsRejected_ButSameNameForAnotherTypeIsFine), out _);
        Assert.True((await svc.SaveAsync(Tpl("Ack", "Outgoing"))).Success);

        Assert.False((await svc.SaveAsync(Tpl("Ack", "Outgoing"))).Success);
        Assert.True((await svc.SaveAsync(Tpl("Ack", "Incoming"))).Success);
        Assert.True((await svc.SaveAsync(Tpl("Ack", null))).Success);
    }

    [Fact]
    public async Task Template_ListFiltersByTypeAndActivity_AndNeverDeletes()
    {
        var svc = NewTemplates(nameof(Template_ListFiltersByTypeAndActivity_AndNeverDeletes), out var ctx);
        var (_, _, outId) = await svc.SaveAsync(Tpl("Out", "Outgoing"));
        await svc.SaveAsync(Tpl("In", "Incoming"));
        await svc.SaveAsync(Tpl("Any", null));

        var forOutgoing = await svc.ListAsync("Outgoing", includeInactive: false);
        Assert.Equal(new[] { "Any", "Out" }, forOutgoing.Select(x => x.Name));

        Assert.True(await svc.SetActiveAsync(outId, false));
        Assert.DoesNotContain(await svc.ListAsync("Outgoing", false), x => x.Name == "Out");
        Assert.Contains(await svc.ListAsync("Outgoing", true), x => x.Name == "Out" && !x.IsActive);
        Assert.Equal(3, ctx.LetterTemplates.Count());                       // soft only
        Assert.False(await svc.SetActiveAsync(99999, true));
        Assert.Null(await svc.GetAsync(99999));
    }

    [Fact]
    public async Task Template_EditingOrDeactivating_NeverChangesAnExistingLetter()
    {
        var svc = NewTemplates(nameof(Template_EditingOrDeactivating_NeverChangesAnExistingLetter), out var ctx);
        var (_, _, id) = await svc.SaveAsync(Tpl());
        var letter = NewLetter();
        letter.Subject = "Acknowledgement";
        ctx.Letters.Add(letter);
        await ctx.SaveChangesAsync();

        var t = (await svc.GetAsync(id))!;
        t.Subject = "Totally different";
        await svc.SaveAsync(t);
        await svc.SetActiveAsync(id, false);

        Assert.Equal("Acknowledgement", ctx.Letters.Single(x => x.LetterId == letter.LetterId).Subject);
    }
}
