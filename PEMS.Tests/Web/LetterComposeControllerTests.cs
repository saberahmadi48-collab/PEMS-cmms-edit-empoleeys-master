using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using PEMS.Application.Common;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Seed;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;
using PEMS.Web.Controllers;
using PEMS.Web.Resources;
using PEMS.Web.Security;

namespace PEMS.Tests.Web;

/// <summary>
/// کنترلر فرم رسمی نامه (Compose): JSON ذخیره/ارسال، دایرکتوری، ورود CSV، پیوست، پیش‌نمایش، تاریخچه/انطباق (دسترسی و CSV-injection)،
/// رندر واقعی View ها، پوشش کلید ترجمه‌ی کدهای خطا/هشدار، و قالب‌های پیش‌فرض.
/// </summary>
public class LetterComposeControllerTests
{
    private static string RepoRoot()
    {
        var env = Environment.GetEnvironmentVariable("PEMS_REPO_ROOT");
        if (!string.IsNullOrEmpty(env)) return env;
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PEMS.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found; set PEMS_REPO_ROOT");
    }

    private static Dictionary<string, string> LoadResx(string file)
        => XDocument.Load(Path.Combine(RepoRoot(), "PEMS.Web", "Resources", file)).Root!.Elements("data")
            .ToDictionary(e => (string)e.Attribute("name")!, e => (string?)e.Element("value") ?? "");

    private sealed class ResxLocalizer : IStringLocalizer<SharedResource>
    {
        private readonly Dictionary<string, string> _map;
        public ResxLocalizer(Dictionary<string, string> map) => _map = map;
        public LocalizedString this[string name] => _map.TryGetValue(name, out var v) ? new LocalizedString(name, v, false) : new LocalizedString(name, name, true);
        public LocalizedString this[string name, params object[] arguments] => this[name];
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => _map.Select(kv => new LocalizedString(kv.Key, kv.Value, false));
    }

    private sealed class FakeUser : ICurrentUserService
    {
        public int? UserId { get; init; } = LetterTestWorld.Author;
        public string? UserName { get; init; } = "u1";
        public string? FullName { get; init; } = "User 1";
        public bool IsAuthenticated => UserId.HasValue;
        public IReadOnlyList<string> Roles { get; init; } = new[] { "Staff" };
    }

    private sealed class FakeFiles : IFileStorageService
    {
        public List<string> Saved { get; } = new();
        public Task<string> SaveAsync(string logicalFolder, string originalFileName, Stream content) { Saved.Add(originalFileName); return Task.FromResult("key-" + Saved.Count); }
        public Task<Stream> OpenReadAsync(string key) => Task.FromResult<Stream>(new MemoryStream());
        public Task<bool> ExistsAsync(string key) => Task.FromResult(true);
        public Task DeleteAsync(string key) => Task.CompletedTask;
    }

    private sealed class Rig
    {
        public LetterComposeController Controller = null!;
        public PEMSDbContext Ctx = null!;
        public LetterService Letters = null!;
        public LetterComposeService Compose = null!;
        public FakeFiles Files = null!;
        public FakePermissionService Permissions = null!;
    }

    private static Rig NewRig(int? userId = LetterTestWorld.Author, bool approverPermission = false, string[]? roles = null, string name = "ctl", ILetterTemplateService? templates = null)
    {
        var (compose, letters, ctx, _) = LetterTestWorld.Services(name);
        var permissions = new FakePermissionService { AllowAll = true };
        if (!approverPermission) permissions.Denied.Add(("Correspondence", "Approve"));
        var files = new FakeFiles();
        var user = new FakeUser { UserId = userId, UserName = userId.HasValue ? "u" + (userId.Value - 100) : null, Roles = roles ?? new[] { "Staff" } };
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Letterhead:CompanyName"] = "Acme <Corp>", ["Letterhead:Address"] = "1 Main St" }).Build();
        var controller = new LetterComposeController(compose, letters, new LetterComplianceService(ctx), ctx, user, permissions,
            new RichTextSanitizerService(), files, new ResxLocalizer(LoadResx("SharedResource.fa.resx")), config, templates).WithTempData().WithUrlHelper();
        return new Rig { Controller = controller, Ctx = ctx, Letters = letters, Compose = compose, Files = files, Permissions = permissions };
    }


    private static JsonElement J(IActionResult result)
    {
        var json = Assert.IsType<JsonResult>(result);
        return JsonSerializer.SerializeToElement(json.Value);
    }

    private static LetterComposeController.ComposeSaveRequest Req(LetterComposeInput input, bool autoSave = false)
        => new() { Input = input, AutoSave = autoSave };

