using System.Globalization;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using PEMS.UI.Abstractions;
using PEMS.UI.Components;
using PEMS.UI.Rendering;

namespace PEMS.UI.Tests;

/// <summary>SCRUM-424 / #424: the loading, empty and error states share one look and one accessibility contract; their words are inputs.</summary>
public sealed class StateComponentsTests : IAsyncLifetime
{
    private ServiceProvider _provider = default!;
    private IPemsComponentRenderer _renderer = default!;

    public Task InitializeAsync()
    {
        _provider = new ServiceCollection().AddPemsUi().BuildServiceProvider();
        _renderer = _provider.CreateScope().ServiceProvider.GetRequiredService<IPemsComponentRenderer>();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _provider.DisposeAsync();

    private static TranslatableText T(string fa, string en) => TranslatableText.Of(("fa", fa), ("en", en));
    private static string Plain(string html) => WebUtility.HtmlDecode(html);
    private static void Has(string expected, string actual) => Assert.Contains(expected, actual, StringComparison.Ordinal);
    private static void HasNot(string unexpected, string actual) => Assert.DoesNotContain(unexpected, actual, StringComparison.Ordinal);

    private async Task<string> Render(string key, object options, string culture) =>
        Plain(await _renderer.RenderAsync(key, new Dictionary<string, object?> { ["Options"] = options }, new CultureInfo(culture)));

    private static readonly EmptyStateOptions Empty = new() { Title = T("رکوردی نیست", "No records"), Description = T("اولین رکورد را بسازید", "Create the first record"), Icon = "bi-inbox", Action = new StateAction { Label = T("افزودن", "Add"), Href = "/x/create" } };
    private static readonly ErrorStateOptions Error = new() { Title = T("بارگذاری نشد", "Could not load"), Description = T("دوباره تلاش کنید", "Try again later"), Detail = T("کد ۵۰۳", "Code 503"), Action = new StateAction { Label = T("تلاش دوباره", "Retry"), Href = "/x" } };
    private static readonly LoadingStateOptions Loading = new() { Title = T("در حال بارگذاری", "Loading"), Lines = 3 };

    [Fact]
    public async Task Empty_state_is_a_status_with_title_text_icon_and_action_in_each_language()
    {
        var en = await Render("empty-state", Empty, "en");
        Has("role=\"status\"", en); Has("pems-state--empty", en); Has(">No records<", en); Has(">Create the first record<", en);
        Has("bi-inbox", en); Has("aria-hidden=\"true\"", en); Has("href=\"/x/create\"", en); Has(">Add</a>", en);
        var fa = await Render("empty-state", Empty, "fa");
        Has(">رکوردی نیست<", fa); Has(">افزودن</a>", fa); HasNot("No records", fa);
    }

    [Fact]
    public async Task Empty_state_without_optional_parts_renders_only_the_title()
    {
        var html = await Render("empty-state", new EmptyStateOptions { Title = T("رکوردی نیست", "No records") }, "en");
        Has(">No records<", html);
        HasNot("pems-state__text", html); HasNot("<a ", html); HasNot("<i ", html);
    }

    [Fact]
    public async Task Error_state_is_an_alert_with_detail_and_retry()
    {
        var en = await Render("error-state", Error, "en");
        Has("role=\"alert\"", en); Has("pems-state--error", en); Has(">Could not load<", en); Has("pems-state__detail", en); Has(">Code 503<", en);
        Has("href=\"/x\"", en); Has("pems-btn--neutral", en);
        var fa = await Render("error-state", Error, "fa");
        Has(">بارگذاری نشد<", fa); Has(">تلاش دوباره</a>", fa);
    }

    [Fact]
    public async Task Error_state_without_retry_has_no_link()
    {
        var html = await Render("error-state", new ErrorStateOptions { Title = T("بارگذاری نشد", "Could not load") }, "en");
        HasNot("<a ", html); HasNot("pems-state__detail", html);
    }

    [Fact]
    public async Task Loading_state_is_a_busy_status_with_the_requested_placeholder_lines()
    {
        var en = await Render("loading-state", Loading, "en");
        Has("role=\"status\"", en); Has("aria-busy=\"true\"", en); Has(">Loading<", en);
        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(en, "pems-skeleton__line").Count);
        Has(">در حال بارگذاری<", await Render("loading-state", Loading, "fa"));
        HasNot("pems-skeleton__line", await Render("loading-state", Loading with { Lines = 0 }, "en"));
    }

    [Fact]
    public async Task A_text_with_no_translation_falls_back_instead_of_showing_nothing()
    {
        var html = await Render("empty-state", new EmptyStateOptions { Title = TranslatableText.Of(("en", "No records")) }, "fa");
        Has(">No records<", html);
    }
}
