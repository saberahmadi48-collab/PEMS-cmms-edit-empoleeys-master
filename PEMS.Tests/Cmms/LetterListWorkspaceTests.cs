using PEMS.Application.DTOs;
using PEMS.Tests.TestSupport;

namespace PEMS.Tests.Cmms;

/// <summary>
/// داده‌ی فهرست مکاتبات برای طراحی جدید: نشان «نخوانده» و فیلتر «فقط نخوانده‌ها»، جستجوی یکجا، خلاصه‌ی متن کنار موضوع،
/// و شمارنده‌ی تب‌ها با همان قانون محرمانگی فهرست.
/// </summary>
public class LetterListWorkspaceTests
{
    private const int Reader = 102;   // employee 2

    private static async Task<(PEMS.Persistence.Services.LetterService Letters, PEMS.Persistence.Services.LetterComposeService Compose, PEMS.Persistence.Context.PEMSDbContext Ctx)> WorldAsync(string name)
    {
        var s = LetterTestWorld.Services(name);
        return (s.Letters, s.Compose, s.Ctx);
    }

    // author (user 101) sends a letter to employee 2
    private static async Task<int> SendAsync(PEMS.Persistence.Services.LetterComposeService compose, PEMS.Persistence.Services.LetterService letters,
        string subject, string body = "<p>Hello</p>", string confidentiality = "Internal")
    {
        var input = LetterTestWorld.ValidInput();
        input.Subject = subject;
        input.Body = body;
        input.ConfidentialityLevel = confidentiality;
        var saved = await compose.SaveDraftAsync(input, LetterTestWorld.AuthorActor());
        Assert.True(saved.Success, string.Join(",", saved.Errors.Select(e => e.Code)));
        var (ok, error) = await letters.SendAsync(saved.LetterId, LetterTestWorld.Author);
        Assert.True(ok, error);
        return saved.LetterId;
    }

    private static Task<PagedResult<LetterDto>> ListAsync(PEMS.Persistence.Services.LetterService letters, int userId, LetterAdvancedFilter? advanced = null, bool canSeeAll = false)
        => letters.SearchAsync(null, null, null, null, null, null, null, null, null, null, 1, 50, canSeeAll, userId, false, advanced);

    // ------------------------------------------------------------------ unread

    [Fact]
    public async Task SentLetter_IsUnreadForItsRecipient_UntilTheyOpenIt()
    {
        var (letters, compose, _) = await WorldAsync("unread");
        var id = await SendAsync(compose, letters, "Budget review");

        var before = (await ListAsync(letters, Reader)).Items.Single(l => l.LetterId == id);
        Assert.True(before.IsUnread);

        var unreadOnly = await ListAsync(letters, Reader, new LetterAdvancedFilter { UnreadOnly = true });
        Assert.Equal(new[] { id }, unreadOnly.Items.Select(l => l.LetterId));

        var viewer = new LetterViewer(Reader, 2, false);
        await letters.GetDetailForViewerAsync(id, viewer);   // opening the letter marks the recipient row as read

        var after = (await ListAsync(letters, Reader)).Items.Single(l => l.LetterId == id);
        Assert.False(after.IsUnread);
        Assert.Empty((await ListAsync(letters, Reader, new LetterAdvancedFilter { UnreadOnly = true })).Items);
    }

    [Fact]
    public async Task TheAuthor_NeverSeesTheirOwnLetterAsUnread_AndUnreadOnlyIsEmptyWithoutAnEmployee()
    {
        var (letters, compose, ctx) = await WorldAsync("unread-author");
        var id = await SendAsync(compose, letters, "Budget review");

        var asAuthor = (await ListAsync(letters, LetterTestWorld.Author)).Items.Single(l => l.LetterId == id);
        Assert.False(asAuthor.IsUnread);

        // a user without an employee record has no recipient rows, so "unread only" is empty rather than "everything"
        ctx.Users.Single(u => u.Id == LetterTestWorld.Other).EmployeeId = null;
        await ctx.SaveChangesAsync();
        Assert.Empty((await ListAsync(letters, LetterTestWorld.Other, new LetterAdvancedFilter { UnreadOnly = true })).Items);
    }

    [Fact]
    public async Task Drafts_AreNeverUnread()
    {
        var (letters, compose, _) = await WorldAsync("unread-draft");
        var saved = await compose.SaveDraftAsync(LetterTestWorld.ValidInput(), LetterTestWorld.AuthorActor());
        Assert.True(saved.Success);

        var asRecipient = await ListAsync(letters, Reader, new LetterAdvancedFilter { UnreadOnly = true });
        Assert.Empty(asRecipient.Items);   // a draft is invisible to the recipient and never counts as unread
    }

    // ------------------------------------------------------------------ one search box