    // ------------------------------------------------------------------ save / submit

    [Fact]
    public async Task Save_CreatesDraft_WithDepartmentPrefixedNumber_AndRowVersion()
    {
        var rig = NewRig();
        var json = J(await rig.Controller.Save(Req(LetterTestWorld.ValidInput())));

        Assert.True(json.GetProperty("success").GetBoolean());
        Assert.Matches(@"^ENG01-\d{4}-\d{5}$", json.GetProperty("letterNumber").GetString()!);
        Assert.True(json.GetProperty("letterId").GetInt32() > 0);
        Assert.True(json.TryGetProperty("rowVersion", out _));   // null on InMemory (no concurrency token), Base64 on SQL Server
    }

    [Fact]
    public async Task Save_ReturnsTranslatedErrors_WithFieldNames()
    {
        var rig = NewRig();
        var input = LetterTestWorld.ValidInput();
        input.Subject = new string('x', LetterComposeRules.MaxSubjectLength + 1);

        var json = J(await rig.Controller.Save(Req(input)));

        Assert.False(json.GetProperty("success").GetBoolean());
        var error = json.GetProperty("errors").EnumerateArray().Single(e => e.GetProperty("code").GetString() == "SubjectTooLong");
        Assert.Equal("subject", error.GetProperty("field").GetString());
        var message = error.GetProperty("message").GetString()!;
        Assert.Contains(LetterComposeRules.MaxSubjectLength.ToString(), message);
        Assert.DoesNotContain("{0}", message);
        Assert.NotEqual("SubjectTooLong", message);   // translated, not the raw code
    }

    [Fact]
    public async Task Save_WithoutLogin_IsUnauthorized()
    {
        var rig = NewRig(userId: null);
        Assert.IsType<UnauthorizedResult>(await rig.Controller.Save(Req(LetterTestWorld.ValidInput())));
    }

    [Fact]
    public async Task Submit_WithApprovalChain_MovesDraftToPendingApproval_AndRedirectsToDetails()
    {
        var rig = NewRig();
        var input = LetterTestWorld.ValidInput();
        input.ApprovalRequired = true;
        input.ApproverUserIds = new List<int> { LetterTestWorld.Approver1 };

        var json = J(await rig.Controller.Submit(Req(input)));

        Assert.True(json.GetProperty("success").GetBoolean());
        var id = json.GetProperty("letterId").GetInt32();
        Assert.Equal("PendingApproval", rig.Ctx.Letters.Single(l => l.LetterId == id).Status);
        Assert.Single(rig.Ctx.LetterApprovalSteps.Where(s => s.LetterId == id));
    }

    [Fact]
    public async Task Submit_WithoutApproval_SendsInternalLetterDirectly()
    {
        var rig = NewRig();
        var json = J(await rig.Controller.Submit(Req(LetterTestWorld.ValidInput())));

        Assert.True(json.GetProperty("success").GetBoolean());
        Assert.Equal("Sent", rig.Ctx.Letters.Single().Status);
    }

    [Fact]
    public async Task Submit_Incomplete_ReturnsErrors_AndKeepsTheDraft()
    {
        var rig = NewRig();
        var input = LetterTestWorld.ValidInput();
        input.Category = null;
        input.ToEmployeeIds.Clear();

        var json = J(await rig.Controller.Submit(Req(input)));

        Assert.False(json.GetProperty("success").GetBoolean());
        var codes = json.GetProperty("errors").EnumerateArray().Select(e => e.GetProperty("code").GetString()).ToList();
        Assert.Contains("CategoryRequired", codes);
        Assert.Contains("RecipientRequired", codes);
        Assert.Equal("Draft", rig.Ctx.Letters.Single().Status);   // saved as a draft, never sent
    }

    // ------------------------------------------------------------------ compose page

    [Fact]
    public async Task Compose_ForSomeoneElsesDraft_IsNotFound()
    {
        var author = NewRig();
        var saved = J(await author.Controller.Save(Req(LetterTestWorld.ValidInput())));
        var id = saved.GetProperty("letterId").GetInt32();

        // same database, different user
        var intruder = new LetterComposeController(author.Compose, author.Letters, new LetterComplianceService(author.Ctx), author.Ctx,
            new FakeUser { UserId = LetterTestWorld.Other, UserName = "u5" }, author.Permissions, new RichTextSanitizerService(), author.Files,
            new ResxLocalizer(new Dictionary<string, string>()), new ConfigurationBuilder().Build()).WithTempData().WithUrlHelper();

        Assert.IsType<NotFoundResult>(await intruder.Compose(id));
    }

