using PEMS.Application.Common;
using PEMS.Application.DTOs;
using PEMS.Domain.Entities;
using PEMS.Tests.TestSupport;

namespace PEMS.Tests.Cmms;

/// <summary>پاسخ در فرم رسمی: مقدار اولیه از نامه‌ی اصلی، گیرنده‌ی واقعی، و اینکه محرمانگی پاسخ هرگز کمتر از نامه‌ی اصلی نمی‌شود.</summary>
public class LetterReplyComposeTests
{
    private static async Task<int> SendInternalAsync(string name, Func<LetterComposeInput, LetterComposeInput>? tweak = null, (
        PEMS.Persistence.Services.LetterComposeService Compose, PEMS.Persistence.Services.LetterService Letters, PEMS.Persistence.Context.PEMSDbContext Ctx, LetterTestNotifications Notes)? existing = null)
    {
        var s = existing ?? LetterTestWorld.Services(name);
        var input = LetterTestWorld.ValidInput();
        input.Subject = "Budget review";
        input = tweak?.Invoke(input) ?? input;
        var saved = await s.Compose.SaveDraftAsync(input, LetterTestWorld.AuthorActor());
        Assert.True(saved.Success, string.Join(",", saved.Errors.Select(e => e.Code)));
        var (ok, error) = await s.Letters.SendAsync(saved.LetterId, LetterTestWorld.Author);
        Assert.True(ok, error);
        return saved.LetterId;
    }

    private static LetterComposeActor Actor(int employeeId) => new(100 + employeeId, "u" + employeeId, employeeId, false, false);

    [Theory]
    [InlineData("Public", 0)]
    [InlineData("Internal", 1)]
    [InlineData("Confidential", 2)]
    [InlineData("Restricted", 3)]
    [InlineData("HighlyConfidential", 4)]
    [InlineData("SomethingElse", 1)]
    [InlineData(null, 1)]
    public void ConfidentialityRank_OrdersTheLevels(string? level, int rank)
        => Assert.Equal(rank, LetterComposeRules.ConfidentialityRank(level));

    [Fact]
    public async Task BuildReply_ForAnInternalLetter_TargetsTheOriginalSender_AndInheritsTheMetadata()
    {
        var s = LetterTestWorld.Services("reply-int");
        var parentId = await SendInternalAsync("x", i => { i.ConfidentialityLevel = "Confidential"; i.Priority = "High"; i.ReferenceNumber = "REF-1"; return i; }, s);

        var input = await s.Compose.BuildReplyInputAsync(parentId, Actor(2));

        Assert.NotNull(input);
        Assert.Equal("Internal", input!.LetterType);
        Assert.Equal(parentId, input.ParentLetterId);
        Assert.Equal(new List<int> { 1 }, input.ToEmployeeIds);
        Assert.Equal("Re: Budget review", input.Subject);
        Assert.Equal("Confidential", input.ConfidentialityLevel);
        Assert.Equal("High", input.Priority);
        Assert.Equal("Request", input.Category);
        Assert.Equal(s.Ctx.Letters.Single(l => l.LetterId == parentId).LetterNumber, input.ReferenceNumber);
        Assert.False(input.ApprovalRequired);
    }

    [Fact]
    public async Task BuildReply_ByTheOriginalSender_GoesToTheOriginalRecipients_NotToThemselves()
    {
        var s = LetterTestWorld.Services("reply-self");
        var parentId = await SendInternalAsync("x", i => { i.ToEmployeeIds = new List<int> { 2, 3 }; return i; }, s);

        var input = await s.Compose.BuildReplyInputAsync(parentId, Actor(1));

        Assert.Equal(new[] { 2, 3 }, input!.ToEmployeeIds.OrderBy(x => x));
    }

    [Fact]
    public async Task BuildReply_SkipsAnInactiveOriginalSender()
    {
        var s = LetterTestWorld.Services("reply-inactive");
        var parentId = await SendInternalAsync("x", null, s);
        s.Ctx.Employees.Single(e => e.EmployeeId == 1).IsActive = false;
        await s.Ctx.SaveChangesAsync();

        var input = await s.Compose.BuildReplyInputAsync(parentId, Actor(2));

        Assert.Empty(input!.ToEmployeeIds);   // the user must pick someone; a deactivated person is never pre-filled
    }

