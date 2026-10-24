using System.Dynamic;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PEMS.Application.DTOs;
using PEMS.Tests.TestSupport;
using PEMS.Web.Controllers;
using PEMS.Web.Security;

namespace PEMS.Tests.Web;

/// <summary>
/// فهرست مکاتبات (طراحی Workspace): نوار ابزار واحد با تب‌ها و شمارنده، پنجره‌ی فیلتر، ردیف غنی (نخوانده/خلاصه/اقدام هنگام Hover)،
/// نوار گروهی روی هدر، منوی بیشتر، و سیم‌کشی نخوانده/جستجو در کنترلر. View واقعی رندر می‌شود (Localizer متن انگلیسی resx را می‌دهد).
/// </summary>
public class LetterWorkspaceIndexTests
{
    private static object Obj(params (string Name, object? Value)[] props)
    {
        IDictionary<string, object?> bag = new ExpandoObject();
        foreach (var (name, value) in props) bag[name] = value;
        return bag;
    }

    private static object NoFilters(string? status = null, string? type = null, bool starred = false) => Obj(
        ("letterType", type), ("letterNumber", null), ("subject", null), ("sender", null), ("receiver", null), ("projectId", null), ("disciplineId", null),
        ("status", status), ("fromDate", null), ("toDate", null), ("starred", starred), ("priority", ""), ("hasAttachments", null), ("hasReferrals", null),
        ("overdueReferral", false), ("overdueResponse", false));

    private static LetterDto Row(int id, string status = "Sent", bool unread = false, string? snippet = null, DateTime? due = null) => new()
    {
        LetterId = id, LetterNumber = $"LTR-{id}", LetterType = "Internal", LetterDate = new DateTime(2026, 3, 1), Subject = $"Subject {id}", Priority = "Normal",
        ConfidentialityLevel = "Internal", Status = status, CreatedDate = new DateTime(2026, 3, 1), LastActivityDate = new DateTime(2026, 3, 2), IsUnread = unread,
        Snippet = snippet, NextResponseDue = due, AttachmentsCount = id % 2, Receiver = "Sara", Sender = "Ali", MyRecipientType = unread ? "To" : null
    };