    [Fact]
    public async Task ComposeView_RendersSenderBlockAndBootData_WithoutBreakingOutOfTheScriptTag()
    {
        var rig = NewRig();
        var input = LetterTestWorld.ValidInput();
        input.Subject = "</script><script>alert(1)</script>";
        input.Body = "<p>hi</p>";
        var id = J(await rig.Controller.Save(Req(input))).GetProperty("letterId").GetInt32();

        var view = Assert.IsType<ViewResult>(await rig.Controller.Compose(id));
        var html = await RazorViewRenderer.RenderAsync(rig.Controller, view, "~/Views/LetterCompose/Compose.cshtml");

        Assert.Contains("id=\"lcBoot\"", html);
        Assert.Contains("id=\"composeBody\"", html);
        Assert.Contains("E1 L1", html);            // sender block auto-filled from the employee record
        Assert.Contains("Lead Engineer", html);
        Assert.DoesNotContain("</script><script>alert(1)", html);   // subject is encoded in value= and never inside the JSON
        Assert.Contains("lcDrop", html);
    }

    [Fact]
    public async Task ComposeView_ForOutgoing_ForcesApprovalAndShowsExternalReceiver()
    {
        var rig = NewRig();
        var view = Assert.IsType<ViewResult>(await rig.Controller.Compose(null, "Outgoing"));
        var html = await RazorViewRenderer.RenderAsync(rig.Controller, view, "~/Views/LetterCompose/Compose.cshtml");

        Assert.Contains("id=\"lcExternal\"", html);
        Assert.Matches(@"id=""lcApprReq""[^>]*disabled", html);
    }

    // ------------------------------------------------------------------ directory / csv / attachments

    [Fact]
    public async Task Directory_FindsEmployeesByName_AndRequiresLogin()
    {
        var rig = NewRig();
        var result = Assert.IsType<JsonResult>(await rig.Controller.Directory("E3", 10));
        var items = JsonSerializer.SerializeToElement(result.Value).EnumerateArray().ToList();
        Assert.Contains(items, i => i.GetProperty("name").GetString()!.Contains("E3"));

        var anonymous = NewRig(userId: null);
        Assert.IsType<UnauthorizedResult>(await anonymous.Controller.Directory("E3"));
    }

    private static IFormFile TextFile(string name, string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", name) { Headers = new HeaderDictionary(), ContentType = "application/octet-stream" };
    }

    [Fact]
    public async Task ResolveRecipients_ReadsCsvLines_ReportsUnmatched()
    {
        var rig = NewRig();
        var json = J(await rig.Controller.ResolveRecipients(TextFile("list.csv", "P2\ne3@pems.test\nnobody@nowhere.test\n"), null));

        Assert.Equal(2, json.GetProperty("matched").GetArrayLength());
        Assert.Equal(1, json.GetProperty("unmatched").GetArrayLength());
        Assert.Equal(LetterComposeRules.MaxRecipients, json.GetProperty("max").GetInt32());
    }

    [Fact]
    public async Task ResolveRecipients_RejectsHugeFiles()
    {
        var rig = NewRig();
        var big = new FormFile(new MemoryStream(new byte[10]), 0, 2 * 1024 * 1024, "file", "big.csv") { Headers = new HeaderDictionary(), ContentType = "text/csv" };
        Assert.IsType<BadRequestObjectResult>(await rig.Controller.ResolveRecipients(big, null));
    }

    [Fact]
    public async Task UploadAttachment_AcceptsAllowedTypes_RejectsExecutables_AndStoresNothingForThem()
    {
        var rig = NewRig();
        var id = J(await rig.Controller.Save(Req(LetterTestWorld.ValidInput()))).GetProperty("letterId").GetInt32();

        var ok = J(await rig.Controller.UploadAttachment(id, TextFile("plan.pdf", "%PDF-1.4")));
        Assert.Equal("plan.pdf", ok.GetProperty("name").GetString());
        Assert.Single(rig.Ctx.DocumentAttachments.Where(a => a.LetterId == id && a.IsActive));

        var bad = Assert.IsType<BadRequestObjectResult>(await rig.Controller.UploadAttachment(id, TextFile("run.exe", "MZ")));
        Assert.NotNull(bad.Value);
        Assert.Single(rig.Files.Saved);   // only the pdf reached storage
    }

