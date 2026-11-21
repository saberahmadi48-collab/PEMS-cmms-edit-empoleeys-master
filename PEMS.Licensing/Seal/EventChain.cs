using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PEMS.Licensing.Formats;

namespace PEMS.Licensing.Seal;

/// <summary>One licensing event; <see cref="EntryHash"/> chains it to the previous entry (design 4.4). Never contains raw serials or personal data.</summary>
public sealed record LicenseEventEntry(long Seq, DateTimeOffset Utc, string Type, string DataHash, string PrevHash, string EntryHash);

public interface ILicenseEventSink
{
    Task<LicenseEventEntry?> LastAsync(CancellationToken ct = default);
    Task AppendAsync(LicenseEventEntry entry, CancellationToken ct = default);
}

public static class EventChain
{
    public const string Genesis = "GENESIS";

    public static LicenseEventEntry Next(LicenseEventEntry? previous, DateTimeOffset utc, string type, string data)
    {
        var seq = (previous?.Seq ?? 0) + 1;
        var prev = previous?.EntryHash ?? Genesis;
        var dataHash = Base64Url.Encode(SHA256.HashData(Encoding.UTF8.GetBytes(data)));
        return new LicenseEventEntry(seq, utc, type, dataHash, prev, Hash(prev, seq, utc, type, dataHash));
    }

    public static string Hash(string prev, long seq, DateTimeOffset utc, string type, string dataHash)
        => Base64Url.Encode(SHA256.HashData(Encoding.UTF8.GetBytes($"{prev}|{seq}|{utc.UtcDateTime:O}|{type}|{dataHash}")));

    public static bool IsValid(IEnumerable<LicenseEventEntry> entries)
    {
        var prev = Genesis;
        long seq = 0;
        foreach (var e in entries)
        {
            if (e.Seq != seq + 1 || e.PrevHash != prev || e.EntryHash != Hash(prev, e.Seq, e.Utc, e.Type, e.DataHash)) return false;
            prev = e.EntryHash;
            seq = e.Seq;
        }
        return true;
    }
}

/// <summary>Append-only JSON-lines event log next to the seal file; works before the database exists (first run).</summary>
public sealed class FileEventSink : ILicenseEventSink
{
    private readonly string _path;
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public FileEventSink(string sealDirectory) => _path = Path.Combine(sealDirectory, "events.jsonl");

    public async Task<LicenseEventEntry?> LastAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_path)) return null;
        var line = (await File.ReadAllLinesAsync(_path, ct)).LastOrDefault(l => l.Length > 0);
        return line is null ? null : JsonSerializer.Deserialize<LicenseEventEntry>(line, Json);
    }

    public async Task AppendAsync(LicenseEventEntry entry, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        await File.AppendAllTextAsync(_path, JsonSerializer.Serialize(entry, Json) + "\n", ct);
    }

    public IReadOnlyList<LicenseEventEntry> ReadAll()
        => File.Exists(_path)
            ? File.ReadAllLines(_path).Where(l => l.Length > 0).Select(l => JsonSerializer.Deserialize<LicenseEventEntry>(l, Json)!).ToList()
            : new List<LicenseEventEntry>();
}
