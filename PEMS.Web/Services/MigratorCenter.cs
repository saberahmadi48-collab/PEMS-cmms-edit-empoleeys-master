using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.Definitions;
using PEMS.Application.Interfaces;
using PEMS.Application.Migration;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Web.Definitions;

namespace PEMS.Web.Services;

/// <summary>The migrator center operations (SCRUM-582); see <see cref="IMigratorCenter"/>.</summary>
public sealed class MigratorCenter(IConfiguration configuration, PEMSDbContext db, ILogger<MigratorCenter> log, PackageFeedApplier feeds, IDefinitionShipper shipper, IDatabaseUpdateService updates) : IMigratorCenter
{
    public async Task<MigratorResult> InspectAsync(byte[] package, string actor)
    {
        var keys = configuration.GetSection("Migrator:TrustedKeys").GetChildren().Where(c => !string.IsNullOrWhiteSpace(c.Value)).ToDictionary(c => c.Key, c => c.Value!, StringComparer.Ordinal);
        if (keys.Count == 0) return new(false, "No vendor key is configured (Migrator:TrustedKeys), so a package cannot be trusted.");
        try
        {
            var (manifest, files, hash) = MigrationPackage.Read(package, new TrustedKeys(keys));
            var empty = !await db.Definitions.AsNoTracking().AnyAsync();
            var version = typeof(IMigratorCenter).Assembly.GetName().Version?.ToString(3);
            var plan = MigrationPackage.Detect(manifest, hash, new DatabaseState(empty, version, null, await AppliedHashesAsync()));
            var lines = new List<string> { $"{plan.Action}: {plan.Reason}", $"Package {manifest.Kind} to version {manifest.ToVersion}, {files.Count} files, {manifest.Definitions.Count} definitions." };
            lines.AddRange(manifest.DataSets.Select(d => $"Data set {d.Entity}: {d.Rows} rows, mode {d.Mode}."));
            log.LogInformation("Migrator inspect by {Actor}: {Action} package {Hash}", actor, plan.Action, hash[..12]);
            return new(plan.Action != PackageAction.Refused, string.Join(" ", lines));
        }
        catch (PackageRefusedException e)
        {
            log.LogWarning("Migrator inspect by {Actor} refused a package: {Reason}", actor, e.Message);
            return new(false, e.Message);
        }
    }

    private async Task<HashSet<string>> AppliedHashesAsync()
        => (await db.AppliedPackages.AsNoTracking().Where(p => p.Succeeded).Select(p => p.PackageHash).Distinct().ToListAsync()).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private TrustedKeys? Keys()
    {
        var keys = configuration.GetSection("Migrator:TrustedKeys").GetChildren().Where(c => !string.IsNullOrWhiteSpace(c.Value)).ToDictionary(c => c.Key, c => c.Value!, StringComparer.Ordinal);
        return keys.Count == 0 ? null : new TrustedKeys(keys);
    }

    public async Task<MigratorResult> RunAsync(byte[] package, bool confirm, string actor)
    {
        if (!confirm) return new(false, "Confirm the run: it changes the database.");
        var keys = Keys();
        if (keys is null) return new(false, "No vendor key is configured (Migrator:TrustedKeys), so a package cannot be trusted.");

        PackageManifest manifest; IReadOnlyDictionary<string, byte[]> files; string hash;
        try { (manifest, files, hash) = MigrationPackage.Read(package, keys); }
        catch (PackageRefusedException e)
        {
            log.LogWarning("Migrator run by {Actor} refused a package: {Reason}", actor, e.Message);
            return new(false, e.Message);
        }

        var empty = !await db.Definitions.AsNoTracking().AnyAsync();
        var version = typeof(IMigratorCenter).Assembly.GetName().Version?.ToString(3);
        var plan = MigrationPackage.Detect(manifest, hash, new DatabaseState(empty, version, null, await AppliedHashesAsync()));
        if (plan.Action == PackageAction.Refused) return new(false, plan.Reason);

        string? backup = null;
        try
        {
            string summary;
            switch (plan.Action)
            {
                case PackageAction.Feed:
                case PackageAction.Refeed:
                {
                    var fed = await feeds.ApplyAsync(manifest, files, hash, ["SystemAdmin"], actor);
                    if (!fed.Ok) return await RecordAsync(hash, manifest, actor, null, false, fed.Error ?? fed.Summary);
                    summary = (plan.Action == PackageAction.Refeed ? "Refeed: " : "") + fed.Summary;
                    break;
                }
                default:    // install and update: the schema first (the update service takes the backup and stops when it fails), then the definitions the package ships
                {
                    var applied = await updates.ApplyAsync("Admin", actor);
                    backup = applied.BackupFile;
                    if (!applied.Succeeded) return await RecordAsync(hash, manifest, actor, backup, false, applied.Message);
                    var shipped = DefinitionsOf(files);
                    if (shipped.Count > 0)
                    {
                        var result = await shipper.ShipAsync(shipped, actor);
                        if (!result.Ok) return await RecordAsync(hash, manifest, actor, backup, false, "Definitions were not shipped: " + string.Join("; ", result.Problems.Take(3)));
                        summary = $"{applied.Message} Definitions: {result.Created} created, {result.Upgraded} upgraded, {result.KeptCustomized} kept as customised.";
                    }
                    else summary = applied.Message;
                    break;
                }
            }
            return await RecordAsync(hash, manifest, actor, backup, true, summary);
        }
        catch (Exception e)
        {
            log.LogError(e, "Migrator run by {Actor} of package {Hash} failed", actor, hash[..12]);
            return await RecordAsync(hash, manifest, actor, backup, false, "The run failed and was stopped: " + e.Message);
        }
    }

    /// <summary>The shipped definitions of a package: the files <c>definitions/*.json</c>, each the exported item (kind, key, app, title, description, payload).</summary>
    public static IReadOnlyList<ShippedDefinition> DefinitionsOf(IReadOnlyDictionary<string, byte[]> files)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var list = new List<ShippedDefinition>();
        foreach (var (path, bytes) in files.Where(f => f.Key.StartsWith("definitions/", StringComparison.Ordinal)).OrderBy(f => f.Key, StringComparer.Ordinal))
        {
            var item = JsonSerializer.Deserialize<DefinitionPackageItem>(bytes, options);
            if (item is null || string.IsNullOrWhiteSpace(item.Key)) continue;
            list.Add(new ShippedDefinition(item.Kind, item.Key, string.IsNullOrWhiteSpace(item.AppKey) ? "core" : item.AppKey, item.TitleJson, item.DescriptionJson, item.PayloadJson));
        }
        return list;
    }

    private async Task<MigratorResult> RecordAsync(string hash, PackageManifest manifest, string actor, string? backup, bool succeeded, string summary)
    {
        db.AppliedPackages.Add(new AppliedPackage
        {
            PackageHash = hash, Kind = manifest.Kind.ToString().ToLowerInvariant(), Version = manifest.ToVersion, Succeeded = succeeded,
            Summary = summary.Length > 1000 ? summary[..1000] : summary, BackupPoint = backup, AppliedBy = actor, AppliedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        log.LogInformation("Migrator run by {Actor}: package {Hash} {Result}", actor, hash[..12], succeeded ? "applied" : "failed");
        return new(succeeded, summary);
    }
}
