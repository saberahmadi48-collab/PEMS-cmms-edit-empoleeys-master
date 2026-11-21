using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PEMS.Application.DTOs;
using PEMS.Tests.TestSupport;
using PEMS.Web.Controllers;

namespace PEMS.Tests.Web;

/// <summary>
/// Letter/Index واقعی: فیلترهای پیشرفته، Chip فیلتر فعال، ستون‌های جدید (هم‌تعداد با هدر)، نوار عملیات گروهی (آرشیو فقط با مجوز)
/// و اینکه وضعیت «معوق» با متن هم نمایش داده می‌شود (نه فقط رنگ). Layout اجرا نمی‌شود (RazorViewRenderer).
/// </summary>
public class LetterIndexViewRenderTests
{
    private static LetterController NewController() => new(null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!)
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
    };

    private static LetterDto Row(int id, DateTime? due = null, string priority = "Normal") => new()
    {
        LetterId = id, LetterNumber = $"LTR-{id}", LetterType = "Outgoing", LetterDate = new DateTime(2026, 3, 1), Subject = $"Subject {id}",
        Priority = priority, ConfidentialityLevel = "Internal", Status = "Draft", CreatedDate = new DateTime(2026, 3, 1),
        LastActivityDate = new DateTime(2026, 3, 2), NextResponseDue = due, AttachmentsCount = id % 2, DisciplineName = "Mech"
    };

    private static async Task<string> Render(bool canArchive, object filters, params LetterDto[] rows)
    {
        var c = NewController();
        c.ViewBag.Dashboard = new LetterDashboardDto();
        c.ViewBag.Projects = new List<object> { Obj(("ProjectId", 7), ("ProjectName", "Alpha")) };
        c.ViewBag.Disciplines = new List<object> { Obj(("DisciplineId", 1), ("Name", "Mech")) };
        c.ViewBag.CanBulkArchive = canArchive;
        c.ViewBag.Filters = filters;

        var result = new ViewResult
        {
            ViewData = new Microsoft.AspNetCore.Mvc.ViewFeatures.ViewDataDictionary(c.ViewData)
            {
                Model = new PagedResult<LetterDto> { Items = rows.ToList(), Page = 1, PageSize = 50, TotalCount = rows.Length }
            }
        };
        return await RazorViewRenderer.RenderAsync(c, result, "~/Views/Letter/Index.cshtml");
    }

    // Anonymous types are internal to this assembly and the Razor `dynamic` binder cannot read them (in the app the view and
    // the controller share an assembly), so the test view data is built with ExpandoObject.
    private static object Obj(params (string Name, object? Value)[] props)
    {
        IDictionary<string, object?> bag = new System.Dynamic.ExpandoObject();
        foreach (var (name, value) in props) bag[name] = value;
        return bag;
    }

    private static object NoFilters() => Obj(("letterType", null), ("letterNumber", null), ("subject", null), ("sender", null), ("receiver", null),
        ("projectId", null), ("disciplineId", null), ("status", null), ("fromDate", null), ("toDate", null),
        ("starred", false), ("priority", ""), ("hasAttachments", null), ("hasReferrals", null), ("overdueReferral", false), ("overdueResponse", false));

    private static object Filtered() => Obj(("letterType", null), ("letterNumber", null), ("subject", "pump"), ("sender", null), ("receiver", null),
        ("projectId", 7), ("disciplineId", null), ("status", null), ("fromDate", null), ("toDate", null),
        ("starred", false), ("priority", "Urgent"), ("hasAttachments", true), ("hasReferrals", false), ("overdueReferral", true), ("overdueResponse", true));

    [Fact]
    public async Task AdvancedFilterControls_AreRendered_WithTheCorrectFieldNames()
    {
        var html = await Render(false, NoFilters(), Row(1));

        foreach (var name in new[] { "priority", "hasAttachments", "hasReferrals", "overdueReferral" })
            Assert.Contains($"name=\"{name}\"", html);
        Assert.Contains("Urgent", html);
        Assert.DoesNotContain("pems-filter-chips", html);   // nothing active -> no chip strip
    }

    [Fact]
    public async Task ActiveFilters_ShowAsChips_EachWithItsOwnRemoveControl_AndClearAll()
    {
        var html = await Render(false, Filtered(), Row(1));

        Assert.Contains("pems-filter-chips", html);
        Assert.Equal(7, Regex.Matches(html, "class=\"pems-filter-chip\"").Count);   // subject, project, priority, attachments, referrals, overdue referral, overdue response
        Assert.Contains("pump", html);
        Assert.Contains("Alpha", html);
        Assert.Equal(7, Regex.Matches(html, "aria-label=\"Remove filter:").Count);
        Assert.Contains("Clear all", html);
    }

    [Fact]
    public async Task EveryRow_HasAsManyCellsAsTheHeader_SoOptionalColumnsStayAligned()
    {
        var html = await Render(false, NoFilters(), Row(1), Row(2, due: DateTime.Today.AddDays(5)));

        var head = Regex.Match(html, "<thead>(.*?)</thead>", RegexOptions.Singleline).Groups[1].Value;
        var headerCount = Regex.Matches(head, "<th\\b").Count;
        var body = Regex.Match(html, "<tbody>(.*?)</tbody>", RegexOptions.Singleline).Groups[1].Value;
        var rows = Regex.Matches(body, "<tr\\b.*?</tr>", RegexOptions.Singleline);

        Assert.Equal(2, rows.Count);
        Assert.All(rows.Select(r => r.Value), r => Assert.Equal(headerCount, Regex.Matches(r, "<td\\b").Count));
        foreach (var key in new[] { "priority", "discipline", "attachments", "responseDue", "lastActivity" })
            Assert.Contains($"data-col-key=\"{key}\"", head);
    }

    [Fact]
    public async Task OverdueResponseDue_IsMarkedWithTextAndIcon_NotOnlyColour()
    {
        var html = await Render(false, NoFilters(), Row(1, due: DateTime.Today.AddDays(-3)), Row(2, due: DateTime.Today.AddDays(4)));

        var rows = Regex.Matches(html, "<tr\\b.*?</tr>", RegexOptions.Singleline).Select(m => m.Value).Where(r => r.Contains("LTR-")).ToList();
        Assert.Contains("pems-due-overdue", rows[0]);
        Assert.Contains("3 days overdue", rows[0]);        // a sentence now, not just "(overdue)"
        Assert.Contains("bi-exclamation-triangle", rows[0]);
        Assert.DoesNotContain("pems-due-overdue", rows[1]);
        Assert.Contains("4 days left", rows[1]);
    }

    [Fact]
    public async Task BulkBar_OffersSafeActionsAndExport_AndNeverApproveIssueOrClose()
    {
        var html = await Render(false, NoFilters(), Row(1));

        Assert.Contains("id=\"letterBulkForm\"", html);
        foreach (var op in new[] { "star", "unstar", "submit" })
            Assert.Contains($"name=\"op\" value=\"{op}\"", html);
        Assert.Contains("format=csv", html);
        Assert.Contains("format=xlsx", html);
        foreach (var forbidden in new[] { "value=\"approve\"", "value=\"issue\"", "value=\"close\"", "value=\"delete\"", "value=\"reject\"" })
            Assert.DoesNotContain(forbidden, html);
        Assert.Contains("id=\"letterBulkCount\"", html);
        Assert.Contains("aria-live=\"polite\"", html);
        // bulk buttons start disabled until something is selected
        Assert.Matches(new Regex("data-bulk-op=\"star\"[^>]*disabled"), html);
    }

    [Fact]
    public async Task ArchiveAction_AndReasonDialog_AreOnlyRenderedForUsersWithDeletePermission()
    {
        var without = await Render(false, NoFilters(), Row(1));
        var with = await Render(true, NoFilters(), Row(1));

        Assert.DoesNotContain("letterArchiveModal", without);
        Assert.DoesNotContain("value=\"archive\"", without);

        Assert.Contains("id=\"letterArchiveModal\"", with);
        Assert.Contains("name=\"op\" value=\"archive\"", with);
        Assert.Contains("id=\"letterArchiveReason\"", with);
        Assert.Contains("name=\"reason\"", with);
        Assert.Contains("id=\"letterArchiveReasonError\"", with);
    }

    [Fact]
    public async Task SelectAllCheckbox_HasAnAccessibleName()
    {
        var html = await Render(false, NoFilters(), Row(1));

        Assert.Matches(new Regex("id=\"selectAllLetters\"[^>]*aria-label=\"Select all letters\""), html);
    }

    // ------------------------------------------------------------------ wiring contracts

    private static string RepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        var root = Environment.GetEnvironmentVariable("PEMS_REPO_ROOT");
        if (!string.IsNullOrEmpty(root) && File.Exists(Path.Combine(root, relative))) return Path.Combine(root, relative);
        throw new FileNotFoundException("Repository file not found (set PEMS_REPO_ROOT when running from a copied output folder): " + relative);
    }

    [Theory]
    [InlineData("PEMS.Web/Resources/SharedResource.resx")]
    [InlineData("PEMS.Web/Resources/SharedResource.fa.resx")]
    public void EveryLocalizationKeyUsedByTheIndexView_ExistsInBothLanguages(string resx)
    {
        var keys = XDocument.Load(RepoFile(resx)).Descendants("data").Select(d => (string?)d.Attribute("name")).ToHashSet();
        var view = File.ReadAllText(RepoFile("PEMS.Web/Views/Letter/Index.cshtml"));

        var used = Regex.Matches(view, "Localizer\\[\"(Lt_[A-Za-z0-9_]+)\"\\]").Select(m => m.Groups[1].Value)
            .Concat(new[] { "Lt_ColAttachments", "Lt_Discipline", "Lt_Priority" }).Distinct().ToList();
        Assert.NotEmpty(used);
        Assert.All(used, k => Assert.Contains(k, keys));
        foreach (var p in new[] { "Urgent", "High", "Normal", "Low" }) Assert.Contains("Lt_P_" + p, keys);
    }

    [Fact]
    public void BulkEndpoint_IsPostWithAntiForgery_AndExcludesApproveIssueAndClose()
    {
        var bulk = typeof(LetterController).GetMethod(nameof(LetterController.Bulk))!;

        Assert.NotEmpty(bulk.GetCustomAttributes(typeof(HttpPostAttribute), true));
        Assert.NotEmpty(bulk.GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), true));

        var source = File.ReadAllText(RepoFile("PEMS.Web/Controllers/LetterController.cs"));
        var bulkBody = source[source.IndexOf("public async Task<IActionResult> Bulk(", StringComparison.Ordinal)..];
        bulkBody = bulkBody[..bulkBody.IndexOf("ToggleStar", StringComparison.Ordinal)];
        foreach (var banned in new[] { "ApproveAsync", "IssueAsync", "CloseAsync", "RejectAsync", "ReturnForRevisionAsync" })
            Assert.DoesNotContain(banned, bulkBody);
        // archive additionally needs the Delete permission, checked on the server
        Assert.Contains("\"Correspondence\", \"Delete\"", bulkBody);
    }

    [Fact]
    public void ExportEndpoint_IsGet()
    {
        var export = typeof(LetterController).GetMethod(nameof(LetterController.Export))!;

        Assert.NotEmpty(export.GetCustomAttributes(typeof(HttpGetAttribute), true));
    }
}
