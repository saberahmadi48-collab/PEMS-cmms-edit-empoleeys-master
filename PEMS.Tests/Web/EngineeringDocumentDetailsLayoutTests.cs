using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Tests.TestSupport;
using PEMS.Web.Controllers;
using PEMS.Web.Security;

namespace PEMS.Tests.Web;

/// <summary>
/// صفحه‌ی جزئیات مدرک مهندسی (طراحی جدید): نمودار مراحل از Draft هم دیده می‌شود با خط «مرحله‌ی بعد»، کامنت‌ها/ترنسمیتال‌ها تب جدا دارند
/// (نه زیر همه‌ی تب‌ها)، Overview ستون کناری خلاصه دارد، و فیلدهای امضا خالی «Assign» می‌دهند.
/// View واقعی رندر می‌شود و Localizer متن انگلیسی resx را برمی‌گرداند؛ پس به همان متن اشاره می‌کنیم.
/// </summary>
public class EngineeringDocumentDetailsLayoutTests
{
    private sealed class FakeUser : ICurrentUserService
    {
        public int? UserId => 1;
        public string? UserName => "u";
        public string? FullName => "U";
        public bool IsAuthenticated => true;
        public IReadOnlyList<string> Roles => new[] { "Staff" };
    }

    private static async Task<string> RenderAsync(EngineeringDocumentDto doc, List<VDRCommentDto>? comments = null)
    {
        var (_, letters, ctx, _) = LetterTestWorld.Services("engdoc");
        var c = new LetterController(letters, null!, ctx, new FakeUser(), new FakePermissionService(), null!, null!, null!, null!, null!, null!).WithTempData();
        c.ViewBag.Comments = comments ?? new List<VDRCommentDto>();
        var result = new ViewResult { ViewData = new ViewDataDictionary(c.ViewData) { Model = doc } };
        return await RazorViewRenderer.RenderAsync(c, result, "~/Views/EngineeringDocuments/Details.cshtml");
    }

    private static EngineeringDocumentDto Doc(string status = "Draft") => new()
    {
        DocumentId = 7, DocumentNo = "J-85-PR-DSH-0910-1804", Title = "Process Data Sheet", DocumentType = "DSH", ProjectName = "Polymer Plant",
        DisciplineName = "Process", Status = status, RevisionNo = "0", Area = "Rubber"
    };

    [Fact]
    public async Task Draft_ShowsTheStepperAndTheNextStepLine()
    {
        var html = await RenderAsync(Doc("Draft"));

        Assert.Contains("Next step:", html);
        Assert.Contains("Submit for review to start the Check stage", html);
        Assert.Contains("pems-wf-diagram", html);   // the stepper partial is rendered even in Draft
    }

    [Theory]
    [InlineData("Submitted", "Waiting for the Check by the Eng. Supervisor")]
    [InlineData("Checked", "Waiting for Approval by the Eng. Manager")]
    [InlineData("Approved", "Ready to issue")]
    [InlineData("Issued", "Record the client response")]
    [InlineData("Rejected", "Revise the document and resubmit it")]
    [InlineData("Revise & Resubmit", "Revise the document and resubmit it")]
    public async Task EachWorkflowState_ExplainsItsNextStep(string status, string expectedText)
        => Assert.Contains(expectedText, await RenderAsync(Doc(status)));

    [Fact]
    public async Task Issued_WithAClientResponse_HasNoNextStepLine()
    {
        var doc = Doc("Issued");
        doc.ClientApprovalStatus = "Approved";
        Assert.DoesNotContain("Record the client response", await RenderAsync(doc));
    }

    [Fact]
    public async Task Tabs_AreInTheMockupOrder_WithTransmittalsAndCommentsAsOwnTabs()
    {
        var html = await RenderAsync(Doc());

        var order = new[] { "#tab-overview", "#tab-attachments", "#tab-revisions", "#tab-transmittals", "#tab-comments", "#tab-workflow", "#tab-correspondence" }
            .Select(t => html.IndexOf("data-bs-target=\"" + t + "\" type=\"button\"", StringComparison.Ordinal)).ToList();
        Assert.All(order, i => Assert.True(i >= 0, "a tab button is missing"));
        Assert.Equal(order.OrderBy(x => x), order);   // already ascending = the expected order

        Assert.Contains("id=\"tab-transmittals\"", html);
        Assert.Contains("id=\"tab-comments\"", html);
    }

