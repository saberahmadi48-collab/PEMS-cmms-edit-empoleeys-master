using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using PEMS.Application.Definitions;

namespace PEMS.Persistence.Definitions;

/// <summary>
/// The definitions of the product as JSON files in the data layer (SCRUM-572, ADR-0014): <c>{folder}/{kind}/{appKey}/{key}.json</c>, each file the envelope
/// <c>{ kind, key, appKey, version, title: {fa, en}, payload }</c>. Every file is checked at load: the folder and the name agree with the envelope, the kind is registered
/// and validates the payload, and every translatable text carries both languages. A change to a file is picked up while the application runs; a file that became
/// invalid keeps its last good version and is listed in <see cref="Problems"/>. A start-up with an invalid file fails when <see cref="DefinitionFileOptions.FailOnInvalid"/>.
/// </summary>
public sealed class FileDefinitionStore : IDefinitionStore, IDefinitionFiles, IDisposable
{
    private static readonly string[] Languages = ["fa", "en"];
    private static readonly Regex LanguageKey = new("^[a-z]{2}(-[A-Za-z]{2})?$", RegexOptions.Compiled);

    private readonly IReadOnlyDictionary<string, IDefinitionKind> _kinds;
    private readonly DefinitionLimits _limits;
    private readonly DefinitionOwnership _ownership;
    private readonly ILogger<FileDefinitionStore>? _log;
    private readonly string _folder;
    private readonly object _gate = new();
    private FileSystemWatcher? _watcher;
    private Timer? _debounce;
    private volatile Snapshot _snapshot = new(new Dictionary<string, PublishedDefinition>(), [], []);
    private long _generation;

    private sealed record Snapshot(IReadOnlyDictionary<string, PublishedDefinition> ByKey, IReadOnlyList<PublishedDefinition> All, IReadOnlyList<DefinitionFileProblem> Problems);

    public FileDefinitionStore(IEnumerable<IDefinitionKind> kinds, DefinitionLimits limits, DefinitionFileOptions options, DefinitionOwnership? ownership = null, ILogger<FileDefinitionStore>? log = null)
    {
        _kinds = kinds.ToDictionary(k => k.Key, StringComparer.Ordinal);
        _limits = limits;
        _ownership = ownership ?? new DefinitionOwnership();
        _log = log;
        _folder = string.IsNullOrWhiteSpace(options.Folder) ? Path.Combine(AppContext.BaseDirectory, "definitions") : Path.GetFullPath(options.Folder);
        Reload();
        if (_snapshot.Problems.Count > 0 && options.FailOnInvalid)
            throw new InvalidOperationException("Invalid definition files in " + _folder + ": " + string.Join("; ", _snapshot.Problems.Take(10).Select(p => $"{p.File}{p.Pointer}: {p.Message}")));
        if (options.Watch && Directory.Exists(_folder)) StartWatching();
    }

    public IReadOnlyList<PublishedDefinition> All => _snapshot.All;
    public IReadOnlyList<DefinitionFileProblem> Problems => _snapshot.Problems;
    public long Generation => Interlocked.Read(ref _generation);
    public event Action? Reloaded;

    public Task<PublishedDefinition?> GetPublishedAsync(string kind, string key, string appKey = "core", CancellationToken ct = default)
        => Task.FromResult(_snapshot.ByKey.TryGetValue(KeyOf(appKey, kind, key), out var d) ? d : null);

