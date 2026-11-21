using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PEMS.Application.Migration;

// SCRUM-581: one signed package (a zip) carries an install, an update or a feed. The migrator reads it, verifies the signature and the checksums, and the detector says what to do with it.
// Layout: manifest.json at the root, every other file listed in the manifest's "files" with its SHA-256 (definitions/*.json, data/*.json, migrations/*.json).
// The vendor's public keys are configuration (Migrator:TrustedKeys, never a file of the repository); the private key never leaves the vendor tool (SCRUM-584).

/// <summary>What the package asks for. The detector may answer with another action (a feed whose hash was fed before is a refeed).</summary>
public enum PackageKind { Install, Update, Feed }

/// <summary>What the migrator will do after detection.</summary>
public enum PackageAction { Install, Update, Feed, Refeed, Refused }

public sealed record PackageDataSet(string Entity, string Mode, string File, int Rows);

/// <summary>The manifest of a package: <c>FormatVersion</c> 1, <c>Kind</c>, product versions, the schema level (last migration id the package needs), the shipped definitions, the data sets and the checksums of every other file.</summary>
public sealed record PackageManifest(
    int FormatVersion, PackageKind Kind, string? FromVersion, string ToVersion, string? SchemaLevel,
    IReadOnlyList<string> Definitions, IReadOnlyList<PackageDataSet> DataSets, IReadOnlyDictionary<string, string> Files, string SignerKeyId, string Signature);

/// <summary>The state of the database the detector compares the package with.</summary>
public sealed record DatabaseState(bool IsEmpty, string? ProductVersion, string? SchemaLevel, IReadOnlySet<string> AppliedPackageHashes);

/// <summary>The answer: the action, why, and (for a refusal) the reason in plain words.</summary>
public sealed record PackagePlan(PackageAction Action, string Reason, string PackageHash, PackageManifest? Manifest);

/// <summary>The public keys the migrator trusts: key id to the public key (SubjectPublicKeyInfo, base64). Read from configuration.</summary>
public sealed record TrustedKeys(IReadOnlyDictionary<string, string> PublicKeys);

public sealed class PackageRefusedException(string message) : Exception(message);

public static class MigrationPackage
{
    public const string ManifestName = "manifest.json";
    private const long MaxEntryBytes = 200L * 1024 * 1024;

    /// <summary>The bytes the vendor signs: the manifest with the <c>signature</c> property removed, written compact in its own property order.</summary>
    public static byte[] SignedBytes(string manifestJson)
    {
        var node = JsonNode.Parse(manifestJson) as JsonObject ?? throw new PackageRefusedException("The manifest is not a JSON object.");
        node.Remove("signature");
        return Encoding.UTF8.GetBytes(node.ToJsonString());
    }

    public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    /// <summary>Reads and verifies a package: the zip shape, the manifest, the signature against the trusted keys, the checksum of every file, and no file the manifest does not list. Throws <see cref="PackageRefusedException"/> for anything wrong.</summary>
    public static (PackageManifest Manifest, IReadOnlyDictionary<string, byte[]> Files, string PackageHash) Read(byte[] package, TrustedKeys trust)
    {
        var entries = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        try
        {
            using var zip = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);
            foreach (var e in zip.Entries)
            {
                if (e.FullName.EndsWith('/')) continue;
                if (e.FullName.Contains("..") || e.FullName.StartsWith('/') || e.FullName.Contains('\\')) throw new PackageRefusedException($"The file name '{e.FullName}' is not allowed.");
                if (e.Length > MaxEntryBytes) throw new PackageRefusedException($"The file '{e.FullName}' is too large.");
                using var s = e.Open();
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                entries[e.FullName] = ms.ToArray();
            }
        }
        catch (InvalidDataException) { throw new PackageRefusedException("The file is not a package (not a zip)."); }

        if (!entries.TryGetValue(ManifestName, out var manifestBytes)) throw new PackageRefusedException("The package has no manifest.json.");
        var manifestJson = Encoding.UTF8.GetString(manifestBytes);
        var manifest = ParseManifest(manifestJson);

