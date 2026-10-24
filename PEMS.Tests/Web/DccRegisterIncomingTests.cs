using System.Dynamic;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using PEMS.Application.Common;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;
using PEMS.Web.Controllers;
using PEMS.Web.Models;
using PEMS.Web.Resources;
using PEMS.Web.Security;

namespace PEMS.Tests.Web;

/// <summary>صفحه‌ی ثبت مکاتبه‌ی وارده‌ی DCC: ورودی→DTO، خطاهای ترجمه‌شده‌ی فیلدی، آدرس‌های جستجو، مجوز، رندر View و پوشش ترجمه.</summary>
public class DccRegisterIncomingTests
{
    private static string RepoRoot()
    {
        var env = Environment.GetEnvironmentVariable("PEMS_REPO_ROOT");
        if (!string.IsNullOrEmpty(env)) return env;
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PEMS.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found; set PEMS_REPO_ROOT");
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { RepoRoot() }.Concat(parts).ToArray()));

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

    private sealed class FakeFiles : IFileStorageService
    {
        public int Count;
        public Task<string> SaveAsync(string logicalFolder, string originalFileName, Stream content) => Task.FromResult("key-" + (++Count));
        public Task<Stream> OpenReadAsync(string key) => Task.FromResult<Stream>(new MemoryStream());
        public Task<bool> ExistsAsync(string key) => Task.FromResult(true);
        public Task DeleteAsync(string key) => Task.CompletedTask;
    }

    private sealed record Rig(DCCController Controller, PEMSDbContext Ctx, IncomingCorrespondenceService Service);

    private static Rig NewRig(string name, bool persian = true, bool approver = false)
    {
        var ctx = LetterTestWorld.NewContext(name);
        ctx.Projects.Add(new Project { ProjectId = 1, ProjectCode = "JPC", ProjectName = "JPC Plant" });
        ctx.Disciplines.Add(new Discipline { DisciplineId = 1, Code = "ME", Name = "Mechanical", IsActive = true });
        ctx.SaveChanges();

        var service = new IncomingCorrespondenceService(ctx, new LetterNumberingService(ctx), new FakeFiles(), new FakeKnowledgeBaseService(), new LetterTestNotifications());
        var permissions = new FakePermissionService { AllowAll = true };
        if (!approver) permissions.Denied.Add(("Correspondence", "Approve"));
        var user = new FakeCurrentUserService { UserId = LetterTestWorld.Author, UserName = "u1", FullName = "User 1", Roles = new[] { "Staff" } };
        var localizer = new ResxLocalizer(LoadResx(persian ? "SharedResource.fa.resx" : "SharedResource.resx"));
        var controller = new DCCController(ctx, service, user, permissions, localizer).WithTempData().WithUrlHelper();
        return new Rig(controller, ctx, service);
    }

    private static IFormFile Upload(string name, byte[] bytes, string contentType = "application/pdf")
        => new FormFile(new MemoryStream(bytes), 0, bytes.Length, "files", name) { Headers = new HeaderDictionary(), ContentType = contentType };

    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.4 scan");

    private static IncomingRegisterRequest Request() => new()
    {
        ProjectId = 1,
        NewPartyName = "Acme Co",
        NewPartyType = "Client",
        SenderReference = "ACME-0452",
        DocumentDate = DateTime.Today.AddDays(-2).ToString("yyyy-MM-dd"),
        ReceivedDate = DateTime.Today.ToString("yyyy-MM-dd"),
        ReceiptChannel = "OfficialLetter",
        Subject = "Request for update",
        Priority = "High",
        Files = new List<IFormFile> { Upload("scan.pdf", Pdf) },
        FileRoles = new List<string> { "Original" }
    };

    private static JsonElement J(IActionResult result)
        => JsonSerializer.SerializeToElement(Assert.IsType<JsonResult>(result).Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));   // MVC's JSON is camelCase

    // ------------------------------------------------------------------ POST

    [Fact]
    public async Task Post_RegistersTheLetter_AndReturnsTheNumberAndDetailsUrl()
    {
        var rig = NewRig("post1");

        var json = J(await rig.Controller.RegisterIncoming(Request()));

        Assert.True(json.GetProperty("success").GetBoolean());
        Assert.Matches($@"^JPC-IN-{JalaliDate.Year(DateTime.Today)}-0001$", json.GetProperty("letterNumber").GetString()!);
        Assert.Equal("/Letter/Details", json.GetProperty("detailsUrl").GetString());
        var letter = rig.Ctx.Letters.Single();
        Assert.Equal("High", letter.Priority);
        Assert.Equal(LetterTestWorld.Author, letter.RegisteredById);
        Assert.Equal(1, rig.Ctx.DocumentAttachments.Count());
    }

    [Fact]
    public async Task Post_FieldErrors_AreTranslated_CarryTheFieldName_AndUse422()
    {
        var rig = NewRig("post2");
        var request = Request();
        request.Subject = " "; request.SenderReference = null; request.Files = null; request.FileRoles = null;

        var json = J(await rig.Controller.RegisterIncoming(request));

        Assert.False(json.GetProperty("success").GetBoolean());
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, rig.Controller.Response.StatusCode);
        var errors = json.GetProperty("errors").EnumerateArray().ToList();
        var subject = errors.Single(e => e.GetProperty("code").GetString() == "SubjectRequired");
        Assert.Equal("subject", subject.GetProperty("field").GetString());
        Assert.Equal("موضوع را وارد کنید.", subject.GetProperty("message").GetString());
        Assert.Contains(errors, e => e.GetProperty("field").GetString() == "files" && e.GetProperty("code").GetString() == "OriginalRequired");
        Assert.Contains(errors, e => e.GetProperty("field").GetString() == "senderReference");
        Assert.Empty(rig.Ctx.Letters);
    }

    [Theory]
    [InlineData("2026/10/21")]
    [InlineData("21-10-2026")]
    [InlineData("tomorrow")]
    public async Task Post_ADateThatIsNotIsoGregorian_IsReportedOnItsField(string bad)
    {
        var rig = NewRig("post3");
        var request = Request(); request.DocumentDate = bad;

        var json = J(await rig.Controller.RegisterIncoming(request));

        var error = json.GetProperty("errors").EnumerateArray().Single(e => e.GetProperty("code").GetString() == "DateInvalid");
        Assert.Equal("documentDate", error.GetProperty("field").GetString());
        Assert.Empty(rig.Ctx.Letters);
    }

    [Fact]
    public async Task Post_AMalformedLinksJson_IsAValidationError_NotAServerError()
    {
        var rig = NewRig("post4");
        var request = Request(); request.Links = "{not json";

        var json = J(await rig.Controller.RegisterIncoming(request));

        Assert.Contains(json.GetProperty("errors").EnumerateArray(), e => e.GetProperty("code").GetString() == "LinkInvalid");
    }

    [Fact]
    public async Task Post_LinksJson_IsParsedCaseInsensitively()
    {
        var rig = NewRig("post5");
        rig.Ctx.MaterialRequisitions.Add(new MaterialRequisition { MRNumber = "MR-1", ProjectId = 1 });
        await rig.Ctx.SaveChangesAsync();
        var request = Request(); request.Links = $"[{{\"linkType\":\"MaterialRequisition\",\"targetId\":{rig.Ctx.MaterialRequisitions.Single().MRId}}}]";

        var json = J(await rig.Controller.RegisterIncoming(request));

        Assert.True(json.GetProperty("success").GetBoolean());
        Assert.Equal("MR-1", rig.Ctx.LetterLinks.Single().TargetNumber);
    }

    [Fact]
    public async Task Post_AFileWithoutAMatchingRole_IsTreatedAsAnAttachment_AndTheLetterNeedsAnOriginal()
    {
        var rig = NewRig("post6");
        var request = Request(); request.FileRoles = new List<string>();

        var json = J(await rig.Controller.RegisterIncoming(request));

        Assert.Contains(json.GetProperty("errors").EnumerateArray(), e => e.GetProperty("code").GetString() == "OriginalRequired");
    }

    [Fact]
    public async Task Post_ADuplicate_ReturnsTheExistingLetterForTheWarning_AndCanBeConfirmed()
    {
        var rig = NewRig("post7");
        var first = Request();
        await rig.Controller.RegisterIncoming(first);
        var party = rig.Ctx.CorrespondentParties.Single();

        var again = Request(); again.NewPartyName = null; again.NewPartyType = null; again.CorrespondentPartyId = party.CorrespondentPartyId;
        var json = J(await rig.Controller.RegisterIncoming(again));

        Assert.False(json.GetProperty("success").GetBoolean());
        Assert.Equal(rig.Ctx.Letters.Single().LetterNumber, json.GetProperty("duplicate").GetProperty("letterNumber").GetString());

        var confirm = Request(); confirm.NewPartyName = null; confirm.NewPartyType = null; confirm.CorrespondentPartyId = party.CorrespondentPartyId; confirm.ConfirmDuplicate = true;
        Assert.True(J(await rig.Controller.RegisterIncoming(confirm)).GetProperty("success").GetBoolean());
        Assert.Equal(2, rig.Ctx.Letters.Count());
    }

    [Fact]
    public async Task Post_EnglishLocale_UsesTheEnglishMessages()
    {
        var rig = NewRig("post8", persian: false);
        var request = Request(); request.Subject = "";

        var json = J(await rig.Controller.RegisterIncoming(request));

        var message = json.GetProperty("errors").EnumerateArray().Single(e => e.GetProperty("code").GetString() == "SubjectRequired").GetProperty("message").GetString();
        Assert.Equal("Enter the subject.", message);
    }

    // ------------------------------------------------------------------ lookups

    [Fact]
    public async Task Lookups_ReturnJson_ForPartiesThreadPeopleLinksNumberAndDuplicate()
    {
        var rig = NewRig("look1");
        await rig.Controller.RegisterIncoming(Request());
        var partyId = rig.Ctx.CorrespondentParties.Single().CorrespondentPartyId;

        Assert.Equal("Acme Co", J(await rig.Controller.Parties("acme", 1)).EnumerateArray().Single().GetProperty("name").GetString());
        Assert.Equal(1, J(await rig.Controller.PartyThread(partyId)).GetProperty("total").GetInt32());
        Assert.IsType<NotFoundResult>(await rig.Controller.PartyThread(9999));
        Assert.Equal(7, J(await rig.Controller.People(null)).GetArrayLength());
        Assert.Equal(0, J(await rig.Controller.Links("Transmittal", null, 1)).GetArrayLength());
        Assert.Equal(0, J(await rig.Controller.OutgoingForReply(partyId)).GetArrayLength());

        var number = J(await rig.Controller.NextNumber(1, DateTime.Today.ToString("yyyy-MM-dd"))).GetProperty("number").GetString();
        Assert.EndsWith("-0002", number);

        var dup = J(await rig.Controller.CheckDuplicate(partyId, "acme-0452"));
        Assert.Equal(rig.Ctx.Letters.Single().LetterNumber, dup.GetProperty("letterNumber").GetString());
        Assert.IsType<JsonResult>(await rig.Controller.CheckDuplicate(partyId, "nope"));
        Assert.Null(((JsonResult)await rig.Controller.CheckDuplicate(partyId, "nope")).Value);
    }

    [Fact]
    public async Task PartyThread_HidesRestrictedLetters_FromUsersWithoutTheApprovePermission()
    {
        var rig = NewRig("look2", approver: false);
        var party = new CorrespondentParty { Name = "Acme Co", PartyType = "Client" };
        rig.Ctx.CorrespondentParties.Add(party);
        await rig.Ctx.SaveChangesAsync();
        rig.Ctx.Letters.Add(new Letter { LetterType = "Outgoing", LetterNumber = "OUT-R", Subject = "secret", Status = "Issued", ConfidentialityLevel = "Restricted", CorrespondentPartyId = party.CorrespondentPartyId, RegisteredById = LetterTestWorld.Other });
        await rig.Ctx.SaveChangesAsync();

        Assert.Equal(0, J(await rig.Controller.PartyThread(party.CorrespondentPartyId)).GetProperty("total").GetInt32());

        var allowed = NewRig("look3", approver: true);
        allowed.Ctx.CorrespondentParties.Add(new CorrespondentParty { Name = "Acme Co", PartyType = "Client" });
        await allowed.Ctx.SaveChangesAsync();
        var partyId = allowed.Ctx.CorrespondentParties.Single().CorrespondentPartyId;
        allowed.Ctx.Letters.Add(new Letter { LetterType = "Outgoing", LetterNumber = "OUT-R", Subject = "secret", Status = "Issued", ConfidentialityLevel = "Restricted", CorrespondentPartyId = partyId, RegisteredById = LetterTestWorld.Other });
        await allowed.Ctx.SaveChangesAsync();
        Assert.Equal(1, J(await allowed.Controller.PartyThread(partyId)).GetProperty("total").GetInt32());
    }

    // ------------------------------------------------------------------ permission

    [Fact]
    public void EveryRegistrationAndLookupAction_RequiresCorrespondenceCreate()
    {
        var actions = typeof(DCCController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.Name is "RegisterIncoming" or "Parties" or "PartyThread" or "OutgoingForReply" or "Links" or "People" or "NextNumber" or "CheckDuplicate").ToList();

        Assert.Equal(9, actions.Count);   // seven lookups + RegisterIncoming (GET and POST)
        foreach (var m in actions)
        {
            var attr = m.GetCustomAttribute<RequirePermissionAttribute>();
            Assert.True(attr != null, m.Name + " has no [RequirePermission]");
            var fields = typeof(RequirePermissionAttribute).GetFields(BindingFlags.NonPublic | BindingFlags.Instance).ToDictionary(f => f.Name, f => (string?)f.GetValue(attr));
            Assert.Equal("Correspondence", fields["_module"]);
            Assert.Equal("Create", fields["_action"]);
        }
    }

    [Fact]
    public void ThePostAction_IsProtectedAgainstForgery()
        => Assert.NotNull(typeof(DCCController).GetMethods().Single(m => m.Name == "RegisterIncoming" && m.GetCustomAttribute<HttpPostAttribute>() != null)
            .GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());

    // ------------------------------------------------------------------ dashboard

    [Fact]
    public async Task Dashboard_UnlinkedCount_IsTheRealCount_NotThePageSize_AndLinkedLettersAreExcluded()
    {
        var rig = NewRig("dash1");
        for (var i = 0; i < 20; i++)
            rig.Ctx.Letters.Add(new Letter { LetterType = "Incoming", LetterNumber = "IN-" + i, Subject = "s" + i, Status = "Registered", IsActive = true, LetterDate = DateTime.Today.AddDays(-i) });
        var linked = new Letter { LetterType = "Incoming", LetterNumber = "IN-L", Subject = "linked", Status = "Registered", IsActive = true };
        linked.Links.Add(new LetterLink { LinkType = "Transmittal", TargetId = 1, TargetNumber = "TR-1" });
        rig.Ctx.Letters.Add(linked);
        await rig.Ctx.SaveChangesAsync();

        var view = await rig.Controller.Index();

        Assert.IsType<ViewResult>(view);
        Assert.Equal(20, (int)rig.Controller.ViewBag.UnlinkedIncomingCount);
        Assert.Equal(15, ((IEnumerable<object>)rig.Controller.ViewBag.UnlinkedIncoming).Count());
    }

    // ------------------------------------------------------------------ view

    private static object Obj(params (string Name, object? Value)[] props)
    {
        IDictionary<string, object?> bag = new ExpandoObject();
        foreach (var (name, value) in props) bag[name] = value;
        return bag;
    }

    private static async Task<string> RenderPage(object? presetParty = null, object? replyTo = null)
    {
        var rig = NewRig("view" + Guid.NewGuid().ToString("N")[..6]);
        var c = rig.Controller;
        c.ViewBag.Projects = new List<object> { Obj(("ProjectId", 1), ("ProjectName", "JPC Plant"), ("ProjectCode", "JPC")) };
        c.ViewBag.Disciplines = new List<object> { Obj(("DisciplineId", 1), ("Name", "Mechanical")) };
        c.ViewBag.PresetParty = presetParty;
        c.ViewBag.ReplyTo = replyTo;
        return await RazorViewRenderer.RenderAsync(c, new ViewResult(), "~/Views/DCC/RegisterIncoming.cshtml");
    }

    [Fact]
    public async Task View_RendersTheTwoPaneForm_WithEveryControlTheScriptNeeds()
    {
        var html = await RenderPage();

        foreach (var id in new[] { "dcr", "dcrForm", "dcrParty", "dcrPartyList", "dcrNewParty", "dcrNewPartyType", "dcrProject", "dcrContract", "dcrRef", "dcrChannel",
                     "dcrSubject", "dcrDocDate", "dcrRecvDate", "dcrPriority", "dcrCategory", "dcrConf", "dcrDiscipline", "dcrReply", "dcrSummaryText", "dcrRespReq",
                     "dcrDueBlock", "dcrDue", "dcrOwner", "dcrOwnerList", "dcrCc", "dcrCcTags", "dcrInstruction", "dcrDrop", "dcrFileInput", "dcrFiles", "dcrLinkType",
                     "dcrLinkSearch", "dcrLinks", "dcrTabDocs", "dcrTabThread", "dcrPaneDocs", "dcrPaneThread", "dcrPreview", "dcrThread", "dcrKeepSender", "dcrSubmit",
                     "dcrSubmitNext", "dcrNumber", "dcrSummary", "dcrDuplicate", "dcrDuplicateConfirm", "dcrSaved", "dcrI18n" })
            Assert.Contains($"id=\"{id}\"", html);

        Assert.Contains("Register incoming correspondence", html);
        Assert.Contains("Sender organisation", html);
        Assert.Contains("name=\"__RequestVerificationToken\"", html);
        Assert.Contains("data-url-register=", html);
        Assert.Contains("data-url-thread=", html);
        Assert.Contains("data-url-letter=", html);
    }

    [Fact]
    public async Task View_OffersTheSameLookupValuesTheServerAccepts()
    {
        var html = await RenderPage();

        foreach (var p in LetterComposeRules.Priorities) Assert.Contains($"<option value=\"{p}\"", html);
        foreach (var c in LetterComposeRules.Categories) Assert.Contains($"<option value=\"{c}\"", html);
        foreach (var l in LetterComposeRules.Confidentialities) Assert.Contains($"<option value=\"{l}\"", html);
        foreach (var ch in IncomingCorrespondenceRules.Channels) Assert.Contains($"name=\"receiptChannel\" value=\"{ch}\"", html);
        foreach (var t in IncomingCorrespondenceRules.PartyTypes) Assert.Contains($"<option value=\"{t}\"", html);
        foreach (var t in IncomingCorrespondenceRules.LinkTypes) Assert.Contains($"<option value=\"{t}\"", html);
        Assert.Contains("<option value=\"1\">JPC Plant</option>", html);
        Assert.Contains("1405/01/01", html);   // the Jalali placeholder
    }

    [Fact]
    public async Task View_ShipsTheErrorAndLabelTextsTheScriptReads_AsEscapedJson()
    {
        var html = await RenderPage();
        var json = Regex.Match(html, "<script type=\"application/json\" id=\"dcrI18n\">(.*?)</script>", RegexOptions.Singleline).Groups[1].Value;

        var map = JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;
        Assert.Equal("Enter the subject.", map["Dcc_Err_SubjectRequired"]);
        Assert.Contains("{0}", map["Dcc_DuplicateFound"]);
        Assert.DoesNotContain("<", json);   // "<" is escaped so the block can never close the script element

        var jsKeys = Regex.Matches(Read("PEMS.Web", "wwwroot", "js", "dcc-register.js"), "T\\(\"(Dcc_[A-Za-z_]+)\"\\)").Select(m => m.Groups[1].Value).Distinct();
        Assert.All(jsKeys, k => Assert.True(map.ContainsKey(k), k + " is read by the script but not shipped to it"));
        foreach (var role in new[] { "Original", "Attachment", "EmailCopy" }) Assert.True(map.ContainsKey("Dcc_Role" + role));
        foreach (var type in IncomingCorrespondenceRules.LinkTypes) Assert.True(map.ContainsKey("Dcc_Link" + type));
    }

    [Fact]
    public async Task View_WithAPresetPartyAndAReplyTarget_EmbedsThemForTheScript()
    {
        var party = new PartySearchItemDto { Id = 7, Name = "Acme \"Co\"", PartyType = "Client" };
        var html = await RenderPage(party, Obj(("LetterId", 12), ("LetterNumber", "OUT-12"), ("Subject", "Our <request>")));

        var preset = Regex.Match(html, "data-preset-party=\"([^\"]*)\"").Groups[1].Value;
        Assert.Contains("&quot;id&quot;:7", System.Net.WebUtility.HtmlEncode(System.Net.WebUtility.HtmlDecode(preset)).Replace("&amp;quot;", "&quot;"));
        Assert.DoesNotContain("<request>", html);   // attribute values are encoded
        Assert.Contains("OUT-12", html);
    }

    // ------------------------------------------------------------------ translations

    private static IEnumerable<string> ErrorCodes()
    {
        var pattern = new Regex("(?:new\\(|new LetterComposeIssue\\()\\s*\"([A-Z][A-Za-z]+)\"|return \"(File[A-Za-z]+)\"");
        var sources = new[]
        {
            Read("PEMS.Application", "Common", "IncomingCorrespondenceRules.cs"),
            Read("PEMS.Persistence", "Servises", "IncomingCorrespondenceService.cs"),
            Read("PEMS.Web", "Controllers", "DCCController.cs")
        };
        return sources.SelectMany(s => pattern.Matches(s).Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value)).Distinct().OrderBy(x => x).ToList();
    }

    [Fact]
    public void EveryErrorCodeTheServerCanReturn_HasAnEnglishAndAPersianMessage()
    {
        var codes = ErrorCodes().ToList();
        Assert.True(codes.Count > 40, "code extraction found only " + codes.Count);

        foreach (var file in new[] { "SharedResource.resx", "SharedResource.fa.resx" })
        {
            var map = LoadResx(file);
            foreach (var code in codes)
            {
                Assert.True(map.TryGetValue("Dcc_Err_" + code, out var text), $"{file}: Dcc_Err_{code} is missing");
                Assert.False(string.IsNullOrWhiteSpace(text));
            }
        }
    }

    [Fact]
    public void EveryDccKey_ExistsInBothLanguages_WithMatchingPlaceholders()
    {
        var en = LoadResx("SharedResource.resx").Where(k => k.Key.StartsWith("Dcc_")).ToDictionary(k => k.Key, k => k.Value);
        var fa = LoadResx("SharedResource.fa.resx").Where(k => k.Key.StartsWith("Dcc_")).ToDictionary(k => k.Key, k => k.Value);

        Assert.True(en.Count > 100);
        Assert.Empty(en.Keys.Except(fa.Keys));
        Assert.Empty(fa.Keys.Except(en.Keys));
        foreach (var key in en.Keys)
            Assert.Equal(en[key].Contains("{0}"), fa[key].Contains("{0}"));
    }

    [Fact]
    public void EveryKeyTheViewAndScriptUse_IsDefined()
    {
        var view = Read("PEMS.Web", "Views", "DCC", "RegisterIncoming.cshtml");
        var js = Read("PEMS.Web", "wwwroot", "js", "dcc-register.js");
        var used = Regex.Matches(view, "L\\(\"(Dcc_[A-Za-z_]+)\"\\)").Select(m => m.Groups[1].Value)
            .Concat(Regex.Matches(view, "\"(Dcc_[A-Za-z]+)\"").Select(m => m.Groups[1].Value))
            .Concat(Regex.Matches(js, "T\\(\"(Dcc_[A-Za-z_]+)\"\\)").Select(m => m.Groups[1].Value))
            .Where(k => !k.EndsWith("_") && k != "Dcc_Link").Distinct().ToList();   // Dcc_Link is a prefix composed at render time

        var fa = LoadResx("SharedResource.fa.resx");
        var en = LoadResx("SharedResource.resx");
        Assert.All(used, k => { Assert.True(fa.ContainsKey(k), "fa: " + k); Assert.True(en.ContainsKey(k), "en: " + k); });
        // keys composed at render time
        foreach (var t in IncomingCorrespondenceRules.PartyTypes) Assert.True(fa.ContainsKey("Dcc_PartyType_" + t));
        foreach (var c in IncomingCorrespondenceRules.Channels) Assert.True(fa.ContainsKey("Dcc_Channel_" + c));
        foreach (var l in IncomingCorrespondenceRules.LinkTypes) Assert.True(fa.ContainsKey("Dcc_Link" + l));
    }

    // ------------------------------------------------------------------ script/stylesheet hygiene

    [Fact]
    public void TheScript_OnlyTouchesElementsThatExistInTheView()
    {
        var view = Read("PEMS.Web", "Views", "DCC", "RegisterIncoming.cshtml");
        var js = Read("PEMS.Web", "wwwroot", "js", "dcc-register.js");

        var ids = Regex.Matches(js, "\\$\\(\"(dcr[A-Za-z]+)\"\\)").Select(m => m.Groups[1].Value).Concat(
                  Regex.Matches(js, "\\$\\((?:mode === \"next\" \\? \"(dcr[A-Za-z]+)\" : \"(dcr[A-Za-z]+)\")\\)").SelectMany(m => new[] { m.Groups[1].Value, m.Groups[2].Value }))
            .Where(x => x.Length > 0).Distinct().ToList();

        Assert.True(ids.Count > 40);
        Assert.All(ids, id => Assert.Contains($"id=\"{id}\"", view));
    }

    [Fact]
    public void TheStylesheet_UsesThemeTokensOnly_NoHardcodedPageColours()
    {
        var css = Read("PEMS.Web", "wwwroot", "css", "dcc-register.css");
        // allowed literals: white on primary/selected controls, shadows and the #198754 fallback of a token
        var colours = Regex.Matches(css, "#[0-9a-fA-F]{3,8}\\b").Select(m => m.Value.ToLowerInvariant()).Distinct().ToList();
        Assert.All(colours, c => Assert.Contains(c, new[] { "#fff", "#198754" }));
    }

    [Fact]
    public void TheView_DoesNotInlineUserDataIntoScripts_AndLoadsScriptsInTheScriptsSection()
    {
        var view = Read("PEMS.Web", "Views", "DCC", "RegisterIncoming.cshtml");
        Assert.Contains("@section Scripts", view);
        Assert.Contains("pems-jalali.js", view);
        Assert.DoesNotContain("@Html.Raw(Model", view);
        Assert.DoesNotContain("innerHTML = \"<script", Read("PEMS.Web", "wwwroot", "js", "dcc-register.js"));
    }
}
