using System.Text.RegularExpressions;
using Microsoft.AspNetCore.StaticFiles;

namespace PEMS.Tests.Deployment;

/// <summary>
/// SCRUM-20 / #42: the product runs offline (Instructions rule 1). No view, component, script or stylesheet may reference an
/// external host, and the PDF viewers use the vendored pdf.js. Database-free: scans the sources.
/// </summary>
public sealed class OfflineAssetsTests
{
    // Hosts that appear as XML/SVG namespaces or in documentation comments; none of them is ever fetched.
    private static readonly HashSet<string> AllowedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "www.w3.org",
        "learn.microsoft.com",
        "www.iso.org",          // placeholder text of the "official reference URL" input (Admin/Help/StandardCreate); shown, never loaded
    };

    private static readonly Regex ExternalUrl = new(@"https?://([A-Za-z0-9.-]+)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PEMS.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
    }

    private static bool IsVendoredOrBuilt(string path)
    {
        var p = path.Replace('\\', '/');
        return p.Contains("/bin/") || p.Contains("/obj/") || p.Contains("/wwwroot/lib/");
    }

    internal static IEnumerable<string> HostsOutsideAllowList(string text) =>
        ExternalUrl.Matches(text).Select(m => m.Groups[1].Value.TrimEnd('.')).Where(h => !AllowedHosts.Contains(h)).Distinct(StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<string> SourceFiles(string root)
    {
        var web = Path.Combine(root, "PEMS.Web");
        var ui = Path.Combine(root, "PEMS.UI");
        var patterns = new (string Folder, string Pattern)[]
        {
            (Path.Combine(web, "Views"), "*.cshtml"),
            (Path.Combine(web, "Areas"), "*.cshtml"),
            (Path.Combine(web, "wwwroot", "js"), "*.js"),
            (Path.Combine(web, "wwwroot", "css"), "*.css"),
            (ui, "*.cshtml"), (ui, "*.razor"), (ui, "*.css"), (ui, "*.js"),
        };
        foreach (var (folder, pattern) in patterns)
        {
            if (!Directory.Exists(folder)) continue;
            foreach (var file in Directory.EnumerateFiles(folder, pattern, SearchOption.AllDirectories))
                if (!IsVendoredOrBuilt(file)) yield return file;
        }
    }

    [Fact]
    public void Detector_flags_a_cdn_host_and_ignores_xml_namespaces()
    {
        Assert.Equal(new[] { "cdnjs.cloudflare.com" }, HostsOutsideAllowList("<script src=\"https://cdnjs.cloudflare.com/ajax/libs/pdf.js/4.0.379/pdf.min.js\"></script>"));
        Assert.Empty(HostsOutsideAllowList("<svg xmlns='http://www.w3.org/2000/svg'></svg> // see https://learn.microsoft.com/aspnet/core"));
        Assert.Equal(new[] { "fonts.googleapis.com" }, HostsOutsideAllowList("@import url(https://fonts.googleapis.com/css?family=X);"));
    }

    [Fact]
    public void No_view_component_script_or_stylesheet_references_an_external_host()
    {
        var root = Root();
        var files = SourceFiles(root).ToList();
        Assert.True(files.Count > 100, "the scan found only " + files.Count + " files; the folders moved?");

        var offenders = files
            .Select(f => (File: Path.GetRelativePath(root, f), Hosts: HostsOutsideAllowList(File.ReadAllText(f)).ToList()))
            .Where(x => x.Hosts.Count > 0)
            .Select(x => x.File + " -> " + string.Join(", ", x.Hosts))
            .ToList();

        Assert.True(offenders.Count == 0, "External hosts break the offline product; vendor the file under wwwroot/lib:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void Pdf_viewers_use_the_vendored_pdfjs_loader()
    {
        var root = Root();
        var lib = Path.Combine(root, "PEMS.Web", "wwwroot", "lib", "pdfjs");
        foreach (var file in new[] { "pdf.min.mjs", "pdf.worker.min.mjs", "LICENSE" })
            Assert.True(File.Exists(Path.Combine(lib, file)), file + " is missing under wwwroot/lib/pdfjs");

        var loader = File.ReadAllText(Path.Combine(root, "PEMS.Web", "wwwroot", "js", "pdfjs-loader.js"));
        Assert.Contains("../lib/pdfjs/pdf.min.mjs", loader);
        Assert.Contains("../lib/pdfjs/pdf.worker.min.mjs", loader);
        Assert.Contains("window.pdfjsLib", loader);

        foreach (var view in new[] { Path.Combine("EngineeringDocuments", "Details.cshtml"), Path.Combine("VDR", "Details.cshtml") })
        {
            var text = File.ReadAllText(Path.Combine(root, "PEMS.Web", "Views", view));
            Assert.Contains("~/js/pdfjs-loader.js", text);
            Assert.DoesNotContain("cdnjs", text);
        }
    }

    [Fact]
    public void Static_files_serve_mjs_as_javascript()
    {
        // Browsers refuse a module script served with another type; the vendored pdf.js is .mjs.
        Assert.True(new FileExtensionContentTypeProvider().TryGetContentType("pdf.min.mjs", out var contentType));
        Assert.Contains("javascript", contentType);
    }
}
