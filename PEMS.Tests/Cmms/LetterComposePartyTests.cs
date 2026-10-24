using Microsoft.EntityFrameworkCore;
using PEMS.Application.DTOs;
using PEMS.Domain.Entities;
using PEMS.Tests.TestSupport;

namespace PEMS.Tests.Cmms;

/// <summary>انتخاب اختیاری «سازمان گیرنده» در فرم نامه‌ی صادره، تا نامه در رشته‌ی مکاتبه‌ی همان طرف در DCC بیاید.</summary>
public class LetterComposePartyTests
{
    private static async Task<int> AddPartyAsync(PEMS.Persistence.Context.PEMSDbContext ctx, string name = "Acme Co", bool active = true)
    {
        var party = new CorrespondentParty { Name = name, PartyType = "Client", IsActive = active };
        ctx.CorrespondentParties.Add(party);
        await ctx.SaveChangesAsync();
        return party.CorrespondentPartyId;
    }

    [Fact]
    public async Task OutgoingDraft_StoresTheParty_AndLoadsItBack()
    {
        var s = LetterTestWorld.Services("party1");
        var partyId = await AddPartyAsync(s.Ctx);
        var input = LetterTestWorld.ValidInput("Outgoing");
        input.CorrespondentPartyId = partyId;

        var saved = await s.Compose.SaveDraftAsync(input, LetterTestWorld.AuthorActor());

        Assert.True(saved.Success, string.Join(",", saved.Errors.Select(e => e.Code)));
        Assert.Equal(partyId, (await s.Ctx.Letters.SingleAsync()).CorrespondentPartyId);
        var loaded = await s.Compose.LoadForEditAsync(saved.LetterId, LetterTestWorld.AuthorActor());
        Assert.Equal(partyId, loaded!.CorrespondentPartyId);
    }

    [Fact]
    public async Task OutgoingDraft_WithAnUnknownOrInactiveParty_IsRejected_AndNothingIsSaved()
    {
        var s = LetterTestWorld.Services("party2");
        var inactive = await AddPartyAsync(s.Ctx, "Old Co", active: false);

        foreach (var id in new[] { 4242, inactive })
        {
            var input = LetterTestWorld.ValidInput("Outgoing");
            input.CorrespondentPartyId = id;
            var saved = await s.Compose.SaveDraftAsync(input, LetterTestWorld.AuthorActor());
            Assert.Contains(saved.Errors, e => e.Code == "PartyNotFound" && e.Field == "correspondentPartyId");
        }

        Assert.Empty(s.Ctx.Letters);
    }

    [Fact]
    public async Task ClearingTheParty_OnALaterSave_RemovesTheLink()
    {
        var s = LetterTestWorld.Services("party3");
        var partyId = await AddPartyAsync(s.Ctx);
        var input = LetterTestWorld.ValidInput("Outgoing");
        input.CorrespondentPartyId = partyId;
        var saved = await s.Compose.SaveDraftAsync(input, LetterTestWorld.AuthorActor());

        input.LetterId = saved.LetterId; input.RowVersion = saved.RowVersion; input.CorrespondentPartyId = null;
        Assert.True((await s.Compose.SaveDraftAsync(input, LetterTestWorld.AuthorActor())).Success);

        Assert.Null((await s.Ctx.Letters.SingleAsync()).CorrespondentPartyId);
    }

    [Fact]
    public async Task InternalLetters_IgnoreTheParty()
    {
        var s = LetterTestWorld.Services("party4");
        var partyId = await AddPartyAsync(s.Ctx);
        var input = LetterTestWorld.ValidInput();
        input.CorrespondentPartyId = partyId;

        Assert.True((await s.Compose.SaveDraftAsync(input, LetterTestWorld.AuthorActor())).Success);
        Assert.Null((await s.Ctx.Letters.SingleAsync()).CorrespondentPartyId);
    }

    [Fact]
    public async Task ReplyingToARegisteredIncomingLetter_PrefillsTheSenderAsTheReceiverAndTheParty()
    {
        var s = LetterTestWorld.Services("party5");
        var partyId = await AddPartyAsync(s.Ctx, "Acme Co");
        s.Ctx.Letters.Add(new Letter
        {
            LetterType = "Incoming", LetterNumber = "JPC-IN-1405-0001", Subject = "Please advise", Status = "Registered", IsActive = true,
            Sender = "Acme Co", CorrespondentPartyId = partyId, ConfidentialityLevel = "Internal", Priority = "Normal", RegisteredById = LetterTestWorld.Author
        });
        await s.Ctx.SaveChangesAsync();
        var parentId = s.Ctx.Letters.Single().LetterId;

        var reply = await s.Compose.BuildReplyInputAsync(parentId, LetterTestWorld.AuthorActor());

        Assert.Equal("Outgoing", reply!.LetterType);
        Assert.Equal("Acme Co", reply.ExternalReceiver);
        Assert.Equal(partyId, reply.CorrespondentPartyId);
    }
}
