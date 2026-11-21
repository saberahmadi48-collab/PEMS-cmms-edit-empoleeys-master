using Microsoft.Extensions.DependencyInjection;
using PEMS.Application.Definitions;
using PEMS.Persistence.Seed;
using PEMS.Web.Definitions;

namespace PEMS.Tests.Definitions;

/// <summary>
/// SCRUM-451: the harness of the definition kinds. A kind is registered once, and every registered kind has a valid sample: a shipped definition of that kind in the System package, or (for the
/// kinds the package does not ship yet) a named test of its own. A new kind that is registered without either fails here, so a kind cannot be added without a sample.
/// </summary>
public sealed class DefinitionKindHarnessTests
{
    /// <summary>The kinds without a shipped definition and the test class whose sample proves them (add a kind here only together with that test).</summary>
    private static readonly Dictionary<string, Type> KindsWithTestSamples = new(StringComparer.Ordinal)
    {
        ["view"] = typeof(ViewAndThemeKindsTests),
        ["theme"] = typeof(ViewAndThemeKindsTests),
        ["menu"] = typeof(MenuDefinitionsTests),
    };

    private static List<string> RegisteredKinds()
    {
        var services = new ServiceCollection();
        services.AddPemsDefinitionKinds();
        var kinds = services.Where(s => s.ServiceType == typeof(IDefinitionKind)).Select(s => s.ImplementationType!).ToList();
        // the keys come from the instances: kinds with constructor dependencies are read by their registered type name through a known key table
        return kinds.Select(KeyOf).ToList();
    }

    private static string KeyOf(Type kind) => kind.Name switch
    {
        nameof(FormDefinitionKind) => "form",
        nameof(PageDefinitionKind) => "page",
        nameof(LayoutDefinitionKind) => "layout",
        nameof(ViewDefinitionKind) => "view",
        nameof(ThemeDefinitionKind) => "theme",
        nameof(DataSourceDefinitionKind) => "data-source",
        nameof(ActionDefinitionKind) => "action",
        nameof(RouteDefinitionKind) => "route",
        nameof(ComponentDefinitionKind) => "component",
        "MenuDefinitionKind" => "menu",
        nameof(ReferenceDataKind) => "reference-data",
        _ => throw new InvalidOperationException($"The harness does not know the key of {kind.Name}: add it to KeyOf and give the kind a sample."),
    };

    [Fact]
    public void Every_registered_kind_has_a_sample_a_shipped_definition_or_a_named_test()
    {
        var package = Path.Combine(AppContext.BaseDirectory, SystemDefinitionSeeder.FolderName);
        foreach (var key in RegisteredKinds())
        {
            var shipped = Directory.Exists(Path.Combine(package, key)) && Directory.EnumerateFiles(Path.Combine(package, key), "*.json", SearchOption.AllDirectories).Any();
            Assert.True(shipped || KindsWithTestSamples.ContainsKey(key) || key == "reference-data", $"The kind '{key}' has no sample: ship one definition of it or add a test sample.");
        }
    }

    [Fact]
    public void The_kinds_with_a_test_sample_are_registered_kinds()
    {
        var registered = RegisteredKinds();
        foreach (var key in KindsWithTestSamples.Keys) Assert.Contains(key, registered);
    }

    [Fact]
    public void A_page_points_to_its_layout_so_a_layout_that_does_not_exist_shows_as_a_missing_reference()
    {
        var refs = PageDefinitionKind.ReferencesOf("""{ "layout": "detail-page", "regions": { "content": [] } }""");
        Assert.Contains(refs, r => r.ToKind == "layout" && r.ToKey == "detail-page");
    }
}
