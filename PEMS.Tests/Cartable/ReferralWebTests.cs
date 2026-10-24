using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using PEMS.Application.DTOs;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;
using PEMS.Tests.Web;
using PEMS.Web.Controllers;
using PEMS.Web.Navigation;
using PEMS.Web.Security;

namespace PEMS.Tests.Cartable;

/// <summary>صفحه‌ی نوشتن ارجاع، Details (گردش پیام‌ها، دکمه‌های پاسخ/ارجاع)، اختیار تاییدکننده‌ی تعیین‌شده، و حذف «برچسب‌ها» از منو و هدر سراسری صفحه.</summary>
public class ReferralWebTests
{
    private sealed class User : ICurrentUserService
    {
        public int? UserId { get; init; }
        public string? UserName => "u" + (UserId - 100);
        public string? FullName => "User";
        public bool IsAuthenticated => true;
        public IReadOnlyList<string> Roles { get; init; } = new[] { "Staff" };
    }

    private static string Web(params string[] parts) => File.ReadAllText(Path.Combine(new[] { Environment.GetEnvironmentVariable("PEMS_REPO_ROOT") ?? FindRoot(), "PEMS.Web" }.Concat(parts).ToArray()));
    private static string FindRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "PEMS.Web"))) dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("repo root not found; set PEMS_REPO_ROOT");
    }

    // ------------------------------------------------------------------ composer in referral mode
    private static async Task<string> RenderCompose(bool referral)
    {
        var c = new LetterComposeController(null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!)
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        var input = new LetterComposeInput
        {
            ParentLetterId = 5, IsReferral = referral, LetterType = "Internal", Subject = "Pump P-101 datasheet revision", Body = "<p></p><blockquote><p>old</p></blockquote>",
            Category = referral ? "Instruction" : null, ApprovalRequired = false
        };
        var model = new LetterComposeViewModel
        {
            Input = input, ParentLetterId = 5, ParentNumber = "IN-5", ParentSubject = "Pump P-101 datasheet revision", IsReferral = referral,
            Sender = new LetterSenderBlockDto { FullName = "Sara" },
            Approvers = new() { new ApproverOptionDto { UserId = 2, FullName = "Boss", Group = "Manager", ManagerLevel = 1, Position = "Manager", Department = "Eng" }, new ApproverOptionDto { UserId = 3, FullName = "Staff Joe", Group = "Other" } }
        };
        var result = new ViewResult { ViewData = new Microsoft.AspNetCore.Mvc.ViewFeatures.ViewDataDictionary(c.ViewData) { Model = model } };
        return await RazorViewRenderer.RenderAsync(c, result, "~/Views/LetterCompose/Compose.cshtml");
    }

    [Fact]
    public async Task ComposeInReferralMode_LocksTheSubject_HidesApprovalAndCategory_AndOffersSwitchingToReply()
    {
        var html = await RenderCompose(referral: true);

        Assert.Matches(@"<input id=""lcSubject""[^>]*readonly", html);
        Assert.Contains("cannot be changed", html);                       // the explanation of the fixed subject
        Assert.Matches(@"<section class=""lc-card"" aria-labelledby=""lcApprTitle"" hidden", html);
        Assert.Matches(@"<div class=""lc-field"" hidden>\s*<label for=""lcCategory""", html);
        Assert.Contains("Referral instruction", html);
        Assert.Contains("Send referral", html);
        Assert.Contains("Action deadline", html);
        Assert.Contains("\"isReferral\":true", html);
        Assert.Contains("asp-route-replyTo", Web("Views", "LetterCompose", "Compose.cshtml"));   // links carry no href in the harness; the target is checked in source
        Assert.DoesNotContain("lcSubjectLen", html);                      // no 80-char counter for a fixed subject
    }

    [Fact]
    public async Task ComposeInReplyMode_StillEditsTheSubject_AndGroupsApproversByOrgChart()
    {
        var html = await RenderCompose(referral: false);

        Assert.DoesNotMatch(@"<input id=""lcSubject""[^>]*readonly", html);
        Assert.Contains("lcSubjectLen", html);
        Assert.Contains("\"isReferral\":false", html);
        var managers = Regex.Match(html, "<optgroup label=\"Your managers [^>]*>(.*?)</optgroup>", RegexOptions.Singleline).Groups[1].Value;
        Assert.Contains("value=\"2\"", managers);
        Assert.Contains("Boss", managers);
        Assert.Contains("Manager", managers);
        Assert.Contains("Eng", managers);                                  // position and department are shown next to the name
        var others = Regex.Match(html, "<optgroup label=\"Other staff\">(.*?)</optgroup>", RegexOptions.Singleline).Groups[1].Value;
        Assert.Contains("value=\"3\"", others);
        Assert.Contains("Staff Joe", others);
        Assert.True(html.IndexOf("Your managers", StringComparison.Ordinal) < html.IndexOf("Other staff", StringComparison.Ordinal));   // managers first
        Assert.DoesNotContain("Holders of the approve permission", html);   // empty groups are not rendered
    }

    // ------------------------------------------------------------------ Details
    private static async Task<string> RenderDetails(string status, List<ConversationMessageDto>? conversation, bool currentApprover = false)
    {
        var c = new LetterController(null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!)
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        c.ViewBag.Departments = new List<object>(); c.ViewBag.AllTags = new List<TagDto>();
        c.ViewBag.Timeline = new List<LetterTimelineEventDto>(); c.ViewBag.Thread = new List<ThreadMessageDto>();
        c.ViewBag.CanEditReturned = false; c.ViewBag.CanEditDraft = false; c.ViewBag.CanReview = false; c.ViewBag.IsCurrentApprover = currentApprover;
        c.ViewBag.Conversation = conversation;
        var model = new LetterDetailDto { Letter = new LetterDto { LetterId = 7, LetterNumber = "LTR-7", LetterType = "Internal", Subject = "Hello", Status = status, Priority = "Normal", ConfidentialityLevel = "Internal", LetterDate = new DateTime(2026, 3, 1) } };
        var result = new ViewResult { ViewData = new Microsoft.AspNetCore.Mvc.ViewFeatures.ViewDataDictionary(c.ViewData) { Model = model } };
        return await RazorViewRenderer.RenderAsync(c, result, "~/Views/Letter/Details.cshtml");
    }

    private static List<ConversationMessageDto> Flow() => new()
    {
        new() { LetterId = 1, LetterNumber = "IN-1", Kind = "Original", SenderName = "ACME", Body = "<p>Please revise the datasheet.</p>", LetterDate = new DateTime(2026, 2, 27), RecipientsSummary = "Sara" },
        new() { LetterId = 2, LetterNumber = "INT-2", Kind = "Referral", SenderName = "Sara", Body = "<p>Please check P-101</p>", LetterDate = new DateTime(2026, 2, 28), RecipientsSummary = "Joe", CcSummary = "Mina", AttachmentsCount = 1 },
        new() { LetterId = 7, LetterNumber = "LTR-7", Kind = "Reply", SenderName = "Joe", Body = "<p>Done</p>", LetterDate = new DateTime(2026, 3, 1), IsCurrent = true }
    };

    [Fact]
    public async Task Details_ShowsThePreviousMessagesWithTheirFullText_KindChipsAndAMarkerForThisLetter()
    {
        var html = await RenderDetails("Sent", Flow());

        Assert.Contains("id=\"conversationPanel\"", html);
        Assert.Contains("Message flow (3)", html);
        Assert.Contains("Please revise the datasheet.", html);
        Assert.Contains("Please check P-101", html);
        Assert.Matches(@"cv-kind cv-kind--original", html);
        Assert.Matches(@"cv-kind cv-kind--referral", html);
        Assert.Matches(@"<details open>", html);                        // previous messages open by default, text included
        Assert.Contains("→ Joe", html.Replace("&rarr;", "→"));
        Assert.Contains("Cc: Mina", html);
        Assert.Contains("This letter", html);
        Assert.Equal(1, Regex.Matches(html, "aria-current=\"true\"").Count);
        Assert.Contains("data-cv-toggle=\"open\"", html);
    }

    [Fact]
    public async Task Details_WithASingleMessage_HasNoConversationPanel_AndFallsBackToTheOldThreadOtherwise()
    {
        Assert.DoesNotContain("id=\"conversationPanel\"", await RenderDetails("Sent", Flow().Take(1).ToList()));
        Assert.DoesNotContain("id=\"conversationPanel\"", await RenderDetails("Sent", null));
    }

    [Fact]
    public async Task Details_OffersReplyAndReferralTogether_OnlyOnceTheLetterIsOut_AndReferralsOfPeopleGoThroughTheComposer()
    {
        var sent = await RenderDetails("Sent", null);
        var source = Web("Views", "Letter", "Details.cshtml");

        Assert.Contains("asp-route-replyTo=\"@l.LetterId\"", source);
        Assert.Contains("asp-route-referTo=\"@l.LetterId\"", source);
        Assert.Contains("Reply</a>", sent.Replace("</i> Reply", "Reply"));
        Assert.Matches(@">\s*Refer\s*</a>", sent);
        foreach (var pre in new[] { "Draft", "PendingApproval", "Rejected" })
        {
            var html = await RenderDetails(pre, null);
            Assert.DoesNotMatch(@">\s*Refer\s*</a>", html);
        }

        // the quick form is for departments only: no employee picker any more in the referral form
        var referralForm = Regex.Match(sent, "<form[^>]*>\\s*<input[^>]*name=\"letterId\"[^>]*/?>\\s*<div class=\"col-md-6\">\\s*<select name=\"referredToDepartmentId\".*?</form>", RegexOptions.Singleline).Value;
        Assert.NotEmpty(referralForm);
        Assert.DoesNotContain("referredToEmployeeId", referralForm);
        Assert.Contains("Refer to a person", sent);
    }

    [Fact]
    public async Task Details_ShowsApprovalButtonsToTheCurrentChainApprover_EvenWithoutTheModulePermission()
    {
        Assert.DoesNotContain("Return</button>", await RenderDetails("PendingApproval", null, currentApprover: false));
        var html = await RenderDetails("PendingApproval", null, currentApprover: true);
        Assert.Contains("asp-action=\"Approve\"", Web("Views", "Letter", "Details.cshtml"));
        Assert.Matches(@"data-bs-target=""#returnModal""", html);
        Assert.Matches(@"data-bs-target=""#rejectModal""", html);
    }

    // ------------------------------------------------------------------ designated approver authority
    private static (LetterController Ctl, PEMSDbContext Ctx) Controller(string name, int userId, bool allowAll = false)
    {
        var ctx = LetterTestWorld.NewContext("RefWeb_" + name);
        var org = new OrganizationSubordinateResolver(ctx);
        var letters = new LetterService(ctx, sanitizer: null!, knowledgeBaseService: null!);
        var c = new LetterController(letters, null!, ctx, new User { UserId = userId }, new FakePermissionService { AllowAll = allowAll }, null!, null!, new NoCache(), null!, null!, null!,
            cartable: new LetterCartableService(ctx, org)).WithTempData().WithUrlHelper();
        return (c, ctx);
    }

    private static Letter PendingLetter(PEMSDbContext ctx, params int[] approvers)
    {
        var l = new Letter { LetterId = 900, LetterType = "Internal", LetterNumber = "P-900", LetterDate = DateTime.Today, Subject = "s", Body = "<p>b</p>", Category = "Request", Status = "PendingApproval",
            ApprovalRequired = true, RegisteredById = 101, IsActive = true, ConfidentialityLevel = "Internal", CreatedDate = DateTime.Now };
        l.Recipients.Add(new LetterRecipient { RecipientType = "To", EmployeeId = 3 });
        ctx.Letters.Add(l); ctx.SaveChanges();
        var order = 1;
        foreach (var a in approvers) ctx.LetterApprovalSteps.Add(new LetterApprovalStep { LetterId = l.LetterId, StepOrder = order++, ApproverUserId = a, Status = "Pending" });
        ctx.SaveChanges();
        return l;
    }

    [Fact]
    public async Task TheCurrentChainApprover_CanApproveWithoutTheModulePermission_OthersCannot()
    {
        var (asApprover, ctx) = Controller(nameof(TheCurrentChainApprover_CanApproveWithoutTheModulePermission_OthersCannot), userId: 102);
        PendingLetter(ctx, 102, 103);

        // the second approver's turn has not come: not allowed yet
        var (notYet, _) = (Controller("later", 103).Ctl, 0);
        Assert.IsType<ForbidResult>(await notYet.Approve(900, null));

        var ok = await asApprover.Approve(900, "fine");
        Assert.IsType<RedirectToActionResult>(ok);
        Assert.Equal("Letter approved", asApprover.TempData["Message"]);
        Assert.Equal("Approved", ctx.LetterApprovalSteps.Single(s => s.ApproverUserId == 102).Status);
    }

    [Fact]
    public async Task SomeoneOutsideTheChain_IsForbiddenFromApproveReturnAndReject()
    {
        var (c, ctx) = Controller(nameof(SomeoneOutsideTheChain_IsForbiddenFromApproveReturnAndReject), userId: 105);
        PendingLetter(ctx, 102);

        Assert.IsType<ForbidResult>(await c.Approve(900, null));
        Assert.IsType<ForbidResult>(await c.ReturnForRevision(900, "no"));
        Assert.IsType<ForbidResult>(await c.Reject(900, "no"));
        Assert.Equal("Pending", ctx.LetterApprovalSteps.Single().Status);
        Assert.Equal("PendingApproval", ctx.Letters.Single().Status);
    }

    [Fact]
    public async Task AnApproverWithTheModulePermission_StillWorks()
    {
        var (c, ctx) = Controller(nameof(AnApproverWithTheModulePermission_StillWorks), userId: 102, allowAll: true);
        PendingLetter(ctx, 102);
        Assert.IsType<RedirectToActionResult>(await c.ReturnForRevision(900, "please fix"));
        Assert.Equal("ReturnedForRevision", ctx.Letters.Single().Status);
    }

    // ------------------------------------------------------------------ sidebar + global header
    [Fact]
    public void TagsLink_IsGoneFromBothSidebars_AndItsStoredRowIsRetiredNotDeleted()
    {
        Assert.DoesNotContain(NavigationMenuDefaults.All, d => d.Key == "proj.tags");
        Assert.Contains("proj.tags", NavigationMenuDefaults.RetiredKeys);
        Assert.DoesNotContain("asp-controller=\"Tag\"", Web("Views", "Shared", "_SidebarMenu.cshtml"));
    }

    [Fact]
    public async Task SyncDefaults_DeactivatesRetiredKeys_WithoutDeletingThem_AndIsIdempotent()
    {
        var ctx = PemsDbContextFactory.CreateInMemory("RetiredNav_" + Guid.NewGuid());
        var menu = new NavigationMenuService(ctx, new MemoryCache(new MemoryCacheOptions()));
        ctx.NavigationMenuItems.Add(new NavigationMenuItem { Key = "proj.tags", Kind = "Link", TitleKey = "Tags", Icon = "bi-tags", Controller = "Tag", Action = "Index", IsActive = true, IsSystem = true });
        ctx.NavigationMenuItems.Add(new NavigationMenuItem { Key = "custom.tags", Kind = "Link", TitleKey = "Mine", Icon = "bi-tags", IsActive = true, IsSystem = false });
        await ctx.SaveChangesAsync();

        await menu.SyncDefaultsAsync(NavigationMenuDefaults.All, default, NavigationMenuDefaults.RetiredKeys);
        Assert.False(ctx.NavigationMenuItems.Single(x => x.Key == "proj.tags").IsActive);
        Assert.True(ctx.NavigationMenuItems.Single(x => x.Key == "custom.tags").IsActive);   // custom rows are never touched

        var count = ctx.NavigationMenuItems.Count();
        Assert.Equal(0, await menu.SyncDefaultsAsync(NavigationMenuDefaults.All, default, NavigationMenuDefaults.RetiredKeys));
        Assert.Equal(count, ctx.NavigationMenuItems.Count());
    }

    [Fact]
    public void TheGlobalLayout_RendersNoPageHeader()
    {
        var layout = Web("Views", "Shared", "_Layout.cshtml");
        Assert.DoesNotContain("_PageHeader", layout);
        Assert.DoesNotContain("pems-page-head", layout);
    }

    // ------------------------------------------------------------------ the big frame with the fixed action bar
    [Fact]
    public void Cartable_FillsTheWindow_AndKeepsTheActionBarAtTheBottomAlways()
    {
        var css = Web("wwwroot", "css", "pems-cartable.css");
        Assert.Contains(".ct-shell { height: calc(100dvh", css);
        Assert.Matches(@"\.ct-shell \.lp-tablewrap \.lp-bulkbar \{ order: 3; position: static; display: flex;", css);   // always shown, never overlaying the header
        Assert.Contains(".lp-tablewrap > form { display: contents; }", css);
        var view = Web("Views", "Letter", "Index.cshtml");
        Assert.Contains("data-empty=\"@Localizer[\"Ct_NoSelection\"]\"", view);
    }
}
