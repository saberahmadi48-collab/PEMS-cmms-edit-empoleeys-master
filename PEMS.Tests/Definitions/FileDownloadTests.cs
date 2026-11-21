using System.Text.Json.Nodes;
using PEMS.Web.Definitions;

namespace PEMS.Tests.Definitions;

/// <summary>SCRUM-615: file.download serves only the folder the definition names, and its settings are checked when the definition is loaded.</summary>
public sealed class FileDownloadTests
{
    [Theory]
    [InlineData("vdr/comments", "vdr/comments/3f2a.pdf", true)]
    [InlineData("vdr/comments", "vdr/comments/sub/3f2a.pdf", true)]
    [InlineData("vdr/comments", "vdr/other/3f2a.pdf", false)]
    [InlineData("vdr/comments", "vdr/comments-secret/3f2a.pdf", false)]
    [InlineData("vdr/comments", "vdr/comments/../letters/1.pdf", false)]
    [InlineData("vdr/comments", "vdr/comments/a\\b.pdf", false)]
    [InlineData("vdr/comments", "/vdr/comments/1.pdf", false)]
    [InlineData("vdr/comments", "C:vdr/comments/1.pdf", false)]
    [InlineData("vdr/comments", "", false)]
    [InlineData("vdr/comments", null, false)]
    public void A_key_is_served_only_inside_the_folder_of_the_definition(string prefix, string? key, bool expected)
        => Assert.Equal(expected, FileDownloadAction.KeyAllowed(prefix, key));

    [Theory]
    [InlineData("""{ "prefix": "vdr/comments" }""", null)]
    [InlineData("""{ "prefix": "vdr/comments", "field": "fileKey", "fileName": "report.pdf", "maxMb": 20 }""", null)]
    [InlineData("""{ }""", "/prefix")]
    [InlineData("""{ "prefix": "../etc" }""", "/prefix")]
    [InlineData("""{ "prefix": "a//b" }""", "/prefix")]
    [InlineData("""{ "prefix": "a", "maxMb": 0 }""", "/maxMb")]
    [InlineData("""{ "prefix": "a", "field": "1x" }""", "/field")]
    [InlineData("""{ "prefix": "a", "path": "x" }""", "/path")]
    [InlineData("""{ "prefix": "a", "authorizeBy": { "dataSource": "vdr-comments-grid", "filter": "fileKey" } }""", null)]
    [InlineData("""{ "prefix": "a", "authorizeBy": { "filter": "fileKey" } }""", "/authorizeBy/dataSource")]
    [InlineData("""{ "prefix": "a", "authorizeBy": { "dataSource": "x" } }""", "/authorizeBy/filter")]
    [InlineData("""{ "prefix": "a", "authorizeBy": { "dataSource": "x", "filter": "1k" } }""", "/authorizeBy/filter")]
    [InlineData("""{ "prefix": "a", "authorizeBy": { "dataSource": "x", "filter": "k", "any": 1 } }""", "/authorizeBy/any")]
    [InlineData("""{ "prefix": "a", "authorizeBy": "x" }""", "/authorizeBy")]
    public void The_settings_are_checked_when_the_definition_is_loaded(string parameters, string? pointer)
    {
        var errors = new FileDownloadAction(null!).ValidateParameters((JsonObject)JsonNode.Parse(parameters)!);
        if (pointer is null) Assert.Empty(errors);
        else Assert.Contains(errors, e => e.Pointer == pointer);
    }
}