    [Theory]
    [InlineData("Incoming", "ACME Ltd", null, "ACME Ltd")]
    [InlineData("Outgoing", null, "Globex", "Globex")]
    public async Task BuildReply_ForExternalLetters_IsAnOutgoingLetterThatNeedsApproval(string type, string? sender, string? receiver, string expectedReceiver)
    {
        var s = LetterTestWorld.Services("reply-ext-" + type);
        s.Ctx.Letters.Add(new Letter
        {
            LetterId = 900, LetterType = type, LetterNumber = type + "-900", LetterDate = DateTime.Today, Subject = "Re: Re: " + new string('x', 120),
            Sender = sender, Receiver = receiver, Priority = "Urgent", ConfidentialityLevel = "Restricted", Status = "Registered", IsActive = true
        });
        await s.Ctx.SaveChangesAsync();

        var input = await s.Compose.BuildReplyInputAsync(900, Actor(2));

        Assert.Equal("Outgoing", input!.LetterType);
        Assert.Equal(expectedReceiver, input.ExternalReceiver);
        Assert.True(input.ApprovalRequired);
        Assert.Equal("Restricted", input.ConfidentialityLevel);
        Assert.StartsWith("Re: Re:", input.Subject);                                   // an existing "Re:" is not doubled
        Assert.True(input.Subject.Length <= LetterComposeRules.MaxSubjectLength);       // and long subjects are cut to the limit
    }

    [Fact]
    public async Task BuildReply_ForAMissingLetter_IsNull()
    {
        var s = LetterTestWorld.Services("reply-missing");
        Assert.Null(await s.Compose.BuildReplyInputAsync(12345, Actor(2)));
    }

    [Fact]
    public async Task SaveReply_LinksTheParent_AndMovesARegisteredParentToReferred()
    {
        var s = LetterTestWorld.Services("reply-link");
        s.Ctx.Letters.Add(new Letter
        {
            LetterId = 901, LetterType = "Incoming", LetterNumber = "IN-901", LetterDate = DateTime.Today, Subject = "Please advise", Sender = "ACME Ltd",
            Priority = "Normal", ConfidentialityLevel = "Internal", Status = "Registered", IsActive = true
        });
        await s.Ctx.SaveChangesAsync();
        var input = await s.Compose.BuildReplyInputAsync(901, Actor(1));
        input!.Body = "<p>Thanks</p>";
        input.Category = "Information";

        var saved = await s.Compose.SaveDraftAsync(input, LetterTestWorld.AuthorActor());

        Assert.True(saved.Success, string.Join(",", saved.Errors.Select(e => e.Code)));
        Assert.Equal(901, s.Ctx.Letters.Single(l => l.LetterId == saved.LetterId).ParentLetterId);
        Assert.Equal("Referred", s.Ctx.Letters.Single(l => l.LetterId == 901).Status);
        Assert.Equal(901, (await s.Compose.LoadForEditAsync(saved.LetterId, LetterTestWorld.AuthorActor()))!.ParentLetterId);   // survives a reload
    }

    [Fact]
    public async Task SaveReply_RefusesLowerConfidentiality_OnCreateAndOnLaterEdits_ButAllowsHigher()
    {
        var s = LetterTestWorld.Services("reply-level");
        var parentId = await SendInternalAsync("x", i => { i.ConfidentialityLevel = "Confidential"; return i; }, s);
        var replier = Actor(2);

        var input = (await s.Compose.BuildReplyInputAsync(parentId, replier))!;
        input.Body = "<p>ok</p>";

        input.ConfidentialityLevel = "Internal";
        var refused = await s.Compose.SaveDraftAsync(input, replier);
        Assert.False(refused.Success);
        Assert.Contains(refused.Errors, e => e.Code == "ConfidentialityBelowParent" && e.Detail == "Confidential");

        input.ConfidentialityLevel = "Restricted";
        var saved = await s.Compose.SaveDraftAsync(input, replier);
        Assert.True(saved.Success);

        // the same rule applies when the draft is edited later, even if the client drops ParentLetterId from the payload
        var edit = (await s.Compose.LoadForEditAsync(saved.LetterId, replier))!;
        edit.ParentLetterId = null;
        edit.ConfidentialityLevel = "Public";
        var lowered = await s.Compose.SaveDraftAsync(edit, replier);
        Assert.False(lowered.Success);
        Assert.Contains(lowered.Errors, e => e.Code == "ConfidentialityBelowParent");
        Assert.Equal("Restricted", s.Ctx.Letters.Single(l => l.LetterId == saved.LetterId).ConfidentialityLevel);
    }

    [Fact]
    public async Task SaveReply_ToAMissingParent_IsRefused()
    {
        var s = LetterTestWorld.Services("reply-noparent");
        var input = LetterTestWorld.ValidInput();
        input.ParentLetterId = 424242;

        var saved = await s.Compose.SaveDraftAsync(input, LetterTestWorld.AuthorActor());

        Assert.False(saved.Success);
        Assert.Contains(saved.Errors, e => e.Code == "ParentNotFound");
        Assert.Empty(s.Ctx.Letters);
    }
}
