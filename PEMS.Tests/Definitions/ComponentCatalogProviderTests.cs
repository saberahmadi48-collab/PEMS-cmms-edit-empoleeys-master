using PEMS.Application.Grids;
using PEMS.UI.Manifest;
using PEMS.Web.Definitions;
using Microsoft.Extensions.DependencyInjection;

namespace PEMS.Tests.Definitions;

/// <summary>SCRUM-340: the component catalog is served as grid rows (list and details are stored pages over them).</summary>
public sealed class ComponentCatalogProviderTests
{
    private static ComponentCatalogGridProvider Provider()
    {
        var sp = PEMS.UI.ServiceCollectionExtensions.AddPemsUi(new ServiceCollection()).BuildServiceProvider().CreateScope().ServiceProvider;
        return new ComponentCatalogGridProvider(sp.GetRequiredService<ComponentManifestCatalog>());
    }

    private static GridDataRequest Request(int size = 500, string? search = null, params GridFilter[] filters)
        => new(new GridRequest(1, size, [], filters, search), "en", [], "hamid", new Dictionary<string, string?>());

    [Fact]
    public async Task Every_registered_component_is_a_row_with_its_parameters_and_sample()
    {
        var (items, total) = await Provider().QueryAsync(Request(), default);
        Assert.True(total >= 100);
        var row = (Dictionary<string, object?>)items.Single(i => (string)((Dictionary<string, object?>)i)["key"]! == "record-view");
        Assert.Equal("display", row["category"]);
        Assert.Contains("options*", (string)row["parameters"]!);   // one required Options record (SCRUM-743: the host-set flags are not options)
        Assert.Contains("\"fields\"", (string)row["sample"]!);
    }

    [Fact]
    public async Task Search_filter_and_paging_work_and_one_key_is_one_row()
    {
        var provider = Provider();
        var (found, count) = await provider.QueryAsync(Request(search: "data-"), default);
        Assert.All(found, i => Assert.Contains("data-", (string)((Dictionary<string, object?>)i)["key"]!));
        Assert.Equal(count, found.Count);
        var (one, _) = await provider.QueryAsync(Request(1, null, new GridFilter("key", "eq", "data-grid")), default);
        Assert.Equal("data-grid", ((Dictionary<string, object?>)Assert.Single(one))["key"]);
        var (paged, all) = await provider.QueryAsync(Request(5), default);
        Assert.Equal(5, paged.Count);
        Assert.True(all > 5);
    }
}