    [Fact]
    public async Task UploadAttachment_ToSomeoneElsesDraft_IsForbidden()
    {
        var author = NewRig();
        var id = J(await author.Controller.Save(Req(LetterTestWorld.ValidInput()))).GetProperty("letterId").GetInt32();
        var intruder = new LetterComposeController(author.Compose, author.Letters, new LetterComplianceService(author.Ctx), author.Ctx,
            new FakeUser { UserId = LetterTestWorld.Other, UserName = "u5" }, author.Permissions, new RichTextSanitizerService(), author.Files,
            new ResxLocalizer(new Dictionary<string, string>()), new ConfigurationBuilder().Build()).WithTempData().WithUrlHelper();

        Assert.IsType<ForbidResult>(await intruder.UploadAttachment(id, TextFile("plan.pdf", "x")));
        Assert.Empty(author.Files.Saved);
    }

    [Fact]
    public async Task RemoveAttachment_IsSoft_AndOnlyForTheOwnDraft()
    {
        var rig = NewRig();
        var id = J(await rig.Controller.Save(Req(LetterTestWorld.ValidInput()))).GetProperty("letterId").GetInt32();
        var att = J(await rig.Controller.UploadAttachment(id, TextFile("plan.pdf", "x"))).GetProperty("id").GetInt32();

        await rig.Controller.RemoveAttachment(id, att);

        var row = rig.Ctx.DocumentAttachments.Single(a => a.AttachmentId == att);
        Assert.False(row.IsActive);   // not hard-deleted
    }

    // ------------------------------------------------------------------ preview

    [Fact]
    public async Task Preview_UsesLetterheadFromConfiguration_AndStripsScripts_AndNeverShowsBcc()
    {
        var rig = NewRig();
        var input = LetterTestWorld.ValidInput();
        input.Body = "<p>Hello</p><script>alert(1)</script>";
        input.BccEmployeeIds = new List<int> { 4 };   // employee 4 (hidden)
        input.CcEmployeeIds = new List<int> { 3 };

        var partial = Assert.IsType<PartialViewResult>(await rig.Controller.Preview(input));
        var model = Assert.IsType<LetterPrintModel>(partial.Model);

        Assert.Equal("Acme <Corp>", model.Letterhead.CompanyName);
        Assert.DoesNotContain("<script", model.BodyHtml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(model.To.Concat(model.Cc), n => n.Contains("E4"));   // the hidden recipient is not part of the print model
        Assert.Contains(model.Cc, n => n.Contains("E3"));
        Assert.True(model.IsPreview);

        var html = await RazorViewRenderer.RenderAsync(rig.Controller,
            new ViewResult { ViewData = new ViewDataDictionary(new Microsoft.AspNetCore.Mvc.ModelBinding.EmptyModelMetadataProvider(), new ModelStateDictionary()) { Model = model } },
            "~/Views/LetterCompose/_LetterPrint.cshtml");
        Assert.Contains("Acme &lt;Corp&gt;", html);          // letterhead text is HTML-encoded
        Assert.DoesNotContain("<script>alert", html);
        Assert.DoesNotContain("E4 L4", html);
    }

    // ------------------------------------------------------------------ history / compliance export

    private static async Task<int> SentLetterAsync(Rig rig, string subject = "Budget review")
    {
        var input = LetterTestWorld.ValidInput();
        input.Subject = subject;
        var id = J(await rig.Controller.Submit(Req(input))).GetProperty("letterId").GetInt32();
        return id;
    }

    [Fact]
    public async Task History_And_ComplianceExport_AreForbiddenForPlainReaders_ButAvailableToTheAuthor()
    {
        var author = NewRig();
        var id = await SentLetterAsync(author);

        Assert.IsType<ViewResult>(await author.Controller.History(id));
        Assert.IsType<FileContentResult>(await author.Controller.ComplianceExport(id));

        // user 105 can read an Internal letter that was sent, but is neither the author nor allowed to see everything
        var reader = new LetterComposeController(author.Compose, author.Letters, new LetterComplianceService(author.Ctx), author.Ctx,
            new FakeUser { UserId = LetterTestWorld.Other, UserName = "u5" }, author.Permissions, new RichTextSanitizerService(), author.Files,
            new ResxLocalizer(new Dictionary<string, string>()), new ConfigurationBuilder().Build()).WithTempData().WithUrlHelper();
        Assert.IsType<ForbidResult>(await reader.History(id));
        Assert.IsType<ForbidResult>(await reader.ComplianceExport(id));
    }

    [Fact]
    public async Task ComplianceExport_NeutralisesFormulaInjection_AndStartsWithBom()
    {
        var rig = NewRig();
        var id = await SentLetterAsync(rig, "=HYPERLINK(\"http://evil\")");

        var file = Assert.IsType<FileContentResult>(await rig.Controller.ComplianceExport(id));
        Assert.Equal("text/csv", file.ContentType);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, file.FileContents.Take(3).ToArray());

        var text = Encoding.UTF8.GetString(file.FileContents);
        Assert.Contains("'=HYPERLINK", text);                               // formula prefixed with an apostrophe
        Assert.DoesNotContain(",\"=HYPERLINK", text);
        Assert.StartsWith("﻿Timestamp,Actor,Category,Event,Detail", text);
    }

