using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using PEMS.Application.Definitions;
using PEMS.Domain.Entities;
using PEMS.Persistence.Definitions;
using PEMS.Persistence.Seed;
using PEMS.Tests.TestSupport;
using PEMS.UI.Manifest;
using PEMS.UI.Rendering;
using PEMS.Web.Controllers;
using PEMS.Web.Definitions;

namespace PEMS.Tests.Definitions;

/// <summary>SCRUM-342 (the W1 pilot): the Departments list is a page definition (layout list-page, a button and a data grid) over the grid endpoint of the existing controller; the create form is the form definition of the form engine.</summary>
public sealed class PilotPagesTests
{
    private static readonly string Package = Path.Combine(AppContext.BaseDirectory, SystemDefinitionSeeder.FolderName);

    private static (IServiceProvider Ui, IDefinitionKind[] Kinds) Setup()
    {
        var ui = PEMS.UI.ServiceCollectionExtensions.AddPemsUi(new ServiceCollection()).BuildServiceProvider().CreateScope().ServiceProvider;
        var registry = ui.GetRequiredService<IPemsComponentRegistry>();
        var catalog = ui.GetRequiredService<ComponentManifestCatalog>();
        return (ui, ShippedKinds.Build(registry, catalog));
    }

    [Fact]
    public async Task The_departments_page_is_a_valid_shipped_definition_that_renders_the_grid_and_the_new_button_in_both_languages()
    {
        var (ui, kinds) = Setup();
        using var files = new FileDefinitionStore(kinds, new DefinitionLimits(), new DefinitionFileOptions { Folder = Package, Watch = false }, FilesOwnership.For(kinds));
        var page = await files.GetPublishedAsync("page", "departments");
        Assert.NotNull(page);
        Assert.Empty(files.Problems);

        var renderer = new PageRenderer(ui.GetRequiredService<IPemsComponentRenderer>(), ui.GetRequiredService<ComponentManifestCatalog>(), files);
        var resolved = new ResolvedDefinition(page!, JsonNode.Parse(page!.PayloadJson)!);
        var request = new PipelineRequest("page", "departments", "core", "en", ["SystemAdmin"], "hamid");
        var en = await renderer.RenderAsync(resolved, request, new CultureInfo("en"));
        Assert.Empty(en.Problems);
        var html = System.Net.WebUtility.HtmlDecode(en.Html);
        Assert.Contains("/d/departments-grid", html);
        Assert.Contains("data-pems-grid", html);
        Assert.Contains("href=\"/f/departments-create\"", html);
        Assert.Contains("New department", html);
        Assert.Equal("Departments", en.Title);

        var fa = await renderer.RenderAsync(resolved, request with { Culture = "fa" }, new CultureInfo("fa"));
        Assert.Contains("دپارتمان جدید", System.Net.WebUtility.HtmlDecode(fa.Html));
        Assert.Equal("دپارتمان‌ها", fa.Title);
    }

    [Fact]
    public void Every_binding_of_the_shipped_definitions_points_at_a_definition_that_ships()
    {
        var (_, kinds) = Setup();
        var package = SystemDefinitionSeeder.LoadPackage(kinds, new DefinitionLimits(), Package);
        var shipped = package.Select(d => (d.Kind, d.Key)).ToHashSet();
        var broken = new List<string>();
        foreach (var d in package)
            foreach (var r in kinds.First(k => k.Key == d.Kind).References(d.PayloadJson))
                if (r.ToKind != "component" && !shipped.Contains((r.ToKind == "component-definition" ? "component" : r.ToKind, r.ToKey))) broken.Add($"{d.Kind}/{d.Key} -> {r.ToKind}/{r.ToKey}");
        Assert.Empty(broken);
        // the pilot page really binds its grid to the data source and the form to its action and options
        Assert.Contains(kinds.First(k => k.Key == "page").References(package.First(d => d.Key == "departments" && d.Kind == "page").PayloadJson), r => r is { ToKind: "data-source", ToKey: "departments-grid" });
        Assert.Contains(kinds.First(k => k.Key == "form").References(package.First(d => d.Key == "departments-create" && d.Kind == "form").PayloadJson), r => r is { ToKind: "action", ToKey: "departments-create" });
    }
}
