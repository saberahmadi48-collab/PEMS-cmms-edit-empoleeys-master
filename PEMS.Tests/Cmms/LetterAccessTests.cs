using PEMS.Application.Common;
using PEMS.Application.DTOs;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Identity;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;

namespace PEMS.Tests.Cmms;

/// <summary>
/// بازطراحی مکاتبات داخلی - کنترل دسترسی: نامه‌ی محرمانه با آدرس مستقیم برای غریبه باز نمی‌شود، گیرنده (To/Cc/Bcc)، ارجاع‌گیرنده،
/// نویسنده و عضو زنجیره‌ی تایید می‌بینند، پیش‌نویس برای غریبه نامرئی است، BCC فقط برای نویسنده/دسترسی کامل/خودِ گیرنده دیده می‌شود،
/// و بازکردن نامه‌ی ارسال‌شده در گزارش «چه کسی دیده» ثبت می‌شود.
/// </summary>
public class LetterAccessTests
{
    private const int AuthorUser = 101, RecipientUser = 102, CcUser = 103, BccUser = 104, StrangerUser = 105, ApproverUser = 106, ReferredUser = 107;
    private const int AuthorEmp = 1, RecipientEmp = 2, CcEmp = 3, BccEmp = 4, StrangerEmp = 5, ApproverEmp = 6, ReferredEmp = 7;

    private static (LetterService Svc, PEMSDbContext Ctx) Create(string name)
    {
        var ctx = PemsDbContextFactory.CreateInMemory($"LetterAccess_{name}");
        for (var i = 1; i <= 7; i++)
        {
            ctx.Employees.Add(new Employee { EmployeeId = i, FirstName = "E" + i, LastName = "L" + i, IsActive = true });
            ctx.Users.Add(new ApplicationUser { Id = 100 + i, UserName = "u" + i, NormalizedUserName = "U" + i, EmployeeId = i, IsActive = true, FullName = "User " + i });
        }
        ctx.SaveChanges();
        return (new LetterService(ctx, new RichTextSanitizerService(), knowledgeBaseService: null!), ctx);
    }

    private static int _id = 20000;

    private static Letter NewLetter(string status = "Sent", string level = "Confidential", string type = "Internal") => new()
    {
        LetterId = ++_id, LetterType = type, LetterNumber = $"ENG-2026-{_id}", Subject = $"Subject {_id}", Body = "<p>secret body</p>",
        Status = status, ConfidentialityLevel = level, RegisteredById = AuthorUser, IsActive = true, LetterDate = DateTime.Today
    };

    private static LetterViewer Viewer(int user, int emp, bool all = false) => new(user, emp, all, "u" + (user - 100));

    private static async Task<(LetterService Svc, PEMSDbContext Ctx, Letter Letter)> Seed(string name, string status = "Sent", string level = "Confidential")
    {
        var (svc, ctx) = Create(name);
        var l = NewLetter(status, level);
        ctx.Letters.Add(l);
        ctx.LetterRecipients.AddRange(
            new LetterRecipient { LetterId = l.LetterId, EmployeeId = RecipientEmp, RecipientType = "To" },
            new LetterRecipient { LetterId = l.LetterId, EmployeeId = CcEmp, RecipientType = "Cc" },
            new LetterRecipient { LetterId = l.LetterId, EmployeeId = BccEmp, RecipientType = "Bcc" });
        await ctx.SaveChangesAsync();
        return (svc, ctx, l);
    }

    // ------------------------------------------------------------------ pure rule

    [Theory]
    // status, level, author, recipient, referral, chain, seeAll, expected
    [InlineData("Sent", "Confidential", false, false, false, false, false, false)]   // stranger, restricted
    [InlineData("Sent", "Internal", false, false, false, false, false, true)]        // stranger, open level
    [InlineData("Sent", "Public", false, false, false, false, false, true)]
    [InlineData("Sent", "Confidential", true, false, false, false, false, true)]     // author
    [InlineData("Sent", "Confidential", false, true, false, false, false, true)]     // recipient
    [InlineData("Sent", "Restricted", false, false, true, false, false, true)]       // referral target
    [InlineData("Sent", "HighlyConfidential", false, false, false, false, true, true)] // admin / approve
    [InlineData("Draft", "Internal", false, false, false, false, false, false)]      // draft hidden from everyone else
    [InlineData("Draft", "Internal", false, true, false, false, false, false)]       // even a listed recipient
    [InlineData("PendingApproval", "Public", false, false, false, false, false, false)]
    [InlineData("PendingApproval", "Public", false, false, false, true, false, true)] // chain approver
    [InlineData("Approved", "Internal", false, true, false, false, false, false)]    // approved but not yet sent
    [InlineData("Draft", "Internal", true, false, false, false, false, true)]
    [InlineData("Rejected", "Public", false, false, false, false, false, false)]
    [InlineData("Closed", "Confidential", false, false, false, false, false, false)]
    [InlineData("Archived", "Internal", false, false, false, false, false, true)]
    public void CanView_FollowsTheAccessMatrix(string status, string level, bool author, bool recipient, bool referral, bool chain, bool all, bool expected)
        => Assert.Equal(expected, LetterAccessRules.CanView(new LetterAccessFacts(status, level, author, recipient, referral, chain, all)));