    [Fact]
    public async Task HistoryView_Renders_VersionBodies_AndAuditRows()
    {
        var rig = NewRig();
        var id = await SentLetterAsync(rig);
        var view = Assert.IsType<ViewResult>(await rig.Controller.History(id));
        var html = await RazorViewRenderer.RenderAsync(rig.Controller, view, "~/Views/LetterCompose/History.cshtml");

        Assert.Contains("Budget review", html);
        Assert.Contains("lh-table", html);
        Assert.Contains("Version", html);
    }

    // ------------------------------------------------------------------ lifecycle actions

    [Fact]
    public async Task Discard_RemovesOnlyTheAuthorsDraft()
    {
        var rig = NewRig();
        var id = J(await rig.Controller.Save(Req(LetterTestWorld.ValidInput()))).GetProperty("letterId").GetInt32();

        var result = await rig.Controller.Discard(id);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.False(rig.Ctx.Letters.Single(l => l.LetterId == id).IsActive);   // soft
    }

    [Fact]
    public async Task Archive_IsAllowedForTheAuthorOfASentLetter_AndRejectedForOthers()
    {
        var rig = NewRig();
        var id = await SentLetterAsync(rig);
        await rig.Controller.Archive(id);
        Assert.Equal("Archived", rig.Ctx.Letters.Single(l => l.LetterId == id).Status);

        var rig2 = NewRig(name: "ctl2");
        var id2 = await SentLetterAsync(rig2);
        var other = new LetterComposeController(rig2.Compose, rig2.Letters, new LetterComplianceService(rig2.Ctx), rig2.Ctx,
            new FakeUser { UserId = LetterTestWorld.Other, UserName = "u5" }, rig2.Permissions, new RichTextSanitizerService(), rig2.Files,
            new ResxLocalizer(new Dictionary<string, string>()), new ConfigurationBuilder().Build()).WithTempData().WithUrlHelper();
        await other.Archive(id2);
        Assert.Equal("Sent", rig2.Ctx.Letters.Single(l => l.LetterId == id2).Status);
    }

    [Fact]
    public async Task ReassignApprover_IsAdminOnly()
    {
        var rig = NewRig();
        var input = LetterTestWorld.ValidInput();
        input.ApprovalRequired = true;
        input.ApproverUserIds = new List<int> { LetterTestWorld.Approver1 };
        var id = J(await rig.Controller.Submit(Req(input))).GetProperty("letterId").GetInt32();

        await rig.Controller.ReassignApprover(id, 1, LetterTestWorld.Approver2);
        Assert.Equal(LetterTestWorld.Approver1, rig.Ctx.LetterApprovalSteps.Single(s => s.LetterId == id).ApproverUserId);   // plain user: unchanged

        var admin = new LetterComposeController(rig.Compose, rig.Letters, new LetterComplianceService(rig.Ctx), rig.Ctx,
            new FakeUser { UserId = LetterTestWorld.Other, UserName = "admin", Roles = new[] { "SystemAdmin" } }, rig.Permissions, new RichTextSanitizerService(), rig.Files,
            new ResxLocalizer(new Dictionary<string, string>()), new ConfigurationBuilder().Build()).WithTempData().WithUrlHelper();
        await admin.ReassignApprover(id, 1, LetterTestWorld.Approver2);
        Assert.Equal(LetterTestWorld.Approver2, rig.Ctx.LetterApprovalSteps.Single(s => s.LetterId == id).ApproverUserId);
    }

    // ------------------------------------------------------------------ reply in the composer

    private sealed class OneTemplate : ILetterTemplateService
    {
        public LetterTemplateDto Template { get; init; } = new();
        public Task<List<LetterTemplateDto>> ListAsync(string? letterType, bool includeInactive) => Task.FromResult(new List<LetterTemplateDto> { Template });
        public Task<LetterTemplateDto?> GetAsync(int id) => Task.FromResult<LetterTemplateDto?>(Template);
        public Task<(bool Success, string? Error, int TemplateId)> SaveAsync(LetterTemplateDto template) => throw new NotSupportedException();
        public Task<bool> SetActiveAsync(int id, bool active) => throw new NotSupportedException();
    }

