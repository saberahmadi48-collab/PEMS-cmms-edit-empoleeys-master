using PEMS.Application.DTOs;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;
using static PEMS.Tests.TestSupport.LetterTestWorld;

namespace PEMS.Tests.Cmms;

/// <summary>
/// گردش‌کار نامه‌ی داخلی/خارجی با زنجیره‌ی تایید ترتیبی: فقط تاییدکننده‌ی مرحله‌ی جاری، نویسنده تاییدکننده‌ی خودش نیست، مرحله‌ی میانی نامه را
/// PendingApproval نگه می‌دارد، بازگشت/ارسال مجدد زنجیره را از نو شروع می‌کند، ارسال فقط توسط نویسنده و فقط بعد از تایید، و اعلان‌ها به درست‌ترین افراد.
/// </summary>
public class LetterApprovalChainTests
{
    private static async Task<int> DraftAsync(LetterComposeService compose, bool approval, params int[] approvers)
    {
        var input = ValidInput();
        input.CcEmployeeIds = new() { 3 };
        input.BccEmployeeIds = new() { 4 };
        input.ApprovalRequired = approval;
        input.ApproverUserIds = approvers.ToList();
        var saved = await compose.SaveDraftAsync(input, AuthorActor());
        Assert.True(saved.Success, string.Join(",", saved.Errors.Select(e => e.Code)));
        return saved.LetterId;
    }

    private static Letter Get(PEMSDbContext ctx, int id) { ctx.ChangeTracker.Clear(); return ctx.Letters.Single(l => l.LetterId == id); }
    private static List<string> StepStatuses(PEMSDbContext ctx, int id) => ctx.LetterApprovalSteps.Where(s => s.LetterId == id).OrderBy(s => s.StepOrder).Select(s => s.Status).ToList();

    // ------------------------------------------------------------------ the happy path

    [Fact]
    public async Task ATwoStepChain_RunsInOrder_ThenTheAuthorSends_AndEveryRecipientIsNotified()
    {
        var (compose, letters, ctx, notes) = Services(nameof(ATwoStepChain_RunsInOrder_ThenTheAuthorSends_AndEveryRecipientIsNotified));
        var id = await DraftAsync(compose, true, Approver1, Approver2);

        Assert.True((await letters.SubmitForReviewAsync(id, Author)).Success);
        Assert.Equal("PendingApproval", Get(ctx, id).Status);
        Assert.Equal(new[] { Approver1 }, notes.Sent.Where(n => n.Title == "Letter awaiting your approval").Select(n => n.UserId));   // only the current step

        // out of turn / wrong person / the author
        Assert.False((await letters.ApproveAsync(id, Approver2, null)).Success);
        Assert.False((await letters.ApproveAsync(id, Other, null)).Success);
        Assert.False((await letters.ApproveAsync(id, Author, null)).Success);
        Assert.Equal("PendingApproval", Get(ctx, id).Status);

        // step 1 approves: the letter stays pending, step 2 is notified, the author hears about the progress
        notes.Sent.Clear();
        Assert.True((await letters.ApproveAsync(id, Approver1, "looks fine")).Success);
        var afterFirst = Get(ctx, id);
        Assert.Equal("PendingApproval", afterFirst.Status);
        Assert.Null(afterFirst.ApprovedById);
        Assert.Equal(new[] { "Approved", "Pending" }, StepStatuses(ctx, id));
        Assert.Contains(notes.Sent, n => n.UserId == Approver2 && n.Title == "Letter awaiting your approval");
        Assert.Contains(notes.Sent, n => n.UserId == Author && n.Title == "Your letter passed an approval step");
        Assert.False((await letters.ApproveAsync(id, Approver1, null)).Success);          // step 1 cannot decide twice

        // step 2 approves: now the letter is Approved, but NOT sent yet
        Assert.True((await letters.ApproveAsync(id, Approver2, null)).Success);
        var approved = Get(ctx, id);
        Assert.Equal("Approved", approved.Status);
        Assert.Equal(Approver2, approved.ApprovedById);
        Assert.Equal(new[] { "Approved", "Approved" }, StepStatuses(ctx, id));

        // sending: only the author, only once
        Assert.False((await letters.SendAsync(id, Approver1)).Success);
        notes.Sent.Clear();
        Assert.True((await letters.SendAsync(id, Author)).Success);
        var sent = Get(ctx, id);
        Assert.Equal("Sent", sent.Status);
        Assert.NotNull(sent.IssuedDate);
        Assert.False((await letters.SendAsync(id, Author)).Success);

        // To, Cc and Bcc recipients are notified (their users 102/103/104); the author is not
        Assert.Equal(new[] { To1, Cc1, Bcc1 }, notes.Sent.Where(n => n.Title == "New letter received").Select(n => n.UserId).OrderBy(x => x));

        // the history has the formal events
        Assert.Equal(new[] { "Draft", "Submitted", "Sent" }, ctx.LetterVersions.Where(v => v.LetterId == id).OrderBy(v => v.VersionNo).Select(v => v.Reason));
        Assert.Equal(2, ctx.LetterReviews.Count(r => r.LetterId == id && r.Decision == LetterReviewDecisions.Approved));
    }

