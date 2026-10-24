using System.Dynamic;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using PEMS.Application.Common;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;
using PEMS.Tests.Web;
using PEMS.Web.Controllers;
using PEMS.Web.Resources;
using PEMS.Web.Security;

namespace PEMS.Tests.Cartable;

/// <summary>صفحه‌ی کارتابل (Index)، Details در پنل کناری، کنترلرها و فایل‌های JS/CSS: پوشه‌ها، درخت برچسب، سطل زباله، رشته‌ها، تنظیمات و گزارش.</summary>
public class CartableWebTests
{
    private const string Repo = "PEMS_REPO_ROOT";

    private sealed class FakeUser : ICurrentUserService
    {
        public int? UserId { get; init; } = 102;
        public string? UserName { get; init; } = "u2";
        public string? FullName { get; init; } = "User 2";
        public bool IsAuthenticated => true;
        public IReadOnlyList<string> Roles { get; init; } = new[] { "Staff" };
    }

    private static object Obj(params (string Name, object? Value)[] props)
    {
        IDictionary<string, object?> bag = new ExpandoObject();
        foreach (var (name, value) in props) bag[name] = value;
        return bag;
    }

    private static object NoFilters(string? status = null) => Obj(("letterType", null), ("letterNumber", null), ("subject", null), ("sender", null), ("receiver", null),
        ("projectId", null), ("disciplineId", null), ("status", status), ("fromDate", null), ("toDate", null), ("starred", false), ("priority", ""),
        ("hasAttachments", null), ("hasReferrals", null), ("overdueReferral", false), ("overdueResponse", false));

    private static LetterDto Row(int id, Action<LetterDto>? edit = null)
    {
        var l = new LetterDto
        {
            LetterId = id, LetterNumber = $"LTR-{id}", LetterType = "Internal", LetterDate = new DateTime(2026, 3, 1), Subject = $"Subject {id}", Priority = "Normal",
            ConfidentialityLevel = "Internal", Status = "Sent", CreatedDate = new DateTime(2026, 3, 1), LastActivityDate = new DateTime(2026, 3, 2), Receiver = "Sara", SenderName = "Ali"
        };
        edit?.Invoke(l);
        return l;
    }

    private sealed record RenderOptions(string? Folder = null, int? TagId = null, string ViewMode = "flat", CartableCountsDto? Cart = null, List<LetterTagNodeDto>? Tags = null,
        LetterUserSettingsRules.Settings? Settings = null, string? Status = null, string? Sort = null, bool SortDesc = true, bool CanBulkArchive = false, int Total = -1);

    private static async Task<string> Render(RenderOptions o, params LetterDto[] rows)
    {
        var c = new LetterController(null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!)
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        c.ViewBag.Dashboard = new LetterDashboardDto();
        c.ViewBag.FolderCounts = new LetterFolderCountsDto();
        c.ViewBag.Projects = new List<object> { Obj(("ProjectId", 7), ("ProjectName", "Alpha")) };
        c.ViewBag.Disciplines = new List<object> { Obj(("DisciplineId", 1), ("Name", "Mech")) };
        c.ViewBag.CanBulkArchive = o.CanBulkArchive;
        c.ViewBag.Filters = NoFilters(o.Status);
        c.ViewBag.Cartable = o.Cart ?? new CartableCountsDto();
        c.ViewBag.TagTree = o.Tags ?? new List<LetterTagNodeDto>();
        c.ViewBag.UserSettings = o.Settings ?? LetterUserSettingsRules.Sanitize(null);
        c.ViewBag.Folder = o.Folder;
        c.ViewBag.TagId = o.TagId;
        c.ViewBag.ViewMode = o.ViewMode;
        c.ViewBag.Sort = o.Sort;
        c.ViewBag.SortDesc = o.SortDesc;
        c.ViewBag.CanManageOrg = true;
        var result = new ViewResult
        {
            ViewData = new Microsoft.AspNetCore.Mvc.ViewFeatures.ViewDataDictionary(c.ViewData)
            { Model = new PagedResult<LetterDto> { Items = rows.ToList(), Page = 1, PageSize = 50, TotalCount = o.Total >= 0 ? o.Total : rows.Length } }
        };
        return await RazorViewRenderer.RenderAsync(c, result, "~/Views/Letter/Index.cshtml");
    }

    private static List<LetterTagNodeDto> Tags() => new()
    {
        new() { TagId = 1, Name = "Engineering", Color = "primary", Depth = 1, CanEdit = true },
        new() { TagId = 2, ParentTagId = 1, Name = "Electrical", Color = "warning", Depth = 2, CanEdit = true },
        new() { TagId = 3, Name = "Mine", Color = "danger", Depth = 1, IsPersonal = true, CanEdit = true }
    };

