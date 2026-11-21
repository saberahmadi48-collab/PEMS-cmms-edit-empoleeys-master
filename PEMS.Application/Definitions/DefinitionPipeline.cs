using System.Diagnostics;
using System.Text.Json.Nodes;

namespace PEMS.Application.Definitions;

// SCRUM-573 (ADR-0014): who owns a definition kind, and the run-time pipeline every page and form goes through.

/// <summary>Where a kind of definition lives. Exactly one owner per kind; there is no second copy anywhere.</summary>
public enum DefinitionOwner
{
    /// <summary>Not owned: nothing is stored or served for the kind.</summary>
    None,
    /// <summary>JSON files in the data layer (Sprint 2; the UI kinds are born here).</summary>
    Files,
    /// <summary>The definition tables (ADR-0010).</summary>
    Database,
    /// <summary>Produced at start-up from code (for example the entity definitions from the EF model); read-only.</summary>
    Generated,
    /// <summary>Kept in its own tables and read by its own service (for example workflows); not routed through the definition store.</summary>
    Native
}

/// <summary>
/// The ownership list, one owner per kind. The defaults are the owner's list of 2026-11-16; <c>Definitions:Ownership:{kind}</c> may change one value
/// (the cutover of a kind in Sprint 3 changes <c>files</c> to <c>database</c>), and a kind can only ever have the one value it is given.
/// </summary>
public sealed class DefinitionOwnership
{
    private readonly Dictionary<string, DefinitionOwner> _owners;

    public static readonly IReadOnlyDictionary<string, DefinitionOwner> Defaults = new Dictionary<string, DefinitionOwner>(StringComparer.Ordinal)
    {
        // SCRUM-579 (the cutover): every UI kind is read from the definition tables; the product's own definitions are installed there by SystemDefinitionSeeder. A kind can still be set to files for development
        // (Definitions:Ownership:{kind}=files, hot reload of a folder); one owner per kind, never both.
        ["layout"] = DefinitionOwner.Database, ["page"] = DefinitionOwner.Database, ["form"] = DefinitionOwner.Database, ["view"] = DefinitionOwner.Database,
        ["dashboard"] = DefinitionOwner.Database, ["action"] = DefinitionOwner.Database, ["data-source"] = DefinitionOwner.Database,
        ["option-set"] = DefinitionOwner.Database, ["menu"] = DefinitionOwner.Database, ["route"] = DefinitionOwner.Database, ["component"] = DefinitionOwner.Database,
        ["entity"] = DefinitionOwner.Generated, ["workflow-ref"] = DefinitionOwner.Native
    };

    public DefinitionOwnership(IReadOnlyDictionary<string, string>? overrides = null)
    {
        _owners = new Dictionary<string, DefinitionOwner>(Defaults, StringComparer.Ordinal);
        if (overrides is null) return;
        foreach (var (kind, value) in overrides)
        {
            if (!Enum.TryParse<DefinitionOwner>(value, ignoreCase: true, out var owner))
                throw new InvalidOperationException($"Definitions:Ownership:{kind} = \"{value}\" is not one of none, files, database, generated, native.");
            _owners[kind] = owner;
        }
    }

    public DefinitionOwner OwnerOf(string kind) => _owners.TryGetValue(kind, out var o) ? o : DefinitionOwner.None;
    public IReadOnlyDictionary<string, DefinitionOwner> All => _owners;
}

/// <summary>The one store the runtime reads: each kind is routed to its single owner; a kind nobody serves gives nothing.</summary>
public sealed class RoutedDefinitionStore(DefinitionOwnership ownership, IReadOnlyDictionary<DefinitionOwner, IDefinitionStore> providers) : IDefinitionStore
{
    public Task<PublishedDefinition?> GetPublishedAsync(string kind, string key, string appKey = "core", CancellationToken ct = default)
        => providers.TryGetValue(ownership.OwnerOf(kind), out var provider) ? provider.GetPublishedAsync(kind, key, appKey, ct) : Task.FromResult<PublishedDefinition?>(null);

    public Task<IReadOnlyList<PublishedDefinition>> ListPublishedAsync(string kind, string appKey = "core", CancellationToken ct = default)
        => providers.TryGetValue(ownership.OwnerOf(kind), out var provider) ? provider.ListPublishedAsync(kind, appKey, ct) : Task.FromResult<IReadOnlyList<PublishedDefinition>>([]);
}

public enum PipelineStatus { Ok, NotFound, Forbidden }

/// <summary>What one request asks of the pipeline: which definition, for whom, in which language.</summary>
public sealed record PipelineRequest(string Kind, string Key, string AppKey, string Culture, IReadOnlyCollection<string> Roles, string? UserName = null, IReadOnlyDictionary<string, string>? Parameters = null);