    // ------------------------------------------------------------------ detail by URL

    [Fact]
    public async Task OpeningAConfidentialLetterByIdIsForbidden_ForAStranger_ButAllowedForEveryParticipant()
    {
        var (svc, ctx, l) = await Seed(nameof(OpeningAConfidentialLetterByIdIsForbidden_ForAStranger_ButAllowedForEveryParticipant));
        ctx.LetterReferrals.Add(new LetterReferral { LetterId = l.LetterId, ReferredToEmployeeId = ReferredEmp, Status = "Pending" });
        ctx.LetterApprovalSteps.Add(new LetterApprovalStep { LetterId = l.LetterId, StepOrder = 1, ApproverUserId = ApproverUser, Status = "Approved" });
        await ctx.SaveChangesAsync();

        Assert.Equal(LetterAccessOutcome.Forbidden, (await svc.GetDetailForViewerAsync(l.LetterId, Viewer(StrangerUser, StrangerEmp))).Outcome);
        foreach (var (u, e) in new[] { (AuthorUser, AuthorEmp), (RecipientUser, RecipientEmp), (CcUser, CcEmp), (BccUser, BccEmp), (ReferredUser, ReferredEmp), (ApproverUser, ApproverEmp) })
            Assert.Equal(LetterAccessOutcome.Ok, (await svc.GetDetailForViewerAsync(l.LetterId, Viewer(u, e))).Outcome);
        Assert.Equal(LetterAccessOutcome.Ok, (await svc.GetDetailForViewerAsync(l.LetterId, Viewer(StrangerUser, StrangerEmp, all: true))).Outcome);
    }

    [Fact]
    public async Task MissingAndArchivedAwayLetters_AreNotFound_NotForbidden()
    {
        var (svc, ctx, l) = await Seed(nameof(MissingAndArchivedAwayLetters_AreNotFound_NotForbidden));
        l.IsActive = false;
        await ctx.SaveChangesAsync();

        Assert.Equal(LetterAccessOutcome.NotFound, (await svc.GetDetailForViewerAsync(l.LetterId, Viewer(AuthorUser, AuthorEmp))).Outcome);
        Assert.Equal(LetterAccessOutcome.NotFound, (await svc.GetDetailForViewerAsync(999999, Viewer(AuthorUser, AuthorEmp))).Outcome);
    }

    [Fact]
    public async Task ADraftIsInvisibleToEveryoneButItsAuthorChainAndPrivilegedUsers_EvenWhenPublic()
    {
        var (svc, ctx, l) = await Seed(nameof(ADraftIsInvisibleToEveryoneButItsAuthorChainAndPrivilegedUsers_EvenWhenPublic), "Draft", "Public");
        ctx.LetterApprovalSteps.Add(new LetterApprovalStep { LetterId = l.LetterId, StepOrder = 1, ApproverUserId = ApproverUser });
        await ctx.SaveChangesAsync();

        Assert.False(await svc.CanViewAsync(l.LetterId, Viewer(StrangerUser, StrangerEmp)));
        Assert.False(await svc.CanViewAsync(l.LetterId, Viewer(RecipientUser, RecipientEmp)));   // listed recipient, but not sent yet
        Assert.True(await svc.CanViewAsync(l.LetterId, Viewer(AuthorUser, AuthorEmp)));
        Assert.True(await svc.CanViewAsync(l.LetterId, Viewer(ApproverUser, ApproverEmp)));
        Assert.True(await svc.CanViewAsync(l.LetterId, Viewer(StrangerUser, StrangerEmp, all: true)));
    }

    // ------------------------------------------------------------------ BCC