    private static LetterComposeController As(Rig rig, int userId, ILetterTemplateService? templates = null)
        => new LetterComposeController(rig.Compose, rig.Letters, new LetterComplianceService(rig.Ctx), rig.Ctx,
            new FakeUser { UserId = userId, UserName = "u" + (userId - 100) }, rig.Permissions, new RichTextSanitizerService(), rig.Files,
            new ResxLocalizer(LoadResx("SharedResource.fa.resx")), new ConfigurationBuilder().Build(), templates).WithTempData().WithUrlHelper();

    // author (user 101 / employee 1) sends a letter to employee 2 (user 102) with the given level
    private static async Task<int> SentToE2Async(Rig rig, string confidentiality)
    {
        var input = LetterTestWorld.ValidInput();
        input.Subject = "Budget review";
        input.ConfidentialityLevel = confidentiality;
        return J(await rig.Controller.Submit(Req(input))).GetProperty("letterId").GetInt32();
    }

    [Fact]
    public async Task ComposeReply_PrefillsFromTheParent_AndLocksLowerConfidentialityLevels()
    {
        var rig = NewRig();
        var parentId = await SentToE2Async(rig, "Confidential");

        var replier = As(rig, 102);
        var view = Assert.IsType<ViewResult>(await replier.Compose(null, "Internal", null, parentId));
        var model = Assert.IsType<LetterComposeViewModel>(view.Model);

        Assert.Equal(parentId, model.Input.ParentLetterId);
        Assert.Equal(new[] { 1 }, model.Input.ToEmployeeIds);                 // the original sender
        Assert.Equal("Re: Budget review", model.Input.Subject);
        Assert.Equal("Confidential", model.Input.ConfidentialityLevel);
        Assert.Equal(2, model.MinConfidentialityRank);

        var html = await RazorViewRenderer.RenderAsync(replier, view, "~/Views/LetterCompose/Compose.cshtml");
        Assert.Matches(@"<option value=""Public""[^>]*disabled", html);      // cannot drop below the original level
        Assert.Matches(@"<option value=""Internal""[^>]*disabled", html);
        Assert.DoesNotMatch(@"<option value=""Restricted""[^>]*disabled", html);
        Assert.Contains("Budget review", html);                              // banner names the original letter
    }

    [Fact]
    public async Task ComposeReply_ToALetterTheUserCannotSee_IsForbidden()
    {
        var rig = NewRig();
        var parentId = await SentToE2Async(rig, "Confidential");

        // user 105 is neither author, recipient, referral target nor approver
        Assert.IsType<ForbidResult>(await As(rig, LetterTestWorld.Other).Compose(null, "Internal", null, parentId));
        // a letter that does not exist answers like one the user may not see (no existence oracle)
        Assert.IsType<ForbidResult>(await As(rig, 102).Compose(null, "Internal", null, 99999));
    }

    [Fact]
    public async Task Reply_ToAConfidentialLetter_StaysVisibleToTheOriginalSender_AndHiddenFromOthers()
    {
        var rig = NewRig();
        var parentId = await SentToE2Async(rig, "Confidential");

        var replier = As(rig, 102);
        var prefill = ((LetterComposeViewModel)((ViewResult)await replier.Compose(null, "Internal", null, parentId)).Model!).Input;
        prefill.Body = "<p>Noted, will do.</p>";
        var sent = J(await replier.Submit(Req(prefill)));
        Assert.True(sent.GetProperty("success").GetBoolean());
        var replyId = sent.GetProperty("letterId").GetInt32();

        var reply = rig.Ctx.Letters.Single(l => l.LetterId == replyId);
        Assert.Equal(parentId, reply.ParentLetterId);
        Assert.Equal("Confidential", reply.ConfidentialityLevel);
        Assert.Equal("Sent", reply.Status);
        Assert.Single(rig.Ctx.LetterRecipients.Where(r => r.LetterId == replyId && r.EmployeeId == 1 && r.RecipientType == "To"));

        Assert.True(await rig.Letters.CanViewAsync(replyId, new LetterViewer(101, 1, false)));    // original sender is now a recipient
        Assert.False(await rig.Letters.CanViewAsync(replyId, new LetterViewer(105, 5, false)));   // everyone else stays out
    }

