using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using PEMS.UI.Abstractions;
using PEMS.UI.Components;
using PEMS.UI.Rendering;
using PEMS.UI.Tests.Host;

namespace PEMS.UI.Tests;

/// <summary>
/// SCRUM-23 / plan row #319: the vertical slice behind ADR-0007. Each test is one criterion of the comparison
/// (binding and validation parity, antiforgery, descriptor rendering, extension loading, RTL, offline, testability, render time).
/// </summary>
public sealed class SpikeTests(SpikeHost host, ITestOutputHelper output) : IClassFixture<SpikeHost>
{
    private static readonly Regex Ws = new(@"\s+", RegexOptions.Compiled);
    private static string Norm(string s) => Ws.Replace(s, " ").Replace("> <", "><").Trim();

    // ---- binding and validation parity (facade over the component)
    [Fact]
    public async Task TagHelper_field_takes_label_required_and_kind_from_the_model_metadata()
    {
        var (html, _, _) = await host.GetFormAsync("/b/form");
        Assert.Contains("name=\"Name\"", html);
        Assert.Contains("pems-field__required", html);              // [Required] on Name
        Assert.Contains("type=\"number\"", html);                   // int Quantity
        Assert.Contains("type=\"date\"", html);                     // DateTime? Due
        Assert.Contains("type=\"checkbox\"", html);                 // bool Active
        Assert.Contains("type=\"file\"", html);                     // kind="File"
        Assert.Contains("id=\"f-Name\"", html);
        Assert.Contains("aria-describedby=\"f-Name-help\"", html);
    }

    [Fact]
    public async Task Invalid_post_shows_modelstate_errors_and_keeps_the_entered_values()
    {
        var (_, token, cookie) = await host.GetFormAsync("/b/form");
        var response = await host.PostFormAsync("/b/form", token, cookie,
            new Dictionary<string, string> { ["Name"] = "", ["Quantity"] = "500", ["Due"] = "2026-10-27" });
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("pems-field__error", html);
        Assert.Contains("role=\"alert\"", html);
        Assert.Contains("aria-invalid=\"true\"", html);
        Assert.Contains("value=\"500\"", html);
        Assert.Contains("value=\"2026-10-27\"", html);
    }

    [Fact]
    public async Task Valid_post_binds_the_model_including_the_uploaded_file()
    {
        var (_, token, cookie) = await host.GetFormAsync("/b/form");
        var response = await host.PostFormAsync("/b/form", token, cookie,
            new Dictionary<string, string> { ["Name"] = "PT-0001", ["Quantity"] = "5", ["Due"] = "2026-10-27", ["Active"] = "true" },
            file: new byte[1234]);
        Assert.Equal("saved:PT-0001:5:2026-10-27:True:1234:0", await response.Content.ReadAsStringAsync());
    }