    [Fact]
    public async Task Bcc_IsHiddenFromOrdinaryRecipients_ShownToAuthorAdminAndTheBccRecipientThemselves()
    {
        var (svc, _, l) = await Seed(nameof(Bcc_IsHiddenFromOrdinaryRecipients_ShownToAuthorAdminAndTheBccRecipientThemselves));

        async Task<List<LetterRecipientDto>> For(int u, int e, bool all = false)
            => (await svc.GetDetailForViewerAsync(l.LetterId, Viewer(u, e, all))).Detail!.Recipients;

        var asTo = await For(RecipientUser, RecipientEmp);
        Assert.Equal(new[] { "Cc", "To" }, asTo.Select(r => r.RecipientType).OrderBy(x => x));
        Assert.DoesNotContain(asTo, r => r.RecipientType == "Bcc");

        var asAuthor = await For(AuthorUser, AuthorEmp);
        Assert.Contains(asAuthor, r => r.RecipientType == "Bcc");

        var asAdmin = await For(StrangerUser, StrangerEmp, all: true);
        Assert.Contains(asAdmin, r => r.RecipientType == "Bcc");

        var asBcc = await For(BccUser, BccEmp);
        var bccRows = asBcc.Where(r => r.RecipientType == "Bcc").ToList();
        Assert.Single(bccRows);
        Assert.Equal(BccEmp, bccRows[0].EmployeeId);
    }

    [Fact]
    public async Task Bcc_IsAlsoHiddenInTheTimeline_ForOrdinaryViewers_AndWithoutAViewer()
    {
        var (svc, _, l) = await Seed(nameof(Bcc_IsAlsoHiddenInTheTimeline_ForOrdinaryViewers_AndWithoutAViewer));

        var ordinary = await svc.GetTimelineAsync(l.LetterId, Viewer(RecipientUser, RecipientEmp));
        var noViewer = await svc.GetTimelineAsync(l.LetterId);
        var author = await svc.GetTimelineAsync(l.LetterId, Viewer(AuthorUser, AuthorEmp));

        Assert.DoesNotContain(ordinary, e => e.Description.Contains("Bcc"));
        Assert.DoesNotContain(noViewer, e => e.Description.Contains("Bcc"));
        Assert.Contains(author, e => e.Description.StartsWith("Added as Bcc:"));
    }

    // ------------------------------------------------------------------ list filter

    [Fact]
    public async Task TheListShowsAConfidentialLetterToItsRecipients_AndHidesItFromStrangers()
    {
        var (svc, _, l) = await Seed(nameof(TheListShowsAConfidentialLetterToItsRecipients_AndHidesItFromStrangers));

        async Task<bool> Sees(int u) => (await svc.SearchAsync(null, null, null, null, null, null, null, null, null, null, 1, 50, false, u)).Items.Any(i => i.LetterId == l.LetterId);

        Assert.True(await Sees(RecipientUser));
        Assert.True(await Sees(CcUser));
        Assert.True(await Sees(BccUser));
        Assert.True(await Sees(AuthorUser));
        Assert.False(await Sees(StrangerUser));
    }

    [Fact]
    public async Task TheList_HidesOthersDrafts_ButShowsThePendingLetterToItsApprover()
    {
        var (svc, ctx, draft) = await Seed(nameof(TheList_HidesOthersDrafts_ButShowsThePendingLetterToItsApprover), "Draft", "Public");
        var pending = NewLetter("PendingApproval", "Public");
        ctx.Letters.Add(pending);
        ctx.LetterApprovalSteps.Add(new LetterApprovalStep { LetterId = pending.LetterId, StepOrder = 1, ApproverUserId = ApproverUser });
        await ctx.SaveChangesAsync();

        var stranger = (await svc.SearchAsync(null, null, null, null, null, null, null, null, null, null, 1, 50, false, StrangerUser)).Items.Select(i => i.LetterId).ToList();
        var approver = (await svc.SearchAsync(null, null, null, null, null, null, null, null, null, null, 1, 50, false, ApproverUser)).Items.Select(i => i.LetterId).ToList();

        Assert.DoesNotContain(draft.LetterId, stranger);
        Assert.DoesNotContain(pending.LetterId, stranger);
        Assert.Contains(pending.LetterId, approver);
        Assert.DoesNotContain(draft.LetterId, approver);
    }

    // ------------------------------------------------------------------ view log

    [Fact]
    public async Task OpeningASentLetter_IsLoggedOncePerUserWithACount_AndMarksTheRecipientRead()
    {
        var (svc, ctx, l) = await Seed(nameof(OpeningASentLetter_IsLoggedOncePerUserWithACount_AndMarksTheRecipientRead));

        await svc.GetDetailForViewerAsync(l.LetterId, Viewer(RecipientUser, RecipientEmp));
        await svc.GetDetailForViewerAsync(l.LetterId, Viewer(RecipientUser, RecipientEmp));
        await svc.GetDetailForViewerAsync(l.LetterId, Viewer(CcUser, CcEmp));

        var logs = ctx.LetterViewLogs.Where(x => x.LetterId == l.LetterId).ToList();
        Assert.Equal(2, logs.Count);
        Assert.Equal(2, logs.Single(x => x.UserId == RecipientUser).ViewCount);
        Assert.Equal(1, logs.Single(x => x.UserId == CcUser).ViewCount);
        Assert.True(ctx.LetterRecipients.Single(r => r.LetterId == l.LetterId && r.EmployeeId == RecipientEmp).IsRead);
        Assert.NotNull(ctx.LetterRecipients.Single(r => r.LetterId == l.LetterId && r.EmployeeId == RecipientEmp).ReadDate);
        Assert.False(ctx.LetterRecipients.Single(r => r.LetterId == l.LetterId && r.EmployeeId == BccEmp).IsRead);   // not opened yet
    }

