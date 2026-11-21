using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PEMS.Application.Definitions;
using PEMS.Persistence.Definitions;

namespace PEMS.Persistence.Seed;

/// <summary>
/// SCRUM-579 (ADR-0014 cutover): the definitions that ship with the product (<c>definitions-system</c> next to the application, from <c>Definitions/System</c>) are written into the definition tables as System
/// definitions at every start: a fresh install gets them, an upgrade gets the new versions of the ones nobody changed. After this the engines read the database only; the files are not an owner, they are the
/// package this seeder installs. Idempotent.
/// </summary>
public static class SystemDefinitionSeeder
{
    public const string FolderName = "definitions-system";

    public static async Task<DefinitionShipResult> SeedAsync(IServiceProvider services, string? folder = null, CancellationToken ct = default)
    {
        var kinds = services.GetServices<IDefinitionKind>().ToList();
        var limits = services.GetService<DefinitionLimits>() ?? new DefinitionLimits();
        var package = LoadPackage(kinds, limits, folder ?? Path.Combine(AppContext.BaseDirectory, FolderName));
        var result = await services.GetRequiredService<IDefinitionShipper>().ShipAsync(package, "system", ct);
        var log = services.GetService<ILoggerFactory>()?.CreateLogger("SystemDefinitionSeeder");
        log?.LogInformation("System definitions: {Created} created, {Upgraded} upgraded, {Unchanged} unchanged, {Kept} kept as customized, {Customer} owned by customers.", result.Created, result.Upgraded, result.Unchanged, result.KeptCustomized, result.CustomerOwned);
        foreach (var problem in result.Problems) log?.LogError("System definition not shipped: {Problem}", problem);
        if (!result.Ok) throw new InvalidOperationException("System definitions could not be shipped: " + string.Join("; ", result.Problems.Take(10)));
        return result;
    }

    /// <summary>Reads and checks the package with the same loader as the files (every text in both languages, every kind validated); a bad file stops the start.</summary>
    public static IReadOnlyList<ShippedDefinition> LoadPackage(IReadOnlyCollection<IDefinitionKind> kinds, DefinitionLimits limits, string folder)
    {
        if (!Directory.Exists(folder)) return [];
        // the package is read as files whatever the runtime ownership says: it is only the source of what the seeder installs
        var ownership = new DefinitionOwnership(kinds.ToDictionary(k => k.Key, _ => "files", StringComparer.Ordinal));
        using var reader = new FileDefinitionStore(kinds, limits, new DefinitionFileOptions { Folder = folder, Watch = false, FailOnInvalid = true }, ownership);
        return reader.All.OrderBy(d => d.Kind, StringComparer.Ordinal).ThenBy(d => d.AppKey, StringComparer.Ordinal).ThenBy(d => d.Key, StringComparer.Ordinal).Select(d =>
        {
            string? description = null;
            var path = Path.Combine(folder, d.Kind, d.AppKey, d.Key + ".json");
            if (File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonObject envelope && envelope["description"] is { } desc) description = desc.ToJsonString();
            return new ShippedDefinition(d.Kind, d.Key, d.AppKey, d.TitleJson ?? "{}", description, d.PayloadJson);
        }).ToList();
    }
}
