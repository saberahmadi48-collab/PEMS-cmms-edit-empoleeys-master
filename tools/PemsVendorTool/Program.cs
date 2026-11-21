#if !PEMS_VENDOR_TOOL
#error The vendor tool must be built from PemsVendorTool.csproj, which defines PEMS_VENDOR_TOOL (build-time flag, design 9.1).
#endif
using PEMS.Shared.Configuration;
using PemsVendorTool.Core;

// Run-time flag + settings. Settings come from vendor.env (same KEY=VALUE format as env/*.env) and real
// environment variables override it; nothing is hard-coded and no secret lives here (rule 10, guideline ch. 01).
var envFile = Environment.GetEnvironmentVariable("VENDOR_ENV_FILE") ?? Path.Combine(AppContext.BaseDirectory, "vendor.env");
var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
if (File.Exists(envFile))
    foreach (var kv in EnvFiles.Parse(File.ReadAllLines(envFile))) values[kv.Key] = kv.Value;
foreach (System.Collections.DictionaryEntry e in Environment.GetEnvironmentVariables())
    if (e.Key.ToString()!.StartsWith("VendorTool__", StringComparison.OrdinalIgnoreCase))
        values[e.Key.ToString()!.Replace("__", ":")] = e.Value?.ToString() ?? "";

string Required(string key) => values.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v)
    ? v
    : throw new InvalidOperationException($"Missing required setting '{key}'. Define it in vendor.env or as the variable {key.Replace(":", "__")}.");

if (!bool.TryParse(Required("VendorTool:Mode"), out var mode) || !mode)
    throw new InvalidOperationException("VendorTool__Mode must be true. This tool only runs on an owner computer (design 9.1).");

var settings = new VendorSettings(
    Required("VendorTool:DataDirectory"),
    Required("VendorTool:OperatorName"),
    int.Parse(Required("VendorTool:TransferCap")),
    Required("VendorTool:Urls"));

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(settings.Urls);
if (!settings.Urls.Contains("127.0.0.1") && !settings.Urls.Contains("localhost"))
    throw new InvalidOperationException("The vendor tool listens on the loopback address only; it is never exposed on a network.");

builder.Services.AddRazorPages();
builder.Services.AddSingleton(settings);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(new Ledger(Path.Combine(settings.DataDirectory, "ledger.sqlite")));
builder.Services.AddSingleton(new VendorKeyStore(Path.Combine(settings.DataDirectory, "keys")));
builder.Services.AddSingleton(sp => new RequestValidator(sp.GetRequiredService<Ledger>(), settings.TransferCap));
builder.Services.AddSingleton<LicenseIssuer>();

var app = builder.Build();
app.UseStaticFiles();
app.UseRouting();
app.MapRazorPages();
app.Run();

public sealed record VendorSettings(string DataDirectory, string OperatorName, int TransferCap, string Urls);

