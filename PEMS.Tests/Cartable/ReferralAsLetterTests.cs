using PEMS.Application.Common;
using PEMS.Application.DTOs;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;

namespace PEMS.Tests.Cartable;

/// <summary>
/// ارجاع و پاسخ یکپارچه‌اند: ارجاع یک نامه‌ی داخلیِ تازه با موضوع ثابتِ نامه‌ی اصلی است که فقط برچسب «ارجاع» می‌گیرد؛ هنگام ارسال برای هر گیرنده یک
/// ردیف ارجاع روی نامه‌ی اصلی می‌سازد (پیگیری مهلت/معوق همان است) و پاسخِ خودِ ارجاع‌گیرنده ارجاعش را می‌بندد. گردش پیام‌ها با متن کامل و به‌شرط دسترسی.
/// </summary>
public class ReferralAsLetterTests
{
    private const int Author = 101, Referrer = 102, Referee1 = 103, Referee2 = 104, Stranger = 105;

    private static LetterComposeActor Actor(int userId) => new(userId, "u" + (userId - 100), userId - 100, false, false);

    private static Letter ParentLetter(PEMSDbContext ctx, string type = "Incoming", string status = "Registered", string conf = "Internal", string? subject = null)
    {
        var l = new Letter
        {
            LetterType = type, LetterNumber = "IN-1", LetterDate = DateTime.Today.AddDays(-2), CreatedDate = DateTime.Today.AddDays(-2), Status = status, IsActive = true,
            Subject = subject ?? "Pump P-101 datasheet revision request " + new string('x', 90),   // longer than the 80-char limit on purpose
            Body = "<p>Please revise the datasheet.</p>", ConfidentialityLevel = conf, Priority = "High", Sender = "ACME Ltd", RegisteredById = Author, ReceiverEmployeeId = 2
        };
        l.Recipients.Add(new LetterRecipient { RecipientType = "To", EmployeeId = 2 });
        ctx.Letters.Add(l); ctx.SaveChanges();
        return l;
    }

    private static async Task<(int LetterId, LetterComposeInput Input)> SaveReferralAsync(LetterComposeService compose, int parentId, int[] to, DateTime? due = null, string instruction = "Please check P-101")
    {
        var input = (await compose.BuildReferralInputAsync(parentId, Actor(Referrer)))!;
        input.Body = input.Body!.Replace("<p></p>", "<p>" + instruction + "</p>");
        input.ToEmployeeIds = to.ToList();
        if (due.HasValue) { input.ResponseRequired = true; input.ResponseDueDate = due; }
        var saved = await compose.SaveDraftAsync(input, Actor(Referrer));
        Assert.True(saved.Success, string.Join(",", saved.Errors.Select(e => e.Code)));
        return (saved.LetterId, input);
    }

    // ------------------------------------------------------------------ pure rules
    [Fact]
    public void InstructionText_DropsTheQuotedOriginal_AndTruncates()
    {
        Assert.Equal("Check it", LetterReferralRules.InstructionText("<p>Check it</p><blockquote><p>old</p><p>text</p></blockquote>"));
        Assert.Null(LetterReferralRules.InstructionText("<p></p><blockquote><p>only quote</p></blockquote>"));
        Assert.Null(LetterReferralRules.InstructionText(null));
        Assert.Equal(LetterReferralRules.MaxInstructionLength, LetterReferralRules.InstructionText("<p>" + new string('a', 5000) + "</p>")!.Length);
    }

    [Fact]
    public void ClosesOnReply_OnlyForTheRepliersOwnOpenReferral_OnTheRepliedLetterOrItsReferralLetter()
    {
        Assert.True(LetterReferralRules.ClosesOnReply(3, "Pending", 10, null, 3, 10));          // reply to the original letter
        Assert.True(LetterReferralRules.ClosesOnReply(3, "Pending", 10, 11, 3, 11));            // reply to the referral letter
        Assert.False(LetterReferralRules.ClosesOnReply(4, "Pending", 10, 11, 3, 11));           // someone else's referral
        Assert.False(LetterReferralRules.ClosesOnReply(3, "Completed", 10, 11, 3, 11));         // already done
        Assert.False(LetterReferralRules.ClosesOnReply(3, "Pending", 10, 11, 3, 99));           // unrelated letter
        Assert.False(LetterReferralRules.ClosesOnReply(3, "Pending", 10, 11, null, 11));        // replier without an employee record
    }

