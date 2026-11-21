using Microsoft.AspNetCore.Http;
using PEMS.Licensing.Evaluation;
using PEMS.Web.Licensing;

namespace PEMS.Tests.Licensing;

public class SurfaceAndPackageTests
{
    [Theory]
    [InlineData("/Account/Login", true)] [InlineData("/account/logout", true)] [InlineData("/Admin/License", true)] [InlineData("/Admin/License/Upload", true)] [InlineData("/p/license", true)] [InlineData("/f/license-upload", true)] [InlineData("/f/license-requests", false)]
    [InlineData("/QuickSetup", true)] [InlineData("/health/database", true)] [InlineData("/Culture/Set", true)]
    [InlineData("/", false)] [InlineData("/Equipment", false)] [InlineData("/Admin/Users", false)] [InlineData("/Admin/LicenseFake", false)]
    [InlineData("/Accounting", false)] [InlineData("/api/work-orders", false)] [InlineData(null, false)]
    public void Only_allowlisted_surfaces_are_free(string? path, bool free)
        => Assert.Equal(free, LicenceFreeSurfaces.IsFree(path));

    private static LicenseStatus Status(LicenseState state) =>
        new(state, new[] { "x" }, null, null, null, null, null, null, null, null);

    private static async Task<DefaultHttpContext> Run(LicenseState state, string method, string path, string accept = "text/html")
    {
        var holder = new LicenseStateHolder();
        holder.Set(Status(state));
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Request.Headers.Accept = accept;
        context.Response.Body = new MemoryStream();
        var reached = false;
        var middleware = new LicenseGateMiddleware(_ => { reached = true; return Task.CompletedTask; });
        await middleware.InvokeAsync(context, holder);
        context.Items["reached"] = reached;
        return context;
    }

    [Theory]
    [InlineData(LicenseState.NotEnforced)] [InlineData(LicenseState.Active)] [InlineData(LicenseState.ActiveWarning)]
    public async Task Licensed_states_pass_everything(LicenseState state)
        => Assert.True((bool)(await Run(state, "POST", "/Equipment/Create")).Items["reached"]!);

    [Fact]
    public async Task Restricted_is_read_only_and_the_licence_page_stays_open()
    {
        Assert.True((bool)(await Run(LicenseState.Restricted, "GET", "/Equipment")).Items["reached"]!);
        var write = await Run(LicenseState.Restricted, "POST", "/Equipment/Create");
        Assert.False((bool)write.Items["reached"]!);
        Assert.Equal(403, write.Response.StatusCode);
        Assert.True((bool)(await Run(LicenseState.Restricted, "POST", "/Admin/License/Upload")).Items["reached"]!);
        Assert.True((bool)(await Run(LicenseState.Restricted, "POST", "/Account/Login")).Items["reached"]!);
    }

    [Fact]
    public async Task Activation_required_sends_browsers_to_the_licence_page_and_refuses_api_calls()
    {
        var page = await Run(LicenseState.ActivationRequired, "GET", "/Equipment");
        Assert.False((bool)page.Items["reached"]!);
        Assert.Equal(302, page.Response.StatusCode);
        Assert.Equal("/Admin/License", page.Response.Headers.Location.ToString());

        var api = await Run(LicenseState.ActivationRequired, "GET", "/api/x", accept: "application/json");
        Assert.Equal(403, api.Response.StatusCode);
        Assert.Equal("ActivationRequired", api.Response.Headers["X-Licence-State"].ToString());
    }

    // ---- package scan (plan rows #110, #119 as far as they are testable without the release workflow)

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PEMS.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    [Fact]
    public void The_customer_product_does_not_reference_the_vendor_tool()
    {
        var web = File.ReadAllText(Path.Combine(Root(), "PEMS.Web", "PEMS.Web.csproj"));
        Assert.DoesNotContain("PemsVendorTool", web);
        var names = typeof(LicenceFreeSurfaces).Assembly.GetReferencedAssemblies().Select(a => a.Name);
        Assert.DoesNotContain("PemsVendorTool", names);
        foreach (var project in new[] { "PEMS.Licensing", "PEMS.Persistence", "PEMS.Application", "PEMS.Domain" })
            Assert.DoesNotContain("PemsVendorTool", File.ReadAllText(Path.Combine(Root(), project, project + ".csproj")));
    }

    [Fact]
    public void The_product_contains_no_signing_code_path()
    {
        var licensing = typeof(PEMS.Licensing.Crypto.Ed25519);
        Assert.Empty(licensing.GetMethods().Where(m => m.Name is "Sign" or "GenerateKeyPair"));
    }

    [Fact]
    public void No_bypass_flag_exists_in_product_sources()
    {
        foreach (var project in new[] { "PEMS.Web", "PEMS.Licensing", "PEMS.Persistence", "PEMS.Application" })
            foreach (var file in Directory.EnumerateFiles(Path.Combine(Root(), project), "*.*", SearchOption.AllDirectories)
                         .Where(f => (f.EndsWith(".cs") || f.EndsWith(".cshtml") || f.EndsWith(".json")) && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")))
            {
                var text = File.ReadAllText(file);
                Assert.DoesNotContain("LICENSE_BYPASS", text, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("LICENCE_BYPASS", text, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("SkipLicense", text, StringComparison.OrdinalIgnoreCase);
            }
    }

    [Fact]
    public void No_private_key_material_is_committed_in_the_repository_sources()
    {
        var root = Root();
        var skip = new[] { $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", $"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", "node_modules", "graphify-out", "legacy-trash", "vendor-data" };
        foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
                     .Where(f => (f.EndsWith(".cs") || f.EndsWith(".json") || f.EndsWith(".env") || f.EndsWith(".md") || f.EndsWith(".pem") || f.EndsWith(".key")) && !skip.Any(f.Contains)))
        {
            var name = Path.GetFileName(file);
            Assert.False(name.EndsWith(".pem") || name.StartsWith("vendor-key-") || name == "install.key", $"Key file committed: {file}");
            if (file.Contains($"{Path.DirectorySeparatorChar}PEMS.Tests{Path.DirectorySeparatorChar}")) continue;
            Assert.DoesNotContain("BEGIN PRIVATE KEY", File.ReadAllText(file));
            Assert.DoesNotContain("BEGIN EC PRIVATE KEY", File.ReadAllText(file));
        }
    }

    [Fact]
    public void Enforcement_is_off_in_every_environment_file_and_all_licensing_keys_exist()
    {
        var required = new[] { "Enforce", "SealDirectory", "PeriodicCheckHours", "HeartbeatHours", "ClockToleranceMinutes", "WarningDays", "RestoreGraceHours", "HostMachineIdPath" };
        var envs = Directory.GetFiles(Path.Combine(Root(), "env"), "*.env").Where(f => !f.EndsWith(".secrets.env")).ToList();
        Assert.Equal(6, envs.Count);
        foreach (var file in envs)
        {
            var lines = File.ReadAllLines(file);
            foreach (var key in required)
                Assert.Contains(lines, l => l.StartsWith($"Licensing__{key}="));
            Assert.Contains("Licensing__Enforce=false", lines);
        }
    }

    [Fact]
    public void The_embedded_trusted_key_set_is_empty_until_the_signing_key_exists()
        => Assert.Empty(PEMS.Licensing.Crypto.TrustedKeySet.LoadEmbedded().Keys);
}
