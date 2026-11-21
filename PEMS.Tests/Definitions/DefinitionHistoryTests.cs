using PEMS.Web.Definitions;

namespace PEMS.Tests.Definitions;

/// <summary>SCRUM-784: the JSON-aware difference of two versions and the reference of a version to export; the history grid itself is a stored page over data sources.</summary>
public sealed class DefinitionHistoryTests
{
    [Fact]
    public void The_difference_names_each_changed_text_by_its_language_pointer()
    {
        var rows = PayloadDiff.Compare("""{ "title": { "en": "Old", "fa": "قدیمی" }, "keep": 1 }""", """{ "title": { "en": "New", "fa": "قدیمی" }, "keep": 1 }""");
        var row = Assert.Single(rows);
        Assert.Equal(new PayloadDifference("/title/en", "changed", "Old", "New"), row);
    }

    [Fact]
    public void Added_and_removed_properties_and_array_items_are_listed()
    {
        var rows = PayloadDiff.Compare("""{ "fields": [ { "name": "a" }, { "name": "b" } ], "gone": true }""", """{ "fields": [ { "name": "a" } ], "new": 5 }""");
        Assert.Contains(new PayloadDifference("/fields/1/name", "removed", "b", null), rows);
        Assert.Contains(new PayloadDifference("/gone", "removed", "true", null), rows);
        Assert.Contains(new PayloadDifference("/new", "added", null, "5"), rows);
        Assert.Equal(3, rows.Count);
    }

    [Fact]
    public void Equal_payloads_have_no_difference_and_a_pointer_escapes_slash_and_tilde()
    {
        Assert.Empty(PayloadDiff.Compare("""{ "a": [1, 2] }""", """{ "a": [1, 2] }"""));
        var rows = PayloadDiff.Compare("""{ "a/b": 1, "c~d": 1 }""", """{ "a/b": 2, "c~d": 2 }""");
        Assert.Contains(rows, r => r.Pointer == "/a~1b");
        Assert.Contains(rows, r => r.Pointer == "/c~0d");
    }

    [Theory]
    [InlineData("core:page:home:3", "core:page:home", 3)]
    [InlineData("core:page:home", "core:page:home", null)]
    public void A_version_reference_is_the_definition_reference_and_the_number(string id, string reference, int? version)
        => Assert.Equal((reference, version), DefinitionExportAction.Parse(id));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("core:page")]
    [InlineData("core:page:home:x")]
    [InlineData("core:page:home:0")]
    [InlineData("core::home")]
    [InlineData("a:b:c:1:2")]
    public void Anything_else_is_not_a_version_reference(string? id) => Assert.Null(DefinitionExportAction.Parse(id));
}