    [Fact]
    public async Task CommentManagement_LivesInsideTheCommentsTab_NotBelowEveryTab()
    {
        var html = await RenderAsync(Doc());

        var commentsPane = html.IndexOf("id=\"tab-comments\"", StringComparison.Ordinal);
        var management = html.IndexOf("Comment Management", StringComparison.Ordinal);
        var modal = html.IndexOf("id=\"addEngCommentModal\"", StringComparison.Ordinal);
        var tabContentEnd = html.IndexOf("id=\"submitWorkflowModal\"", StringComparison.Ordinal);

        Assert.True(commentsPane >= 0 && management > commentsPane, "Comment Management must come after the comments pane opens");
        Assert.True(management < tabContentEnd, "and before the modals that follow the tab content");
        Assert.True(modal > management, "the add-comment modal stays available outside the pane");
    }

    [Fact]
    public async Task CommentsTabBadge_CountsOnlyOpenComments()
    {
        var comments = new List<VDRCommentDto>
        {
            new() { VDRCommentId = 1, CommentNo = "C1", CommentText = "a", ActionStatus = "OpenComment" },
            new() { VDRCommentId = 2, CommentNo = "C2", CommentText = "b", ActionStatus = "VendorResponsePending" },
            new() { VDRCommentId = 3, CommentNo = "C3", CommentText = "c", ActionStatus = "Closed" },
            new() { VDRCommentId = 4, CommentNo = "C4", CommentText = "d", ActionStatus = "NoActionRequired" }
        };

        var html = await RenderAsync(Doc(), comments);

        Assert.Matches(new Regex(@"data-bs-target=""#tab-comments""[^>]*>\s*<i[^>]*></i>\s*Comments\s*<span class=""badge bg-danger"">2<"), html);
        Assert.Matches(@"2 open.{1,12}2 closed", html);        // the overview card shows "2 open · 2 closed"
    }

    [Fact]
    public async Task OverviewSideColumn_OffersAddCommentAndUpload()
    {
        var html = await RenderAsync(Doc());

        Assert.Contains("col-lg-4", html);
        Assert.Matches(@"data-bs-target=""#addEngCommentModal""", html);
        Assert.Contains("No files yet", html);
        Assert.Contains("No comments yet", html);
    }

    [Fact]
    public async Task EmptySignOffFields_OfferAnAssignLink_UntilTheDocumentIsFrozenOrIssued()
    {
        var open = await RenderAsync(Doc("Draft"));
        Assert.Equal(2, Regex.Matches(open, @">Assign</a>").Count);      // prepared by + checked by

        var frozen = Doc("Draft"); frozen.IsFrozen = true;
        Assert.DoesNotContain(">Assign</a>", await RenderAsync(frozen));
        Assert.DoesNotContain(">Assign</a>", await RenderAsync(Doc("Issued")));

        var named = Doc("Draft"); named.PreparedBy = "Sara"; named.CheckedBy = "Ali";
        var html = await RenderAsync(named);
        Assert.DoesNotContain(">Assign</a>", html);
        Assert.Contains("Sara", html);
        Assert.Contains("Ali", html);
    }

    [Fact]
    public void EveryNewLocalizationKey_ExistsInBothLanguages()
    {
        var root = Environment.GetEnvironmentVariable("PEMS_REPO_ROOT") ?? FindRoot();
        var view = File.ReadAllText(Path.Combine(root, "PEMS.Web", "Views", "EngineeringDocuments", "Details.cshtml"));
        var keys = Regex.Matches(view, "Localizer\\[\"(Ed_(?:Next|Tab(?:Transmittals|Comments)|OpenComments|OpenClosed|NoCommentsHint|NoFilesHint|FilesCount|ViewAll|NotSet|Assign)[A-Za-z_]*)\"\\]")
            .Select(m => m.Groups[1].Value).ToHashSet();
        Assert.True(keys.Count >= 15, "expected the new keys to be discovered");

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