    public Task<IReadOnlyList<PublishedDefinition>> ListPublishedAsync(string kind, string appKey = "core", CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<PublishedDefinition>>(_snapshot.All.Where(d => d.Kind == kind && d.AppKey == appKey).ToList());

    private static string KeyOf(string appKey, string kind, string key) => appKey + "|" + kind + "|" + key;

    public void Reload()
    {
        lock (_gate)
        {
            var previous = _snapshot;
            var found = new Dictionary<string, PublishedDefinition>(StringComparer.Ordinal);
            var problems = new List<DefinitionFileProblem>();
            if (Directory.Exists(_folder))
            {
                foreach (var file in Directory.EnumerateFiles(_folder, "*.json", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
                {
                    var rel = Path.GetRelativePath(_folder, file).Replace('\\', '/');
                    var before = problems.Count;
                    var def = Load(file, rel, problems);
                    if (def is not null && problems.Count == before) found[KeyOf(def.AppKey, def.Kind, def.Key)] = def;
                }
            }
            // a file that is invalid now keeps the last good version of the same key
            foreach (var p in problems)
            {
                var parts = p.File.Split('/');
                if (parts.Length != 3) continue;
                var key = KeyOf(parts[1], parts[0], Path.GetFileNameWithoutExtension(parts[2]));
                if (!found.ContainsKey(key) && previous.ByKey.TryGetValue(key, out var good)) found[key] = good;
            }
            _snapshot = new Snapshot(found, found.Values.OrderBy(d => d.AppKey).ThenBy(d => d.Kind).ThenBy(d => d.Key).ToList(), problems);
            Interlocked.Increment(ref _generation);
            foreach (var p in problems) _log?.LogWarning("Definition file {File}{Pointer}: {Message}", p.File, p.Pointer, p.Message);
        }
        Reloaded?.Invoke();
    }

    private PublishedDefinition? Load(string path, string rel, List<DefinitionFileProblem> problems)
    {
        void Bad(string pointer, string message) => problems.Add(new DefinitionFileProblem(rel, pointer, message));
        string text;
        try { text = File.ReadAllText(path, Encoding.UTF8); }
        catch (IOException ex) { Bad("", "The file cannot be read: " + ex.Message); return null; }
        if (Encoding.UTF8.GetByteCount(text) > _limits.MaxPayloadBytes) { Bad("", $"The file is larger than {_limits.MaxPayloadBytes} bytes."); return null; }

        JsonObject? root;
        try { root = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { MaxDepth = _limits.MaxJsonDepth }) as JsonObject; }
        catch (JsonException ex) { Bad("", "Not valid JSON: " + ex.Message); return null; }
        if (root is null) { Bad("", "The file must be one JSON object."); return null; }

        string? Str(string name) => root[name] is JsonValue v && v.GetValueKind() == JsonValueKind.String ? (string?)v : null;
        var kind = Str("kind"); var key = Str("key"); var appKey = Str("appKey") ?? "core";
        if (string.IsNullOrWhiteSpace(kind)) { Bad("/kind", "kind is required."); return null; }
        if (string.IsNullOrWhiteSpace(key)) { Bad("/key", "key is required."); return null; }
        var parts = rel.Split('/');
        if (parts.Length != 3 || parts[0] != kind || parts[1] != appKey || Path.GetFileNameWithoutExtension(parts[2]) != key)
        { Bad("", $"The path must be {kind}/{appKey}/{key}.json."); return null; }
        if (_ownership.OwnerOf(kind) != DefinitionOwner.Files) { Bad("/kind", $"\"{kind}\" is owned by {_ownership.OwnerOf(kind).ToString().ToLowerInvariant()}, not by the files (one owner per kind)."); return null; }
        if (!_kinds.TryGetValue(kind, out var handler)) { Bad("/kind", $"\"{kind}\" is not a registered definition kind."); return null; }
        if (root["title"] is null) Bad("/title", "title is required (fa and en).");
        if (root["payload"] is not JsonObject payload) { Bad("/payload", "payload must be an object."); return null; }
        foreach (var unknown in root.Select(p => p.Key).Where(n => n is not ("kind" or "key" or "appKey" or "version" or "title" or "payload" or "description" or "$schema")))
            Bad("/" + unknown, $"\"{unknown}\" is not a property of the envelope.");
        var version = root["version"] is JsonValue vv && vv.TryGetValue<int>(out var n) && n > 0 ? n : 1;

        CheckTexts(root, "", Bad);
        var payloadJson = payload.ToJsonString();
        foreach (var e in handler.Validate(payloadJson, _limits)) Bad("/payload" + e.Pointer, e.Message);

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payloadJson))).ToLowerInvariant();
        return new PublishedDefinition(kind, key, appKey, version, payloadJson, hash, root["title"]?.ToJsonString());
    }

    /// <summary>A translatable text is an object whose keys are all language codes: it must carry both product languages, not empty.</summary>
    private static void CheckTexts(JsonNode? node, string pointer, Action<string, string> bad)
    {
        switch (node)
        {
            case JsonObject o:
                // the arguments of an operation (name to source, for example { "id": "field:id" }) are not a translatable text even when a name looks like a language code
                if (o.Count > 0 && !pointer.EndsWith("/args", StringComparison.Ordinal) && !pointer.EndsWith("/options", StringComparison.Ordinal) && o.All(p => LanguageKey.IsMatch(p.Key) && p.Value is JsonValue))
                {
                    foreach (var lang in Languages)
                        if (!(o[lang] is JsonValue v && v.GetValueKind() == JsonValueKind.String && !string.IsNullOrWhiteSpace((string?)v)))
                            bad(pointer, $"The text has no \"{lang}\" translation.");
                    return;
                }
                foreach (var p in o) CheckTexts(p.Value, pointer + "/" + p.Key, bad);
                break;
            case JsonArray a:
                for (var i = 0; i < a.Count; i++) CheckTexts(a[i], pointer + "/" + i, bad);
                break;
        }
    }

    private void StartWatching()
    {
        _watcher = new FileSystemWatcher(_folder, "*.json") { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.DirectoryName };
        _debounce = new Timer(_ => { try { Reload(); } catch (Exception ex) { _log?.LogError(ex, "Reloading the definition files failed."); } }, null, Timeout.Infinite, Timeout.Infinite);
        void Changed(object? s, FileSystemEventArgs e) => _debounce?.Change(300, Timeout.Infinite);
        _watcher.Changed += Changed; _watcher.Created += Changed; _watcher.Deleted += Changed; _watcher.Renamed += (s, e) => Changed(s, e);
        _watcher.EnableRaisingEvents = true;
    }

    public void Dispose() { _watcher?.Dispose(); _debounce?.Dispose(); }
}
