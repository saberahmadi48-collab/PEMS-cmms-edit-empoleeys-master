using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using PEMS.UI.Rendering;

namespace PEMS.UI.Tests.Host;

/// <summary>An in-process MVC application (no browser, no database) that consumes PEMS.UI the way PEMS.Web would.</summary>
public sealed class SpikeHost : IAsyncLifetime
{
    private WebApplication _app = default!;
    public HttpClient Client { get; private set; } = default!;
    public IServiceProvider Services => _app.Services;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(SpikeHost).Assembly.GetName().Name,
            EnvironmentName = "Development"
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddPemsUi();
        // the same registration PEMS.Web does (SCRUM-421)
        builder.Services.AddControllersWithViews(options => PEMS.UI.Binding.MvcOptionsExtensions.AddPemsInputBinding(options))
            .AddApplicationPart(typeof(SpikeController).Assembly)
            .AddApplicationPart(typeof(PemsTableViewComponent).Assembly);
        _app = builder.Build();
        // Same setup as PEMS.Web (Program.cs): cultures "en" and "fa", and the Persian culture keeps the GREGORIAN calendar, because
        // .NET's default for "fa" is the Persian calendar and every stored date is Gregorian. The culture is chosen with ?culture=fa&ui-culture=fa.
        var cultures = new[] { "en", "fa" }.Select(name =>
        {
            var c = new System.Globalization.CultureInfo(name);
            if (c.TwoLetterISOLanguageName == "fa") c.DateTimeFormat.Calendar = new System.Globalization.GregorianCalendar();
            return c;
        }).ToList();
        _app.UseRequestLocalization(new Microsoft.AspNetCore.Builder.RequestLocalizationOptions
        {
            DefaultRequestCulture = new Microsoft.AspNetCore.Localization.RequestCulture(cultures[0], cultures[0]),
            SupportedCultures = cultures,
            SupportedUICultures = cultures
        });
        _app.UseRouting();
        _app.MapControllers();
        await _app.StartAsync();
        Client = _app.GetTestClient();
    }

    public async Task DisposeAsync() => await _app.DisposeAsync();

    private static readonly Regex TokenRx = new("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"", RegexOptions.Compiled);

    /// <summary>GET a form page and return its html, the antiforgery request token and the antiforgery cookie.</summary>
    public async Task<(string Html, string Token, string Cookie)> GetFormAsync(string path)
    {
        var response = await Client.GetAsync(path);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        var m = TokenRx.Match(html);
        var token = m.Success ? m.Groups[1].Value : "";
        var cookie = response.Headers.TryGetValues("Set-Cookie", out var cs)
            ? string.Join("; ", cs.Select(c => c.Split(';')[0])) : "";
        return (html, token, cookie);
    }

    public async Task<HttpResponseMessage> PostFormAsync(string path, string? token, string cookie,
        IDictionary<string, string> fields, byte[]? file = null)
    {
        var content = new MultipartFormDataContent();
        foreach (var kv in fields) content.Add(new StringContent(kv.Value), kv.Key);
        if (token is not null) content.Add(new StringContent(token), "__RequestVerificationToken");
        if (file is not null) content.Add(new ByteArrayContent(file), "Attachment", "datasheet.pdf");
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = content };
        if (cookie.Length > 0) request.Headers.Add("Cookie", cookie);
        return await Client.SendAsync(request);
    }
}