        if (!trust.PublicKeys.TryGetValue(manifest.SignerKeyId, out var publicKey)) throw new PackageRefusedException($"The signer '{manifest.SignerKeyId}' is not trusted.");
        if (!VerifySignature(SignedBytes(manifestJson), manifest.Signature, publicKey)) throw new PackageRefusedException("The signature does not match: the package was changed or is not from the vendor.");

        foreach (var (path, expected) in manifest.Files)
        {
            if (!entries.TryGetValue(path, out var bytes)) throw new PackageRefusedException($"The file '{path}' listed in the manifest is missing.");
            if (!string.Equals(Hash(bytes), expected, StringComparison.OrdinalIgnoreCase)) throw new PackageRefusedException($"The file '{path}' does not match its checksum.");
        }
        foreach (var path in entries.Keys.Where(k => k != ManifestName))
            if (!manifest.Files.ContainsKey(path)) throw new PackageRefusedException($"The file '{path}' is not listed in the manifest.");
        foreach (var ds in manifest.DataSets)
            if (!manifest.Files.ContainsKey(ds.File)) throw new PackageRefusedException($"The data set '{ds.Entity}' points at '{ds.File}', which is not in the package.");

        return (manifest, entries.Where(e => e.Key != ManifestName).ToDictionary(e => e.Key, e => e.Value), Hash(manifestBytes));
    }

    public static PackageManifest ParseManifest(string json)
    {
        JsonObject root;
        try { root = JsonNode.Parse(json) as JsonObject ?? throw new PackageRefusedException("The manifest is not a JSON object."); }
        catch (JsonException) { throw new PackageRefusedException("The manifest is not valid JSON."); }

        var format = root["formatVersion"]?.GetValue<int>() ?? 0;
        if (format != 1) throw new PackageRefusedException($"The package format {format} is not known (this product reads format 1).");
        var kindText = root["kind"]?.GetValue<string>();
        if (!Enum.TryParse<PackageKind>(kindText, ignoreCase: true, out var kind)) throw new PackageRefusedException($"The package kind '{kindText}' is not known (install, update or feed).");
        var to = root["toVersion"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(to)) throw new PackageRefusedException("The manifest has no toVersion.");
        var signer = root["signerKeyId"]?.GetValue<string>();
        var signature = root["signature"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(signer) || string.IsNullOrWhiteSpace(signature)) throw new PackageRefusedException("The package is not signed.");

        var definitions = (root["definitions"] as JsonArray)?.Select(n => n!.GetValue<string>()).ToList() ?? [];
        var dataSets = (root["dataSets"] as JsonArray)?.Select(n =>
        {
            var o = n as JsonObject ?? throw new PackageRefusedException("A data set is not an object.");
            var mode = o["mode"]?.GetValue<string>() ?? "";
            if (mode is not ("append" or "overwrite" or "merge")) throw new PackageRefusedException($"The data set mode '{mode}' is not known (append, overwrite or merge).");
            return new PackageDataSet(o["entity"]?.GetValue<string>() ?? "", mode, o["file"]?.GetValue<string>() ?? "", o["rows"]?.GetValue<int>() ?? 0);
        }).ToList() ?? [];
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        if (root["files"] is JsonObject fo) foreach (var p in fo) files[p.Key] = p.Value!.GetValue<string>();

        return new PackageManifest(format, kind, root["fromVersion"]?.GetValue<string>(), to, root["schemaLevel"]?.GetValue<string>(), definitions, dataSets, files, signer, signature);
    }

    private static bool VerifySignature(byte[] data, string signatureBase64, string publicKeyBase64)
    {
        try
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKeyBase64), out _);
            return key.VerifyData(data, Convert.FromBase64String(signatureBase64), HashAlgorithmName.SHA256);
        }
        catch (Exception e) when (e is CryptographicException or FormatException) { return false; }
    }

    /// <summary>
    /// What to do with a verified package (SCRUM-581): an install needs an empty database; an update needs the version it comes from and a newer target; a feed needs an installed database
    /// and, when the same package was fed before (same hash), is a refeed that runs again idempotently. Anything else is refused with the reason.
    /// </summary>
    public static PackagePlan Detect(PackageManifest manifest, string packageHash, DatabaseState db)
    {
        PackagePlan Refuse(string why) => new(PackageAction.Refused, why, packageHash, manifest);
        switch (manifest.Kind)
        {
            case PackageKind.Install:
                return db.IsEmpty ? new(PackageAction.Install, "The database is empty: first installation.", packageHash, manifest) : Refuse("The database is not empty; an install package only runs on an empty database.");
            case PackageKind.Update:
                if (db.IsEmpty) return Refuse("The database is empty; install first.");
                if (string.IsNullOrWhiteSpace(manifest.FromVersion)) return Refuse("An update package names the version it updates from.");
                if (!string.Equals(db.ProductVersion, manifest.FromVersion, StringComparison.Ordinal))
                    return Refuse($"This update is from version {manifest.FromVersion}; the database is at {db.ProductVersion ?? "an unknown version"}.");
                if (CompareVersions(manifest.ToVersion, manifest.FromVersion) <= 0) return Refuse("The update does not go to a newer version.");
                return new(PackageAction.Update, $"Update from {manifest.FromVersion} to {manifest.ToVersion}.", packageHash, manifest);
            case PackageKind.Feed:
                if (db.IsEmpty) return Refuse("The database is empty; install first, then feed.");
                if (manifest.DataSets.Count == 0 && manifest.Definitions.Count == 0) return Refuse("The feed has nothing to feed.");
                return db.AppliedPackageHashes.Contains(packageHash)
                    ? new(PackageAction.Refeed, "This package was fed before: it runs again and changes nothing that is already there.", packageHash, manifest)
                    : new(PackageAction.Feed, "Data feed into the installed database.", packageHash, manifest);
            default:
                return Refuse("Unknown package kind.");
        }
    }

    /// <summary>Compares dotted numeric versions (1.10.2 &gt; 1.9); a part that is not a number compares as text.</summary>
    public static int CompareVersions(string a, string b)
    {
        var pa = a.Split('.');
        var pb = b.Split('.');
        for (var i = 0; i < Math.Max(pa.Length, pb.Length); i++)
        {
            var x = i < pa.Length ? pa[i] : "0";
            var y = i < pb.Length ? pb[i] : "0";
            var c = long.TryParse(x, out var nx) && long.TryParse(y, out var ny) ? nx.CompareTo(ny) : string.CompareOrdinal(x, y);
            if (c != 0) return c;
        }
        return 0;
    }
}

