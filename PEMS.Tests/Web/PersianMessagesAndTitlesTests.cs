using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Localization;
using PEMS.Web.Configuration;
using PEMS.Web.Localization;
using PEMS.Web.Resources;
using PEMS.Web.Security;

namespace PEMS.Tests.Web;

/// <summary>
/// SCRUM-540: on a Persian page the validation messages, the password-policy messages and many browser tab titles were English.
/// Identity messages come from <c>Id_*</c> keys, the default validation messages become resource keys, and every page title is a key with
/// a Persian value (the auth and embed layouts translate the title like _Layout does).
/// </summary>
public class PersianMessagesAndTitlesTests
{
    private static string Repo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PEMS.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([Repo(), .. parts]));

    private static Dictionary<string, string> Resx(string file)
        => Regex.Matches(Read("PEMS.Web", "Resources", file), "<data name=\"([^\"]+)\"[^>]*>\\s*<value>([^<]*)</value>")
            .GroupBy(m => System.Net.WebUtility.HtmlDecode(m.Groups[1].Value))
            .ToDictionary(g => g.Key, g => System.Net.WebUtility.HtmlDecode(g.First().Groups[2].Value));

    /// <summary>Shows which key and arguments the describer asked for.</summary>
    private sealed class KeyLocalizer : IStringLocalizer<SharedResource>
    {
        public LocalizedString this[string name] => new(name, "fa:" + name);
        public LocalizedString this[string name, params object[] arguments] => new(name, "fa:" + name + ":" + string.Join(",", arguments));
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }

    private sealed class SelectiveLocalizer : IStringLocalizer<SharedResource>
    {
        public LocalizedString this[string name] => name == "Known UI text"
            ? new(name, "متن رابط کاربری")
            : new(name, name, resourceNotFound: true);
        public LocalizedString this[string name, params object[] arguments] => this[name];
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }

    [Fact]
    public void IdentityMessages_ComeFromTheResources_AndKeepTheirCodes()
    {
        var d = new LocalizedIdentityErrorDescriber(new KeyLocalizer());
        var tooShort = d.PasswordTooShort(8);
        Assert.Equal("PasswordTooShort", tooShort.Code);
        Assert.Equal("fa:Id_PasswordTooShort:8", tooShort.Description);
        Assert.Equal("fa:Id_DuplicateUserName:ali", d.DuplicateUserName("ali").Description);
        Assert.Equal("fa:Id_PasswordMismatch", d.PasswordMismatch().Description);
    }

    [Fact]
    public void EveryIdentityKey_HasAnEnglishAndAPersianValue_WithTheSamePlaceholders()
    {
        var en = Resx("SharedResource.resx");
        var fa = Resx("SharedResource.fa.resx");
        var keys = Regex.Matches(Read("PEMS.Web", "Security", "LocalizedIdentityErrorDescriber.cs"), "\"(Id_[A-Za-z]+)\"").Select(m => m.Groups[1].Value).Distinct().ToList();
        Assert.True(keys.Count >= 22);
        foreach (var key in keys)
        {
            Assert.True(en.ContainsKey(key), "en " + key);
            Assert.True(fa.ContainsKey(key), "fa " + key);
            Assert.Equal(Regex.Matches(en[key], @"\{\d\}").Count, Regex.Matches(fa[key], @"\{\d\}").Count);
        }
    }

    [Fact]
    public void DefaultValidationMessages_BecomeKeys_ButOwnMessagesAreKept()
    {
        Assert.Equal("The {0} field is required.", ValidationMessageKeys.KeyFor(new RequiredAttribute()));
        Assert.Equal("The field {0} must be a string with a maximum length of {1}.", ValidationMessageKeys.KeyFor(new StringLengthAttribute(50)));
        Assert.Equal("The field {0} must be a string with a minimum length of {2} and a maximum length of {1}.", ValidationMessageKeys.KeyFor(new StringLengthAttribute(50) { MinimumLength = 2 }));

        var own = new RequiredAttribute { ErrorMessage = "Mine" };
        var plain = new RequiredAttribute();
        var context = new Microsoft.AspNetCore.Mvc.ModelBinding.Metadata.ValidationMetadataProviderContext(
            Microsoft.AspNetCore.Mvc.ModelBinding.Metadata.ModelMetadataIdentity.ForType(typeof(string)), Microsoft.AspNetCore.Mvc.ModelBinding.ModelAttributes.GetAttributesForType(typeof(string)));
        context.ValidationMetadata.ValidatorMetadata.Add(own);
        context.ValidationMetadata.ValidatorMetadata.Add(plain);
        new ValidationMessageKeys().CreateValidationMetadata(context);
        Assert.Equal("Mine", own.ErrorMessage);
        Assert.Equal("The {0} field is required.", plain.ErrorMessage);
    }

    [Fact]
    public void EveryValidationKey_HasAPersianValue_WithItsPlaceholder()
    {
        var fa = Resx("SharedResource.fa.resx");
        foreach (var key in new[] { "The {0} field is required.", "The {0} field is not a valid e-mail address.", "The field {0} must be between {1} and {2}.", "'{0}' and '{1}' do not match." })
        {
            Assert.True(fa.ContainsKey(key), key);
            Assert.Contains("{0}", fa[key]);
            Assert.DoesNotMatch("[A-Za-z]", fa[key]);
        }
    }

    [Fact]
    public void ProgramWires_TheDescriberAndTheValidationKeys()
    {
        var program = Read("PEMS.Web", "Program.cs");
        Assert.Contains(".AddErrorDescriber<PEMS.Web.Security.LocalizedIdentityErrorDescriber>()", program);
        Assert.Contains("options.ModelMetadataDetailsProviders.Add(new PEMS.Web.Configuration.ValidationMessageKeys())", program);
    }

    [Fact]
    public void EveryLiteralPageTitle_HasAPersianValue_AndNoTitleIsWrittenInPersian()
    {
        var fa = Resx("SharedResource.fa.resx");
        var web = Path.Combine(Repo(), "PEMS.Web");
        var titles = Directory.EnumerateFiles(web, "*.*", SearchOption.AllDirectories)
            .Where(f => (f.EndsWith(".cshtml") || f.EndsWith(".cs")) && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) && !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), "ViewData\\[\"Title\"\\]\\s*=\\s*\"([^\"]*)\"").Select(m => (File: Path.GetFileName(f), Title: m.Groups[1].Value)))
            .ToList();
        Assert.True(titles.Count > 200);
        Assert.Empty(titles.Where(t => Regex.IsMatch(t.Title, "[\u0600-\u06FF]")).Select(t => t.File + ": " + t.Title));
        Assert.Empty(titles.Where(t => t.Title.Length > 0 && !fa.ContainsKey(t.Title)).Select(t => t.File + ": " + t.Title).Distinct());
    }

    [Theory]
    [InlineData("_AuthLayout.cshtml")]
    [InlineData("_LayoutEmbed.cshtml")]
    public void TheAuthAndEmbedLayouts_TranslateTheTitle(string layout)
        => Assert.Contains("Localizer[ViewData[\"Title\"]!.ToString()!].Value) - PEMS</title>", Read("PEMS.Web", "Views", "Shared", layout));

    [Fact]
    public void TheMessagesTheReviewFound_GoThroughTheLocalizer()
    {
        Assert.Contains("T(\"Equipment, Vendor and Project are required.\")", Read("PEMS.Web", "Controllers", "MDBController.cs"));
        Assert.Contains("T(\"Please select at least one document.\")", Read("PEMS.Web", "Controllers", "TransmittalController.cs"));
        Assert.Contains("Refuse(\"A valid, active discipline is required to create an employee.\")", Read("PEMS.Web", "Services", "UserAdminPageService.cs"));
    }

    [Fact]
    public void RecoveryPageTitles_HaveOneCompiledResourceKeyPerLanguage()
    {
        foreach (var file in new[] { "SharedResource.resx", "SharedResource.fa.resx" })
        {
            var keys = Resx(file).Keys;
            foreach (var title in new[] { "Recovery codes", "Administrator recovery", "Break-glass recovery", "Reset administrator account" })
            {
                Assert.Single(keys, key => string.Equals(key, title, StringComparison.OrdinalIgnoreCase));
                Assert.Contains(title, keys);
            }
        }
    }

    [Fact]
    public void RequiredRoleAndTransmittalFieldNames_HaveLocalizedDisplayKeys()
    {
        var action = typeof(PEMS.Web.Controllers.TransmittalController).GetMethods()
            .Single(method => method.Name == "Create" && method.GetParameters().Length > 0);
        var displayKeys = action.GetParameters()
            .Select(parameter => parameter.GetCustomAttributes(typeof(DisplayAttribute), false).Cast<DisplayAttribute>().SingleOrDefault()?.Name)
            .OfType<string>();
        Assert.Equal(new[] { "Transmittal code", "From", "To", "Purpose" }, displayKeys);
        var en = Resx("SharedResource.resx");
        var fa = Resx("SharedResource.fa.resx");
        foreach (var key in displayKeys.Append("Role Name (English, no spaces)"))
        {
            Assert.True(en.ContainsKey(key), key);
            Assert.True(fa.ContainsKey(key), key);
            Assert.DoesNotMatch("[A-Za-z]", fa[key]);
        }
    }

    [Fact]
    public void ChangePasswordValidation_UsesBilingualFieldAndMessageKeys()
    {
        var en = Resx("SharedResource.resx");
        var fa = Resx("SharedResource.fa.resx");
        var fields = typeof(PEMS.Web.Models.Account.ChangePasswordViewModel).GetProperties();
        foreach (var field in fields)
        {
            var display = field.GetCustomAttributes(typeof(DisplayAttribute), false).Cast<DisplayAttribute>().Single();
            Assert.True(en.ContainsKey(display.Name!), display.Name);
            Assert.True(fa.ContainsKey(display.Name!), display.Name);
            foreach (var validation in field.GetCustomAttributes(typeof(ValidationAttribute), false).Cast<ValidationAttribute>())
            {
                var key = validation.ErrorMessage ?? ValidationMessageKeys.KeyFor(validation);
                if (key is null) continue;
                Assert.True(en.ContainsKey(key), key);
                Assert.True(fa.ContainsKey(key), key);
            }
        }
    }

    [Fact]
    public void DashboardCardsAndDocumentStatuses_HavePersianResources()
    {
        var fa = Resx("SharedResource.fa.resx");
        var home = Read("PEMS.Web", "Controllers", "HomeController.cs");
        var keys = Regex.Matches(home, "(?:Title|Hint) = \\\"([^\\\"]+)\\\"")
            .Select(match => match.Groups[1].Value)
            .Concat(new[] { "Issued", "Approved", "Archived", "Submitted", "Draft", "Rejected", "Client Approved", "IDC Review", "Checked" });
        foreach (var key in keys)
        {
            Assert.True(fa.ContainsKey(key), key);
            Assert.NotEqual(key, fa[key]);
        }
    }

    [Fact]
    public void DashboardMdbAndTransmittalExplicitKeys_HaveBilingualResources()
    {
        var en = Resx("SharedResource.resx");
        var fa = Resx("SharedResource.fa.resx");
        foreach (var view in new[] { "Home/Index.cshtml", "MDB/Create.cshtml", "Transmittal/Create.cshtml" })
        {
            if (!File.Exists(Path.Combine(Repo(), "PEMS.Web", "Views", view))) continue;   // the home dashboard is a definition page now
            var source = Read("PEMS.Web", "Views", view);
            foreach (Match match in Regex.Matches(source, "Localizer\\[\\\"((?:Dashboard_|MDB_|Transmittal_)[^\\\"]+)\\\""))
            {
                var key = match.Groups[1].Value;
                Assert.True(en.ContainsKey(key), key);
                Assert.True(fa.ContainsKey(key), key);
                Assert.NotEqual(en[key], fa[key]);
            }
        }
        foreach (var resource in new[] { en, fa })
        {
            Assert.Contains("{0}", resource["Transmittal_DocumentCounter"]);
            Assert.Contains("{1}", resource["Transmittal_DocumentCounter"]);
            foreach (var key in new[] { "Transmittal_CreateCodeRequired", "Transmittal_CreateCodeDuplicate", "Transmittal_CreateDocumentsHeld", "Transmittal created successfully", "MDB created successfully", "Unassigned" })
                Assert.True(resource.ContainsKey(key), key);
            foreach (var purpose in new[] { "IFA", "IFR", "IFI", "FI", "For Approval", "For Construction", "For Information", "For Review" })
                Assert.True(resource.ContainsKey("Transmittal_Purpose_" + purpose), purpose);
        }
        var script = Read("PEMS.Web", "wwwroot", "js", "views", "transmittal-create.js");
        Assert.Contains("counter.dataset.format", script);
        Assert.Contains("input.setCustomValidity(input.dataset.requiredMessage)", script);
        Assert.Contains("data-required-message", Read("PEMS.Web", "Views", "Transmittal", "Create.cshtml"));
    }

    [Fact]
    public void LegacyPersianLiteralResourceKeys_HaveEnglishValues()
    {
        var en = Resx("SharedResource.resx");
        var views = new[] { Path.Combine(Repo(), "PEMS.Web", "Views"), Path.Combine(Repo(), "PEMS.Web", "Areas") };
        var keys = views.SelectMany(dir => Directory.EnumerateFiles(dir, "*.cshtml", SearchOption.AllDirectories))
            .SelectMany(file => Regex.Matches(File.ReadAllText(file), "Localizer\\[\\\"([^\\\"\\r\\n]+)\\\"\\]").Select(m => m.Groups[1].Value))
            .Where(key => Regex.IsMatch(key, "[\\u0600-\\u06FF]"))
            .Distinct();
        var untranslated = keys.Where(key => !en.TryGetValue(key, out var value) || Regex.IsMatch(value, "[\\u0600-\\u06FF]"));
        Assert.Empty(untranslated);
    }

    [Fact]
    public void JiraReviewLabelsAndMessages_HaveBothLanguageResources()
    {
        var en = Resx("SharedResource.resx");
        var fa = Resx("SharedResource.fa.resx");
        var keys = new[] { "By Role", "Lock/Tag", "Isolated By", "Verified By", "Tested At", "Parent Equipment", "Installation Date", "Inspection Type", "Inspection Date", "Measurement Value", "Lower Limit", "Permit Not Required", "LOTO Required", "Save MR", "Equipment Tag (optional)", "Responsible Engineer", "Purchase Order created as Draft", "Contract created successfully", "Purchase Order issued", "IRN submitted for inspection.", "Disposition recorded - awaiting QA approval.", "Disposition approved - accepted quantity posted to stock.", "User created successfully", "User updated successfully", "This IRN is not awaiting approval.", "EngineeringQuestion", "TechnicalClarification", "Answered", "Repl_St_Converted", "NotQualified", "Public", "Confidential", "Restricted", "SR_ConfirmAdvance", "WarehouseIntakePurpose" };
        foreach (var key in keys)
        {
            Assert.True(en.ContainsKey(key), key);
            Assert.True(fa.ContainsKey(key), key);
            Assert.NotEqual(en[key], fa[key]);
        }
    }

    [Fact]
    public void MaterialItemDetails_HaveBilingualLabelsAndBarcodeMessages()
    {
        var en = Resx("SharedResource.resx");
        var fa = Resx("SharedResource.fa.resx");
        foreach (var key in new[] { "Mat_ReceiveStock", "Mat_Stock", "Mat_RecentTransactions", "Mat_SourceImports", "Mat_NoStock", "Mat_SourceImportsHint", "Mat_ScanWarehouse", "Mat_BarcodeResolveFailed", "Mat_BarcodeWrongType", "Mat_BarcodeWarehouseNotListed", "Mat_AdjustStock", "Mat_TransferStock" })
        {
            Assert.True(en.ContainsKey(key), key);
            Assert.True(fa.ContainsKey(key), key);
            Assert.NotEqual(en[key], fa[key]);
        }
    }

    [Fact]
    public void SearchAndWarehousePurpose_TranslateOnlyControlledText()
    {
        var l = new KeyLocalizer();
        var item = new PEMS.Application.DTOs.GlobalSearchResultItemDto { SubLabel = "Incoming order — QA vendor", SubLabelSuffixKey = "Incoming" };
        Assert.Equal("Incoming order — QA vendor — fa:Incoming", PEMS.Web.Navigation.SearchResultText.SubLabel(item, l));
        Assert.Equal("fa:WarehouseIntakePurpose:WO-001", PEMS.Web.Helpers.StoreRequisitionUi.Purpose(l, "Warehouse intake of work order WO-001"));
        Assert.Equal("QA free-text purpose", PEMS.Web.Helpers.StoreRequisitionUi.Purpose(l, "QA free-text purpose"));
        var fallback = new SelectiveLocalizer();
        Assert.Equal("متن رابط کاربری", PEMS.Web.Helpers.VdrUi.Loc(fallback, "Missing_", "Known UI text"));
        Assert.Equal("Unknown status", PEMS.Web.Helpers.VdrUi.Loc(fallback, "Missing_", "Unknown status"));
    }

    [Fact]
    public void LegacyHtmlSafetyNet_TranslatesOnlyKnownUiText_InTextAndAccessibleAttributes()
    {
        var localizer = new SelectiveLocalizer();
        const string html = "<p>Known UI text</p><button title=\"Known UI text\">Known UI text</button><span>user-entered text</span>";

        var result = HtmlLocalization.LocalizeLegacyHtml(html, localizer);

        Assert.Equal("<p>متن رابط کاربری</p><button title=\"متن رابط کاربری\">متن رابط کاربری</button><span>user-entered text</span>", result);
    }
}