    // ------------------------------------------------------------------ composing a referral
    [Fact]
    public async Task BuildReferralInput_KeepsTheSubject_QuotesTheOriginal_AndNeedsNoApproval()
    {
        var (compose, _, ctx, _) = LetterTestWorld.Services(nameof(BuildReferralInput_KeepsTheSubject_QuotesTheOriginal_AndNeedsNoApproval));
        var parent = ParentLetter(ctx);

        var input = (await compose.BuildReferralInputAsync(parent.LetterId, Actor(Referrer)))!;

        Assert.True(input.IsReferral);
        Assert.Equal(parent.LetterId, input.ParentLetterId);
        Assert.Equal("Internal", input.LetterType);
        Assert.Equal(parent.Subject, input.Subject);
        Assert.False(input.ApprovalRequired);
        Assert.Equal("High", input.Priority);
        Assert.Empty(input.ToEmployeeIds);                                    // the user picks the people
        Assert.Contains("<blockquote>", input.Body);
        Assert.Contains("Please revise the datasheet.", input.Body);
        Assert.Contains("IN-1", input.Body);
        Assert.Null(await compose.BuildReferralInputAsync(9999, Actor(Referrer)));
    }

    [Fact]
    public async Task SaveReferralDraft_ForcesTheParentSubject_TagsIt_AndNeverNeedsApproval()
    {
        var (compose, _, ctx, _) = LetterTestWorld.Services(nameof(SaveReferralDraft_ForcesTheParentSubject_TagsIt_AndNeverNeedsApproval));
        var parent = ParentLetter(ctx);
        var input = (await compose.BuildReferralInputAsync(parent.LetterId, Actor(Referrer)))!;
        input.Subject = "HACKED SUBJECT"; input.ToEmployeeIds = new() { 3 }; input.ApprovalRequired = true; input.Category = "Notice";

        var saved = await compose.SaveDraftAsync(input, Actor(Referrer));

        Assert.True(saved.Success);
        var letter = ctx.Letters.Single(l => l.LetterId == saved.LetterId);
        Assert.True(letter.IsReferral);
        Assert.Equal(parent.Subject, letter.Subject);
        Assert.Equal("Instruction", letter.Category);
        Assert.False(letter.ApprovalRequired);
        Assert.Equal(parent.LetterId, letter.ParentLetterId);

        var tag = Assert.Single(ctx.Tags, t => t.Name == LetterComposeRules.ReferralTagName);
        Assert.Null(tag.OwnerUserId);                                         // organizational tag, visible to everyone
        Assert.Single(ctx.LetterTags, lt => lt.LetterId == saved.LetterId && lt.TagId == tag.TagId);

        // a second save (auto-save) neither duplicates the tag nor the link, and reloading keeps the flag
        input.LetterId = saved.LetterId; input.RowVersion = saved.RowVersion;
        Assert.True((await compose.SaveDraftAsync(input, Actor(Referrer))).Success);
        Assert.Single(ctx.Tags, t => t.Name == LetterComposeRules.ReferralTagName);
        Assert.Single(ctx.LetterTags, lt => lt.LetterId == saved.LetterId);
        Assert.True((await compose.LoadForEditAsync(saved.LetterId, Actor(Referrer)))!.IsReferral);
    }

    [Fact]
    public async Task Referral_RequiresAnExistingParent_AndIsIgnoredForExternalLetters()
    {
        var (compose, _, ctx, _) = LetterTestWorld.Services(nameof(Referral_RequiresAnExistingParent_AndIsIgnoredForExternalLetters));
        var orphan = new LetterComposeInput { IsReferral = true, LetterType = "Internal", Subject = "x", ToEmployeeIds = new() { 3 } };
        var r = await compose.SaveDraftAsync(orphan, Actor(Referrer));
        Assert.False(r.Success);
        Assert.Contains(r.Errors, e => e.Code == "ReferralNeedsParent");

        var parent = ParentLetter(ctx);
        var external = new LetterComposeInput { IsReferral = true, ParentLetterId = parent.LetterId, LetterType = "Outgoing", Subject = "Re", ExternalReceiver = "ACME", ApprovalRequired = true };
        var saved = await compose.SaveDraftAsync(external, Actor(Referrer));
        Assert.True(saved.Success);
        Assert.False(ctx.Letters.Single(l => l.LetterId == saved.LetterId).IsReferral);
    }