    private static string Root()
    {
        var root = Environment.GetEnvironmentVariable(Repo);
        if (!string.IsNullOrEmpty(root)) return root;
        var dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "PEMS.Web"))) dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("repo root not found; set PEMS_REPO_ROOT");
    }

    private static string Web(params string[] parts) => File.ReadAllText(Path.Combine(new[] { Root(), "PEMS.Web" }.Concat(parts).ToArray()));

    // ------------------------------------------------------------------ folders + tag tree
    [Fact]
    public async Task SidebarListsEveryPersonalFolder_DelegatedOnlyWhenSomethingIsDelegated()
    {
        var without = await Render(new RenderOptions());
        foreach (var label in new[] { "Active correspondence", "Inbox", "Referred to me", "Sent", "Drafts", "Starred", "My archive", "Trash", "All messages" })
            Assert.Contains($"<span class=\"ct-label\">{label}</span>", without);
        Assert.DoesNotContain("Delegated to me", without);

        var with = await Render(new RenderOptions(Cart: new CartableCountsDto { Delegated = 2 }));
        Assert.Matches(@"Delegated to me</span>\s*<span class=""ct-count ct-count--hot"">2</span>", with);
    }

    [Fact]
    public async Task TagTree_IsNested_MarksPersonalTags_AndHighlightsTheActiveOne()
    {
        var html = await Render(new RenderOptions(TagId: 2, Tags: Tags(), Cart: new CartableCountsDto { TagCounts = { [1] = 9, [2] = 4 } }));

        Assert.Contains("role=\"tree\"", html);
        var engineering = Regex.Match(html, "<li role=\"treeitem\"[^>]*data-tag-id=\"1\">(.*?)</li>\\s*<li role=\"treeitem\"[^>]*data-tag-id=\"3\">", RegexOptions.Singleline).Groups[1].Value;
        Assert.Contains("role=\"group\"", engineering);                   // Electrical is nested under Engineering
        Assert.Contains("Electrical", engineering);
        Assert.Contains("data-tag-toggle=\"1\"", html);                   // only the parent has a toggle
        Assert.DoesNotContain("data-tag-toggle=\"2\"", html);
        Assert.Matches(@"class=""ct-tag active""[^>]*aria-current=""page""", html);
        Assert.Matches(@"Engineering</span>\s*<span class=""ct-count"">9</span>", html);
        Assert.Contains("class=\"bi bi-person ct-tag-personal\"", html);   // «Mine» is personal and says so
        Assert.Contains("Tag: Electrical", html);                          // active tag also shows as a removable filter chip
    }

    [Fact]
    public async Task WorkflowQueues_AndAllFoldersOffcanvas_StayAvailable()
    {
        var html = await Render(new RenderOptions());
        Assert.Contains("id=\"lxMenu\"", html);
        foreach (var status in new[] { "Draft", "PendingApproval", "ReturnedForRevision", "Approved", "Issued", "Closed", "Sent", "Archived" })
            Assert.Matches($@"asp-route-status=""{status}""|status={status}|class=""pems-folder-link", html);
        Assert.Contains("Under review", html);
        Assert.Contains("Returned", html);
    }

    // ------------------------------------------------------------------ rows
    [Fact]
    public async Task Rows_ShowThreadReturnedRevivedAndReminderStates()
    {
        var html = await Render(new RenderOptions(),
            Row(1, l => { l.ThreadCount = 3; l.Status = "ReturnedForRevision"; l.IsRevived = true; l.IsArchivedByMe = true; l.MyReminderAt = new DateTime(2026, 5, 1, 9, 0, 0); }),
            Row(2));

        var first = Regex.Matches(html, "<tr class=\"lp-row.*?</tr>", RegexOptions.Singleline).Select(m => m.Value).First(r => r.Contains("LTR-1"));
        var second = Regex.Matches(html, "<tr class=\"lp-row.*?</tr>", RegexOptions.Singleline).Select(m => m.Value).First(r => r.Contains("LTR-2"));
        Assert.Contains("data-thread-toggle=\"1\"", first);
        Assert.Matches(@"data-thread-toggle=""1""[^>]*>\s*<i[^>]*></i>\s*3", first);
        Assert.Contains("Returned for revision", first);                  // the chip and the translated status
        Assert.Contains("ct-chip--return", first);
        Assert.Contains("ct-chip--revived", first);
        Assert.Contains("ct-chip--remind", first);
        Assert.DoesNotContain("data-thread-toggle", second);              // a single message has no thread chip
        Assert.DoesNotContain("ct-chip", second);
        Assert.Contains("data-preview-url", first);                       // opens in the side pane when enabled
        Assert.Contains("data-row-op=\"trash\"", first);
    }

    [Fact]
    public async Task EveryRow_StillMatchesTheHeaderCellCount_InEveryFolder()
    {
        foreach (var folder in new[] { null, LetterFolders.Inbox, LetterFolders.Trash, LetterFolders.Archive })
        {
            var html = await Render(new RenderOptions(Folder: folder), Row(1), Row(2, l => l.IsTrashed = true));
            var head = Regex.Match(html, "<thead>(.*?)</thead>", RegexOptions.Singleline).Groups[1].Value;
            var body = Regex.Match(html, "<tbody>(.*?)</tbody>", RegexOptions.Singleline).Groups[1].Value;
            var rows = Regex.Matches(body, "<tr\\b.*?</tr>", RegexOptions.Singleline);
            Assert.Equal(2, rows.Count);
            Assert.All(rows.Select(r => r.Value), r => Assert.Equal(Regex.Matches(head, "<th\\b").Count, Regex.Matches(r, "<td\\b").Count));
        }
    }

    // ------------------------------------------------------------------ trash / archive
    [Fact]
    public async Task TrashFolder_OffersRestorePurgeAndEmpty_NotTheNormalBulkActions()
    {
        var html = await Render(new RenderOptions(Folder: LetterFolders.Trash), Row(1, l => l.IsTrashed = true));

        Assert.Contains("class=\"ct-trash-bar\"", html);
        Assert.Contains("hidden only for you", html);
        Assert.Contains("data-confirm=", Regex.Match(html, "<form[^>]*data-confirm=[^>]*>", RegexOptions.Singleline).Value);   // emptying asks first
        Assert.Contains("data-bulk-op=\"restore\"", html);
        Assert.Contains("data-bulk-op=\"purge\"", html);
        Assert.DoesNotContain("data-bulk-op=\"trash\"", html);
        Assert.DoesNotContain("data-bulk-op=\"star\"", html);
        Assert.Contains("ct-trashed", html);
        Assert.Contains("data-row-op=\"restore\"", html);
        Assert.Contains("Remove for good", html);
    }

    [Fact]
    public async Task EmptyTrash_ShowsAFriendlyMessage()
    {
        var html = await Render(new RenderOptions(Folder: LetterFolders.Trash), Array.Empty<LetterDto>());
        Assert.Contains("The trash is empty", html);
        Assert.DoesNotContain("Empty trash", html);   // nothing to empty
    }

    [Fact]
    public async Task ArchiveFolder_OffersUnarchiveInsteadOfArchive()
    {
        var html = await Render(new RenderOptions(Folder: LetterFolders.Archive), Row(1, l => l.IsArchivedByMe = true));
        Assert.Contains("data-bulk-op=\"unarchiveme\"", html);
        Assert.DoesNotContain("data-bulk-op=\"archiveme\"", html);
    }

    [Fact]
    public async Task NormalFolder_OffersReadUnreadTagStarArchiveTrash_AndKeepsTheOfficialArchiveBehindPermission()
    {
        var without = await Render(new RenderOptions(Tags: Tags()), Row(1));
        foreach (var op in new[] { "read", "unread", "star", "unstar", "archiveme", "trash", "submit" })
            Assert.Contains($"data-bulk-op=\"{op}\"", without);
        Assert.Contains("data-bulk-tag=\"2\"", without);
        Assert.DoesNotContain("letterArchiveModal", without);
        Assert.DoesNotContain("data-row-archive", without);

        var with = await Render(new RenderOptions(Tags: Tags(), CanBulkArchive: true), Row(1));
        Assert.Contains("id=\"letterArchiveModal\"", with);
        Assert.Contains("data-row-archive=\"1\"", with);   // the per-row official archive is still there
        foreach (var forbidden in new[] { "value=\"approve\"", "value=\"issue\"", "value=\"close\"", "value=\"delete\"", "value=\"reject\"" })
            Assert.DoesNotContain(forbidden, with);
    }

    // ------------------------------------------------------------------ sorting / views
    [Fact]
    public async Task ColumnHeaders_AreSortLinks_WithAriaSortOnTheActiveColumn()
    {
        var byDate = await Render(new RenderOptions(), Row(1));
        Assert.Matches(@"data-col-key=""date""[^>]*aria-sort=""descending""", byDate);
        Assert.Matches(@"data-col-key=""subject""[^>]*aria-sort=""none""", byDate);

        var bySubject = await Render(new RenderOptions(Sort: "subject", SortDesc: false), Row(1));
        Assert.Matches(@"data-col-key=""subject""[^>]*aria-sort=""ascending""", bySubject);
        Assert.Contains("bi-sort-up", bySubject);
    }

    [Fact]
    public async Task ViewSwitch_MarksTheCurrentMode()
    {
        var thread = await Render(new RenderOptions(ViewMode: "thread"));
        Assert.Matches(@"aria-pressed=""true""[^>]*title=""Threads""", thread);
        var flat = await Render(new RenderOptions());
        Assert.Matches(@"aria-pressed=""true""[^>]*title=""Flat""", flat);
    }

    // ------------------------------------------------------------------ config + settings modal
    [Fact]
    public async Task ConfigBlock_IsValidJson_AndCannotBreakOutOfTheScriptTag()
    {
        var tags = Tags();
        tags[0].Name = "</script><img src=x onerror=alert(1)>";
        var html = await Render(new RenderOptions(Tags: tags, Settings: LetterUserSettingsRules.Sanitize(new LetterUserSettingsRules.Settings { View = "thread", Density = "dense", FrozenColumns = 2 })));

        var json = Regex.Match(html, "<script id=\"ctConfig\" type=\"application/json\">(.*?)</script>", RegexOptions.Singleline).Groups[1].Value;
        Assert.DoesNotContain("<img", json);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("thread", doc.RootElement.GetProperty("settings").GetProperty("View").GetString());
        Assert.Equal(2, doc.RootElement.GetProperty("settings").GetProperty("FrozenColumns").GetInt32());
        Assert.Equal("</script><img src=x onerror=alert(1)>", doc.RootElement.GetProperty("tags")[0].GetProperty("Name").GetString());
        Assert.True(doc.RootElement.GetProperty("i18n").TryGetProperty("confirmTrash", out _));
        foreach (var key in new[] { "threadMembers", "settingsSave", "tagSave", "delegationCreate", "ruleSave", "groupSave", "reminderAdd" })
            Assert.True(doc.RootElement.GetProperty("urls").TryGetProperty(key, out _), key);
        Assert.Contains("data-density=\"dense\"", html);
        Assert.Contains("data-frozen=\"2\"", html);
    }

    [Fact]
    public async Task SettingsModal_HasAllEightSections_AndTheAntiforgeryToken()
    {
        var html = await Render(new RenderOptions());
        foreach (var pane in new[] { "general", "columns", "tags", "delegation", "rules", "contacts", "reminders", "keys" })
        {
            Assert.Contains($"data-pane=\"{pane}\"", html);
            Assert.Contains($"data-pane-body=\"{pane}\"", html);
        }
        Assert.Contains("id=\"ctSettings\"", html);
        Assert.Contains("name=\"__RequestVerificationToken\"", html);
        Assert.Contains("id=\"ctReminderModal\"", html);
        Assert.Contains("id=\"ctDetailFrame\"", html);
    }

    [Fact]
    public async Task ColumnDefaults_FollowTheUsersSettings()
    {
        var all = await Render(new RenderOptions(Settings: LetterUserSettingsRules.Sanitize(new LetterUserSettingsRules.Settings { HiddenColumns = new() })));
        Assert.Matches(@"data-col-toggle=""discipline"" data-default-hidden=""false""", all);
        var legacy = await Render(new RenderOptions());
        Assert.Matches(@"data-col-toggle=""discipline"" data-default-hidden=""true""", legacy);
        Assert.Matches(@"data-col-toggle=""priority"" data-default-hidden=""false""", legacy);
    }

    // ------------------------------------------------------------------ Details (embedded pane + history options)
    private static async Task<string> RenderDetails(bool embed, string role = "all", string order = "asc")
    {
        var c = new LetterController(null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!)
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        c.ViewBag.Departments = new List<object>();
        c.ViewBag.AllTags = new List<TagDto>();
        c.ViewBag.Timeline = new List<LetterTimelineEventDto> { new() { EventDate = new DateTime(2026, 3, 1), EventType = "Created", Description = "created", ActorEmployeeId = 3 } };
        c.ViewBag.Thread = new List<ThreadMessageDto>();
        c.ViewBag.CanEditReturned = false; c.ViewBag.CanEditDraft = false; c.ViewBag.CanReview = false;
        c.ViewBag.Embed = embed; c.ViewBag.HistoryRole = role; c.ViewBag.HistoryOrder = order;
        var model = new LetterDetailDto
        {
            Letter = new LetterDto { LetterId = 5, LetterNumber = "LTR-5", LetterType = "Internal", Subject = "Hello", Status = "Sent", Priority = "Normal", ConfidentialityLevel = "Internal", LetterDate = new DateTime(2026, 3, 1) }
        };
        var result = new ViewResult { ViewData = new Microsoft.AspNetCore.Mvc.ViewFeatures.ViewDataDictionary(c.ViewData) { Model = model } };
        return await RazorViewRenderer.RenderAsync(c, result, "~/Views/Letter/Details.cshtml");
    }

    [Fact]
    public async Task Details_HistoryOptions_ReflectTheChosenOrderAndRole_AndStayInsideThePane()
    {
        var html = await RenderDetails(embed: true, role: "manager", order: "desc");
        Assert.Contains("name=\"historyOrder\"", html);
        Assert.Contains("name=\"historyRole\"", html);
        Assert.Matches(@"<option value=""desc"" selected", html);
        Assert.Matches(@"<option value=""manager"" selected", html);
        foreach (var role in new[] { "all", "me", "manager", "subordinates" }) Assert.Contains($"<option value=\"{role}\"", html);
        Assert.Contains("name=\"embed\" value=\"true\"", html);             // the pane keeps its embed flag across the filter
        Assert.Contains("target=\"_self\"", html);                          // and the filter form reloads inside the pane
    }

    [Fact]
    public async Task Details_FullPage_HasNoEmbedFlag()
    {
        var html = await RenderDetails(embed: false);
        Assert.DoesNotContain("name=\"embed\"", html);
        Assert.Contains("name=\"historyRole\"", html);
    }

    // ------------------------------------------------------------------ LetterController (cartable operations)
    private static (LetterController Ctl, PEMSDbContext Ctx, LetterCartableService Cart) Controller(string name, ICurrentUserService? user = null)
    {
        var ctx = LetterTestWorld.NewContext("CartWeb_" + name);
        var org = new OrganizationSubordinateResolver(ctx);
        var cart = new LetterCartableService(ctx, org);
        var tags = new LetterTagTreeService(ctx);
        var letters = new LetterService(ctx, sanitizer: null!, knowledgeBaseService: null!);
        var c = new LetterController(letters, null!, ctx, user ?? new FakeUser(), new FakePermissionService { AllowAll = false }, null!, null!, new NoCache(), null!, null!, null!,
            cartable: cart, tagTree: tags, userSettings: new LetterUserSettingsService(ctx)).WithTempData().WithUrlHelper();
        return (c, ctx, cart);
    }

    private static Letter SentLetter(PEMSDbContext ctx, int id, int author = 101, int[]? to = null, string conf = "Internal")
    {
        var l = new Letter { LetterId = id, LetterType = "Internal", LetterNumber = "N-" + id, LetterDate = DateTime.Today, Subject = "S" + id, ConfidentialityLevel = conf, Status = "Sent", RegisteredById = author, IsActive = true };
        foreach (var e in to ?? new[] { 2 }) l.Recipients.Add(new LetterRecipient { RecipientType = "To", EmployeeId = e });
        ctx.Letters.Add(l); ctx.SaveChanges();
        return l;
    }

    [Fact]
    public async Task Bulk_TrashAndRestore_GoThroughTheCartableService_AndRedirectBack()
    {
        var (c, ctx, _) = Controller(nameof(Bulk_TrashAndRestore_GoThroughTheCartableService_AndRedirectBack));
        var l = SentLetter(ctx, 700);
        var trashed = await c.Bulk("trash", new[] { 700 }, null, "/Letter");
        Assert.IsType<LocalRedirectResult>(trashed);
        Assert.True(ctx.LetterUserStates.Single().IsTrashed);
        Assert.Contains("1 letter(s) moved to trash", (string)c.TempData["Message"]!);

        await c.Bulk("restore", new[] { 700 }, null, "/Letter");
        Assert.False(ctx.LetterUserStates.Single().IsTrashed);
        Assert.Equal("Sent", ctx.Letters.Single(x => x.LetterId == 700).Status);   // the letter itself never changes
    }

    [Fact]
    public async Task Bulk_TagAndUntag_UseTheChosenTag_AndNeedOne()
    {
        var (c, ctx, _) = Controller(nameof(Bulk_TagAndUntag_UseTheChosenTag_AndNeedOne));
        SentLetter(ctx, 701);
        ctx.Tags.Add(new Tag { TagId = 1, Name = "T" }); await ctx.SaveChangesAsync();

        await c.Bulk("tag", new[] { 701 }, null, "/Letter");
        Assert.Equal("No tag was chosen.", c.TempData["Error"]);
        Assert.Empty(ctx.LetterTags);

        await c.Bulk("tag", new[] { 701 }, null, "/Letter", tagId: 1);
        Assert.Single(ctx.LetterTags);
        await c.Bulk("untag", new[] { 701 }, null, "/Letter", tagId: 1);
        Assert.Empty(ctx.LetterTags);
    }

    [Fact]
    public async Task Bulk_ReadUnread_AndNothingToDoIsReportedHonestly()
    {
        var (c, ctx, _) = Controller(nameof(Bulk_ReadUnread_AndNothingToDoIsReportedHonestly));
        SentLetter(ctx, 702);
        await c.Bulk("read", new[] { 702 }, null, "/Letter");
        Assert.True(ctx.LetterRecipients.Single().IsRead);
        await c.Bulk("read", new[] { 702 }, null, "/Letter");
        Assert.Equal("Nothing to change for the selected letters.", c.TempData["Error"]);
    }

    [Fact]
    public async Task Bulk_CannotTouchLettersTheUserCannotSee()
    {
        var (c, ctx, _) = Controller(nameof(Bulk_CannotTouchLettersTheUserCannotSee), new FakeUser { UserId = 105 });
        SentLetter(ctx, 703, to: new[] { 2 }, conf: "Restricted");
        await c.Bulk("trash", new[] { 703 }, null, "/Letter");
        Assert.Empty(ctx.LetterUserStates);
        Assert.NotNull(c.TempData["Error"]);
    }

    [Fact]
    public async Task EmptyTrash_PurgesOnlyTheCurrentUsersTrash()
    {
        var (c, ctx, cart) = Controller(nameof(EmptyTrash_PurgesOnlyTheCurrentUsersTrash));
        SentLetter(ctx, 704, to: new[] { 2, 3 });
        await cart.TrashAsync(new[] { 704 }, 102, false);
        await cart.TrashAsync(new[] { 704 }, 103, false);

        var r = Assert.IsType<RedirectToActionResult>(await c.EmptyTrash());
        Assert.Equal("Index", r.ActionName);
        Assert.Equal(LetterFolders.Trash, r.RouteValues!["folder"]);
        Assert.True(ctx.LetterUserStates.Single(s => s.ApplicationUserId == 102).IsPurged);
        Assert.False(ctx.LetterUserStates.Single(s => s.ApplicationUserId == 103).IsPurged);
    }

    [Fact]
    public async Task ThreadMembers_ForbidsALetterTheUserCannotSee_AndListsTheThreadOtherwise()
    {
        var (c, ctx, _) = Controller(nameof(ThreadMembers_ForbidsALetterTheUserCannotSee_AndListsTheThreadOtherwise));
        var root = SentLetter(ctx, 705, to: new[] { 2 });
        var reply = SentLetter(ctx, 706, author: 102, to: new[] { 1 }); reply.ParentLetterId = root.LetterId; await ctx.SaveChangesAsync();
        var secret = SentLetter(ctx, 707, to: new[] { 4 }, conf: "Restricted");

        var ok = Assert.IsType<JsonResult>(await c.ThreadMembers(705));
        var json = JsonSerializer.Serialize(ok.Value);
        Assert.Contains("N-705", json);
        Assert.Contains("N-706", json);
        Assert.IsType<ForbidResult>(await c.ThreadMembers(secret.LetterId));
    }

    [Fact]
    public async Task Index_FillsTheCartableViewBag_HonoursFolderTagViewAndSettings()
    {
        var (c, ctx, cart) = Controller(nameof(Index_FillsTheCartableViewBag_HonoursFolderTagViewAndSettings));
        var root = SentLetter(ctx, 710, to: new[] { 2 });
        var reply = SentLetter(ctx, 711, author: 102, to: new[] { 1 }); reply.ParentLetterId = root.LetterId;
        var other = SentLetter(ctx, 712, to: new[] { 2 });
        ctx.Tags.Add(new Tag { TagId = 1, Name = "Eng" }); ctx.LetterTags.Add(new LetterTag { LetterId = other.LetterId, TagId = 1 });
        await ctx.SaveChangesAsync();
        await cart.TrashAsync(new[] { 712 }, 102, false);
        await new LetterUserSettingsService(ctx).SaveAsync(102, new LetterUserSettingsRules.Settings { View = "thread", PageSize = 20 });

        // default list: no trash, thread view collapses the reply under its root
        var view = Assert.IsType<ViewResult>(await c.Index(null, null, null, null, null, null, null, null, null, null));
        var page = Assert.IsType<PagedResult<LetterDto>>(view.Model);
        Assert.Equal(new[] { 710 }, page.Items.Select(i => i.LetterId));
        Assert.Equal(2, page.Items[0].ThreadCount);
        Assert.Equal("thread", (string)c.ViewBag.ViewMode);
        Assert.Equal(20, page.PageSize);
        Assert.Equal(1, ((CartableCountsDto)c.ViewBag.Cartable).Trash);
        Assert.Contains((IEnumerable<LetterTagNodeDto>)c.ViewBag.TagTree, n => n.Name == "Eng");

        // an explicit URL parameter beats the saved setting; folder + sort + tag are passed through
        var flat = Assert.IsType<ViewResult>(await c.Index(null, null, null, null, null, null, null, null, null, null, view: "flat", folder: "trash"));
        Assert.Equal(new[] { 712 }, ((PagedResult<LetterDto>)flat.Model!).Items.Select(i => i.LetterId));
        Assert.Equal("trash", (string)c.ViewBag.Folder);
        Assert.True(((PagedResult<LetterDto>)flat.Model!).Items[0].IsTrashed);

        var byTag = Assert.IsType<ViewResult>(await c.Index(null, null, null, null, null, null, null, null, null, null, view: "flat", tagId: 1, folder: "all"));
        Assert.Equal(new[] { 712 }, ((PagedResult<LetterDto>)byTag.Model!).Items.Select(i => i.LetterId));

        var sorted = Assert.IsType<ViewResult>(await c.Index(null, null, null, null, null, null, null, null, null, null, view: "flat", sort: "subject", dir: "asc"));
        Assert.Equal(new[] { 710, 711 }, ((PagedResult<LetterDto>)sorted.Model!).Items.Select(i => i.LetterId));
        Assert.Equal("subject", (string)c.ViewBag.Sort);
        Assert.False((bool)c.ViewBag.SortDesc);

        // a bogus folder falls back to the default view instead of failing
        var bogus = Assert.IsType<ViewResult>(await c.Index(null, null, null, null, null, null, null, null, null, null, view: "flat", folder: "../etc"));
        Assert.Null((string?)c.ViewBag.Folder);
        Assert.Equal(2, ((PagedResult<LetterDto>)bogus.Model!).Items.Count);
    }

    // ------------------------------------------------------------------ LetterCartableController
    private static LetterCartableController Cartable(string name, out PEMSDbContext ctx, ICurrentUserService? user = null)
    {
        ctx = LetterTestWorld.NewContext("CartCtl_" + name);
        var org = new OrganizationSubordinateResolver(ctx);
        var localizer = new Microsoft.Extensions.DependencyInjection.ServiceCollection().AddLogging().AddLocalization().BuildServiceProvider().GetRequiredService<IStringLocalizer<SharedResource>>();
        return new LetterCartableController(new LetterTagTreeService(ctx), new LetterUserSettingsService(ctx), new LetterDelegationService(ctx, org),
            new LetterRoutingRuleService(ctx, org), new LetterContactService(ctx), new LetterPersonalReminderService(ctx), new LetterReportService(ctx, org),
            user ?? new FakeUser(), new FakePermissionService { AllowAll = false }, ctx, localizer)
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
    }

    private static T Value<T>(IActionResult r, string prop)
    {
        var json = JsonSerializer.Serialize(Assert.IsType<JsonResult>(r).Value);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty(prop).Deserialize<T>()!;
    }

    [Fact]
    public async Task CartableController_TagCreateReturnsLocalizedErrors_AndOkOnSuccess()
    {
        var c = Cartable(nameof(CartableController_TagCreateReturnsLocalizedErrors_AndOkOnSuccess), out var ctx);

        var bad = await c.TagSave(new LetterCartableController.TagRequest(null, "", null, null, true, 0));
        Assert.False(Value<bool>(bad, "ok"));
        Assert.Contains("tag name", Value<string>(bad, "error"), StringComparison.OrdinalIgnoreCase);   // English text from the resx, not the raw key

        var ok = await c.TagSave(new LetterCartableController.TagRequest(null, "Mine", "danger", null, true, 0));
        Assert.True(Value<bool>(ok, "ok"));
        Assert.Equal(102, ctx.Tags.Single().OwnerUserId);

        var orgDenied = await c.TagSave(new LetterCartableController.TagRequest(null, "Org", null, null, false, 0));
        Assert.False(Value<bool>(orgDenied, "ok"));   // FakePermissionService has no Approve for Staff
    }

    [Fact]
    public async Task CartableController_DelegationAndRuleEndpoints_Work_AndRejectBadInput()
    {
        var c = Cartable(nameof(CartableController_DelegationAndRuleEndpoints_Work_AndRejectBadInput), out var ctx);

        var self = await c.DelegationCreate(new LetterCartableController.DelegationRequest(102, DateTime.Today, DateTime.Today.AddDays(2), null));
        Assert.False(Value<bool>(self, "ok"));
        var okDel = await c.DelegationCreate(new LetterCartableController.DelegationRequest(103, DateTime.Today, DateTime.Today.AddDays(2), "trip"));
        Assert.True(Value<bool>(okDel, "ok"));
        var list = Assert.IsType<JsonResult>(await c.Delegations());
        Assert.Contains("\"given\"", JsonSerializer.Serialize(list.Value));
        Assert.True(Value<bool>(await c.DelegationRevoke(new LetterCartableController.IdRequest(Value<int>(okDel, "id"))), "ok"));

        var noCond = await c.RuleSave(new LetterRoutingRuleDto { Name = "x", StarLetter = true });
        Assert.False(Value<bool>(noCond, "ok"));
        var rule = await c.RuleSave(new LetterRoutingRuleDto { Name = "urgent", MatchPriority = "Urgent", StarLetter = true, IsActive = true });
        Assert.True(Value<bool>(rule, "ok"));
        Assert.Single(ctx.LetterRoutingRules);
        Assert.True(Value<bool>(await c.RuleToggle(new LetterCartableController.ToggleRequest(Value<int>(rule, "id"), false)), "ok"));
        Assert.True(Value<bool>(await c.RuleDelete(new LetterCartableController.IdRequest(Value<int>(rule, "id"))), "ok"));
    }

    [Fact]
    public async Task CartableController_GroupsRemindersAndSettings_RoundTrip()
    {
        var c = Cartable(nameof(CartableController_GroupsRemindersAndSettings_RoundTrip), out var ctx);
        SentLetter(ctx, 800, to: new[] { 2 });

        var g = await c.GroupSave(new LetterCartableController.GroupRequest(null, "Team", true, new List<int> { 3, 4 }, new List<int>()));
        Assert.True(Value<bool>(g, "ok"));
        Assert.Contains("Team", JsonSerializer.Serialize(Assert.IsType<JsonResult>(await c.Groups()).Value));

        var rem = await c.ReminderAdd(new LetterCartableController.ReminderRequest(800, DateTime.Now.AddHours(2), "call"));
        Assert.True(Value<bool>(rem, "ok"));
        Assert.Contains("call", JsonSerializer.Serialize(Assert.IsType<JsonResult>(await c.Reminders()).Value));

        await c.SettingsSave(new LetterCartableController.SettingsRequest("thread", "dense", new List<string> { "priority", "bogus" }, 2, false, false, true, 100));
        var json = JsonSerializer.Serialize(Assert.IsType<JsonResult>(await c.Settings()).Value);
        Assert.Contains("\"View\":\"thread\"", json);
        Assert.DoesNotContain("bogus", json);
    }

    [Fact]
    public async Task ReportPage_RendersKpisSlaAndTheSubordinateTable()
    {
        var c = Cartable(nameof(ReportPage_RendersKpisSlaAndTheSubordinateTable), out _);
        var model = new CartableReportDto
        {
            FromDate = new DateTime(2026, 1, 1), ToDate = new DateTime(2026, 3, 31), Received = 12, Unread = 3, Sent = 4, OpenReferrals = 2, OverdueReferrals = 1, CompletedReferrals = 5,
            ResponseRequired = 6, AnsweredOnTime = 3, AnsweredLate = 1, OpenOverdue = 1, OpenWithinSla = 1, AvgResponseDays = 2.5, AvgReferralDays = 1.5, HasSubordinates = true,
            ByMonth = new() { new("2026-01", 5), new("2026-02", 7) }, ByDiscipline = new() { new("Mech", 8) },
            Subordinates = new() { new SubordinateReportRowDto { EmployeeId = 3, Name = "Sam <Eng>", Position = "Engineer", Depth = 1, Received = 4, OverdueReferrals = 2 } }
        };
        var result = new ViewResult { ViewData = new Microsoft.AspNetCore.Mvc.ViewFeatures.ViewDataDictionary(c.ViewData) { Model = model } };
        var html = await RazorViewRenderer.RenderAsync(c, result, "~/Views/LetterCartable/Report.cshtml");

        Assert.Contains("Response SLA", html);
        Assert.Contains("Subordinates&#x27; performance", html.Replace("&#39;", "&#x27;"));
        Assert.Contains("Sam &lt;Eng&gt;", html);                       // user text is encoded
        Assert.Matches(@"class=""num bad"">2</td>", html);              // overdue referrals stand out
        Assert.Contains("data-bk-chart", html);
        Assert.True(Regex.Matches(html, "data-bk-chart").Count >= 3);   // SLA donut + gauge + monthly bars + discipline bars
        Assert.Contains("name=\"from\"", html);
        Assert.Contains("name=\"to\"", html);

        model.HasSubordinates = false; model.Subordinates = new();
        var without = await RazorViewRenderer.RenderAsync(c, new ViewResult { ViewData = new Microsoft.AspNetCore.Mvc.ViewFeatures.ViewDataDictionary(c.ViewData) { Model = model } }, "~/Views/LetterCartable/Report.cshtml");
        Assert.DoesNotContain("Subordinates&#x27; performance", without.Replace("&#39;", "&#x27;"));
    }

    // ------------------------------------------------------------------ static guarantees on the front-end files
    [Fact]
    public void SettingsScript_NeverUsesInnerHtml_AndSendsTheAntiforgeryHeader()
    {
        var settings = Web("wwwroot", "js", "pems-cartable-settings.js");
        var shell = Web("wwwroot", "js", "pems-cartable.js");
        Assert.DoesNotContain("innerHTML", settings);
        Assert.DoesNotContain("innerHTML", shell);
        Assert.DoesNotContain("eval" + "(", settings + shell);
        Assert.Contains("RequestVerificationToken", shell);
        Assert.Contains("textContent", settings);
    }

    [Fact]
    public void Stylesheet_UsesThemeTokens_AndOverridesThemesWithRootDataTheme()
    {
        var css = Web("wwwroot", "css", "pems-cartable.css");
        Assert.Contains(":root[data-theme=\"dark\"]", css);
        Assert.Contains(":root[data-theme=\"turquoise\"]", css);
        Assert.Contains("var(--pems-surface)", css);
        Assert.DoesNotContain("backdrop-filter", css);                    // would trap dropdowns inside their parent (see the theme cascade note)
        Assert.DoesNotContain(".ct-count.alert", css);                    // Bootstrap's .alert would restyle it
        Assert.Contains("prefers-reduced-motion", css);
        Assert.Contains("@media (max-width: 991.98px)", css);
    }

    [Fact]
    public void EveryCartableLocalizerKey_ExistsInBothLanguages()
    {
        var resourcesDir = Path.Combine(Root(), "PEMS.Web", "Resources");
        var en = File.ReadAllText(Path.Combine(resourcesDir, "SharedResource.resx"));
        var fa = File.ReadAllText(Path.Combine(resourcesDir, "SharedResource.fa.resx"));
        var sources = new[]
        {
            Web("Views", "Letter", "Index.cshtml"), Web("Views", "Letter", "Details.cshtml"), Web("Views", "LetterCartable", "Report.cshtml"),
            Web("Controllers", "LetterCartableController.cs")
        }.Concat(Directory.GetFiles(Path.Combine(Root(), "PEMS.Application"), "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText)).ToList();
        var keys = sources.SelectMany(s => Regex.Matches(s, "\"((?:Ct|Cart|Tag|Del|Rule|Grp|Rem)_(?:Err_)?[A-Za-z]+)\"").Select(m => m.Groups[1].Value))
            .Where(k => !k.EndsWith("_")).Distinct().ToList();
        Assert.True(keys.Count > 50);
        foreach (var key in keys)
        {
            Assert.True(fa.Contains($"name=\"{key}\""), "fa missing " + key);
            Assert.True(en.Contains($"name=\"{key}\""), "en missing " + key);
        }
    }
}
