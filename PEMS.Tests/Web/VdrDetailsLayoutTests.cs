using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using PEMS.Application.DTOs;
using PEMS.Domain.Entities;
using PEMS.Tests.TestSupport;
using PEMS.Web.Controllers;
using PEMS.Web.Security;

namespace PEMS.Tests.Web;

/// <summary>
/// صفحه‌ی جزئیات VDR (طراحی جدید): هدر با شماره‌ی مدرک و مسیر صفحه، نمودار مراحل داخل تب گردش‌کار (که پیش‌فرض است)، فرم شروع بازبینی
/// به‌صورت دو کارت مرحله‌ای + خلاصه، شماره‌ی بازنگری فقط‌خواندنی (ولی همچنان ارسال می‌شود) و فیلدهای فرم بدون تغییر نام.
/// View واقعی رندر می‌شود؛ Localizer متن انگلیسی resx را برمی‌گرداند.
/// </summary>
public class VdrDetailsLayoutTests
{
    private sealed class FakeUser : ICurrentUserService
    {
        public int? UserId => 1;
        public string? UserName => "u";
        public string? FullName => "U";
        public bool IsAuthenticated => true;
        public IReadOnlyList<string> Roles => new[] { "Staff" };
    }

    private static VDRItemDto Item(string status = "Planned", int? latestRev = null) => new()
    {
        VDRItemId = 5, VendorId = 9, VendorName = "Supcon", ProjectName = "Polymer Plant", DocumentNumber = "J-80-SUP-QA-RPT-1543-6901-001",
        DocumentTypeCode = "RPT", DisciplineCodeRaw = "QA", WorkflowStatus = status, LatestRevisionNo = latestRev
    };

    private static async Task<string> RenderAsync(VDRItemDto item, bool running = false, string? stage = null)
    {
        var (_, letters, ctx, _) = LetterTestWorld.Services("vdr");
        var c = new LetterController(letters, null!, ctx, new FakeUser(), new FakePermissionService(), null!, null!, null!, null!, null!, null!).WithTempData();
        c.ViewBag.Comments = new List<VDRCommentDto>();
        c.ViewBag.AllDisciplines = new List<Discipline>
        {
            new() { DisciplineId = 1, Name = "Electrical", IsActive = true },
            new() { DisciplineId = 2, Name = "Process", IsActive = true }
        };
        c.ViewBag.WorkflowRunning = running;
        c.ViewBag.CurrentStageName = stage;
        c.ViewBag.ReviewStatus = new List<PEMS.Application.Interfaces.VdrReviewStatusItemDto>();
        var result = new ViewResult { ViewData = new ViewDataDictionary(c.ViewData) { Model = item } };
        return await RazorViewRenderer.RenderAsync(c, result, "~/Views/VDR/Details.cshtml");
    }

    [Fact]
    public async Task Header_ShowsTheDocumentNumberAsTheHeading_WithBreadcrumbAndRevisionPill()
    {
        var html = await RenderAsync(Item(latestRev: 2));

        Assert.Matches(@"<h1 class=""vs-doc-number vs-num"">J-80-SUP-QA-RPT-1543-6901-001</h1>", html);
        Assert.Contains("Breadcrumb", html);
        Assert.Matches(@"vs-pill vs-pill--current"">Rev <span class=""vs-num"">2</span>", html);
        Assert.Contains("Supcon", html);
    }

    [Fact]
    public async Task Tabs_AreOverviewWorkflowSubmissionsCommentsCorrespondence_WithWorkflowOpenByDefault()
    {
        var html = await RenderAsync(Item());

        var order = new[] { "#tab-overview", "#tab-workflow", "#tab-submissions", "#tab-comments", "#tab-correspondence" }
            .Select(t => html.IndexOf("data-bs-target=\"" + t + "\" type=\"button\"", StringComparison.Ordinal)).ToList();
        Assert.All(order, i => Assert.True(i >= 0));
        Assert.Equal(order.OrderBy(x => x), order);

        Assert.Matches(@"nav-link active""\s+data-bs-toggle=""tab"" data-bs-target=""#tab-workflow""", html);
        Assert.DoesNotMatch(@"nav-link active""\s+data-bs-toggle=""tab"" data-bs-target=""#tab-overview""", html);
        Assert.Matches(@"tab-pane fade show active"" id=""tab-workflow""", html);
    }

    [Fact]
    public async Task ClosedDocuments_OpenOnTheOverview()
    {
        var html = await RenderAsync(Item("Closed", 1));

        Assert.Matches(@"tab-pane fade show active"" id=""tab-overview""", html);
        Assert.DoesNotMatch(@"tab-pane fade show active"" id=""tab-workflow""", html);
    }

    [Fact]
    public async Task Stepper_LivesInsideTheWorkflowTab_NotAboveTheTabs()
    {
        var html = await RenderAsync(Item());

        var tabs = html.IndexOf("id=\"vdrTabs\"", StringComparison.Ordinal);
        var workflowPane = html.IndexOf("id=\"tab-workflow\"", StringComparison.Ordinal);
        var tracker = html.IndexOf("vs-tracker", StringComparison.Ordinal);

        Assert.True(tabs >= 0 && workflowPane > tabs);
        Assert.True(tracker > workflowPane, "the stage tracker must be rendered inside the workflow pane");
        Assert.Equal(1, Regex.Matches(html, "class=\"vs-tracker\"").Count);
        Assert.Contains("aria-current=\"step\"", html);        // the current stage is announced
    }