    // ---- antiforgery
    [Fact]
    public async Task Post_without_antiforgery_token_is_rejected()
    {
        var response = await host.PostFormAsync("/b/form", token: null, cookie: "",
            new Dictionary<string, string> { ["Name"] = "x", ["Quantity"] = "1" });
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Descriptor_form_works_with_the_request_token_passed_as_a_parameter()
    {
        var (html, token, cookie) = await host.GetFormAsync("/b/descriptor-form");
        Assert.NotEqual("", token);
        Assert.Contains("enctype=\"multipart/form-data\"", html);
        var response = await host.PostFormAsync("/b/descriptor-form", token, cookie,
            new Dictionary<string, string> { ["Name"] = "PT-9", ["Quantity"] = "2" }, file: new byte[10]);
        Assert.Equal("saved:PT-9:10", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Framework_AntiforgeryToken_component_inside_a_static_component_is_recorded()
    {
        // The plan names this as the known risk. The result is written to the test output and into ADR-0007.
        try
        {
            var response = await host.Client.GetAsync("/b/antiforgery-probe");
            var html = await response.Content.ReadAsStringAsync();
            output.WriteLine($"AntiforgeryToken component: HTTP {(int)response.StatusCode}, token input present: {html.Contains("__RequestVerificationToken")}");
        }
        catch (Exception ex)
        {
            output.WriteLine($"AntiforgeryToken component: throws {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
        }
    }

    [Fact]
    public async Task Framework_AntiforgeryToken_component_outside_a_request_is_recorded()
    {
        try
        {
            var renderer = host.Services.CreateScope().ServiceProvider.GetRequiredService<IPemsComponentRenderer>();
            var html = await renderer.RenderAsync<AntiforgeryProbe>(new Dictionary<string, object?>());
            output.WriteLine($"AntiforgeryToken component outside a request: renders, token input present: {html.Contains("__RequestVerificationToken")}");
        }
        catch (Exception ex)
        {
            output.WriteLine($"AntiforgeryToken component outside a request: throws {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
        }
    }

    [Fact]
    public async Task A_valid_model_value_for_a_date_field_uses_the_html_date_format()
    {
        var html = (await host.GetFormAsync("/b/form")).Html;
        Assert.DoesNotContain("value=\"01/01", html);               // an unset DateTime? renders no value
        var renderer = host.Services.CreateScope().ServiceProvider.GetRequiredService<IPemsComponentRenderer>();
        var field = await renderer.RenderAsync("field", new Dictionary<string, object?>
        {
            ["Descriptor"] = new FieldDescriptor { Name = "Due", Label = "Due", Kind = FieldKind.Date }, ["Value"] = "2026-10-27"
        });
        Assert.Contains("type=\"date\"", field);
        Assert.Contains("value=\"2026-10-27\"", field);
    }

    // ---- one rendering path
    [Fact]
    public async Task Descriptor_host_renders_a_component_chosen_by_key()
    {
        var html = await host.Client.GetStringAsync("/b/descriptor-table");
        Assert.Contains("<table class=\"pems-table\" id=\"spike-table\">", html);
        Assert.Equal(6, Regex.Matches(html, "<th ").Count);
        Assert.Equal(5, Regex.Matches(html, "PT-000").Count);
    }

    [Fact]
    public async Task The_same_table_renders_identically_from_an_mvc_view_and_standalone_without_a_request()
    {
        var renderer = host.Services.CreateScope().ServiceProvider.GetRequiredService<IPemsComponentRenderer>();
        var standalone = await renderer.RenderAsync("table", new Dictionary<string, object?>
        {
            ["View"] = SpikeData.Table, ["Rows"] = SpikeData.Rows(1000)
        });
        var fromMvc = await host.Client.GetStringAsync("/b/table");
        Assert.Equal(Norm(standalone), Norm(fromMvc));
    }

    [Fact]
    public async Task A_component_from_an_extension_assembly_loads_at_runtime_and_renders_by_key()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "PEMS.UI.Tests.ExtensionPack.dll");
        var context = new AssemblyLoadContext("extension-pack", isCollectible: true);
        try
        {
            var assembly = context.LoadFromAssemblyPath(path);
            var registry = new PemsComponentRegistry();
            Assert.Equal(1, registry.RegisterAssembly(assembly));

            var services = new ServiceCollection().AddPemsUi();
            services.AddSingleton<IPemsComponentRegistry>(registry);
            await using var provider = services.BuildServiceProvider();
            var html = await provider.GetRequiredService<IPemsComponentRenderer>().RenderAsync("sample-kpi",
                new Dictionary<string, object?> { ["Value"] = "42", ["Label"] = "Open work orders" });
            Assert.Contains("pems-kpi__value\">42<", html);
        }
        finally { context.Unload(); }
    }

    // ---- RTL and offline
    [Fact]
    public async Task Markup_and_stylesheet_are_direction_neutral_and_offline()
    {
        var html = await host.Client.GetStringAsync("/b/table") + (await host.GetFormAsync("/b/form")).Html;
        Assert.DoesNotContain("style=", html);
        Assert.DoesNotMatch(@"class=""[^""]*\b(left|right|float-)", html);
        Assert.DoesNotContain("http://", html);
        Assert.DoesNotContain("https://", html);

        var css = await File.ReadAllTextAsync(FindFile("PEMS.UI/wwwroot/pems-ui.css"));
        Assert.DoesNotMatch(@"(margin|padding|border)-(left|right)|(^|[\s;{])(left|right)\s*:|text-align\s*:\s*(left|right)|float\s*:", css);
        Assert.DoesNotContain("url(", css);
        Assert.DoesNotContain("http", css);
        Assert.DoesNotMatch(@"#[0-9a-fA-F]{3,8}\b", css);           // tokens only, no hex colours
    }

    [Fact]
    public async Task Token_file_is_offline_and_has_no_rules_only_custom_properties()
    {
        var css = await File.ReadAllTextAsync(FindFile("PEMS.UI/wwwroot/tokens.css"));
        Assert.DoesNotContain("url(", css);
        Assert.DoesNotContain("@import", css);
        Assert.DoesNotContain("http", css);
        Assert.Contains(":root {", css);
        Assert.Contains(":root[data-theme=\"dark\"] {", css);
        Assert.Contains("--pems-primary:", css);
    }

    // ---- testability
    [Fact]
    public void Component_state_is_plain_csharp_and_testable_without_rendering()
    {
        var button = ButtonState.From(new ButtonOptions { Label = "Save", Tone = Tone.Danger, Size = Size.Small, Href = "/x", Skin = ComponentSkin.Pems });
        Assert.Equal("pems-btn pems-btn--danger pems-btn--small", button.Css);
        Assert.True(button.AsLink);
        Assert.False(ButtonState.From(new ButtonOptions { Label = "Save", Href = "/x", Disabled = true, Skin = ComponentSkin.Pems }).AsLink);

        var field = FieldState.From(new FieldDescriptor { Name = "Items[0].Name", Label = "Name", HelpText = "h" }, hasError: true);
        Assert.Equal("f-Items-0--Name", field.Id);
        Assert.Equal("f-Items-0--Name-error", field.DescribedBy);
        Assert.True(field.Invalid);
    }

    // ---- render time (numbers go into ADR-0007)
    [Fact]
    public async Task Render_time_of_a_1000_row_table()
    {
        var renderer = host.Services.CreateScope().ServiceProvider.GetRequiredService<IPemsComponentRenderer>();
        var rows = SpikeData.Rows(1000);
        var p = new Dictionary<string, object?> { ["View"] = SpikeData.Table, ["Rows"] = rows };

        var results = new (string Name, double Ms, int Bytes)[]
        {
            await Measure("B standalone, rows in one component", async () => await renderer.RenderAsync<PemsTable>(p)),
            await Measure("B standalone, one component per cell", async () => await renderer.RenderAsync<PemsTablePerCell>(p)),
            await Measure("A MVC request, View Component", async () => await host.Client.GetStringAsync("/a/table")),
            await Measure("B MVC request, rows in one component", async () => await host.Client.GetStringAsync("/b/table")),
            await Measure("B MVC request, one component per cell", async () => await host.Client.GetStringAsync("/b/table-percell")),
        };
        output.WriteLine("| Case (1,000 rows x 6 columns) | median ms | html bytes |");
        output.WriteLine("|---|---|---|");
        foreach (var r in results) output.WriteLine($"| {r.Name} | {r.Ms:0.0} | {r.Bytes:N0} |");
        Assert.All(results, r => Assert.True(r.Bytes > 100_000));
    }

    private static async Task<(string, double, int)> Measure(string name, Func<Task<string>> render)
    {
        string last = "";
        for (var i = 0; i < 5; i++) last = await render();            // warm-up (JIT, caches)
        var times = new List<double>();
        for (var i = 0; i < 25; i++)
        {
            var sw = Stopwatch.StartNew();
            last = await render();
            times.Add(sw.Elapsed.TotalMilliseconds);
        }
        times.Sort();
        return (name, times[times.Count / 2], last.Length);
    }

    private static string FindFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, relative))) dir = dir.Parent;
        return dir is null ? throw new FileNotFoundException(relative) : Path.Combine(dir.FullName, relative);
    }
}
