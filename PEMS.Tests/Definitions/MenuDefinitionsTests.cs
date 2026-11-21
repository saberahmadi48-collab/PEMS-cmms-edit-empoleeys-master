using PEMS.Application.Definitions;
using PEMS.Domain.Entities;
using PEMS.Web.Navigation;

namespace PEMS.Tests.Definitions;

/// <summary>SCRUM-590: the menu as a definition: the feed is validated when it is loaded and becomes the records the sidebar sync takes; no code and no controller for a new page in the menu.</summary>
public sealed class MenuDefinitionsTests
{
    private static readonly MenuDefinitionKind Kind = new();

    private const string Feed = """
        { "items": [
            { "key": "stock", "kind": "section", "order": 400, "title": { "en": "Stock", "fa": "موجودی" }, "icon": "bi-box", "rule": "Always" },
            { "key": "stock.list", "parent": "stock", "kind": "link", "order": 410, "title": { "en": "Stock list", "fa": "فهرست موجودی" }, "icon": "bi-list", "rule": "Perm:Stock.View", "page": "stock.list" },
            { "key": "stock.new", "parent": "stock", "kind": "link", "order": 420, "titleKey": "New stock", "rule": "Perm:Stock.Create & Flag:Replenishment", "form": "stock.new", "newTab": true },
            { "key": "stock.help", "parent": "stock", "kind": "link", "order": 430, "title": { "en": "Help", "fa": "راهنما" }, "rule": "Role:Admin | Always", "url": "/Help/Stock" }
        ] }
        """;

    private static IReadOnlyList<DefinitionFieldError> Validate(string payload) => Kind.Validate(payload, new DefinitionLimits());

    [Fact]
    public void A_good_feed_is_valid_and_its_links_are_references_to_the_pages_and_forms_they_open()
    {
        Assert.Empty(Validate(Feed));
        var refs = Kind.References(Feed);
        Assert.Equal([("page", "stock.list"), ("form", "stock.new")], refs.Select(r => (r.ToKind, r.ToKey)));
    }

    [Theory]
    [InlineData("""{ "items": [] }""", "/items", "required")]
    [InlineData("""{ "items": [ { "key": "A B", "kind": "section", "order": 1, "titleKey": "T", "rule": "Always" } ] }""", "/items/0/key", "invalid-key")]
    [InlineData("""{ "items": [ { "key": "a", "kind": "section", "order": 1, "titleKey": "T", "rule": "Always" }, { "key": "a", "kind": "section", "order": 2, "titleKey": "T", "rule": "Always" } ] }""", "/items/1/key", "duplicate")]
    [InlineData("""{ "items": [ { "key": "a", "kind": "tile", "order": 1, "titleKey": "T", "rule": "Always" } ] }""", "/items/0/kind", "unknown-kind")]
    [InlineData("""{ "items": [ { "key": "a", "kind": "section", "order": -1, "titleKey": "T", "rule": "Always" } ] }""", "/items/0/order", "range")]
    [InlineData("""{ "items": [ { "key": "a", "kind": "section", "order": 1, "rule": "Always" } ] }""", "/items/0/title", "required")]
    [InlineData("""{ "items": [ { "key": "a", "kind": "section", "order": 1, "title": { "en": "T" }, "rule": "Always" } ] }""", "/items/0/title", "missing-translation")]
    [InlineData("""{ "items": [ { "key": "a", "kind": "section", "order": 1, "titleKey": "T", "rule": "Always", "icon": "fa-box" } ] }""", "/items/0/icon", "invalid-icon")]
    [InlineData("""{ "items": [ { "key": "a", "kind": "section", "order": 1, "titleKey": "T" } ] }""", "/items/0/rule", "invalid-rule")]
    [InlineData("""{ "items": [ { "key": "a", "kind": "section", "order": 1, "titleKey": "T", "rule": "Whenever:Now" } ] }""", "/items/0/rule", "invalid-rule")]
    [InlineData("""{ "items": [ { "key": "a", "kind": "link", "order": 1, "titleKey": "T", "rule": "Always" } ] }""", "/items/0/page", "target")]
    [InlineData("""{ "items": [ { "key": "a", "kind": "link", "order": 1, "titleKey": "T", "rule": "Always", "page": "p", "form": "f" } ] }""", "/items/0/page", "target")]
    [InlineData("""{ "items": [ { "key": "a", "kind": "link", "order": 1, "titleKey": "T", "rule": "Always", "url": "//evil.example/x" } ] }""", "/items/0/url", "invalid-target")]
    [InlineData("""{ "items": [ { "key": "a", "kind": "link", "order": 1, "titleKey": "T", "rule": "Always", "url": "https://evil.example/x" } ] }""", "/items/0/url", "invalid-target")]
    [InlineData("""{ "items": [ { "key": "a", "kind": "section", "order": 1, "titleKey": "T", "rule": "Always", "page": "p" } ] }""", "/items/0/page", "target")]
    [InlineData("""{ "items": [ { "key": "a", "parent": "a", "kind": "section", "order": 1, "titleKey": "T", "rule": "Always" } ] }""", "/items/0/parent", "invalid-key")]
    [InlineData("""{ "items": [ { "key": "a", "kind": "section", "order": 1, "titleKey": "T", "rule": "Always", "color": "red" } ] }""", "/items/0/color", "unknown-property")]
    public void A_bad_feed_is_refused_with_the_item_and_the_reason(string payload, string pointer, string code)
        => Assert.Contains(Validate(payload), e => e.Pointer == pointer && e.Code == code);