    [Fact]
    public async Task StartReviewForm_HasTwoSteps_ASummary_AndKeepsTheOriginalFieldNames()
    {
        var html = await RenderAsync(Item(latestRev: 2));

        Assert.Contains("Record vendor transmittal", html);
        Assert.Contains("Step 1 of 2", html);
        Assert.Contains("Review summary", html);
        Assert.Contains("id=\"vdrStartForm\"", html);

        foreach (var name in new[] { "vdrItemId", "revisionNo", "vendorTransmittalNo", "vendorActualIssuedDate", "purposeOfIssueCode", "disciplineIds" })
            Assert.Contains($"name=\"{name}\"", html);

        Assert.Matches(@"<input type=""hidden"" name=""revisionNo"" value=""3"" />", html);   // latest 2 -> next 3, still posted
        Assert.DoesNotContain("name=\"revisionNo\" type=\"number\"", html);                    // no longer an editable number box
        Assert.Matches(@"<input id=""vdrTransmittal""[^>]*\brequired\b", html);
    }

    [Fact]
    public async Task FirstSubmission_StartsAtRevisionZero()
    {
        var html = await RenderAsync(Item(latestRev: null));
        Assert.Matches(@"name=""revisionNo"" value=""0""", html);
    }

    [Fact]
    public async Task PurposeOptions_AreLabelled_AndLegacyCodesRemain()
    {
        var html = await RenderAsync(Item());

        Assert.Contains(">IFA: Issued for approval<", html);
        Assert.Contains(">IFR: Issued for review<", html);
        Assert.Contains(">IFI: Issued for information<", html);
        Assert.Contains("value=\"AB\"", html);
        Assert.Contains("value=\"AFD\"", html);
    }

    [Fact]
    public async Task Disciplines_AreInACollapsedPicker_WithAnAccessibleSummary()
    {
        var html = await RenderAsync(Item());

        Assert.Matches(@"data-bs-target=""#vdrDiscPicker""[^>]*aria-expanded=""false""", html);
        Assert.Matches(@"id=""vdrDiscPicker""", html);
        Assert.Contains("data-name=\"Electrical\"", html);
        Assert.Contains("data-name=\"Process\"", html);
        Assert.Matches(@"id=""vdrDiscSummary"" aria-live=""polite""", html);
        Assert.Contains("None selected", html);
    }

    [Fact]
    public async Task StartButton_IsServerEnabled_AndExplainsItselfThroughAHiddenReason()
    {
        var html = await RenderAsync(Item());

        // JavaScript disables it until a transmittal is typed; without JavaScript the form still submits and the server validates
        Assert.DoesNotMatch(@"id=""vdrStartBtn""[^>]*\bdisabled\b", html);
        Assert.Matches(@"id=""vdrStartBtn""[^>]*aria-describedby=""vdrWhyDisabled""", html);
        Assert.Matches(@"id=""vdrWhyDisabled""[^>]*\bhidden\b", html);
        Assert.Contains("Enter the vendor transmittal to enable this action.", html);

        // the harness renders without a layout, so the Scripts section is checked in the source: enable/disable on input, summary on change
        var root = Environment.GetEnvironmentVariable("PEMS_REPO_ROOT") ?? FindRoot();
        var source = File.ReadAllText(Path.Combine(root, "PEMS.Web", "Views", "VDR", "Details.cshtml"));
        var scripts = source.Substring(source.IndexOf("@section Scripts", StringComparison.Ordinal));
        Assert.Contains("getElementById('vdrStartBtn')", scripts);
        Assert.Contains("button.disabled = !ok", scripts);
        Assert.Contains("input.addEventListener('input', syncStart)", scripts);
        Assert.Contains("addEventListener('change', syncDisciplines)", scripts);
    }

    [Fact]
    public async Task RunningCycle_ShowsTheStageActions_NotTheStartForm()
    {
        var html = await RenderAsync(Item("LeadReview"), running: true, stage: "LeadReview");

        Assert.DoesNotContain("id=\"vdrStartForm\"", html);
        Assert.Contains("name=\"reviewCategory\"", html);                 // the final-decision form is still there
        Assert.Contains("name=\"clientCommentSheetNo\"", html);
        Assert.Matches(@"class=""vs-step vs-step--current"" aria-current=""step""", html);
    }

    [Fact]
    public void EveryNewVdrKey_ExistsInBothLanguages()
    {
        var root = Environment.GetEnvironmentVariable("PEMS_REPO_ROOT") ?? FindRoot();
        var view = File.ReadAllText(Path.Combine(root, "PEMS.Web", "Views", "VDR", "Details.cshtml"));
        var keys = Regex.Matches(view, "Localizer\\[\"(Vd_(?:Breadcrumb|VendorLabel|ReviewStages|Step1[A-Za-z]*|TransmittalHelp|IssuedDateHelp|PoiFull|SelectPurpose|PoiOpt_[A-Z]+|PoiHelp|SystemSetRev|Step2Title|Optional|NoDiscSelected|DiscSelected|ChooseDisciplines|ReviewSummary|Reviewers|None|EnterTransmittalToEnable))\"\\]")
            .Select(m => m.Groups[1].Value).ToHashSet();
        Assert.True(keys.Count >= 20, "expected the new keys to be discovered");

        foreach (var file in new[] { "SharedResource.fa.resx", "SharedResource.resx" })
        {
            var text = File.ReadAllText(Path.Combine(root, "PEMS.Web", "Resources", file));
            var missing = keys.Where(k => !text.Contains("name=\"" + k + "\"")).ToList();
            Assert.True(missing.Count == 0, file + " is missing: " + string.Join(", ", missing));
        }
    }

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PEMS.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found; set PEMS_REPO_ROOT");
    }
}