    // ------------------------------------------------------------------ return / reject / resubmit

    [Fact]
    public async Task ReturningARestartedChain_ResetsEveryStepAndNotifiesTheFirstApproverAgain()
    {
        var (compose, letters, ctx, notes) = Services(nameof(ReturningARestartedChain_ResetsEveryStepAndNotifiesTheFirstApproverAgain));
        var id = await DraftAsync(compose, true, Approver1, Approver2);
        await letters.SubmitForReviewAsync(id, Author);
        await letters.ApproveAsync(id, Approver1, null);

        Assert.False((await letters.ReturnForRevisionAsync(id, Approver1, "late")).Success);     // not their turn any more
        Assert.True((await letters.ReturnForRevisionAsync(id, Approver2, "fix the date")).Success);
        Assert.Equal("ReturnedForRevision", Get(ctx, id).Status);
        Assert.Equal(new[] { "Approved", "Returned" }, StepStatuses(ctx, id));

        notes.Sent.Clear();
        var resubmitted = await letters.ResubmitAsync(id, "Quarterly maintenance schedule v2", "<p>fixed</p>", Author, null);

        Assert.True(resubmitted.Success, resubmitted.Error);
        Assert.Equal("PendingApproval", Get(ctx, id).Status);
        Assert.Equal(new[] { "Pending", "Pending" }, StepStatuses(ctx, id));
        Assert.Equal(new[] { Approver1 }, notes.Sent.Where(n => n.Title == "Revised letter awaiting your approval").Select(n => n.UserId));
        Assert.Contains(ctx.LetterVersions, v => v.LetterId == id && v.Reason == "Resubmitted");
    }

    [Fact]
    public async Task RejectingAtTheCurrentStep_IsTerminal_AndOnlyThatApproverMayReject()
    {
        var (compose, letters, ctx, _) = Services(nameof(RejectingAtTheCurrentStep_IsTerminal_AndOnlyThatApproverMayReject));
        var id = await DraftAsync(compose, true, Approver1, Approver2);
        await letters.SubmitForReviewAsync(id, Author);

        Assert.False((await letters.RejectAsync(id, Approver2, "no")).Success);                  // not their turn
        Assert.False((await letters.RejectAsync(id, Approver1, "")).Success);                    // reason required
        Assert.True((await letters.RejectAsync(id, Approver1, "duplicate request")).Success);

        Assert.Equal("Rejected", Get(ctx, id).Status);
        Assert.Equal(new[] { "Rejected", "Pending" }, StepStatuses(ctx, id));
        Assert.False((await letters.SendAsync(id, Author)).Success);
    }

    // ------------------------------------------------------------------ no approval needed / readiness

    [Fact]
    public async Task WithoutApproval_AnInternalDraftIsSentDirectly_AndCannotBeSubmitted()
    {
        var (compose, letters, ctx, notes) = Services(nameof(WithoutApproval_AnInternalDraftIsSentDirectly_AndCannotBeSubmitted));
        var id = await DraftAsync(compose, false);

        var submit = await letters.SubmitForReviewAsync(id, Author);
        Assert.False(submit.Success);
        Assert.Contains("تایید ندارد", submit.Error);

        Assert.True((await letters.SendAsync(id, Author)).Success);
        Assert.Equal("Sent", Get(ctx, id).Status);
        Assert.Contains(notes.Sent, n => n.UserId == To1 && n.Title == "New letter received");
    }

