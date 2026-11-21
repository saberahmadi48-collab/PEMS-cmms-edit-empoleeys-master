using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PEMS.Application.Definitions;
using PEMS.Application.Interfaces;
using PEMS.Application.Migration;
using PEMS.Web.Definitions;

namespace PEMS.Web.Services;

/// <summary>The vendor package panel operations (SCRUM-584); see <see cref="IPackageBuilder"/>.</summary>
public sealed class PackageBuilder(IConfiguration configuration, IDefinitionService definitions, ILogger<PackageBuilder> log, EntityCatalog? catalog = null) : IPackageBuilder
{
    public Task<PackageFileResult> BuildFeedAsync(string toVersion, string entity, string mode, string rowsJson, string? keyFields = null)
    {
        var (file, bytes, set, refused, _, error) = PrepareFeed(entity, mode, rowsJson, keyFields);
        if (error != null) return Task.FromResult(Refuse(error));
        if (refused.Count > 0) return Task.FromResult(Refuse($"{refused.Count} row(s) cannot be fed: " + string.Join(" ", refused.Take(5))));
        return Task.FromResult(Build(PackageKind.Feed, null, toVersion, null, [], [set!], new Dictionary<string, byte[]> { [file!] = bytes! }, $"pems-feed-{entity.Trim().ToLowerInvariant()}-{toVersion}.zip"));
    }

    public Task<MigratorResult> PreviewFeedAsync(string toVersion, string entity, string mode, string rowsJson, string? keyFields = null)
    {
        var (_, _, set, refused, keys, error) = PrepareFeed(entity, mode, rowsJson, keyFields);
        if (error != null) return Task.FromResult(new MigratorResult(false, error));
        var lines = new List<string> { $"Feed package to version {toVersion}: entity {set!.Entity}, mode {set.Mode}, key {string.Join(" + ", keys)}, {set.Rows} row(s)." };
        lines.Add(refused.Count == 0 ? "Every row passes the entity check." : $"{refused.Count} row(s) are refused and the package would not be built: " + string.Join(" ", refused.Take(5)));
        return Task.FromResult(new MigratorResult(refused.Count == 0, string.Join(" ", lines)));
    }

