using PEMS.Application.DTOs;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;
using PEMS.Web.Helpers;

namespace PEMS.Tests.Cmms;

/// <summary>
/// فیلترهای پیشرفته، ستون‌های مشتق‌شده، خروجی و عملیات گروهی امن فهرست مکاتبات. تمرکز بر قواعد:
/// محرمانگی در فیلتر/خروجی/ستاره‌ی گروهی رعایت می‌شود؛ ارسال گروهی فقط پیش‌نویس‌های خود کاربر؛ آرشیو گروهی فقط
/// Draft/Rejected، فقط نویسنده یا SystemAdmin، دلیل الزامی، IsActive=false (نه حذف) و ردیف AuditLog صریح.
/// </summary>
public class LetterListEnhancementsTests
{
    private const int Author = 11;
    private const int Other = 12;

    private static LetterService Create(string name, out PEMSDbContext ctx)
    {
        ctx = PemsDbContextFactory.CreateInMemory($"LetterList_{name}");
        return new LetterService(ctx, sanitizer: null!, knowledgeBaseService: null!);
    }

    private static int _id = 1000;

    private static Letter NewLetter(string status = "Registered", string priority = "Normal", string confidentiality = "Public",
        int? registeredBy = null, string type = "Outgoing", string subject = "S") => new()
    {
        LetterId = ++_id,
        LetterType = type,
        LetterNumber = $"LTR-{_id}",
        LetterDate = DateTime.Today,
        Subject = subject,
        Priority = priority,
        ConfidentialityLevel = confidentiality,
        Status = status,
        RegisteredById = registeredBy,
        IsActive = true
    };

    private static Task<PagedResult<LetterDto>> Search(LetterService s, LetterAdvancedFilter? advanced, bool canViewConfidential = true, int? userId = null)
        => s.SearchAsync(null, null, null, null, null, null, null, null, null, null, 1, 100, canViewConfidential, userId, false, advanced);

    // ------------------------------------------------------------------ advanced filters

