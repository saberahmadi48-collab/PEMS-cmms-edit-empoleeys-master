using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace PEMS.Tests.Web;

/// <summary>
/// SCRUM-477: the login page showed one Persian link among hard-coded English text, because only that link went through the
/// localizer and the sign-in layout was fixed to English left-to-right. Every visible text of the login page, its validation and
/// its error messages must come from SharedResource (both languages), and the sign-in layout must follow the current language.
/// </summary>
public class LoginPageLocalizationTests
{
    private static string Here([CallerFilePath] string p = "") => p;
    private static string Web() => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Here())!, "..", "..", "PEMS.Web"));
    private static string Read(string relative) => File.ReadAllText(Path.Combine(Web(), relative));

    private const string LoginView = "Views/Account/Login.cshtml";
    private const string AuthLayout = "Views/Shared/_AuthLayout.cshtml";

    private static HashSet<string> Keys(string file) =>
        XDocument.Load(Path.Combine(Web(), "Resources", file)).Root!.Elements("data").Select(e => (string)e.Attribute("name")!).ToHashSet(StringComparer.Ordinal);

    /// <summary>Visible text between tags after Razor expressions, entities and comments are removed.</summary>
    private static List<string> HardCodedText(string markup)
    {
        markup = markup.Replace("\r\n", "\n");
        markup = Regex.Replace(markup, @"@\*.*?\*@", " ", RegexOptions.Singleline);
        markup = Regex.Replace(markup, @"^@(model|using|inject)\b.*$", " ", RegexOptions.Multiline);
        markup = Regex.Replace(markup, @"@\{.*?\n\}", " ", RegexOptions.Singleline);
        markup = Regex.Replace(markup, @"@section\b.*", " ", RegexOptions.Singleline);
        markup = Regex.Replace(markup, @"@await\s+[\w.]+\([^)]*\)", " ");
        markup = Regex.Replace(markup, @"<script\b.*?</script>", " ", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        markup = Regex.Replace(markup, @"<(style|head)\b.*?</\1>", " ", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        var found = new List<string>();
        foreach (Match m in Regex.Matches(markup, @">([^<>]+)<"))
        {
            var text = m.Groups[1].Value;
            text = Regex.Replace(text, @"@Localizer\[""[^""]*""\](\.Value)?", " ");
            text = Regex.Replace(text, @"@[A-Za-z_][\w.]*(\([^)]*\))?", " ");
            text = Regex.Replace(text, @"&\w+;", " ");
            text = Regex.Replace(text, @"[{}()\s]+", " ").Trim();
            // the product name and version are not translated
            text = Regex.Replace(text, @"\bPEMS\b|\bv\d+(\.\d+)*\b|\belse\b", " ").Trim();
            if (Regex.IsMatch(text, @"\p{L}")) found.Add(text);
        }
        return found;
    }

    [Theory]
    [InlineData(LoginView)]
    [InlineData(AuthLayout)]
    public void Sign_in_markup_has_no_hard_coded_text(string view)
    {
        var text = HardCodedText(Read(view));
        Assert.True(text.Count == 0, $"Hard-coded text in {view}: " + string.Join(" | ", text));
    }

    [Fact]
    public void Login_placeholders_and_loading_text_are_localized()
    {
        var view = Read(LoginView);
        foreach (Match m in Regex.Matches(view, @"placeholder=""([^""]*)"""))
            Assert.StartsWith("@Localizer[", m.Groups[1].Value);
        Assert.DoesNotContain("Signing in", view);
        Assert.Contains("Localizer[\"Login_SigningIn\"]", view);
    }

    [Fact]
    public void Sign_in_layout_follows_the_current_language()
    {
        var layout = Read(AuthLayout);
        Assert.DoesNotContain("<html lang=\"en\"", layout);
        Assert.Contains("dir=\"@(isRtl ? \"rtl\" : \"ltr\")\"", layout);
        Assert.Contains("bootstrap.rtl.min.css", layout);
    }

    [Fact]
    public void Login_errors_are_not_hard_coded_in_the_controller()
    {
        var controller = Read("Controllers/AccountController.cs");
        var start = controller.IndexOf("public async Task<IActionResult> Login(LoginViewModel model)", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var end = controller.IndexOf("public IActionResult ChangePassword()", start, StringComparison.Ordinal);
        var login = controller[start..end];
        Assert.DoesNotMatch(new Regex(@"AddModelError\(\s*string\.Empty\s*,\s*\$?""", RegexOptions.Singleline), login);
        Assert.DoesNotMatch(new Regex(@"[?:]\s*\$?""[^""]*\p{IsArabic}"), login);
    }

    [Fact]
    public void Every_login_key_exists_in_both_languages()
    {
        var en = Keys("SharedResource.resx");
        var fa = Keys("SharedResource.fa.resx");
        var used = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var view in new[] { LoginView, AuthLayout })
            foreach (Match m in Regex.Matches(Read(view), @"Localizer\[""([^""]+)"""))
                used.Add(m.Groups[1].Value);
        foreach (Match m in Regex.Matches(Read("Models/Account/LoginViewModel.cs"), @"(?:ErrorMessage|Name)\s*=\s*""([^""]+)"""))
            used.Add(m.Groups[1].Value);
        foreach (Match m in Regex.Matches(Read("Controllers/AccountController.cs"), @"_t\[""([^""]+)"""))
            used.Add(m.Groups[1].Value);

        Assert.Contains("Login_Err_Invalid", used);
        var missing = used.Where(k => !en.Contains(k) || !fa.Contains(k)).ToList();
        Assert.True(missing.Count == 0, "Login keys missing in SharedResource.resx or SharedResource.fa.resx: " + string.Join(", ", missing));
    }
}