    // ------------------------------------------------------------------ sending a referral
    [Fact]
    public async Task SendingAReferral_CreatesOneReferralPerToRecipient_LinkedToTheLetter_WithTheInstructionOnly()
    {
        var (compose, letters, ctx, notes) = LetterTestWorld.Services(nameof(SendingAReferral_CreatesOneReferralPerToRecipient_LinkedToTheLetter_WithTheInstructionOnly));
        var parent = ParentLetter(ctx);
        var due = DateTime.Today.AddDays(5);
        var (letterId, _) = await SaveReferralAsync(compose, parent.LetterId, new[] { 3, 4 }, due);

        var check = await compose.ValidateForSubmitAsync(letterId, Actor(Referrer));
        Assert.True(check.Success, string.Join(",", check.Errors.Select(e => e.Code)));
        var (ok, error) = await letters.SendAsync(letterId, Referrer);
        Assert.True(ok, error);

        var rows = ctx.LetterReferrals.Where(r => r.LetterId == parent.LetterId).ToList();
        Assert.Equal(new[] { 3, 4 }, rows.Select(r => r.ReferredToEmployeeId!.Value).OrderBy(x => x));
        Assert.All(rows, r =>
        {
            Assert.Equal(letterId, r.ReferralLetterId);
            Assert.Equal("Pending", r.Status);
            Assert.Equal("Please check P-101", r.Instructions);              // no quoted original in the instruction
            Assert.Equal(due, r.DueDate);
            Assert.Equal("E2 L2", r.ReferredByName);
        });
        Assert.Equal("Referred", ctx.Letters.Single(l => l.LetterId == parent.LetterId).Status);   // a registered incoming letter moves on
        Assert.Equal("Sent", ctx.Letters.Single(l => l.LetterId == letterId).Status);
        Assert.Equal(2, notes.Sent.Count);
        Assert.All(notes.Sent, n => Assert.Equal("نامه‌ای به شما ارجاع شد", n.Title));

        // sending is one-shot; nothing is duplicated
        Assert.Equal(2, ctx.LetterReferrals.Count());
    }

    [Fact]
    public async Task CcRecipientsOfAReferralGetTheLetterButNoReferralRow()
    {
        var (compose, letters, ctx, _) = LetterTestWorld.Services(nameof(CcRecipientsOfAReferralGetTheLetterButNoReferralRow));
        var parent = ParentLetter(ctx);
        var input = (await compose.BuildReferralInputAsync(parent.LetterId, Actor(Referrer)))!;
        input.ToEmployeeIds = new() { 3 }; input.CcEmployeeIds = new() { 4 };
        var saved = await compose.SaveDraftAsync(input, Actor(Referrer));
        Assert.True((await letters.SendAsync(saved.LetterId, Referrer)).Success);

        Assert.Equal(3, Assert.Single(ctx.LetterReferrals).ReferredToEmployeeId);
        Assert.Equal(2, ctx.LetterRecipients.Count(r => r.LetterId == saved.LetterId));
    }