    [Fact]
    public async Task Query_MatchesNumberSubjectSenderAndReceiver()
    {
        var (letters, compose, ctx) = await WorldAsync("query");
        var a = await SendAsync(compose, letters, "Pump inspection");
        var b = await SendAsync(compose, letters, "Cable schedule");
        var number = ctx.Letters.Single(l => l.LetterId == a).LetterNumber;

        async Task<int[]> Find(string q) => (await ListAsync(letters, LetterTestWorld.Author, new LetterAdvancedFilter { Query = q })).Items.Select(l => l.LetterId).OrderBy(x => x).ToArray();

        Assert.Equal(new[] { a }, await Find("Pump"));                       // subject
        Assert.Equal(new[] { a }, await Find(number));                       // letter number
        Assert.Equal(new[] { a, b }.OrderBy(x => x), await Find("E1 L1"));   // sender name (the author)
        Assert.Equal(new[] { a, b }.OrderBy(x => x), await Find("E2 L2"));   // receiver text
        Assert.Empty(await Find("nothing matches this"));
        Assert.Equal(new[] { a }, await Find("  Pump  "));                   // surrounding spaces are ignored
    }

    [Fact]
    public void AdvancedFilter_IsEmptyOnlyWhenNothingIsSet()
    {
        Assert.True(new LetterAdvancedFilter().IsEmpty);
        Assert.False(new LetterAdvancedFilter { UnreadOnly = true }.IsEmpty);
        Assert.False(new LetterAdvancedFilter { Query = "x" }.IsEmpty);
        Assert.True(new LetterAdvancedFilter { Query = "   " }.IsEmpty);
    }

    // ------------------------------------------------------------------ snippet

    [Fact]
    public async Task Snippet_IsPlainText_ShortenedAtOneHundredTwentyCharacters_AndMissingFromExports()
    {
        var (letters, compose, _) = await WorldAsync("snippet");
        var longBody = "<p><strong>Dear</strong> team,</p><p>" + new string('x', 300) + "</p>";
        var id = await SendAsync(compose, letters, "Long one", longBody);

        var row = (await ListAsync(letters, LetterTestWorld.Author)).Items.Single(l => l.LetterId == id);

        Assert.NotNull(row.Snippet);
        Assert.StartsWith("Dear team,", row.Snippet);
        Assert.DoesNotContain("<", row.Snippet);
        Assert.True(row.Snippet!.Length <= 121, "120 characters plus the ellipsis");
        Assert.EndsWith("…", row.Snippet);

        var exported = await letters.ExportAsync(null, null, null, null, null, null, null, null, null, null, false, LetterTestWorld.Author, false, null);
        Assert.All(exported, e => Assert.Null(e.Snippet));   // exports skip the extra text work
    }

    [Fact]
    public async Task Snippet_OfAnEmptyBody_IsNull()
    {
        var (letters, compose, _) = await WorldAsync("snippet-empty");
        var id = await SendAsync(compose, letters, "Short", "<p>Short body</p>");
        var row = (await ListAsync(letters, LetterTestWorld.Author)).Items.Single(l => l.LetterId == id);
        Assert.Equal("Short body", row.Snippet);
    }

    // ------------------------------------------------------------------ folder counts

    [Fact]
    public async Task FolderCounts_CountTheAuthorsDraftsPendingApprovedAndStarred()
    {
        var (letters, compose, ctx) = await WorldAsync("counts");

        var draft = LetterTestWorld.ValidInput();
        Assert.True((await compose.SaveDraftAsync(draft, LetterTestWorld.AuthorActor())).Success);

        var pending = LetterTestWorld.ValidInput();
        pending.ApprovalRequired = true;
        pending.ApproverUserIds = new List<int> { LetterTestWorld.Approver1 };
        var pendingId = (await compose.SaveDraftAsync(pending, LetterTestWorld.AuthorActor())).LetterId;
        Assert.True((await letters.SubmitForReviewAsync(pendingId, LetterTestWorld.Author)).Success);

        var sent = await SendAsync(compose, letters, "Sent letter");
        await letters.ToggleStarAsync(sent, LetterTestWorld.Author);

        var counts = await letters.GetFolderCountsAsync(false, LetterTestWorld.Author);

        Assert.Equal(1, counts.Draft);
        Assert.Equal(1, counts.PendingApproval);
        Assert.Equal(0, counts.ReturnedForRevision);
        Assert.Equal(0, counts.Approved);
        Assert.Equal(1, counts.Starred);
    }

    [Fact]
    public async Task FolderCounts_FollowTheSameVisibilityRulesAsTheList()
    {
        var (letters, compose, _) = await WorldAsync("counts-vis");
        Assert.True((await compose.SaveDraftAsync(LetterTestWorld.ValidInput(), LetterTestWorld.AuthorActor())).Success);

        // someone else's draft is invisible to a normal user, visible to a user who can see everything
        Assert.Equal(0, (await letters.GetFolderCountsAsync(false, Reader)).Draft);
        Assert.Equal(1, (await letters.GetFolderCountsAsync(true, Reader)).Draft);
        Assert.Equal(1, (await letters.GetFolderCountsAsync(false, LetterTestWorld.Author)).Draft);

        // and the number matches what the list actually returns for that folder
        var list = await letters.SearchAsync(null, null, null, null, null, null, null, "Draft", null, null, 1, 50, false, Reader, false, null);
        Assert.Equal(0, list.TotalCount);
    }
}