    [Fact]
    public async Task OpeningADraft_IsNotLogged_AndAForbiddenAttemptLeavesNoTrace()
    {
        var (svc, ctx, draft) = await Seed(nameof(OpeningADraft_IsNotLogged_AndAForbiddenAttemptLeavesNoTrace), "Draft");
        var sent = NewLetter("Sent");
        ctx.Letters.Add(sent);
        await ctx.SaveChangesAsync();

        await svc.GetDetailForViewerAsync(draft.LetterId, Viewer(AuthorUser, AuthorEmp));
        await svc.GetDetailForViewerAsync(sent.LetterId, Viewer(StrangerUser, StrangerEmp));   // forbidden

        Assert.Empty(ctx.LetterViewLogs);
    }

    // ------------------------------------------------------------------ Phase 2 regression: the detail DTO now carries the response data

    [Fact]
    public async Task TheDetailDto_CarriesTheResponseRequirement_ThatPhase2NeverFilledIn()
    {
        var (svc, ctx, l) = await Seed(nameof(TheDetailDto_CarriesTheResponseRequirement_ThatPhase2NeverFilledIn), "Sent", "Internal");
        l.ResponseRequired = true;
        l.ResponseDueDate = DateTime.Today.AddDays(-3);
        await ctx.SaveChangesAsync();

        var overdue = (await svc.GetDetailForViewerAsync(l.LetterId, Viewer(AuthorUser, AuthorEmp))).Detail!.Letter;
        Assert.True(overdue.ResponseRequired);
        Assert.Equal(DateTime.Today.AddDays(-3), overdue.ResponseDueDate);
        Assert.True(overdue.ResponseOverdue);
        Assert.False(overdue.ResponseAnswered);

        ctx.Letters.Add(new Letter { LetterId = ++_id, LetterType = "Outgoing", LetterNumber = "R-" + _id, Subject = "Re", Status = "Issued", ParentLetterId = l.LetterId, IsActive = true, ConfidentialityLevel = "Public" });
        await ctx.SaveChangesAsync();

        var answered = (await svc.GetDetailForViewerAsync(l.LetterId, Viewer(AuthorUser, AuthorEmp))).Detail!.Letter;
        Assert.True(answered.ResponseAnswered);
        Assert.False(answered.ResponseOverdue);
    }

    [Fact]
    public async Task TheDetailDto_CarriesTheSenderBlockSnapshot_AndTheApprovalChain()
    {
        var (svc, ctx, l) = await Seed(nameof(TheDetailDto_CarriesTheSenderBlockSnapshot_AndTheApprovalChain), "PendingApproval", "Internal");
        ctx.Departments.Add(new Department { DepartmentId = 9, Code = "ENG", Name = "Engineering" });
        l.SenderEmployeeId = AuthorEmp; l.SenderTitle = "Lead Engineer"; l.SenderDepartmentId = 9; l.Category = "Request"; l.ReferenceNumber = "REF-7"; l.ApprovalRequired = true;
        ctx.LetterApprovalSteps.AddRange(
            new LetterApprovalStep { LetterId = l.LetterId, StepOrder = 1, ApproverUserId = ApproverUser, Status = "Approved" },
            new LetterApprovalStep { LetterId = l.LetterId, StepOrder = 2, ApproverUserId = CcUser, Status = "Pending" });
        await ctx.SaveChangesAsync();

        var detail = (await svc.GetDetailForViewerAsync(l.LetterId, Viewer(AuthorUser, AuthorEmp))).Detail!;

        Assert.Equal("E1 L1", detail.Letter.SenderName);
        Assert.Equal("Lead Engineer", detail.Letter.SenderTitle);
        Assert.Equal("Engineering", detail.Letter.SenderDepartmentName);
        Assert.Equal("Request", detail.Letter.Category);
        Assert.Equal("REF-7", detail.Letter.ReferenceNumber);
        Assert.True(detail.Letter.ApprovalRequired);
        Assert.Equal(new[] { 1, 2 }, detail.ApprovalSteps.Select(s => s.StepOrder));
        Assert.False(detail.ApprovalSteps[0].IsCurrent);
        Assert.True(detail.ApprovalSteps[1].IsCurrent);       // first Pending step while PendingApproval
        Assert.Equal("User 6", detail.ApprovalSteps[0].ApproverName);
    }
}