    [Fact]
    public async Task PriorityFilter_ReturnsOnlyThatPriority()
    {
        var s = Create(nameof(PriorityFilter_ReturnsOnlyThatPriority), out var ctx);
        ctx.Letters.AddRange(NewLetter(priority: "Urgent"), NewLetter(priority: "Normal"), NewLetter(priority: "Urgent"));
        await ctx.SaveChangesAsync();

        var result = await Search(s, new LetterAdvancedFilter { Priority = "Urgent" });

        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, i => Assert.Equal("Urgent", i.Priority));
    }

    [Fact]
    public async Task AttachmentAndReferralFilters_WorkInBothDirections()
    {
        var s = Create(nameof(AttachmentAndReferralFilters_WorkInBothDirections), out var ctx);
        var withBoth = NewLetter(); var plain = NewLetter();
        ctx.Letters.AddRange(withBoth, plain);
        ctx.DocumentAttachments.Add(new DocumentAttachment { LetterId = withBoth.LetterId, FileName = "a.pdf", FileExtension = ".pdf", FilePath = "x" });
        ctx.LetterReferrals.Add(new LetterReferral { LetterId = withBoth.LetterId, Status = "Pending" });
        await ctx.SaveChangesAsync();

        Assert.Equal(withBoth.LetterId, Assert.Single((await Search(s, new LetterAdvancedFilter { HasAttachments = true })).Items).LetterId);
        Assert.Equal(plain.LetterId, Assert.Single((await Search(s, new LetterAdvancedFilter { HasAttachments = false })).Items).LetterId);
        Assert.Equal(withBoth.LetterId, Assert.Single((await Search(s, new LetterAdvancedFilter { HasReferrals = true })).Items).LetterId);
        Assert.Equal(plain.LetterId, Assert.Single((await Search(s, new LetterAdvancedFilter { HasReferrals = false })).Items).LetterId);
    }

    [Fact]
    public async Task OverdueReferralFilter_IsComputed_OpenAndPastDueOnly()
    {
        var s = Create(nameof(OverdueReferralFilter_IsComputed_OpenAndPastDueOnly), out var ctx);
        var overdue = NewLetter(); var future = NewLetter(); var doneLate = NewLetter();
        ctx.Letters.AddRange(overdue, future, doneLate);
        ctx.LetterReferrals.AddRange(
            new LetterReferral { LetterId = overdue.LetterId, Status = "Pending", DueDate = DateTime.Now.AddDays(-2) },
            new LetterReferral { LetterId = future.LetterId, Status = "Pending", DueDate = DateTime.Now.AddDays(3) },
            new LetterReferral { LetterId = doneLate.LetterId, Status = "Completed", DueDate = DateTime.Now.AddDays(-5), CompletedDate = DateTime.Now.AddDays(-4) });
        await ctx.SaveChangesAsync();

        var result = await Search(s, new LetterAdvancedFilter { OverdueReferralOnly = true });

        var only = Assert.Single(result.Items);
        Assert.Equal(overdue.LetterId, only.LetterId);
        Assert.True(only.HasOverdueReferral);
    }

    [Fact]
    public async Task AdvancedFilters_NeverWidenWhatAConfidentialityRestrictedUserSees()
    {
        var s = Create(nameof(AdvancedFilters_NeverWidenWhatAConfidentialityRestrictedUserSees), out var ctx);
        var secret = NewLetter(priority: "Urgent", confidentiality: "Confidential", registeredBy: Author);
        var mine = NewLetter(priority: "Urgent", confidentiality: "Confidential", registeredBy: Other);
        var open = NewLetter(priority: "Urgent", confidentiality: "Public", registeredBy: Author);
        ctx.Letters.AddRange(secret, mine, open);
        await ctx.SaveChangesAsync();

        var ids = (await Search(s, new LetterAdvancedFilter { Priority = "Urgent" }, canViewConfidential: false, userId: Other))
            .Items.Select(i => i.LetterId).ToHashSet();

        Assert.Contains(mine.LetterId, ids);
        Assert.Contains(open.LetterId, ids);
        Assert.DoesNotContain(secret.LetterId, ids);
    }

    // ------------------------------------------------------------------ derived columns

    [Fact]
    public async Task NextResponseDue_IsTheEarliestOpenReferralDueDate_AndNullWithoutOne()
    {
        var s = Create(nameof(NextResponseDue_IsTheEarliestOpenReferralDueDate_AndNullWithoutOne), out var ctx);
        var l = NewLetter(); var none = NewLetter();
        ctx.Letters.AddRange(l, none);
        var soon = DateTime.Today.AddDays(2); var later = DateTime.Today.AddDays(9);
        ctx.LetterReferrals.AddRange(
            new LetterReferral { LetterId = l.LetterId, Status = "Pending", DueDate = later },
            new LetterReferral { LetterId = l.LetterId, Status = "Pending", DueDate = soon },
            new LetterReferral { LetterId = l.LetterId, Status = "Completed", DueDate = DateTime.Today.AddDays(-30), CompletedDate = DateTime.Today.AddDays(-29) });
        await ctx.SaveChangesAsync();

        var items = (await Search(s, null)).Items.ToDictionary(i => i.LetterId);

        Assert.Equal(soon, items[l.LetterId].NextResponseDue);
        Assert.Null(items[none.LetterId].NextResponseDue);
    }

    [Fact]
    public async Task LastActivity_IsTheLatestEventAcrossLetterReferralsAndReviews()
    {
        var s = Create(nameof(LastActivity_IsTheLatestEventAcrossLetterReferralsAndReviews), out var ctx);
        var l = NewLetter();
        l.ApprovedDate = DateTime.Today.AddDays(-10);
        var latest = DateTime.Now.AddHours(-3);
        var referral = new LetterReferral { LetterId = l.LetterId, Status = "Completed", ReferredDate = DateTime.Today.AddDays(-5), CompletedDate = latest };
        var review = new LetterReview { LetterId = l.LetterId, Decision = LetterReviewDecisions.Approved, IsActive = true };
        ctx.Letters.Add(l);
        ctx.LetterReferrals.Add(referral);
        ctx.LetterReviews.Add(review);
        await ctx.SaveChangesAsync();

        // the context stamps CreatedDate on insert, so backdate with a second save
        l.CreatedDate = DateTime.Today.AddDays(-20);
        review.CreatedDate = DateTime.Today.AddDays(-8);
        await ctx.SaveChangesAsync();

        var item = Assert.Single((await Search(s, null)).Items);

        Assert.Equal(latest, item.LastActivityDate);
    }

    // ------------------------------------------------------------------ export

    [Fact]
    public async Task Export_UsesTheSameFiltersAndTheSameConfidentialityRule()
    {
        var s = Create(nameof(Export_UsesTheSameFiltersAndTheSameConfidentialityRule), out var ctx);
        var visible = NewLetter(priority: "High", confidentiality: "Public");
        var hidden = NewLetter(priority: "High", confidentiality: "Confidential", registeredBy: Author);
        var otherPriority = NewLetter(priority: "Low", confidentiality: "Public");
        ctx.Letters.AddRange(visible, hidden, otherPriority);
        await ctx.SaveChangesAsync();

        var rows = await s.ExportAsync(null, null, null, null, null, null, null, null, null, null,
            canViewConfidential: false, requestingApplicationUserId: Other, starredOnly: false,
            advanced: new LetterAdvancedFilter { Priority = "High" });

        Assert.Equal(visible.LetterId, Assert.Single(rows).LetterId);
    }

    [Fact]
    public async Task Export_IsCappedAtMaxRows()
    {
        var s = Create(nameof(Export_IsCappedAtMaxRows), out var ctx);
        for (var i = 0; i < 12; i++) ctx.Letters.Add(NewLetter());
        await ctx.SaveChangesAsync();

        var rows = await s.ExportAsync(null, null, null, null, null, null, null, null, null, null, true, null, false, null, maxRows: 5);

        Assert.Equal(5, rows.Count);
    }

    [Theory]
    [InlineData("=HYPERLINK(\"http://x\")", "'=HYPERLINK(\"http://x\")")]
    [InlineData("+1+1", "'+1+1")]
    [InlineData("-2", "'-2")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("Normal subject", "Normal subject")]
    [InlineData("", "")]
    public void Safe_NeutralisesSpreadsheetFormulas(string input, string expected)
        => Assert.Equal(expected, LetterListExport.Safe(input));

    private static LetterDto Dto(string subject, string number = "N-1") => new()
    {
        LetterId = 1, LetterNumber = number, LetterType = "Outgoing", LetterDate = new DateTime(2026, 3, 4), Subject = subject,
        Priority = "High", ConfidentialityLevel = "Public", Status = "Draft", LastActivityDate = new DateTime(2026, 3, 5),
        NextResponseDue = new DateTime(2026, 4, 1), HasOverdueReferral = true, OpenReferralsCount = 2, AttachmentsCount = 3
    };

    [Fact]
    public void Csv_HasBomHeaderQuotingAndNeutralisedFormulas()
    {
        var bytes = LetterListExport.BuildCsv(new[] { Dto("Hello, \"world\"\nline2"), Dto("=cmd|' /C calc'!A0", "N-2") });
        var text = System.Text.Encoding.UTF8.GetString(bytes);

        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());
        Assert.Contains("Letter No,Type,Date,Subject", text);
        Assert.Contains("\"Hello, \"\"world\"\"\nline2\"", text);
        Assert.Contains("'=cmd|' /C calc'!A0", text);
        Assert.Contains("2026-04-01", text);
        Assert.Contains(",Yes,", text);                      // overdue flag as text, not colour
    }

    [Fact]
    public void Excel_StoresEveryCellAsText_SoNothingIsEvaluated()
    {
        var bytes = LetterListExport.BuildExcel(new[] { Dto("=1+1") });
        using var wb = new ClosedXML.Excel.XLWorkbook(new MemoryStream(bytes));
        var sheet = wb.Worksheet(1);

        Assert.Equal("Letter No", sheet.Cell(1, 1).GetString());
        // ClosedXML turns the leading apostrophe into Excel's "quote prefix" (the cell is text, never evaluated)
        Assert.Equal("=1+1", sheet.Cell(2, 4).GetString());
        Assert.True(sheet.Cell(2, 4).Style.IncludeQuotePrefix);
        Assert.False(sheet.Cell(2, 4).HasFormula);
        Assert.Equal(ClosedXML.Excel.XLDataType.Text, sheet.Cell(2, 4).DataType);
    }

    // ------------------------------------------------------------------ bulk star

    [Fact]
    public async Task BulkStar_StarsVisibleLetters_RefusesHiddenOnes_AndUnstars()
    {
        var s = Create(nameof(BulkStar_StarsVisibleLetters_RefusesHiddenOnes_AndUnstars), out var ctx);
        var a = NewLetter(); var b = NewLetter();
        var secret = NewLetter(confidentiality: "Confidential", registeredBy: Author);
        ctx.Letters.AddRange(a, b, secret);
        await ctx.SaveChangesAsync();

        var result = await s.BulkSetStarAsync(new[] { a.LetterId, b.LetterId, secret.LetterId }, Other, true, canViewConfidential: false);

        Assert.Equal(2, result.Succeeded);
        Assert.Equal(secret.LetterId, Assert.Single(result.Failed).LetterId);
        Assert.Equal(2, ctx.LetterStars.Count(x => x.ApplicationUserId == Other));

        // idempotent + unstar
        await s.BulkSetStarAsync(new[] { a.LetterId }, Other, true, false);
        Assert.Equal(2, ctx.LetterStars.Count(x => x.ApplicationUserId == Other));
        await s.BulkSetStarAsync(new[] { a.LetterId, b.LetterId }, Other, false, false);
        Assert.Equal(0, ctx.LetterStars.Count(x => x.ApplicationUserId == Other));
    }

    [Fact]
    public async Task BulkStar_NeverTouchesAnotherUsersStars()
    {
        var s = Create(nameof(BulkStar_NeverTouchesAnotherUsersStars), out var ctx);
        var a = NewLetter();
        ctx.Letters.Add(a);
        ctx.LetterStars.Add(new LetterStar { LetterId = a.LetterId, ApplicationUserId = Author });
        await ctx.SaveChangesAsync();

        await s.BulkSetStarAsync(new[] { a.LetterId }, Other, false, true);

        Assert.Equal(1, ctx.LetterStars.Count(x => x.ApplicationUserId == Author));
    }

    // ------------------------------------------------------------------ bulk submit

    [Fact]
    public async Task BulkSubmit_SubmitsOnlyOwnDrafts_AndReportsWhyOthersWereSkipped()
    {
        var s = Create(nameof(BulkSubmit_SubmitsOnlyOwnDrafts_AndReportsWhyOthersWereSkipped), out var ctx);
        var mine = NewLetter("Draft", registeredBy: Author);
        var notMine = NewLetter("Draft", registeredBy: Other);
        var notDraft = NewLetter("Approved", registeredBy: Author);
        ctx.Letters.AddRange(mine, notMine, notDraft);
        await ctx.SaveChangesAsync();

        var result = await s.BulkSubmitForReviewAsync(new[] { mine.LetterId, notMine.LetterId, notDraft.LetterId }, Author);

        Assert.Equal(1, result.Succeeded);
        Assert.Equal(2, result.Failed.Count);
        Assert.Equal("PendingApproval", ctx.Letters.Single(x => x.LetterId == mine.LetterId).Status);
        Assert.Equal("Draft", ctx.Letters.Single(x => x.LetterId == notMine.LetterId).Status);
        Assert.Equal("Approved", ctx.Letters.Single(x => x.LetterId == notDraft.LetterId).Status);
        Assert.All(result.Failed, f => Assert.False(string.IsNullOrWhiteSpace(f.Reason)));
    }

    // ------------------------------------------------------------------ bulk archive

    [Fact]
    public async Task BulkArchive_ArchivesOwnDraftAndRejected_HidesThemAndWritesAnAuditRowWithTheReason()
    {
        var s = Create(nameof(BulkArchive_ArchivesOwnDraftAndRejected_HidesThemAndWritesAnAuditRowWithTheReason), out var ctx);
        var draft = NewLetter("Draft", registeredBy: Author);
        var rejected = NewLetter("Rejected", registeredBy: Author);
        ctx.Letters.AddRange(draft, rejected);
        await ctx.SaveChangesAsync();

        var result = await s.BulkArchiveAsync(new[] { draft.LetterId, rejected.LetterId }, Author, "author", false, "  duplicate entry  ");

        Assert.Equal(2, result.Succeeded);
        Assert.Empty(result.Failed);
        Assert.All(ctx.Letters.Where(x => x.LetterId == draft.LetterId || x.LetterId == rejected.LetterId), l =>
        {
            Assert.False(l.IsActive);                       // soft archive, row still exists
        });
        Assert.Empty((await Search(s, null)).Items);

        var audit = ctx.AuditLogs.Where(a => a.Action == "Archive" && a.EntityName == nameof(Letter)).ToList();
        Assert.Equal(2, audit.Count);
        Assert.All(audit, a =>
        {
            Assert.Equal(Author, a.UserId);
            Assert.Equal("author", a.UserName);
            Assert.Contains("Reason: duplicate entry", a.ChangedFieldsSummary);
        });
    }

    [Theory]
    [InlineData("Registered")]
    [InlineData("PendingApproval")]
    [InlineData("Approved")]
    [InlineData("Issued")]
    [InlineData("Closed")]
    [InlineData("ReturnedForRevision")]
    public async Task BulkArchive_RefusesEveryStatusExceptDraftAndRejected(string status)
    {
        var s = Create($"{nameof(BulkArchive_RefusesEveryStatusExceptDraftAndRejected)}_{status}", out var ctx);
        var l = NewLetter(status, registeredBy: Author);
        ctx.Letters.Add(l);
        await ctx.SaveChangesAsync();

        var result = await s.BulkArchiveAsync(new[] { l.LetterId }, Author, "a", true, "cleanup");

        Assert.Equal(0, result.Succeeded);
        Assert.Single(result.Failed);
        Assert.True(ctx.Letters.Single(x => x.LetterId == l.LetterId).IsActive);
        Assert.Empty(ctx.AuditLogs.Where(a => a.Action == "Archive"));
    }

    [Fact]
    public async Task BulkArchive_OnlyTheAuthorOrASystemAdminMayArchive()
    {
        var s = Create(nameof(BulkArchive_OnlyTheAuthorOrASystemAdminMayArchive), out var ctx);
        var l1 = NewLetter("Draft", registeredBy: Author);
        var l2 = NewLetter("Draft", registeredBy: Author);
        ctx.Letters.AddRange(l1, l2);
        await ctx.SaveChangesAsync();

        var stranger = await s.BulkArchiveAsync(new[] { l1.LetterId }, Other, "other", false, "r");
        Assert.Equal(0, stranger.Succeeded);
        Assert.True(ctx.Letters.Single(x => x.LetterId == l1.LetterId).IsActive);

        var admin = await s.BulkArchiveAsync(new[] { l2.LetterId }, Other, "admin", true, "r");
        Assert.Equal(1, admin.Succeeded);
        Assert.False(ctx.Letters.Single(x => x.LetterId == l2.LetterId).IsActive);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task BulkArchive_ReasonIsRequired_AndNothingChangesWithoutIt(string? reason)
    {
        var s = Create($"{nameof(BulkArchive_ReasonIsRequired_AndNothingChangesWithoutIt)}_{reason?.Length}", out var ctx);
        var l = NewLetter("Draft", registeredBy: Author);
        ctx.Letters.Add(l);
        await ctx.SaveChangesAsync();

        var result = await s.BulkArchiveAsync(new[] { l.LetterId }, Author, "a", false, reason!);

        Assert.Equal(0, result.Succeeded);
        Assert.Single(result.Failed);
        Assert.True(ctx.Letters.Single(x => x.LetterId == l.LetterId).IsActive);
    }

    [Fact]
    public async Task BulkArchive_IgnoresDuplicateIds_AndAlreadyArchivedLetters_AndCapsTheBatch()
    {
        var s = Create(nameof(BulkArchive_IgnoresDuplicateIds_AndAlreadyArchivedLetters_AndCapsTheBatch), out var ctx);
        var l = NewLetter("Draft", registeredBy: Author);
        ctx.Letters.Add(l);
        await ctx.SaveChangesAsync();

        var first = await s.BulkArchiveAsync(new[] { l.LetterId, l.LetterId, -5, 0 }, Author, "a", false, "r");
        Assert.Equal(1, first.Succeeded);
        Assert.Empty(first.Failed);

        var again = await s.BulkArchiveAsync(new[] { l.LetterId }, Author, "a", false, "r");
        Assert.Equal(0, again.Succeeded);
        Assert.Single(again.Failed);

        // more ids than the cap are simply not processed (no unbounded loop)
        var huge = await s.BulkArchiveAsync(Enumerable.Range(5000, 500).ToArray(), Author, "a", false, "r");
        Assert.True(huge.Failed.Count <= 200);
    }
}