/// <summary>
/// The definition for this request: a private copy of the published payload that the hydrators may fill (<see cref="Hydrated"/>); the cached published
/// definition is never touched, so it can be shared by every user.
/// </summary>
public sealed class ResolvedDefinition(PublishedDefinition source, JsonNode payload)
{
    public PublishedDefinition Source { get; } = source;
    public JsonNode Payload { get; } = payload;
    /// <summary>What the hydrators resolved for this request (option lists, lookups, current values), by the id of the thing they belong to.</summary>
    public IDictionary<string, object?> Hydrated { get; } = new Dictionary<string, object?>(StringComparer.Ordinal);
    /// <summary>What the hydrators loaded for this request (option lists, options, rows ...), reported next to the stage timings (SCRUM-577).</summary>
    public IDictionary<string, int> Counts { get; } = new Dictionary<string, int>(StringComparer.Ordinal);
}

public sealed record PipelineResult(PipelineStatus Status, ResolvedDefinition? Resolved, IReadOnlyDictionary<string, TimeSpan> Timings, IReadOnlyDictionary<string, int>? CountsOrNull = null)
{
    public IReadOnlyDictionary<string, int> Counts => CountsOrNull ?? new Dictionary<string, int>();
    public bool Ok => Status == PipelineStatus.Ok;
}

/// <summary>Stage 2: may this user open the page or form? Asked before any data is read.</summary>
public interface IDefinitionAccess
{
    /// <param name="permission">The definition's <c>permission</c> ("Module.Action"), null when the definition names none.</param>
    Task<bool> CanOpenAsync(string? permission, IReadOnlyCollection<string> roles, CancellationToken ct = default);

    /// <summary>SCRUM-591: the same question for a known definition, so the grants kept in the database for its key and roles can add to or take from the permission's answer. The default ignores the grants.</summary>
    Task<bool> CanOpenAsync(string kind, string key, string? permission, IReadOnlyCollection<string> roles, CancellationToken ct = default) => CanOpenAsync(permission, roles, ct);
}

/// <summary>Stage 3: resolves what the definition declares for this request (options, lookups, values, the parts the user may see). Runs in <see cref="Order"/>, never changes the cached definition.</summary>
public interface IDefinitionHydrator
{
    int Order { get; }
    Task HydrateAsync(ResolvedDefinition definition, PipelineRequest request, CancellationToken ct = default);
}

/// <summary>
/// Resolve, authorize, hydrate (evaluate and render follow with the expression engine and the page host). The published definition comes from its single owner
/// through <see cref="IDefinitionStore"/>; the time of every stage is returned so the engine can be measured (SCRUM-577).
/// </summary>
public sealed class DefinitionPipeline(IDefinitionStore store, IDefinitionAccess access, IEnumerable<IDefinitionHydrator> hydrators, RenderPlanCache? plans = null)
{
    private readonly IReadOnlyList<IDefinitionHydrator> _hydrators = hydrators.OrderBy(h => h.Order).ToList();

    public async Task<PipelineResult> RunAsync(PipelineRequest request, CancellationToken ct = default)
    {
        var timings = new Dictionary<string, TimeSpan>(StringComparer.Ordinal);
        var clock = Stopwatch.StartNew();

        var published = await store.GetPublishedAsync(request.Kind, request.Key, request.AppKey, ct);
        timings["resolve"] = clock.Elapsed;
        if (published is null) return new PipelineResult(PipelineStatus.NotFound, null, timings, new Dictionary<string, int> { ["definitions"] = 0 });

        var payload = plans?.Get(published)?.Instantiate() ?? JsonNode.Parse(published.PayloadJson)!;   // a private copy for this request (SCRUM-576: from the compiled plan, parsed once per content hash)
        clock.Restart();
        var permission = payload is JsonObject o && o["permission"] is JsonValue pv && pv.TryGetValue<string>(out var p) ? p : null;
        var allowed = await access.CanOpenAsync(request.Kind, request.Key, permission, request.Roles, ct);
        timings["authorize"] = clock.Elapsed;
        if (!allowed) return new PipelineResult(PipelineStatus.Forbidden, null, timings, new Dictionary<string, int> { ["definitions"] = 1 });

        clock.Restart();
        var resolved = new ResolvedDefinition(published, payload);
        foreach (var hydrator in _hydrators) await hydrator.HydrateAsync(resolved, request, ct);
        timings["hydrate"] = clock.Elapsed;
        var counts = new Dictionary<string, int>(resolved.Counts, StringComparer.Ordinal) { ["definitions"] = 1, ["hydrators"] = _hydrators.Count, ["payloadBytes"] = published.PayloadJson.Length };
        if (plans is not null) counts["planCompiles"] = (int)plans.Compiles;
        return new PipelineResult(PipelineStatus.Ok, resolved, timings, counts);
    }
}
