using System.Text.Json.Nodes;
using PEMS.UI.Components;
using PEMS.UI.Manifest;

namespace PEMS.UI.Tests;

/// <summary>SCRUM-339: the comments thread (replies, status, action) and the audit trail (field, old to new, who, when) draw localized entries, newest or oldest first as their kind needs, and are checked by the manifest.</summary>
public sealed class ThreadAuditTests
{
    private static string Render(string key, string optionsType, string culture)
    {
        object options = key == "comments-thread"
            ? System.Text.Json.JsonSerializer.Deserialize<CommentsThreadOptions>(ComponentSamples.Get(key)!.ToJsonString(), PEMS.UI.Abstractions.PemsJson.Options)!
            : System.Text.Json.JsonSerializer.Deserialize<AuditTrailOptions>(ComponentSamples.Get(key)!.ToJsonString(), PEMS.UI.Abstractions.PemsJson.Options)!;
        return System.Net.WebUtility.HtmlDecode(UiTestRenderer.Instance.RenderAsync(key, new Dictionary<string, object?> { ["Options"] = options }, new System.Globalization.CultureInfo(culture)).GetAwaiter().GetResult());
    }

    [Fact]
    public void The_thread_lists_comments_oldest_first_with_replies_status_and_action()
    {
        var html = Render("comments-thread", "", "en");
        Assert.True(html.IndexOf("Please attach the drawing.", StringComparison.Ordinal) < html.IndexOf("Second question", StringComparison.Ordinal));
        Assert.True(html.IndexOf("Please attach the drawing.", StringComparison.Ordinal) < html.IndexOf("Attached.", StringComparison.Ordinal));
        Assert.Contains("pems-thread__item pems-thread__item--reply", html);
        Assert.Contains("pems-thread__status pems-thread__status--success\">Answered", html);
        Assert.Contains("<a class=\"pems-thread__action\" href=\"/x/reply/1\">Reply</a>", html);
    }

    [Fact]
    public void The_trail_lists_changes_newest_first_and_writes_the_text_for_an_empty_value()
    {
        var html = Render("audit-trail", "", "en");
        Assert.True(html.IndexOf("Approved", StringComparison.Ordinal) < html.IndexOf("M. Karimi", StringComparison.Ordinal));   // 10 November before 9 November
        Assert.Contains("(empty)", html);                                          // the owner had no old value
        Assert.Contains("<th scope=\"col\">Who</th>", html);
        Assert.Contains("(خالی)", Render("audit-trail", "", "fa"));
    }

    [Theory]
    [InlineData("comments-thread", "هنوز نظری نیست")]
    [InlineData("audit-trail", "هنوز تغییری نیست")]
    public void Without_entries_the_empty_text_follows_the_language(string key, string fa)
    {
        var sample = JsonNode.Parse(ComponentSamples.Get(key)!.ToJsonString())!.AsObject();
        sample.Remove(key == "audit-trail" ? "entries" : "comments");
        object options = key == "audit-trail"
            ? System.Text.Json.JsonSerializer.Deserialize<AuditTrailOptions>(sample.ToJsonString(), PEMS.UI.Abstractions.PemsJson.Options)!
            : System.Text.Json.JsonSerializer.Deserialize<CommentsThreadOptions>(sample.ToJsonString(), PEMS.UI.Abstractions.PemsJson.Options)!;
        string Go(string c) => System.Net.WebUtility.HtmlDecode(UiTestRenderer.Instance.RenderAsync(key, new Dictionary<string, object?> { ["Options"] = options }, new System.Globalization.CultureInfo(c)).GetAwaiter().GetResult());
        Assert.Contains(fa, Go("fa"));
        Assert.DoesNotContain("<table", Go("en"));
    }

    [Theory]
    [InlineData("comments-thread")]
    [InlineData("audit-trail")]
    public void The_options_are_checked_by_the_manifest(string key)
    {
        var catalog = new ComponentManifestCatalog(new PEMS.UI.Rendering.PemsComponentRegistry());
        var ok = ComponentSamples.Get(key)!.ToJsonString();
        Assert.True(catalog.Validate(key, ok).IsValid);
        var bad = JsonNode.Parse(ok)!.AsObject();
        bad["nonsense"] = 1;
        Assert.False(catalog.Validate(key, bad.ToJsonString()).IsValid);
    }
}