/// <summary>Builds and signs a package (SCRUM-584 uses it from the vendor tool). The private key is passed in by the caller; nothing here stores a key.</summary>
public static class MigrationPackageBuilder
{
    public static byte[] Build(PackageKind kind, string? fromVersion, string toVersion, string? schemaLevel, IReadOnlyList<string> definitions, IReadOnlyList<PackageDataSet> dataSets, IReadOnlyDictionary<string, byte[]> files, string signerKeyId, ECDsa privateKey)
    {
        var manifest = new JsonObject
        {
            ["formatVersion"] = 1,
            ["kind"] = kind.ToString().ToLowerInvariant(),
            ["fromVersion"] = fromVersion,
            ["toVersion"] = toVersion,
            ["schemaLevel"] = schemaLevel,
            ["definitions"] = new JsonArray(definitions.Select(d => (JsonNode?)JsonValue.Create(d)).ToArray()),
            ["dataSets"] = new JsonArray(dataSets.Select(d => (JsonNode?)new JsonObject { ["entity"] = d.Entity, ["mode"] = d.Mode, ["file"] = d.File, ["rows"] = d.Rows }).ToArray()),
            ["files"] = new JsonObject(files.OrderBy(f => f.Key, StringComparer.Ordinal).Select(f => KeyValuePair.Create(f.Key, (JsonNode?)JsonValue.Create(MigrationPackage.Hash(f.Value))))),
            ["signerKeyId"] = signerKeyId
        };
        var signature = Convert.ToBase64String(privateKey.SignData(MigrationPackage.SignedBytes(manifest.ToJsonString()), HashAlgorithmName.SHA256));
        manifest["signature"] = signature;

        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string name, byte[] bytes) { using var s = zip.CreateEntry(name).Open(); s.Write(bytes); }
            Add(MigrationPackage.ManifestName, Encoding.UTF8.GetBytes(manifest.ToJsonString()));
            foreach (var f in files) Add(f.Key, f.Value);
        }
        return output.ToArray();
    }
}