    [Fact]
    public async Task AnIncompleteFormalLetter_CannotBeSubmittedOrSent()
    {
        var (compose, letters, ctx, _) = Services(nameof(AnIncompleteFormalLetter_CannotBeSubmittedOrSent));
        var noBody = ValidInput(); noBody.Body = "<p> </p>";
        var noRecipient = ValidInput(); noRecipient.ToEmployeeIds = new();
        var noChain = ValidInput(); noChain.ApprovalRequired = true;

        var bodyId = (await compose.SaveDraftAsync(noBody, AuthorActor())).LetterId;
        var recipientId = (await compose.SaveDraftAsync(noRecipient, AuthorActor())).LetterId;
        var chainId = (await compose.SaveDraftAsync(noChain, AuthorActor())).LetterId;

        Assert.Contains("متن", (await letters.SendAsync(bodyId, Author)).Error);
        Assert.Contains("گیرنده", (await letters.SendAsync(recipientId, Author)).Error);
        Assert.Contains("زنجیره", (await letters.SubmitForReviewAsync(chainId, Author)).Error);
        Assert.All(new[] { bodyId, recipientId, chainId }, x => Assert.Equal("Draft", Get(ctx, x).Status));
    }

    [Fact]
    public async Task LegacyLettersWithoutAChain_KeepTheOldSingleStepBehaviour()
    {
        var (_, letters, ctx, _) = Services(nameof(LegacyLettersWithoutAChain_KeepTheOldSingleStepBehaviour));
        ctx.Letters.Add(new Letter { LetterId = 7000, LetterType = "Outgoing", LetterNumber = "OLD-1", Subject = "legacy", Status = "Draft", RegisteredById = Author, IsActive = true, ConfidentialityLevel = "Internal" });
        await ctx.SaveChangesAsync();

        Assert.True((await letters.SubmitForReviewAsync(7000, Author)).Success);                 // no Category -> no new strictness
        Assert.True((await letters.ApproveAsync(7000, Other, null)).Success);                    // any reviewer, as before
        Assert.Equal("Approved", Get(ctx, 7000).Status);
        Assert.True((await letters.IssueAsync(7000, Author)).Success);
    }

    // ------------------------------------------------------------------ Outgoing

    [Fact]
    public async Task OutgoingLetters_GoThroughApproval_ThenAreIssued_NotSent()
    {
        var (compose, letters, ctx, _) = Services(nameof(OutgoingLetters_GoThroughApproval_ThenAreIssued_NotSent));
        var input = ValidInput("Outgoing");
        input.ApproverUserIds = new() { Approver1 };
        var id = (await compose.SaveDraftAsync(input, AuthorActor())).LetterId;

        Assert.False((await letters.SendAsync(id, Author)).Success);                             // Send is for Internal only
        Assert.True((await letters.SubmitForReviewAsync(id, Author)).Success);
        Assert.True((await letters.ApproveAsync(id, Approver1, null)).Success);
        Assert.True((await letters.IssueAsync(id, Author)).Success);

        Assert.Equal("Issued", Get(ctx, id).Status);
        Assert.Contains(ctx.LetterVersions, v => v.LetterId == id && v.Reason == "Sent");
    }

    // ------------------------------------------------------------------ response tracking

    [Fact]
    public async Task AnswerSent_ClosesTheParentThatRequiredAResponse_ButNotOneThatDidNot()
    {
        var (compose, letters, ctx, _) = Services(nameof(AnswerSent_ClosesTheParentThatRequiredAResponse_ButNotOneThatDidNot));
        var needs = await DraftAsync(compose, false);
        var optional = await DraftAsync(compose, false);
        var parentNeeds = Get(ctx, needs); parentNeeds.ResponseRequired = true; parentNeeds.ResponseDueDate = DateTime.Today.AddDays(5);
        await ctx.SaveChangesAsync();
        await letters.SendAsync(needs, Author);
        await letters.SendAsync(optional, Author);

        foreach (var parent in new[] { needs, optional })
        {
            var reply = ValidInput();
            var replyId = (await compose.SaveDraftAsync(reply, new LetterComposeActor(To1, "u2", 2, false, false))).LetterId;
            ctx.Letters.Single(l => l.LetterId == replyId).ParentLetterId = parent;
            await ctx.SaveChangesAsync();
            Assert.True((await letters.SendAsync(replyId, To1)).Success);
        }

        var closed = Get(ctx, needs);
        Assert.Equal("Closed", closed.Status);
        Assert.NotNull(closed.ClosedDate);
        Assert.Equal("Sent", Get(ctx, optional).Status);
    }