    [Fact]
    public async Task TheRefereesReply_ClosesTheirOwnReferralOnly_AndAnUnrelatedReplyClosesNothing()
    {
        var (compose, letters, ctx, _) = LetterTestWorld.Services(nameof(TheRefereesReply_ClosesTheirOwnReferralOnly_AndAnUnrelatedReplyClosesNothing));
        var parent = ParentLetter(ctx);
        var (referralId, _) = await SaveReferralAsync(compose, parent.LetterId, new[] { 3, 4 });
        Assert.True((await letters.SendAsync(referralId, Referrer)).Success);

        // a stranger replying to the referral letter closes nothing
        var strangerReply = (await compose.BuildReplyInputAsync(referralId, Actor(Stranger)))!;
        strangerReply.Body = "<p>fyi</p>"; strangerReply.Category = "Information"; strangerReply.ToEmployeeIds = new() { 2 };
        var s1 = await compose.SaveDraftAsync(strangerReply, Actor(Stranger));
        Assert.True((await letters.SendAsync(s1.LetterId, Stranger)).Success);
        Assert.All(ctx.LetterReferrals, r => Assert.Equal("Pending", r.Status));

        // referee 3 replies to the referral letter: only their own referral is completed
        var reply = (await compose.BuildReplyInputAsync(referralId, Actor(Referee1)))!;
        reply.Body = "<p>Done, datasheet revised.</p>"; reply.Category = "Information";
        var saved = await compose.SaveDraftAsync(reply, Actor(Referee1));
        Assert.True(saved.Success, string.Join(",", saved.Errors.Select(e => e.Code)));
        Assert.True((await letters.SendAsync(saved.LetterId, Referee1)).Success);

        var mine = ctx.LetterReferrals.Single(r => r.ReferredToEmployeeId == 3);
        Assert.Equal("Completed", mine.Status);
        Assert.NotNull(mine.CompletedDate);
        Assert.True(mine.IsRead);
        Assert.Contains(ctx.Letters.Single(l => l.LetterId == saved.LetterId).LetterNumber, mine.ResponseNote);
        Assert.Equal("Pending", ctx.LetterReferrals.Single(r => r.ReferredToEmployeeId == 4).Status);
    }

    [Fact]
    public async Task ReplyingStraightToTheOriginalLetter_AlsoClosesTheRefereesReferral()
    {
        var (compose, letters, ctx, _) = LetterTestWorld.Services(nameof(ReplyingStraightToTheOriginalLetter_AlsoClosesTheRefereesReferral));
        var parent = ParentLetter(ctx, type: "Internal", status: "Sent");
        var (referralId, _) = await SaveReferralAsync(compose, parent.LetterId, new[] { 3 });
        Assert.True((await letters.SendAsync(referralId, Referrer)).Success);

        var reply = (await compose.BuildReplyInputAsync(parent.LetterId, Actor(Referee1)))!;
        reply.Body = "<p>Handled</p>"; reply.Category = "Information";
        var saved = await compose.SaveDraftAsync(reply, Actor(Referee1));
        Assert.True((await letters.SendAsync(saved.LetterId, Referee1)).Success);

        Assert.Equal("Completed", Assert.Single(ctx.LetterReferrals).Status);
    }