    private static async Task<string> Render(object filters, IEnumerable<LetterDto> rows, bool canArchive = false, LetterFolderCountsDto? counts = null,
        int myReferrals = 0, bool unread = false, string? query = null, int total = -1, CartableCountsDto? cartable = null)
    {
        var c = new LetterController(null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        c.ViewBag.Dashboard = new LetterDashboardDto { MyOpenReferrals = myReferrals };
        c.ViewBag.FolderCounts = counts ?? new LetterFolderCountsDto();
        c.ViewBag.Cartable = cartable ?? new CartableCountsDto();
        c.ViewBag.Projects = new List<object> { Obj(("ProjectId", 7), ("ProjectName", "Alpha")) };
        c.ViewBag.Disciplines = new List<object> { Obj(("DisciplineId", 1), ("Name", "Mech")) };
        c.ViewBag.CanBulkArchive = canArchive;
        c.ViewBag.Filters = filters;
        c.ViewBag.Unread = unread;
        c.ViewBag.SearchQuery = query;

        var list = rows.ToList();
        var result = new ViewResult
        {
            ViewData = new Microsoft.AspNetCore.Mvc.ViewFeatures.ViewDataDictionary(c.ViewData)
            {
                Model = new PagedResult<LetterDto> { Items = list, Page = 1, PageSize = 25, TotalCount = total >= 0 ? total : list.Count }
            }
        };
        return await RazorViewRenderer.RenderAsync(c, result, "~/Views/Letter/Index.cshtml");
    }

    private static string Row(string html, string number)
    {
        var body = Regex.Match(html, "<tbody>(.*?)</tbody>", RegexOptions.Singleline).Groups[1].Value;
        return Regex.Matches(body, "<tr\\b.*?</tr>", RegexOptions.Singleline).Select(m => m.Value).Single(r => r.Contains(number));
    }

    // ------------------------------------------------------------------ toolbar tabs

    // ------------------------------------------------------------------ folder tree (the cartable side panel replaced the toolbar tabs)

    [Fact]
    public async Task Folders_ShowCounts_OnlyWhereSomethingIsWaiting_AndMarkTheReturnedAndReferralFoldersAsAlerts()
    {
        var html = await Render(NoFilters(), new[] { Row(1) }, counts: new LetterFolderCountsDto { ReturnedForRevision = 2, Starred = 0 }, myReferrals: 5,
            cartable: new CartableCountsDto { Drafts = 3, InboxUnread = 4 });

        Assert.Matches(@"<span class=""ct-label"">Active correspondence</span>\s*</a>", html);   // the default view never carries a count
        Assert.Matches(@"<span class=""ct-label"">Inbox</span>\s*<span class=""ct-count ct-count--hot"">4</span>", html);
        Assert.Matches(@"<span class=""ct-label"">Drafts</span>\s*<span class=""ct-count "">3</span>", html);
        Assert.Matches(@"<span class=""ct-label"">Returned</span>\s*<span class=""ct-count ct-count--alert"">2</span>", html);
        Assert.Matches(@"<span class=""ct-label"">Referred to me</span>\s*<span class=""ct-count ct-count--alert"">5</span>", html);
        Assert.DoesNotMatch(@"<span class=""ct-label"">Starred</span>\s*<span class=""ct-count", html);   // zero = no badge
        Assert.DoesNotMatch(@"<span class=""ct-label"">Approved</span>\s*<span class=""ct-count", html);
    }

    [Theory]
    [InlineData("PendingApproval", "Under review")]
    [InlineData("ReturnedForRevision", "Returned")]
    [InlineData("Approved", "Approved")]
    public async Task ActiveWorkflowFolder_IsMarkedWithAriaCurrent(string status, string label)
    {
        var html = await Render(NoFilters(status), new[] { Row(1) });

        Assert.Matches(@"class=""ct-folder active""[^>]*aria-current=""page""[^>]*>\s*<i class=""bi [^""]+"" aria-hidden=""true""></i>\s*<span class=""ct-label"">" + label + "</span>", html);
        Assert.Equal(1, Regex.Matches(html, "aria-current=\"page\"").Count);
    }

    [Fact]
    public async Task DraftStatusLink_KeepsWorking_AndTitlesTheList()
    {
        var html = await Render(NoFilters("Draft"), new[] { Row(1) });
        Assert.Matches(@"<h2>Drafts</h2>", html);
        Assert.DoesNotContain("aria-current=\"page\"", html);   // no sidebar item stands for the legacy status filter
    }

    [Fact]
    public async Task DefaultFolder_IsActive_WhenNoStatusOrStarFilterIsSet_EvenWithATypeFilter()
    {
        var html = await Render(NoFilters(type: "Incoming"), new[] { Row(1) });
        Assert.Matches(@"class=""ct-folder active""[^>]*aria-current=""page""[^>]*>\s*<i class=""bi bi-collection""", html);
    }

    // ------------------------------------------------------------------ filter popover + chips

    [Fact]
    public async Task FilterPopover_HasEveryFilterControl_WithLabels_AndNoBadgeWhenNothingIsActive()
    {
        var html = await Render(NoFilters(), new[] { Row(1) });

        foreach (var name in new[] { "letterType", "priority", "fromDate", "toDate", "disciplineId", "projectId", "unread", "overdueResponse", "overdueReferral", "hasAttachments", "hasReferrals" })
            Assert.Contains($"name=\"{name}\"", html);
        foreach (var id in new[] { "fpFrom", "fpTo", "fpDiscipline", "fpProject" })
            Assert.Matches($@"for=""{id}""", html);
        Assert.DoesNotContain("lp-badge", html);
        Assert.DoesNotContain("lp-chips", html);
    }

    [Fact]
    public async Task ActiveFilters_ShowABadgeOnTheIcon_AndOneChipEach_IncludingTheNewOnes()
    {
        var filters = Obj(("letterType", "Incoming"), ("letterNumber", null), ("subject", null), ("sender", null), ("receiver", null), ("projectId", null),
            ("disciplineId", 1), ("status", null), ("fromDate", new DateTime(2026, 3, 1)), ("toDate", new DateTime(2026, 3, 31)), ("starred", false),
            ("priority", "High"), ("hasAttachments", null), ("hasReferrals", null), ("overdueReferral", false), ("overdueResponse", false));

        var html = await Render(filters, new[] { Row(1) }, unread: true, query: "pump", total: 12);

        Assert.Matches(@"class=""lp-badge"">6<", html);                         // type, discipline, from, to, priority, unread (search has its own chip)
        Assert.Equal(7, Regex.Matches(html, "class=\"pems-filter-chip\"").Count);
        Assert.Contains("Mech", html);
        Assert.Contains("2026-03-01", html);
        Assert.Contains("2026-03-31", html);
        Assert.Matches(@"(«|&#xAB;)pump(»|&#xBB;)", html);          // the search text has its own chip (guillemets are HTML-encoded)
        Assert.Contains("12 results", html);
        Assert.Matches(@"id=""fpUnread""[^>]*checked", html);
        Assert.Matches(@"id=""fpType_Incoming""[^>]*checked", html);
        Assert.Matches(@"id=""fpPri_High""[^>]*checked", html);
        Assert.Matches(@"id=""fpFrom""[^>]*value=""2026-03-01""", html);
    }

    // ------------------------------------------------------------------ search

    [Fact]
    public async Task SearchForm_IsHiddenUntilOpened_AndKeepsTheGlobalSuggestionsAttached()
    {
        var html = await Render(NoFilters(), new[] { Row(1) }, query: "pump");

        Assert.Matches(@"<form method=""get"" class=""lp-search"" id=""lpSearchForm"" role=""search"" hidden>", html);
        Assert.Matches(@"<input type=""search"" name=""q"" id=""lpSearchInput"" value=""pump""[^>]*data-pems-search data-search-type=""Letter""", html);
        Assert.Matches(@"id=""lpSearchBtn""[^>]*aria-expanded=""false""", html);
    }

    // ------------------------------------------------------------------ rows

    [Fact]
    public async Task UnreadRows_AreMarked_ForStyleAndForScreenReaders()
    {
        var html = await Render(NoFilters(), new[] { Row(1, unread: true), Row(2) });

        var unread = Row(html, "LTR-1");
        var read = Row(html, "LTR-2");
        Assert.Contains("lp-unread", unread);
        Assert.Contains("class=\"visually-hidden\">Unread<", unread);
        Assert.DoesNotContain("lp-unread", read);
        Assert.DoesNotContain(">Unread<", read);
    }

    [Fact]
    public async Task Snippet_IsShownNextToTheSubject_AndHtmlInItIsEncoded()
    {
        var html = await Render(NoFilters(), new[] { Row(1, snippet: "Please review <b>now</b>"), Row(2) });

        var withSnippet = Row(html, "LTR-1");
        Assert.Contains("class=\"lp-snippet\"", withSnippet);
        Assert.Contains("Please review &lt;b&gt;now&lt;/b&gt;", withSnippet);
        Assert.DoesNotContain("<b>now</b>", withSnippet);
        Assert.DoesNotContain("lp-snippet", Row(html, "LTR-2"));
    }

    [Fact]
    public async Task HoverActions_OfferReplyAndRefer_ForEveryRow_ButArchiveOnlyWithPermission()
    {
        var without = Row(await Render(NoFilters(), new[] { Row(1) }), "LTR-1");
        var with = Row(await Render(NoFilters(), new[] { Row(1) }, canArchive: true), "LTR-1");

        foreach (var html in new[] { without, with })
        {
            Assert.Contains("Reply: LTR-1", html);
            Assert.Contains("Refer: LTR-1", html);
            Assert.Contains("lp-act lp-act--always", html);          // open + more stay visible without hovering
        }
        Assert.DoesNotContain("data-row-archive", without);
        // links have no href in the render harness (no routes), so the target is checked in the source: a referral is now a letter written in the composer
        Assert.Contains("asp-route-referTo=\"@l.LetterId\"", File.ReadAllText(Path.Combine(Root(), "PEMS.Web", "Views", "Letter", "Index.cshtml")));
        Assert.Matches(@"data-row-archive=""1""", with);
    }

    [Fact]
    public async Task ResponseDue_IsASentence_WithTheDateInTheTooltip()
    {
        var html = await Render(NoFilters(), new[]
        {
            Row(1, due: DateTime.Today.AddDays(-2)), Row(2, due: DateTime.Today), Row(3, due: DateTime.Today.AddDays(2)), Row(4, due: DateTime.Today.AddDays(9)), Row(5)
        });

        Assert.Contains("2 days overdue", Row(html, "LTR-1"));
        Assert.Contains("pems-due-overdue", Row(html, "LTR-1"));
        Assert.Contains("Due today", Row(html, "LTR-2"));
        Assert.Contains("lp-due-soon", Row(html, "LTR-2"));
        Assert.Contains("2 days left", Row(html, "LTR-3"));
        Assert.Contains("lp-due-soon", Row(html, "LTR-3"));
        Assert.Contains("9 days left", Row(html, "LTR-4"));
        Assert.DoesNotContain("lp-due-soon", Row(html, "LTR-4"));
        Assert.Contains($"title=\"{DateTime.Today.AddDays(9):yyyy-MM-dd}\"", Row(html, "LTR-4"));
    }

    [Fact]
    public async Task EmptyList_ShowsAFriendlyEmptyState_WithClearAllOnlyWhenFiltered()
    {
        var plain = await Render(NoFilters(), Array.Empty<LetterDto>());
        Assert.Contains("lp-empty", plain);
        Assert.Contains("No letters found", plain);
        Assert.DoesNotContain("btn btn-sm btn-outline-secondary mt-2", plain);

        var filtered = await Render(NoFilters(), Array.Empty<LetterDto>(), query: "zzz");
        Assert.Contains("btn btn-sm btn-outline-secondary mt-2", filtered);
    }

    // ------------------------------------------------------------------ menus, bulk bar

    [Fact]
    public async Task MoreMenu_HoldsExport_Columns_Density_Shortcuts_AndAllFolders()
    {
        var html = await Render(NoFilters(), new[] { Row(1) });

        Assert.Contains("format=csv", html);
        Assert.Contains("format=xlsx", html);
        Assert.Matches(@"id=""lpDensity""[^>]*role=""menuitemcheckbox""[^>]*aria-checked=""false""", html);
        foreach (var key in new[] { "priority", "discipline", "attachments", "responseDue", "lastActivity" })
            Assert.Contains($"data-col-toggle=\"{key}\"", html);
        Assert.Contains("data-bs-target=\"#lpShortcuts\"", html);
        Assert.Contains("data-bs-target=\"#lxMenu\"", html);
        Assert.Contains("id=\"letterColumnsReset\"", html);
    }

    [Fact]
    public async Task ComposeButton_CarriesTheUrlTheCShortcutUses()
    {
        var html = await Render(NoFilters(), new[] { Row(1) });
        Assert.Matches(@"data-compose-url=""[^""]*""", html);
    }

    [Fact]
    public async Task BulkBar_SitsOverTheTable_AndIsHiddenUntilSomethingIsSelected()
    {
        var html = await Render(NoFilters(), new[] { Row(1) });

        var wrap = html.IndexOf("class=\"lp-tablewrap\"", StringComparison.Ordinal);
        var bar = html.IndexOf("id=\"letterBulkBar\"", StringComparison.Ordinal);
        var table = html.IndexOf("<table", StringComparison.Ordinal);
        Assert.True(wrap >= 0 && bar > wrap && table > bar, "bulk bar must be inside the table wrapper, before the table");
        Assert.Contains("lp-bulkbar", html);
        Assert.Contains("id=\"lpClearSelection\"", html);
        Assert.DoesNotContain("pems-bulk-active", html);     // the script adds it only while rows are selected
    }

    [Fact]
    public async Task Pagination_PreservesNothingBecauseTheTestRequestHasNoQuery_ButStillRenders()
    {
        var html = await Render(NoFilters(), Enumerable.Range(1, 3).Select(i => Row(i)), total: 80);
        Assert.Contains("data-grid-key=\"Letter\"", html);
    }

    // ------------------------------------------------------------------ localization + script contracts

    private static string Root()
    {
        var env = Environment.GetEnvironmentVariable("PEMS_REPO_ROOT");
        if (!string.IsNullOrEmpty(env)) return env;
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PEMS.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found; set PEMS_REPO_ROOT");
    }

    [Fact]
    public void EveryNewLpKey_ExistsInBothLanguages()
    {
        var view = File.ReadAllText(Path.Combine(Root(), "PEMS.Web", "Views", "Letter", "Index.cshtml"));
        var keys = Regex.Matches(view, "Localizer\\[\"((?:Lp_|Lt_Select\\b)[A-Za-z0-9_]*)\"\\]").Select(m => m.Groups[1].Value).ToHashSet();
        Assert.True(keys.Count >= 30, "expected the workspace keys to be discovered");

        foreach (var file in new[] { "SharedResource.fa.resx", "SharedResource.resx" })
        {
            var text = File.ReadAllText(Path.Combine(Root(), "PEMS.Web", "Resources", file));
            var missing = keys.Where(k => !text.Contains("name=\"" + k + "\"")).ToList();
            Assert.True(missing.Count == 0, file + " is missing: " + string.Join(", ", missing));
        }
    }

    [Fact]
    public void WorkspaceScript_UsesPhysicalKeys_IgnoresTyping_AndNeverEvaluatesText()
    {
        var js = File.ReadAllText(Path.Combine(Root(), "PEMS.Web", "wwwroot", "js", "pems-letter-workspace.js"));

        foreach (var code in new[] { "KeyJ", "KeyK", "KeyX", "KeyS", "KeyE", "KeyC", "KeyD", "Slash" })
            Assert.Contains($"\"{code}\"", js);                     // works on a Persian keyboard layout too
        Assert.Contains("isTypingTarget", js);
        Assert.Contains(".modal.show", js);                         // no shortcuts while a dialog is open
        Assert.DoesNotContain("eval(", js);
        Assert.DoesNotContain("innerHTML", js);
    }

    // ------------------------------------------------------------------ controller wiring (real service, in-memory data)

    private sealed class FakeUser : ICurrentUserService
    {
        public int? UserId => LetterTestWorld.Author + 1;      // employee 2 receives the letters sent by the author
        public string? UserName => "u2";
        public string? FullName => "User 2";
        public bool IsAuthenticated => true;
        public IReadOnlyList<string> Roles => new[] { "Staff" };
    }

    [Fact]
    public async Task Index_PassesUnreadAndSearchToTheService_AndExposesTheCountsAndFlags()
    {
        var (compose, letters, ctx, _) = LetterTestWorld.Services("indexctl");
        foreach (var subject in new[] { "Pump inspection", "Cable schedule" })
        {
            var input = LetterTestWorld.ValidInput();
            input.Subject = subject;
            var saved = await compose.SaveDraftAsync(input, LetterTestWorld.AuthorActor());
            Assert.True((await letters.SendAsync(saved.LetterId, LetterTestWorld.Author)).Success);
        }

        var controller = new LetterController(letters, null!, ctx, new FakeUser(), new FakePermissionService { AllowAll = false }, null!, null!, new NoCache(), null!, null!, null!);

        var result = Assert.IsType<ViewResult>(await controller.Index(null, null, null, null, null, null, null, null, null, null, unread: true, q: "Pump"));
        var page = Assert.IsType<PagedResult<LetterDto>>(result.Model);

        var only = Assert.Single(page.Items);
        Assert.Equal("Pump inspection", only.Subject);
        Assert.True(only.IsUnread);
        Assert.True((bool)controller.ViewBag.Unread);
        Assert.Equal("Pump", (string)controller.ViewBag.SearchQuery);
        Assert.IsType<LetterFolderCountsDto>(controller.ViewBag.FolderCounts);

        // without the unread filter both letters are listed; after opening one of them only the other stays unread
        await letters.GetDetailForViewerAsync(only.LetterId, new LetterViewer(LetterTestWorld.Author + 1, 2, false));
        var again = Assert.IsType<PagedResult<LetterDto>>(((ViewResult)await controller.Index(null, null, null, null, null, null, null, null, null, null, unread: true)).Model);
        Assert.Equal(new[] { "Cable schedule" }, again.Items.Select(i => i.Subject));
    }
}