    // ------------------------------------------------------------------ archive

    [Fact]
    public async Task Archive_IsForSentOrClosedLetters_ByTheAuthorOrAManager_Only()
    {
        var (compose, letters, ctx, _) = Services(nameof(Archive_IsForSentOrClosedLetters_ByTheAuthorOrAManager_Only));
        var id = await DraftAsync(compose, false);

        Assert.False((await letters.ArchiveAsync(id, Author, false)).Success);                   // a draft is discarded, not archived
        await letters.SendAsync(id, Author);

        Assert.False((await letters.ArchiveAsync(id, Other, false)).Success);
        Assert.True((await letters.ArchiveAsync(id, Other, canArchiveAny: true)).Success);
        var archived = Get(ctx, id);
        Assert.Equal("Archived", archived.Status);
        Assert.NotNull(archived.ArchivedDate);
        Assert.False((await letters.ArchiveAsync(id, Author, false)).Success);                   // already archived
        Assert.True(archived.IsActive);                                                          // still retrievable
    }

    // ------------------------------------------------------------------ reassign

    [Fact]
    public async Task ReassigningAStep_IsAdminOnly_PendingOnly_NeverTheAuthor_NeverADuplicate_NeverAnUnauthorisedUser()
    {
        var (compose, letters, ctx, notes) = Services(nameof(ReassigningAStep_IsAdminOnly_PendingOnly_NeverTheAuthor_NeverADuplicate_NeverAnUnauthorisedUser));
        var id = await DraftAsync(compose, true, Approver1, Approver2);
        await letters.SubmitForReviewAsync(id, Author);

        Assert.False((await letters.ReassignApprovalStepAsync(id, 1, Other, Author, isAdmin: false)).Success);
        Assert.False((await letters.ReassignApprovalStepAsync(id, 1, Author, 1, true)).Success);        // the author
        Assert.False((await letters.ReassignApprovalStepAsync(id, 1, Approver2, 1, true)).Success);     // already in the chain
        Assert.False((await letters.ReassignApprovalStepAsync(id, 1, To1, 1, true)).Success);           // no Approve permission
        Assert.False((await letters.ReassignApprovalStepAsync(id, 9, Other, 1, true)).Success);         // no such step

        notes.Sent.Clear();
        Assert.True((await letters.ReassignApprovalStepAsync(id, 1, Other, 1, true)).Success);
        Assert.Equal(new[] { Other, Approver2 }, ctx.LetterApprovalSteps.Where(s => s.LetterId == id).OrderBy(s => s.StepOrder).Select(s => s.ApproverUserId));
        Assert.Contains(notes.Sent, n => n.UserId == Other && n.Title == "Letter awaiting your approval");  // the new current approver is told
        Assert.Contains(ctx.AuditLogs, a => a.Action == "ReassignApprover");

        Assert.False((await letters.ApproveAsync(id, Approver1, null)).Success);                 // the old approver lost the turn
        await letters.ApproveAsync(id, Other, null);
        Assert.False((await letters.ReassignApprovalStepAsync(id, 1, Approver1, 1, true)).Success);     // step 1 is decided now
    }

    // ------------------------------------------------------------------ resilience

    [Fact]
    public async Task ANotificationFailure_NeverBlocksSubmitApproveOrSend()
    {
        var (compose, letters, ctx, notes) = Services(nameof(ANotificationFailure_NeverBlocksSubmitApproveOrSend));
        var id = await DraftAsync(compose, true, Approver1);
        notes.Throw = true;

        Assert.True((await letters.SubmitForReviewAsync(id, Author)).Success);
        Assert.True((await letters.ApproveAsync(id, Approver1, null)).Success);
        Assert.True((await letters.SendAsync(id, Author)).Success);
        Assert.Equal("Sent", Get(ctx, id).Status);
    }
}