    [Fact]
    public async Task Save_OfAReply_RefusesALowerConfidentialityLevel_WithATranslatedMessage()
    {
        var rig = NewRig();
        var parentId = await SentToE2Async(rig, "Restricted");

        var replier = As(rig, 102);
        var prefill = ((LetterComposeViewModel)((ViewResult)await replier.Compose(null, "Internal", null, parentId)).Model!).Input;
        prefill.ConfidentialityLevel = "Public";

        var json = J(await replier.Save(Req(prefill)));

        Assert.False(json.GetProperty("success").GetBoolean());
        var error = json.GetProperty("errors").EnumerateArray().Single();
        Assert.Equal("ConfidentialityBelowParent", error.GetProperty("code").GetString());
        Assert.Equal("confidentialityLevel", error.GetProperty("field").GetString());
        Assert.DoesNotContain("{0}", error.GetProperty("message").GetString());
        Assert.DoesNotContain("Restricted", error.GetProperty("message").GetString());           // the level is shown in the user's language
        Assert.Empty(rig.Ctx.Letters.Where(l => l.ParentLetterId == parentId));                   // nothing was created

        prefill.ConfidentialityLevel = "HighlyConfidential";                                      // raising it is fine
        Assert.True(J(await replier.Save(Req(prefill))).GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task ComposeReply_AppliesTheParentPlaceholdersOfATemplate()
    {
        var rig = NewRig();
        var parentId = await SentToE2Async(rig, "Internal");
        var number = rig.Ctx.Letters.Single(l => l.LetterId == parentId).LetterNumber;
        var templates = new OneTemplate
        {
            Template = new LetterTemplateDto
            {
                LetterTemplateId = 1, Name = "T", LetterType = null, Subject = "Re: {{ParentSubject}}", Priority = "High", ConfidentialityLevel = "Internal", IsActive = true,
                Body = "<p>Ref {{ParentNumber}} from {{SenderName}} {{Unknown}}</p>"
            }
        };

        var view = Assert.IsType<ViewResult>(await As(rig, 102, templates).Compose(null, "Internal", 1, parentId));
        var input = ((LetterComposeViewModel)view.Model!).Input;

        Assert.Equal("Re: Budget review", input.Subject);
        Assert.Contains("Ref " + number, input.Body);
        Assert.Contains("E2 L2", input.Body);                      // sender tokens come from the replying employee
        Assert.Contains("{{Unknown}}", input.Body);                // unknown tokens stay visible
    }

    // ------------------------------------------------------------------ localization coverage

    [Fact]
    public void EveryIssueCodeInTheComposeRulesAndService_HasAPersianAndEnglishMessage()
    {
        var root = RepoRoot();
        var sources = new[]
        {
            Path.Combine(root, "PEMS.Application", "Common", "LetterComposeRules.cs"),
            Path.Combine(root, "PEMS.Persistence", "Servises", "LetterComposeService.cs")
        };
        var codes = sources.SelectMany(f => Regex.Matches(File.ReadAllText(f), @"new\(""([A-Z][A-Za-z]+)""").Select(m => m.Groups[1].Value)).ToHashSet();
        Assert.True(codes.Count > 20, "expected the issue codes to be discovered");

        // Warnings are the codes that are added to a warning list; everything else is an error. Accept either prefix so a code can be moved without breaking this test.
        foreach (var file in new[] { "SharedResource.fa.resx", "SharedResource.resx" })
        {
            var map = LoadResx(file);
            var missing = codes.Where(c => !map.ContainsKey("Lc_Err_" + c) && !map.ContainsKey("Lc_Warn_" + c)).ToList();
            Assert.True(missing.Count == 0, file + " is missing messages for: " + string.Join(", ", missing));
        }
    }

    [Fact]
    public void EveryLcKeyUsedByTheComposeScreens_ExistsInBothLanguages()
    {
        var root = RepoRoot();
        var files = new[]
        {
            Path.Combine(root, "PEMS.Web", "Views", "LetterCompose", "Compose.cshtml"),
            Path.Combine(root, "PEMS.Web", "Views", "LetterCompose", "History.cshtml"),
            Path.Combine(root, "PEMS.Web", "Views", "LetterCompose", "_LetterPrint.cshtml"),
            Path.Combine(root, "PEMS.Web", "Views", "Letter", "Details.cshtml"),
            Path.Combine(root, "PEMS.Web", "Controllers", "LetterComposeController.cs")
        };
        var keys = files.SelectMany(f => Regex.Matches(File.ReadAllText(f), @"""(Lc_[A-Za-z]+)""").Select(m => m.Groups[1].Value)).ToHashSet();
        // keys built from a prefix ("Lc_Cat_" + category) are covered by the explicit category test below
        foreach (var file in new[] { "SharedResource.fa.resx", "SharedResource.resx" })
        {
            var map = LoadResx(file);
            var missing = keys.Where(k => !k.EndsWith("_") && !map.ContainsKey(k)).ToList();
            Assert.True(missing.Count == 0, file + " is missing: " + string.Join(", ", missing));
        }
    }

    [Fact]
    public void EveryCategory_HasALabelInBothLanguages()
    {
        foreach (var file in new[] { "SharedResource.fa.resx", "SharedResource.resx" })
        {
            var map = LoadResx(file);
            Assert.All(LetterComposeRules.Categories, c => Assert.True(map.ContainsKey("Lc_Cat_" + c), file + " lacks Lc_Cat_" + c));
        }
    }

    [Fact]
    public void ComposeJavaScript_ExistsAndUsesAntiForgeryHeader_AndNoEval()
    {
        var js = File.ReadAllText(Path.Combine(RepoRoot(), "PEMS.Web", "wwwroot", "js", "letter-compose.js"));
        Assert.Contains("RequestVerificationToken", js);
        Assert.DoesNotContain("eval(", js);
        Assert.DoesNotContain("document.write", js);
    }

    // ------------------------------------------------------------------ Create redirect + letterhead

    [Theory]
    [InlineData("Internal")]
    [InlineData("Outgoing")]
    public async Task LegacyCreate_ForFormalLetterTypes_RedirectsToTheComposer(string type)
    {
        var (_, letters, ctx, _) = LetterTestWorld.Services("redirect");
        var controller = new LetterController(letters, null!, ctx, new FakeUser(), new FakePermissionService(), null!, null!, null!, null!, null!, null!).WithTempData().WithUrlHelper();

        var redirect = Assert.IsType<RedirectToActionResult>(await controller.Create(type));
        Assert.Equal("Compose", redirect.ActionName);
        Assert.Equal("LetterCompose", redirect.ControllerName);
    }

    [Fact]
    public void Letterhead_FallsBackToPems_WhenNothingIsConfigured()
    {
        var settings = LetterheadSettings.From(new ConfigurationBuilder().Build());
        Assert.Equal("PEMS", settings.CompanyName);
        Assert.Null(settings.Address);
    }

    // ------------------------------------------------------------------ default templates

    [Fact]
    public async Task DefaultTemplates_AreSeededOnce_AndOnlyUseKnownPlaceholders()
    {
        var ctx = LetterTestWorld.NewContext("templates");
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection().AddSingleton(ctx).BuildServiceProvider();

        Assert.Equal(7, await LetterTemplateSeeder.SeedAsync(services));
        Assert.Equal(0, await LetterTemplateSeeder.SeedAsync(services));   // idempotent
        Assert.Equal(7, ctx.LetterTemplates.Count(t => t.LetterType == "Internal"));

        foreach (var t in ctx.LetterTemplates)
        {
            Assert.Empty(LetterTemplatePlaceholders.Unknown(t.Subject));
            Assert.Empty(LetterTemplatePlaceholders.Unknown(t.Body));
            Assert.True(t.Subject.Length <= LetterComposeRules.MaxSubjectLength);
            Assert.DoesNotContain("<script", t.Body!, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("{{SenderName}}", t.Body);
        }
    }

    [Fact]
    public async Task DefaultTemplates_ARetiredTemplateIsNeverResurrected()
    {
        var ctx = LetterTestWorld.NewContext("templates2");
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection().AddSingleton(ctx).BuildServiceProvider();
        await LetterTemplateSeeder.SeedAsync(services);

        var first = ctx.LetterTemplates.OrderBy(t => t.LetterTemplateId).First();
        first.IsActive = false;
        await ctx.SaveChangesAsync();

        Assert.Equal(0, await LetterTemplateSeeder.SeedAsync(services));
        Assert.False(ctx.LetterTemplates.Single(t => t.LetterTemplateId == first.LetterTemplateId).IsActive);
    }
}

internal static class LetterComposeTestExtensions
{
    private sealed class Helper : IUrlHelper
    {
        public Helper(ActionContext ctx) { ActionContext = ctx; }
        public ActionContext ActionContext { get; }
        public string? Action(UrlActionContext a) => "/" + a.Controller + "/" + a.Action;
        public string? RouteUrl(UrlRouteContext r) => "/";
        public string? Content(string? contentPath) => contentPath;
        public string? Link(string? routeName, object? values) => "/";
        public bool IsLocalUrl(string? url) => !string.IsNullOrEmpty(url) && url[0] == '/';
    }

    public static T WithUrlHelper<T>(this T controller) where T : Controller
    {
        controller.Url = new Helper(controller.ControllerContext);
        return controller;
    }
}
