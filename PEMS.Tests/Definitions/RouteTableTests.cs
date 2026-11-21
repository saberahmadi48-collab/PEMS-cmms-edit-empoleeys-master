using PEMS.Application.Definitions;
using PEMS.Web.Definitions;

namespace PEMS.Tests.Definitions;

/// <summary>SCRUM-768: the route table maps a URL pattern to a generic host from definitions; it validates what it is given and never shadows a code route.</summary>
public sealed class RouteTableTests
{
    private sealed class Store(params (string Kind, string Key, string Payload)[] items) : IDefinitionStore
    {
        public Task<PublishedDefinition?> GetPublishedAsync(string kind, string key, string appKey = "core", CancellationToken ct = default)
            => Task.FromResult(items.Where(i => i.Kind == kind && i.Key == key).Select(i => (PublishedDefinition?)Def(i)).FirstOrDefault());

        public Task<IReadOnlyList<PublishedDefinition>> ListPublishedAsync(string kind, string appKey = "core", CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<PublishedDefinition>>(items.Where(i => i.Kind == kind).Select(Def).ToList());

        private static PublishedDefinition Def((string Kind, string Key, string Payload) i) => new(i.Kind, i.Key, "core", 1, i.Payload, i.Payload.GetHashCode().ToString());
    }

    private static readonly RouteDefinitionKind Kind = new();
    private static IReadOnlyList<DefinitionFieldError> Validate(string payload) => Kind.Validate(payload, new DefinitionLimits());

    [Fact]
    public void A_route_with_a_pattern_and_a_generic_host_target_is_valid_and_references_its_target()
    {
        const string route = "{\"pattern\":\"WorkOrder/Details/{id}\",\"target\":\"/p/work-order-details?id={id}\"}";
        Assert.Empty(Validate(route));
        var refs = Kind.References(route);
        Assert.Equal(new DefinitionRef("page", "work-order-details", "routes-to"), Assert.Single(refs));
    }

    [Theory]
    [InlineData("{\"pattern\":\"/WorkOrder\",\"target\":\"/p/x\"}", "/pattern")]                 // leading slash
    [InlineData("{\"pattern\":\"api/users\",\"target\":\"/p/x\"}", "/pattern")]                  // reserved prefix
    [InlineData("{\"pattern\":\"a/{id\",\"target\":\"/p/x\"}", "/pattern")]                      // broken template
    [InlineData("{\"pattern\":\"WorkOrder\",\"target\":\"/Admin/Users\"}", "/target")]           // not a generic host
    [InlineData("{\"pattern\":\"WorkOrder/{id}\",\"target\":\"/p/x?id={other}\"}", "/target")]   // unknown placeholder
    [InlineData("{\"pattern\":\"WorkOrder\"}", "/target")]                                       // missing target
    [InlineData("{\"pattern\":\"WorkOrder\",\"target\":\"/p/x\",\"extra\":1}", "/extra")]        // unknown property
    public void A_bad_route_is_refused_with_the_pointer_of_the_problem(string payload, string pointer)
        => Assert.Contains(Validate(payload), e => e.Pointer == pointer);

    [Fact]
    public async Task A_url_with_values_resolves_to_the_target_with_the_captured_values_and_the_query_string()
    {
        var table = new RouteTable(new Store(("route", "wo-details", "{\"pattern\":\"WorkOrder/Details/{id}\",\"target\":\"/p/work-order-details?id={id}\"}")));
        var match = await table.MatchAsync("/WorkOrder/Details/42", "?tab=parts");
        Assert.NotNull(match);
        Assert.Equal("/p/work-order-details?id=42&tab=parts", match!.Location);
        Assert.False(match.Permanent);
    }

    [Fact]
    public async Task A_permanent_route_is_marked_and_an_unknown_url_resolves_to_nothing()
    {
        var table = new RouteTable(new Store(("route", "old", "{\"pattern\":\"OldList\",\"target\":\"/p/new-list\",\"permanent\":true}")));
        Assert.True((await table.MatchAsync("/OldList", null))!.Permanent);
        Assert.Null(await table.MatchAsync("/Nothing/here", null));
    }

    [Fact]
    public async Task A_page_that_declares_a_route_is_served_at_it_and_a_route_definition_wins_over_a_page_route()
    {
        var store = new Store(
            ("page", "departments", "{\"layout\":\"list-page\",\"route\":\"departments\",\"regions\":{}}"),
            ("page", "other", "{\"layout\":\"list-page\",\"route\":\"alias\",\"regions\":{}}"),
            ("route", "alias-route", "{\"pattern\":\"alias\",\"target\":\"/p/departments\"}"));
        var table = new RouteTable(store);
        Assert.Equal("/p/departments", (await table.MatchAsync("/departments", null))!.Location);
        Assert.Equal("/p/departments", (await table.MatchAsync("/alias", null))!.Location);
    }

    [Fact]
    public async Task Values_are_escaped_so_a_crafted_segment_cannot_change_the_target()
    {
        var table = new RouteTable(new Store(("route", "r", "{\"pattern\":\"x/{id}\",\"target\":\"/p/page?id={id}\"}")));
        var match = await table.MatchAsync("/x/1%26admin%3Dtrue", null);
        Assert.NotNull(match);
        Assert.DoesNotContain("&admin", match!.Location);
    }
}
