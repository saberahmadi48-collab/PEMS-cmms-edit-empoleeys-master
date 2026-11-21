using System.Text.RegularExpressions;

namespace PEMS.UI.Tests;

/// <summary>
/// ADR-0008 gate: a PEMS.UI component holds no literal user-visible text; every text is an input. This scans the markup of the library
/// (.razor and .cshtml) for words between tags and in text attributes. It cannot see strings built in C# code, which review covers.
/// </summary>
public sealed class LiteralTextGateTests
{
    private static readonly Regex TextNode = new(@">([^<>]*\p{L}[^<>]*)<", RegexOptions.Compiled);
    private static readonly Regex TextAttribute = new(@"\b(title|aria-label|placeholder|alt|label|aria-description)\s*=\s*""([^""@]*\p{L}[^""@]*)""", RegexOptions.Compiled);
    private static readonly Regex SwitchLabels = new(@"^[ \t]*(case\s+[^\r\n:]+:|default:|break;)[ \t]*$", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly string[] ControlWords = ["if", "foreach", "for", "while", "switch", "lock", "using"];
    private static readonly string[] DirectiveWords = ["namespace", "inject", "model", "attribute", "page", "inherits", "addTagHelper", "implements", "layout", "typeparam", "rendermode"];

    /// <summary>Returns the literal user-visible texts found in the markup (empty when the markup is clean).</summary>
    public static IReadOnlyList<string> FindLiteralTexts(string markup)
    {
        // Windows checkouts convert files to CRLF; in Multiline mode `$` matches before \n but not before \r, so the switch labels would survive as "text".
        var markupOnly = SwitchLabels.Replace(RemoveRazorCode(markup.Replace("\r\n", "\n")), "");
        var found = new List<string>();
        foreach (Match m in TextAttribute.Matches(markupOnly)) found.Add($"{m.Groups[1].Value}=\"{m.Groups[2].Value}\"");
        foreach (Match m in TextNode.Matches(markupOnly))
        {
            var text = m.Groups[1].Value.Trim();
            if (text.Length > 0) found.Add(text);
        }
        return found;
    }

    /// <summary>Removes comments, @code/@{ } blocks, directives, control-flow headers and @expressions, leaving markup and its literal text.</summary>
    private static string RemoveRazorCode(string s)
    {
        var o = new System.Text.StringBuilder();
        for (var i = 0; i < s.Length;)
        {
            var c = s[i];
            if (c == '@' && i + 1 < s.Length)
            {
                var n = s[i + 1];
                if (n == '@') { o.Append('@'); i += 2; continue; }
                if (n == '*') { var e = s.IndexOf("*@", i + 2, StringComparison.Ordinal); i = e < 0 ? s.Length : e + 2; continue; }
                if (n == '{') { i = SkipBalanced(s, i + 1, '{', '}'); continue; }
                if (n == '(') { i = SkipBalanced(s, i + 1, '(', ')'); continue; }
                if (char.IsLetter(n))
                {
                    var j = i + 1;
                    while (j < s.Length && (char.IsLetterOrDigit(s[j]) || s[j] == '_')) j++;
                    var word = s[(i + 1)..j];
                    var k = j;
                    while (k < s.Length && (s[k] == ' ' || s[k] == '\t')) k++;
                    if (word is "code" or "functions") { i = k < s.Length && s[k] == '{' ? SkipBalanced(s, k, '{', '}') : j; continue; }
                    if (ControlWords.Contains(word) && k < s.Length && s[k] == '(') { i = SkipBalanced(s, k, '(', ')'); continue; }
                    if (DirectiveWords.Contains(word) || word == "using") { var e = s.IndexOf('\n', j); i = e < 0 ? s.Length : e + 1; continue; }
                    // implicit expression: Ident(.Ident | (args) | [index])*
                    while (j < s.Length)
                    {
                        if (s[j] == '.' && j + 1 < s.Length && char.IsLetter(s[j + 1])) { j++; while (j < s.Length && (char.IsLetterOrDigit(s[j]) || s[j] == '_')) j++; }
                        else if (s[j] == '(') j = SkipBalanced(s, j, '(', ')');
                        else if (s[j] == '[') j = SkipBalanced(s, j, '[', ']');
                        else break;
                    }
                    i = j;
                    continue;
                }
            }
            // "} else" / "} else if (...)" continue a Razor statement
            if (c == 'e' && string.CompareOrdinal(s, i, "else", 0, 4) == 0 && o.ToString().TrimEnd().EndsWith('}'))
            {
                i += 4;
                var k = i;
                while (k < s.Length && char.IsWhiteSpace(s[k])) k++;
                if (string.CompareOrdinal(s, k, "if", 0, 2) == 0)
                {
                    k += 2;
                    while (k < s.Length && char.IsWhiteSpace(s[k])) k++;
                    if (k < s.Length && s[k] == '(') i = SkipBalanced(s, k, '(', ')');
                }
                continue;
            }
            o.Append(c);
            i++;
        }
        return o.ToString();
    }

    /// <summary>Index just after the bracket that closes the one at <paramref name="open"/> (string literals are skipped).</summary>
    private static int SkipBalanced(string s, int open, char up, char down)
    {
        var depth = 0;
        for (var i = open; i < s.Length; i++)
        {
            var c = s[i];
            if (c == '"') { i++; while (i < s.Length && s[i] != '"') { if (s[i] == '\\') i++; i++; } continue; }
            if (c == up) depth++;
            else if (c == down && --depth == 0) return i + 1;
        }
        return s.Length;
    }

    [Theory]
    [InlineData("<button>Save</button>")]
    [InlineData("<label>Name <span>*</span> Tag</label>")]
    [InlineData("<input placeholder=\"Type here\" />")]
    [InlineData("<div title=\"Close\"></div>")]
    [InlineData("<td>\r\n    No records\r\n</td>")]
    public void The_scanner_finds_literal_text(string markup) => Assert.NotEmpty(FindLiteralTexts(markup));

    [Theory]
    [InlineData("<button>@label</button>")]
    [InlineData("<span aria-hidden=\"true\">*</span>")]
    [InlineData("<th>@Locale.Text(c.Header)</th>")]
    [InlineData("<input placeholder=\"@placeholderAttr\" />")]
    [InlineData("@* Measurement only: not shown *@\r\n<table></table>")]
    [InlineData("<div>@code { string x = \"Hello world\"; }</div>")]
    [InlineData("@{ var title = \"Hello world\"; }\r\n<h2>@title</h2>")]
    public void The_scanner_accepts_expressions_symbols_comments_and_code(string markup) => Assert.Empty(FindLiteralTexts(markup));

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]                       // a Windows checkout (autocrlf) must give the same result
    public void The_scanner_ignores_switch_labels_whatever_the_line_endings(string eol)
    {
        var markup = string.Join(eol, "@switch (Kind)", "{", "    case FieldKind.TextArea:", "        <textarea>@Value</textarea>", "        break;", "    default:", "        <input value=\"@Value\" />", "        break;", "}");
        Assert.Empty(FindLiteralTexts(markup));
    }

    [Fact]
    public void No_component_of_the_library_contains_literal_user_visible_text()
    {
        var root = FindLibraryRoot();
        var files = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(f => (f.EndsWith(".razor") || f.EndsWith(".cshtml")) && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToList();
        Assert.NotEmpty(files);

        // Shared partials moved from PEMS.Web keep their existing text until #388 turns them into inputs.
        var movedFromWeb = new[] { "_PemsLoadingOverlay.cshtml" };
        var offences = files
            .Where(f => !movedFromWeb.Contains(Path.GetFileName(f)))
            .SelectMany(f => FindLiteralTexts(File.ReadAllText(f)).Select(t => $"{Path.GetRelativePath(root, f)}: {t}"))
            .ToList();
        Assert.True(offences.Count == 0, "Literal text in components (make it an input, ADR-0008):\n" + string.Join("\n", offences));
    }

    private static string FindLibraryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "PEMS.UI", "Components"))) dir = dir.Parent;
        return dir is null ? throw new DirectoryNotFoundException("PEMS.UI") : Path.Combine(dir.FullName, "PEMS.UI");
    }
}