    /// <summary>The rows of a feed as the data file the applier reads (<c>{ keyFields, rows }</c>), checked against the entity catalog and the key rules before anything is signed.</summary>
    private (string? File, byte[]? Bytes, PackageDataSet? Set, IReadOnlyList<string> Refused, IReadOnlyList<string> Keys, string? Error) PrepareFeed(string entity, string mode, string rowsJson, string? keyFields)
    {
        IReadOnlyList<string> none = [];
        if (string.IsNullOrWhiteSpace(entity)) return (null, null, null, none, none, "Say which entity the rows are for.");
        if (mode is not ("append" or "overwrite" or "merge")) return (null, null, null, none, none, "The mode is append, overwrite or merge.");
        var registration = catalog?.Get(entity.Trim());
        if (catalog is not null && registration is null) return (null, null, null, none, none, $"'{entity.Trim()}' is not an entity a feed may write.");

        JsonArray array;
        try { array = FeedRows.Parse(rowsJson); }
        catch (JsonException) { return (null, null, null, none, none, "The rows are not valid JSON."); }
        catch (FormatException e) { return (null, null, null, none, none, e.Message); }
        if (array.Count == 0 || array.Any(r => r is not JsonObject)) return (null, null, null, none, none, "The rows are a JSON array of objects (field name to text) or CSV with a header line, at least one row.");

        var keys = (keyFields ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        if (keys.Count == 0) keys = [FeedRows.DefaultKey(array)];

        var asText = array.OfType<JsonObject>().Select(o => (IReadOnlyDictionary<string, string?>)o.ToDictionary(p => p.Key, p => p.Value is null ? null : p.Value is JsonValue v && v.TryGetValue<string>(out var t) ? t : p.Value.ToJsonString(), StringComparer.Ordinal)).ToList();
        string? Validate(IReadOnlyDictionary<string, string?> row)
        {
            if (registration is null) return null;
            foreach (var (name, value) in row)
            {
                if (!registration.Writable.Contains(name) && !keys.Contains(name)) return $"'{name}' is not a field a feed may write.";
                var pi = registration.ClrType.GetProperty(name);
                if (pi is null) return $"'{name}' is not a field of {registration.Name}.";
                if (EntityJson.Convert(pi.PropertyType, value) is EntityJson.NotConvertible) return $"'{name}' has a value that does not fit.";
            }
            return null;
        }
        var plan = FeedPlanner.Plan(entity.Trim(), mode, asText, keys, new Dictionary<string, IReadOnlyDictionary<string, string?>>(), Validate);
        var refused = plan.Rows.Where(r => r.Action == FeedRowAction.Refuse).Select(r => $"row {r.Row}: {r.Error}").ToList();

        var file = $"data/{entity.Trim()}.json";
        var data = new JsonObject { ["keyFields"] = new JsonArray(keys.Select(k => (JsonNode?)JsonValue.Create(k)).ToArray()), ["rows"] = array };
        return (file, Encoding.UTF8.GetBytes(data.ToJsonString()), new PackageDataSet(entity.Trim(), mode, file, array.Count), refused, keys, null);
    }

    public async Task<PackageFileResult> BuildInstallAsync(string toVersion, string schemaLevel, string? definitionRefs)
    {
        var (files, names, error) = await DefinitionFilesAsync(definitionRefs);
        return error != null ? Refuse(error) : Build(PackageKind.Install, null, toVersion, schemaLevel, names, [], files, $"pems-install-{toVersion}.zip");
    }

    public async Task<PackageFileResult> BuildUpdateAsync(string fromVersion, string toVersion, string schemaLevel, string? definitionRefs)
    {
        if (string.IsNullOrWhiteSpace(fromVersion)) return Refuse("An update names the version it comes from.");
        var (files, names, error) = await DefinitionFilesAsync(definitionRefs);
        return error != null ? Refuse(error) : Build(PackageKind.Update, fromVersion, toVersion, schemaLevel, names, [], files, $"pems-update-{fromVersion}-to-{toVersion}.zip");
    }

    private PackageFileResult Build(PackageKind kind, string? from, string to, string? schemaLevel, IReadOnlyList<string> definitionNames, IReadOnlyList<PackageDataSet> dataSets, Dictionary<string, byte[]> files, string fileName)
    {
        if (string.IsNullOrWhiteSpace(to)) return Refuse("Say which version the package goes to.");
        var keyText = configuration["Migrator:SigningKey"];
        var keyId = configuration["Migrator:SignerKeyId"];
        if (string.IsNullOrWhiteSpace(keyText) || string.IsNullOrWhiteSpace(keyId)) return Refuse("This installation has no vendor signing key; packages are built on the vendor's own deployment.");
        try
        {
            using var key = ECDsa.Create();
            key.ImportPkcs8PrivateKey(Convert.FromBase64String(keyText), out _);
            var bytes = MigrationPackageBuilder.Build(kind, from, to.Trim(), schemaLevel, definitionNames, dataSets, files, keyId, key);
            // verify before download: the same read the migrator center does, against the public key of this key
            var trust = new TrustedKeys(new Dictionary<string, string> { [keyId] = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()) });
            MigrationPackage.Read(bytes, trust);
            log.LogInformation("Vendor package built: {File}, {Bytes} bytes", fileName, bytes.Length);
            return new PackageFileResult { Success = true, Bytes = bytes, FileName = fileName };
        }
        catch (Exception e) when (e is CryptographicException or FormatException or PackageRefusedException)
        {
            return Refuse("The package could not be signed or verified: " + e.Message);
        }
    }