    [Fact]
    public void The_feed_becomes_the_records_the_sidebar_sync_takes_with_titles_targets_and_the_visibility_rule()
    {
        var published = new PublishedDefinition("menu", "stock", "core", 1, Feed, "h");
        var items = MenuDefinitions.From([published]).ToDictionary(i => i.Key);
        Assert.Equal(4, items.Count);

        var section = items["stock"];
        Assert.Equal((NavigationMenuItemKinds.Section, 400, "bi-box", "Always", (string?)null), (section.Kind, section.SortOrder, section.Icon, section.VisibilityRule, section.ExternalPath));
        Assert.Equal(("Stock", "موجودی", "Stock"), (section.TitleEn, section.TitleFa, section.TitleKey));

        var page = items["stock.list"];
        Assert.Equal((NavigationMenuItemKinds.Link, "stock", "/p/stock.list", "Perm:Stock.View"), (page.Kind, page.ParentKey, page.ExternalPath, page.VisibilityRule));

        var form = items["stock.new"];
        Assert.Equal(("/f/stock.new", true, "New stock", "Perm:Stock.Create & Flag:Replenishment"), (form.ExternalPath, form.OpenInNewTab, form.TitleKey, form.VisibilityRule));
        Assert.Null(form.TitleEn);                                                          // a titleKey item has no shipped titles: the localization key is the title

        Assert.Equal("/Help/Stock", items["stock.help"].ExternalPath);
    }

    [Fact]
    public void Several_menu_definitions_are_read_in_a_stable_order_so_the_sync_is_repeatable()
    {
        var a = new PublishedDefinition("menu", "b-second", "core", 1, """{ "items": [ { "key": "b", "kind": "section", "order": 2, "titleKey": "B", "rule": "Always" } ] }""", "h2");
        var b = new PublishedDefinition("menu", "a-first", "core", 1, """{ "items": [ { "key": "a", "kind": "section", "order": 1, "titleKey": "A", "rule": "Always" } ] }""", "h1");
        Assert.Equal(["a", "b"], MenuDefinitions.From([a, b]).Select(i => i.Key));
        Assert.Equal(["a", "b"], MenuDefinitions.From([b, a]).Select(i => i.Key));
    }
}
