using System.Text.Json.Nodes;
using PEMS.UI.Components;
using PEMS.UI.Manifest;

namespace PEMS.UI.Tests;

/// <summary>SCRUM-58: the workflow history timeline draws newest first, in the language of the request, and its options are checked by the manifest.</summary>
public sealed class WorkflowHistoryTests
{
    private static WorkflowHistoryOptions Sample() => System.Text.Json.JsonSerializer.Deserialize<WorkflowHistoryOptions>(ComponentSamples.Get("workflow-history")!.ToJsonString(), PEMS.UI.Abstractions.PemsJson.Options)!;

    private static string Render(WorkflowHistoryOptions o, string culture)
        => UiTestRenderer.Instance.RenderAsync("workflow-history", new Dictionary<string, object?> { ["Options"] = o }, new System.Globalization.CultureInfo(culture)).GetAwaiter().GetResult();

    [Fact]
    public void Entries_are_listed_newest_first_with_tone_route_actor_and_comment()
    {
        var html = System.Net.WebUtility.HtmlDecode(Render(Sample(), "en"));
        Assert.True(html.IndexOf("Approved", StringComparison.Ordinal) < html.IndexOf("Returned", StringComparison.Ordinal));
        Assert.Contains("pems-wfh__item--success", html);
        Assert.Contains("pems-wfh__item--warning", html);
        Assert.Contains("Review → Approval", html);
        Assert.Contains("A. Rezaei", html);
        Assert.Contains("<blockquote class=\"pems-wfh__comment\">Looks good</blockquote>", html);
    }

    [Fact]
    public void An_empty_history_writes_the_empty_text_in_the_language_of_the_request()
    {
        var o = Sample() with { Entries = [] };
        Assert.Contains("No history yet", Render(o, "en"));
        Assert.Contains("هنوز تاریخچه‌ای نیست", System.Net.WebUtility.HtmlDecode(Render(o, "fa")));
    }

    private static WorkflowActionBarOptions Bar() => System.Text.Json.JsonSerializer.Deserialize<WorkflowActionBarOptions>(ComponentSamples.Get("workflow-action-bar")!.ToJsonString(), PEMS.UI.Abstractions.PemsJson.Options)!;

    private static string RenderBar(WorkflowActionBarOptions o, string culture)
        => UiTestRenderer.Instance.RenderAsync("workflow-action-bar", new Dictionary<string, object?> { ["Options"] = o }, new System.Globalization.CultureInfo(culture)).GetAwaiter().GetResult();

    [Fact]
    public void The_action_bar_posts_the_action_type_and_comment_with_the_token_and_shows_denied_actions_disabled()
    {
        var html = System.Net.WebUtility.HtmlDecode(RenderBar(Bar(), "en"));
        Assert.Contains("action=\"/Permit/WorkflowAction\"", html);
        Assert.Contains("name=\"__RequestVerificationToken\"", html);
        Assert.Contains("name=\"id\" value=\"12\"", html);
        Assert.Contains("name=\"comment\"", html);
        Assert.Contains("name=\"actionType\" value=\"Approve\"", html);
        Assert.Contains("data-pems-confirm=\"Reject this item?\"", html);
        Assert.Matches("value=\"Reject\"[^>]*data-pems-require=\"comment\"", html);
        Assert.Contains("Return: Not your stage", html);
        Assert.Contains("→ Review", html);
        Assert.Matches("value=\"Return\"[^>]*disabled", html);
        Assert.Contains("title=\"Not your stage\"", html);
    }

    [Fact]
    public void Without_actions_the_bar_says_so_in_the_language_of_the_request()
    {
        var o = Bar() with { Actions = [] };
        Assert.Contains("No action available", RenderBar(o, "en"));
        Assert.Contains("اقدامی در دسترس نیست", System.Net.WebUtility.HtmlDecode(RenderBar(o, "fa")));
        Assert.True(new PEMS.UI.Manifest.ComponentManifestCatalog(new PEMS.UI.Rendering.PemsComponentRegistry()).Validate("workflow-action-bar", ComponentSamples.Get("workflow-action-bar")!.ToJsonString()).IsValid);
    }

    [Fact]
    public void The_options_are_checked_by_the_manifest()
    {
        var catalog = new ComponentManifestCatalog(new PEMS.UI.Rendering.PemsComponentRegistry());
        var ok = ComponentSamples.Get("workflow-history")!.ToJsonString();
        Assert.True(catalog.Validate("workflow-history", ok).IsValid);
        var bad = JsonNode.Parse(ok)!.AsObject();
        bad["nonsense"] = 1;
        Assert.False(catalog.Validate("workflow-history", bad.ToJsonString()).IsValid);
    }
}