    /// <summary>The published payload of each listed definition (appKey:kind:key) as a file of the package.</summary>
    private async Task<(Dictionary<string, byte[]> Files, List<string> Names, string? Error)> DefinitionFilesAsync(string? refs)
    {
        var files = new Dictionary<string, byte[]>();
        var names = new List<string>();
        foreach (var r in (refs ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = r.Split(':', 3);
            if (parts.Length != 3) return (new Dictionary<string, byte[]>(), new List<string>(), $"'{r}' is not appKey:kind:key.");
            var found = (await definitions.ListAsync(new DefinitionListQuery(parts[1], parts[0], parts[2], null, [], 1, 20))).Items.FirstOrDefault(d => d.Kind == parts[1] && d.Key == parts[2] && d.AppKey == parts[0]);
            if (found is null) return (new Dictionary<string, byte[]>(), new List<string>(), $"The definition {r} does not exist.");
            var export = await definitions.ExportAsync(found.Id, null);
            if (!export.Success || export.Value!.Items.Count == 0) return (new Dictionary<string, byte[]>(), new List<string>(), $"The definition {r} has no published version to ship.");
            files[$"definitions/{parts[0]}.{parts[1]}.{parts[2]}.json"] = JsonSerializer.SerializeToUtf8Bytes(export.Value.Items[0]);
            names.Add($"{parts[1]}/{parts[2]}");
        }
        return (files, names, null);
    }

    private static PackageFileResult Refuse(string why) => new() { Success = false, Error = why };
}

/// <summary>The rows of a feed from JSON or CSV text (SCRUM-815).</summary>
public static class FeedRows
{
    /// <summary>A JSON array when the text starts with <c>[</c>; otherwise CSV with the field names in the first line. A CSV row with another number of cells than the header is refused.</summary>
    public static JsonArray Parse(string text)
    {
        var trimmed = (text ?? "").TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
        if (trimmed.StartsWith('[')) return JsonNode.Parse(trimmed) as JsonArray ?? throw new JsonException("not an array");
        var table = Csv(trimmed);
        if (table.Count < 2) throw new FormatException("The CSV needs a header line and at least one row.");
        var header = table[0].Select(h => h.Trim()).ToList();
        if (header.Any(h => h.Length == 0) || header.Distinct(StringComparer.Ordinal).Count() != header.Count) throw new FormatException("The CSV header has an empty or repeated field name.");
        var array = new JsonArray();
        for (var r = 1; r < table.Count; r++)
        {
            if (table[r].Count == 1 && table[r][0].Trim().Length == 0) continue;   // a blank line
            if (table[r].Count != header.Count) throw new FormatException($"CSV line {r + 1} has {table[r].Count} cells; the header has {header.Count}.");
            var row = new JsonObject();
            for (var c = 0; c < header.Count; c++) row[header[c]] = table[r][c];
            array.Add(row);
        }
        return array;
    }

    /// <summary>The key fields when none are given: <c>Code</c> if every row has it, else <c>Name</c>, else the first field of the first row.</summary>
    public static string DefaultKey(JsonArray rows)
    {
        foreach (var candidate in new[] { "Code", "Name" })
            if (rows.OfType<JsonObject>().All(r => r.ContainsKey(candidate))) return candidate;
        return rows.OfType<JsonObject>().First().First().Key;
    }

    /// <summary>RFC 4180 style: the separator is the one of the header line (comma, semicolon or tab), quotes protect a separator or a line break, a doubled quote is a quote.</summary>
    public static List<List<string>> Csv(string text)
    {
        var firstLine = text.Split('\n', 2)[0];
        var separator = new[] { ',', ';', '\t' }.OrderByDescending(c => firstLine.Count(x => x == c)).First();
        var rows = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (quoted)
            {
                if (ch == '"' && i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                else if (ch == '"') quoted = false;
                else cell.Append(ch);
            }
            else if (ch == '"') quoted = true;
            else if (ch == separator) { row.Add(cell.ToString()); cell.Clear(); }
            else if (ch == '\n' || ch == '\r')
            {
                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(cell.ToString()); cell.Clear(); rows.Add(row); row = [];
            }
            else cell.Append(ch);
        }
        if (quoted) throw new FormatException("The CSV has a quote that is never closed.");
        if (cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString()); rows.Add(row); }
        return rows;
    }
}