    // ------------------------------------------------------------------ message flow
    [Fact]
    public async Task Conversation_ListsOriginalReplyAndReferralWithFullTextInOrder_AndHidesWhatTheViewerMaySeeNot()
    {
        var (compose, letters, ctx, _) = LetterTestWorld.Services(nameof(Conversation_ListsOriginalReplyAndReferralWithFullTextInOrder_AndHidesWhatTheViewerMaySeeNot));
        var parent = ParentLetter(ctx, type: "Internal", status: "Sent");
        parent.CreatedDate = DateTime.Today.AddDays(-3); parent.LetterDate = DateTime.Today.AddDays(-3);

        var (referralId, _) = await SaveReferralAsync(compose, parent.LetterId, new[] { 3 });
        Assert.True((await letters.SendAsync(referralId, Referrer)).Success);
        var reply = (await compose.BuildReplyInputAsync(referralId, Actor(Referee1)))!;
        reply.Body = "<p>Done</p>"; reply.Category = "Information"; reply.BccEmployeeIds = new() { 5 };
        var saved = await compose.SaveDraftAsync(reply, Actor(Referee1));
        Assert.True((await letters.SendAsync(saved.LetterId, Referee1)).Success);

        var cart = new LetterCartableService(ctx, new OrganizationSubordinateResolver(ctx));
        var flow = await cart.GetConversationAsync(referralId, Referee1, canViewConfidential: false);

        Assert.Equal(new[] { parent.LetterId, referralId, saved.LetterId }, flow.Select(m => m.LetterId));
        Assert.Equal(new[] { "Original", "Referral", "Reply" }, flow.Select(m => m.Kind));
        Assert.Equal(referralId, Assert.Single(flow, m => m.IsCurrent).LetterId);
        Assert.Contains("Please revise the datasheet.", flow[0].Body);
        Assert.Contains("Please check P-101", flow[1].Body);
        Assert.Equal("E2 L2", flow[1].SenderName);
        Assert.Contains("E3 L3", flow[1].RecipientsSummary);
        Assert.DoesNotContain("E5", string.Join(" ", flow.Select(m => m.RecipientsSummary + m.CcSummary)));   // Bcc never shown

        // a restricted reply is invisible to someone who is neither a recipient nor privileged
        var secret = new Letter { LetterType = "Internal", LetterNumber = "S-1", LetterDate = DateTime.Today, Status = "Sent", IsActive = true, Subject = "s", Body = "<p>secret</p>",
            ConfidentialityLevel = "Restricted", ParentLetterId = parent.LetterId, RegisteredById = Author, CreatedDate = DateTime.Now };
        secret.Recipients.Add(new LetterRecipient { RecipientType = "To", EmployeeId = 2 });
        ctx.Letters.Add(secret); ctx.SaveChanges();
        var seenByStranger = await cart.GetConversationAsync(parent.LetterId, Stranger, canViewConfidential: false);
        Assert.DoesNotContain(seenByStranger, m => m.LetterId == secret.LetterId);
        var seenByRecipient = await cart.GetConversationAsync(parent.LetterId, Referrer, canViewConfidential: false);
        Assert.Contains(seenByRecipient, m => m.LetterId == secret.LetterId);
    }

    // ------------------------------------------------------------------ approvers
    [Fact]
    public async Task ApproverOptions_AreTheCompanyStaff_ManagersFirstThenApproveHolders_AuthorExcluded()
    {
        var (compose, _, ctx, _) = LetterTestWorld.Services(nameof(ApproverOptions_AreTheCompanyStaff_ManagersFirstThenApproveHolders_AuthorExcluded));
        // org chart: author (employee 1) -> manager employee 4 -> that manager's manager employee 3
        ctx.Positions.AddRange(new Position { PositionId = 21, Title = "Director", IsActive = true }, new Position { PositionId = 22, Title = "Manager", ParentPositionId = 21, IsActive = true },
            new Position { PositionId = 23, Title = "Engineer", ParentPositionId = 22, IsActive = true });
        void Hold(int pos, int emp) => ctx.EmployeeAssignments.Add(new EmployeeAssignment { PositionId = pos, EmployeeId = emp, IsPrimary = true, IsActive = true, StartDate = DateTime.Today.AddYears(-1) });
        Hold(21, 3); Hold(22, 4); Hold(23, 1);
        ctx.Users.Single(u => u.Id == 107).IsActive = false;
        await ctx.SaveChangesAsync();

        var options = await compose.GetApproverOptionsAsync(Author);

        Assert.DoesNotContain(options, o => o.UserId == Author);
        Assert.DoesNotContain(options, o => o.UserId == 107);                                    // inactive
        Assert.Equal(new[] { 104, 103 }, options.Where(o => o.Group == "Manager").Select(o => o.UserId));   // nearest manager first
        Assert.Equal(new[] { 1, 2 }, options.Where(o => o.Group == "Manager").Select(o => o.ManagerLevel));
        Assert.Equal(new[] { 105, 106 }, options.Where(o => o.Group == "Eligible").Select(o => o.UserId).OrderBy(x => x));
        Assert.Equal(new[] { 102 }, options.Where(o => o.Group == "Other").Select(o => o.UserId));          // staff without the approve role are selectable too
        Assert.Equal(new[] { "Manager", "Manager", "Eligible", "Eligible", "Other" }, options.Select(o => o.Group));
        Assert.All(options, o => Assert.False(string.IsNullOrWhiteSpace(o.FullName)));

        Assert.Contains(await compose.GetApproverOptionsAsync(), o => o.UserId == Author);       // without an author nobody is excluded
    }
}
