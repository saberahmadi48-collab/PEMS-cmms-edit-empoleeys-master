using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PEMS.Web.Api;

namespace PEMS.Tests.Api;

/// <summary>
/// The OpenAPI 3.1 document is generated from the code (chapter 08, rule 17) and compared with an approved snapshot, so an API change
/// cannot slip in unnoticed. To approve a change: set <c>PEMS_GOLDEN_RECORD=1</c>, run this test once, and review the snapshot diff.
/// </summary>
public class OpenApiDocumentTests
{
    private static readonly string SnapshotPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Api", "Snapshots", "openapi-v1.json");

    private static async Task<string> GenerateAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Services.AddControllers().AddApplicationPart(typeof(ApiControllerBase).Assembly);
        builder.Services.AddPemsApi();
        await using var app = builder.Build();
        app.MapOpenApi("/api/v1/openapi.json");
        await app.StartAsync();
        var text = await app.GetTestClient().GetStringAsync("/api/v1/openapi.json");
        await app.StopAsync();
        return JsonSerializer.Serialize(JsonDocument.Parse(text).RootElement, new JsonSerializerOptions { WriteIndented = true }).ReplaceLineEndings("\n") + "\n";
    }

    [Fact]
    public async Task Document_is_openapi_3_1_and_describes_only_the_api()
    {
        using var doc = JsonDocument.Parse(await GenerateAsync());
        Assert.StartsWith("3.1", doc.RootElement.GetProperty("openapi").GetString());
        var paths = doc.RootElement.GetProperty("paths").EnumerateObject().Select(p => p.Name).ToList();
        Assert.NotEmpty(paths);
        Assert.All(paths, p => Assert.StartsWith("/api/v1/", p));
    }

    [Fact]
    public async Task Every_operation_has_a_success_response_and_a_unique_operation_id()
    {
        using var doc = JsonDocument.Parse(await GenerateAsync());
        var ids = new List<string>();
        foreach (var path in doc.RootElement.GetProperty("paths").EnumerateObject())
            foreach (var op in path.Value.EnumerateObject())
            {
                var responses = op.Value.GetProperty("responses").EnumerateObject().Select(r => r.Name).ToList();
                Assert.True(responses.Any(r => r.StartsWith('2')), $"{op.Name} {path.Name} has no 2xx response");
                if (op.Value.TryGetProperty("operationId", out var id)) ids.Add(id.GetString()!);
            }
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public async Task Document_matches_the_approved_snapshot()
    {
        var actual = await GenerateAsync();
        if (Environment.GetEnvironmentVariable("PEMS_GOLDEN_RECORD") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SnapshotPath)!);
            File.WriteAllText(SnapshotPath, actual);
        }
        Assert.True(File.Exists(SnapshotPath), $"Snapshot missing: {SnapshotPath} (record it with PEMS_GOLDEN_RECORD=1)");
        Assert.Equal(File.ReadAllText(SnapshotPath).ReplaceLineEndings("\n"), actual);
    }
}
