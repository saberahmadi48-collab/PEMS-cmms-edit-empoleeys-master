using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.Definitions;
using PEMS.Domain.Entities.Definitions;
using PEMS.Persistence.Context;

namespace PEMS.Persistence.Services;

/// <summary>
/// The write path of the definition store (SCRUM-434, ADR-0010/0011): draft → validated → published → retired, rollback by republishing,
/// optimistic concurrency by revision numbers, kind validation, references, per-kind cache stamps. The REST API is its only caller.
/// </summary>
public sealed class DefinitionService : IDefinitionService, IDefinitionShipper, IDefinitionPreflight
{
    private static readonly Regex KeyPattern = new(@"^[a-z0-9]+([._-][a-z0-9]+)*$", RegexOptions.Compiled);
    private static readonly Regex AppKeyPattern = new(@"^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.Compiled);

    private readonly PEMSDbContext _db;
    private readonly IReadOnlyDictionary<string, IDefinitionKind> _kinds;
    private readonly DefinitionLimits _limits;
    private readonly DefinitionStore _store;

    public DefinitionService(PEMSDbContext db, IEnumerable<IDefinitionKind> kinds, DefinitionLimits limits, DefinitionStore store)
    {
        _db = db;
        _kinds = kinds.ToDictionary(k => k.Key, StringComparer.Ordinal);
        _limits = limits;
        _store = store;
    }

    // ------------------------------------------------------------------ reads

    public async Task<DefinitionPageResult> ListAsync(DefinitionListQuery q, CancellationToken ct = default)
    {
        IQueryable<Definition> rows = _db.Definitions.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q.Kind)) rows = rows.Where(d => d.Kind == q.Kind);
        if (!string.IsNullOrWhiteSpace(q.AppKey)) rows = rows.Where(d => d.AppKey == q.AppKey);
        if (!string.IsNullOrWhiteSpace(q.Q)) { var t = q.Q.Trim(); rows = rows.Where(d => d.Key.Contains(t) || d.TitleJson.Contains(t)); }
        if (q.Published is { } p) rows = rows.Where(d => (d.PublishedVersionId != null) == p);

        var total = await rows.CountAsync(ct);
        IOrderedQueryable<Definition>? ordered = null;
        foreach (var (field, desc) in q.Sort)
        {
            ordered = (field.ToLowerInvariant(), ordered == null, desc) switch
            {
                ("key", true, false) => rows.OrderBy(d => d.Key),
                ("key", true, true) => rows.OrderByDescending(d => d.Key),
                ("key", false, false) => ordered!.ThenBy(d => d.Key),
                ("key", false, true) => ordered!.ThenByDescending(d => d.Key),
                ("kind", true, false) => rows.OrderBy(d => d.Kind),
                ("kind", true, true) => rows.OrderByDescending(d => d.Kind),
                ("kind", false, false) => ordered!.ThenBy(d => d.Kind),
                ("kind", false, true) => ordered!.ThenByDescending(d => d.Kind),
                ("createddate", true, false) => rows.OrderBy(d => d.CreatedAtUtc),
                ("createddate", true, true) => rows.OrderByDescending(d => d.CreatedAtUtc),
                ("createddate", false, false) => ordered!.ThenBy(d => d.CreatedAtUtc),
                ("createddate", false, true) => ordered!.ThenByDescending(d => d.CreatedAtUtc),
                _ => ordered,
            };
        }
        ordered = ordered == null ? rows.OrderBy(d => d.Kind).ThenBy(d => d.Key) : ordered.ThenBy(d => d.DefinitionId);

        var page = await ordered.Skip((q.Page - 1) * q.PageSize).Take(q.PageSize)
            .Select(d => new
            {
                d,
                Published = d.PublishedVersion != null ? (int?)d.PublishedVersion.VersionNumber : null,
                HasDraft = d.Versions.Any(v => v.Status == DefinitionStatus.Draft || v.Status == DefinitionStatus.Validated),
            }).ToListAsync(ct);
        return new DefinitionPageResult(page.Select(x => Summary(x.d, x.Published, x.HasDraft)).ToList(), total);
    }

    public async Task<DefinitionOutcome<DefinitionDetail>> GetAsync(int id, CancellationToken ct = default)
    {
        var def = await LoadAsync(id, tracking: false, ct);
        return def == null ? DefinitionOutcome<DefinitionDetail>.NotFound() : DefinitionOutcome<DefinitionDetail>.Ok(Detail(def));
    }

    public async Task<DefinitionOutcome<DefinitionVersionPayload>> GetVersionAsync(int id, int versionNumber, CancellationToken ct = default)
    {
        var def = await LoadAsync(id, tracking: false, ct);
        var v = def?.Versions.FirstOrDefault(x => x.VersionNumber == versionNumber);
        return v == null ? DefinitionOutcome<DefinitionVersionPayload>.NotFound() : DefinitionOutcome<DefinitionVersionPayload>.Ok(Payload(def!, v));
    }

    public async Task<DefinitionOutcome<DefinitionVersionPayload>> GetDraftAsync(int id, CancellationToken ct = default)
    {
        var def = await LoadAsync(id, tracking: false, ct);
        if (def == null) return DefinitionOutcome<DefinitionVersionPayload>.NotFound();
        var draft = OpenDraft(def);
        return draft == null ? DefinitionOutcome<DefinitionVersionPayload>.Fail(DefinitionFailure.NotFound, "no-draft") : DefinitionOutcome<DefinitionVersionPayload>.Ok(Payload(def, draft));
    }

    // ------------------------------------------------------------------ writes

    public async Task<DefinitionOutcome<DefinitionDetail>> CreateAsync(CreateDefinition c, string actor, CancellationToken ct = default)
    {
        var errors = new List<DefinitionFieldError>();
        var appKey = string.IsNullOrWhiteSpace(c.AppKey) ? "core" : c.AppKey.Trim();
        var kind = (c.Kind ?? "").Trim();
        var key = (c.Key ?? "").Trim();
        if (!_kinds.ContainsKey(kind)) errors.Add(Err("/kind", "unknown-kind", "Def_Err_UnknownKind", $"'{kind}' is not a registered definition kind."));
        if (key.Length is 0 or > 200 || !KeyPattern.IsMatch(key)) errors.Add(Err("/key", "invalid-key", "Def_Err_InvalidKey", "Use lower-case letters, digits and . _ - (for example work-orders.list)."));
        if (appKey.Length > 100 || !AppKeyPattern.IsMatch(appKey)) errors.Add(Err("/appKey", "invalid-app-key", "Def_Err_InvalidAppKey", "Use lower-case letters, digits and -."));
        if (!IsJsonValue(c.TitleJson)) errors.Add(Err("/title", "invalid-json", "Def_Err_InvalidJson", "The title must be a string or a language map."));
        if (c.DescriptionJson != null && !IsJsonValue(c.DescriptionJson)) errors.Add(Err("/description", "invalid-json", "Def_Err_InvalidJson", "The description must be a string or a language map."));
        var canonical = Canonicalize(c.PayloadJson, out var payloadError);
        if (payloadError != null) errors.Add(payloadError with { Pointer = "/payload" });
        if (errors.Count > 0) return DefinitionOutcome<DefinitionDetail>.Invalid(errors);

        if (await _db.Definitions.AnyAsync(d => d.AppKey == appKey && d.Kind == kind && d.Key == key, ct))
            return DefinitionOutcome<DefinitionDetail>.Fail(DefinitionFailure.Conflict, "duplicate-key");

        var now = DateTime.UtcNow;
        var def = new Definition { Kind = kind, Key = key, AppKey = appKey, Origin = DefinitionOrigin.Customer, TitleJson = c.TitleJson, DescriptionJson = c.DescriptionJson, CreatedAtUtc = now, CreatedBy = actor };
        def.Versions.Add(NewVersion(1, canonical!, c.ChangeSummary, now, actor, null));
        _db.Definitions.Add(def);
        if (!await SaveAsync(ct)) return DefinitionOutcome<DefinitionDetail>.Fail(DefinitionFailure.Conflict, "duplicate-key");
        return DefinitionOutcome<DefinitionDetail>.Ok(Detail(def));
    }

    public async Task<DefinitionOutcome<DefinitionDetail>> UpdateEnvelopeAsync(int id, string titleJson, string? descriptionJson, long expectedRevision, string actor, CancellationToken ct = default)
    {
        var def = await LoadAsync(id, tracking: true, ct);
        if (def == null) return DefinitionOutcome<DefinitionDetail>.NotFound();
        var errors = new List<DefinitionFieldError>();
        if (!IsJsonValue(titleJson)) errors.Add(Err("/title", "invalid-json", "Def_Err_InvalidJson", "The title must be a string or a language map."));
        if (descriptionJson != null && !IsJsonValue(descriptionJson)) errors.Add(Err("/description", "invalid-json", "Def_Err_InvalidJson", "The description must be a string or a language map."));
        if (errors.Count > 0) return DefinitionOutcome<DefinitionDetail>.Invalid(errors);
        if (def.Revision != expectedRevision) return DefinitionOutcome<DefinitionDetail>.Stale();

        Touch(def, expectedRevision);
        def.TitleJson = titleJson;
        def.DescriptionJson = descriptionJson;
        return await SaveDetailAsync(def, ct);
    }

    public async Task<DefinitionOutcome<DefinitionVersionPayload>> CreateDraftAsync(int id, int? fromVersion, long expectedEnvelopeRevision, string actor, CancellationToken ct = default)
    {
        var def = await LoadAsync(id, tracking: true, ct);
        if (def == null) return DefinitionOutcome<DefinitionVersionPayload>.NotFound();
        if (def.Revision != expectedEnvelopeRevision) return DefinitionOutcome<DefinitionVersionPayload>.Stale();
        if (OpenDraft(def) != null) return DefinitionOutcome<DefinitionVersionPayload>.Fail(DefinitionFailure.Conflict, "draft-exists");
        var source = fromVersion is { } n ? def.Versions.FirstOrDefault(v => v.VersionNumber == n && v.Status is DefinitionStatus.Published or DefinitionStatus.Retired) : def.PublishedVersion;
        if (source == null) return DefinitionOutcome<DefinitionVersionPayload>.Fail(DefinitionFailure.Conflict, fromVersion != null ? "version-not-found" : "no-published-version");

        Touch(def, expectedEnvelopeRevision);
        var draft = NewVersion(def.Versions.Max(v => v.VersionNumber) + 1, source.PayloadJson, $"Draft from version {source.VersionNumber}", DateTime.UtcNow, actor, source.DefinitionVersionId);
        draft.Definition = def;
        _db.DefinitionVersions.Add(draft);
        if (!await SaveAsync(ct)) return DefinitionOutcome<DefinitionVersionPayload>.Stale();
        return DefinitionOutcome<DefinitionVersionPayload>.Ok(Payload(def, draft));
    }

    public async Task<DefinitionOutcome<DefinitionVersionPayload>> SaveDraftAsync(int id, string payloadJson, string? changeSummary, long expectedDraftRevision, string actor, CancellationToken ct = default)
    {
        var (def, draft, miss) = await LoadDraftAsync<DefinitionVersionPayload>(id, ct);
        if (miss != null) return miss;
        if (draft!.Revision != expectedDraftRevision) return DefinitionOutcome<DefinitionVersionPayload>.Stale();
        var canonical = Canonicalize(payloadJson, out var error);
        if (error != null) return DefinitionOutcome<DefinitionVersionPayload>.Invalid(new[] { error with { Pointer = "/payload" } });

        Touch(draft, expectedDraftRevision);
        draft.PayloadJson = canonical!;
        draft.ContentHash = Hash(canonical!);
        draft.Status = DefinitionStatus.Draft;
        draft.ValidatedAtUtc = null;
        draft.ValidatedBy = null;
        if (changeSummary != null) draft.ChangeSummary = Trim(changeSummary, 500);
        _db.DefinitionReferences.RemoveRange(_db.DefinitionReferences.Where(r => r.FromVersionId == draft.DefinitionVersionId));
        if (!await SaveAsync(ct)) return DefinitionOutcome<DefinitionVersionPayload>.Stale();
        return DefinitionOutcome<DefinitionVersionPayload>.Ok(Payload(def!, draft));
    }

    public async Task<DefinitionOutcome<DefinitionVersionPayload>> ValidateAsync(int id, long expectedDraftRevision, string actor, CancellationToken ct = default)
    {
        var (def, draft, miss) = await LoadDraftAsync<DefinitionVersionPayload>(id, ct);
        if (miss != null) return miss;
        if (draft!.Revision != expectedDraftRevision) return DefinitionOutcome<DefinitionVersionPayload>.Stale();
        var errors = Check(def!.Kind, draft.PayloadJson).Concat(OwnershipErrors(def, draft.PayloadJson)).ToList();
        if (errors.Count > 0) return DefinitionOutcome<DefinitionVersionPayload>.Invalid(errors);

        Touch(draft, expectedDraftRevision);
        MarkValidated(draft, actor);
        await ReplaceReferencesAsync(draft, def.Kind, ct);
        if (!await SaveAsync(ct)) return DefinitionOutcome<DefinitionVersionPayload>.Stale();
        return DefinitionOutcome<DefinitionVersionPayload>.Ok(Payload(def, draft));
    }

    public async Task<DefinitionOutcome<DefinitionDetail>> PublishAsync(int id, long expectedDraftRevision, string actor, CancellationToken ct = default)
    {
        var (def, draft, miss) = await LoadDraftAsync<DefinitionDetail>(id, ct);
        if (miss != null) return miss;
        if (draft!.Revision != expectedDraftRevision) return DefinitionOutcome<DefinitionDetail>.Stale();
        var errors = Check(def!.Kind, draft.PayloadJson).Concat(OwnershipErrors(def, draft.PayloadJson)).ToList();
        if (errors.Count > 0) return DefinitionOutcome<DefinitionDetail>.Invalid(errors);

        Touch(draft, expectedDraftRevision);
        MarkValidated(draft, actor);
        await ReplaceReferencesAsync(draft, def.Kind, ct);
        await PublishVersionAsync(def, draft, actor, ct);
        return await SaveDetailAsync(def, ct, evictKind: def.Kind);
    }

    public async Task<DefinitionOutcome<DefinitionDetail>> RollbackAsync(int id, int toVersion, long expectedEnvelopeRevision, string actor, CancellationToken ct = default, string? changeSummary = null)
    {
        var def = await LoadAsync(id, tracking: true, ct);
        if (def == null) return DefinitionOutcome<DefinitionDetail>.NotFound();
        if (def.Revision != expectedEnvelopeRevision) return DefinitionOutcome<DefinitionDetail>.Stale();
        var target = def.Versions.FirstOrDefault(v => v.VersionNumber == toVersion);
        if (target == null || target.Status is DefinitionStatus.Draft or DefinitionStatus.Validated)
            return DefinitionOutcome<DefinitionDetail>.Fail(DefinitionFailure.Conflict, "version-not-found");
        if (target.DefinitionVersionId == def.PublishedVersionId) return DefinitionOutcome<DefinitionDetail>.Fail(DefinitionFailure.Conflict, "already-published");
        var errors = Check(def.Kind, target.PayloadJson);
        if (errors.Count > 0) return DefinitionOutcome<DefinitionDetail>.Invalid(errors);

        Touch(def, expectedEnvelopeRevision);
        var now = DateTime.UtcNow;
        var copy = NewVersion(def.Versions.Max(v => v.VersionNumber) + 1, target.PayloadJson, changeSummary ?? $"Rollback to version {target.VersionNumber}", now, actor, target.DefinitionVersionId);
        copy.Definition = def;
        copy.PayloadSchemaVersion = target.PayloadSchemaVersion;
        _db.DefinitionVersions.Add(copy);
        MarkValidated(copy, actor);
        await ReplaceReferencesAsync(copy, def.Kind, ct);
        await PublishVersionAsync(def, copy, actor, ct, envelopeAlreadyTouched: true);
        return await SaveDetailAsync(def, ct, evictKind: def.Kind);
    }

    public async Task<DefinitionOutcome<DefinitionUpstream>> GetUpstreamAsync(int id, CancellationToken ct = default)
    {
        var def = await LoadAsync(id, tracking: false, ct);
        if (def == null) return DefinitionOutcome<DefinitionUpstream>.NotFound();
        var shipped = def.Versions.FirstOrDefault(v => v.DefinitionVersionId == def.ShippedVersionId);
        var published = def.Versions.FirstOrDefault(v => v.DefinitionVersionId == def.PublishedVersionId);
        if (def.Origin != DefinitionOrigin.System || shipped == null || published == null) return DefinitionOutcome<DefinitionUpstream>.NotFound();

        // the shipped version the customer's published version descends from: follow ClonedFromVersionId back to a version made by shipping
        var basedOn = BasedOnShipped(def, published);
        var upstreamChanged = def.IsCustomized && basedOn != null && basedOn != shipped.VersionNumber;
        var diff = new List<DefinitionDiffEntry>();
        var truncated = false;
        if (def.IsCustomized)
        {
            DiffNode("", JsonNode.Parse(published.PayloadJson), JsonNode.Parse(shipped.PayloadJson), diff, MaxDiffEntries);
            truncated = diff.Count >= MaxDiffEntries;
        }
        return DefinitionOutcome<DefinitionUpstream>.Ok(new DefinitionUpstream(def.IsCustomized, upstreamChanged, shipped.VersionNumber, basedOn, published.VersionNumber, diff, truncated));
    }

    public async Task<DefinitionOutcome<DefinitionDetail>> ResetToShippedAsync(int id, long expectedEnvelopeRevision, string actor, CancellationToken ct = default)
    {
        var def = await LoadAsync(id, tracking: true, ct);
        if (def == null) return DefinitionOutcome<DefinitionDetail>.NotFound();
        var shipped = def.Versions.FirstOrDefault(v => v.DefinitionVersionId == def.ShippedVersionId);
        if (def.Origin != DefinitionOrigin.System || shipped == null) return DefinitionOutcome<DefinitionDetail>.Fail(DefinitionFailure.Conflict, "not-shipped");
        if (def.Revision != expectedEnvelopeRevision) return DefinitionOutcome<DefinitionDetail>.Stale();
        if (shipped.DefinitionVersionId == def.PublishedVersionId || !def.IsCustomized) return DefinitionOutcome<DefinitionDetail>.Fail(DefinitionFailure.Conflict, "already-published");   // the published payload already is the shipped one
        return await RollbackAsync(id, shipped.VersionNumber, expectedEnvelopeRevision, actor, ct);
    }

    /// <summary>SCRUM-515: a customer's edit of a shipped form may not touch its platform fields (see <see cref="FormOwnership"/>).</summary>
    private static IReadOnlyList<DefinitionFieldError> OwnershipErrors(Definition def, string payloadJson)
    {
        if (def.Kind != "form" || def.Origin != DefinitionOrigin.System) return [];
        var shipped = def.Versions.FirstOrDefault(v => v.DefinitionVersionId == def.ShippedVersionId);
        return shipped == null ? [] : FormOwnership.Protect(shipped.PayloadJson, payloadJson);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<OwnershipProblem>> CheckOwnershipAsync(IReadOnlyList<ShippedDefinition> items, CancellationToken ct = default) => OwnershipPreflightAsync(items, ct);

    private async Task<IReadOnlyList<OwnershipProblem>> OwnershipPreflightAsync(IReadOnlyList<ShippedDefinition> items, CancellationToken ct)
    {
        var problems = new List<OwnershipProblem>();
        foreach (var item in items.Where(i => i.Kind == "form"))
        {
            var def = await _db.Definitions.Include(d => d.Versions).AsNoTracking().FirstOrDefaultAsync(d => d.AppKey == item.AppKey && d.Kind == item.Kind && d.Key == item.Key, ct);
            if (def is not { Origin: DefinitionOrigin.System, IsCustomized: true }) continue;
            var published = def.Versions.FirstOrDefault(v => v.DefinitionVersionId == def.PublishedVersionId);
            if (published == null) continue;
            var canonical = Canonicalize(item.PayloadJson, out var perr);
            if (perr != null) continue;                                  // reported by the normal path
            foreach (var c in FormOwnership.Merge(published.PayloadJson, canonical!).Conflicts)
                problems.Add(new OwnershipProblem(item.Kind, item.AppKey, item.Key, c.Field, c.Code, c.Message));
        }
        return problems;
    }

    /// <summary>Publishes the customer's form again with the new platform fields merged in (application fields stay as they are); false when nothing changed.</summary>
    private async Task<bool> MergePlatformFieldsAsync(Definition def, DefinitionVersion current, string newShippedCanonical, string actor, DateTime now, CancellationToken ct)
    {
        var merge = FormOwnership.Merge(current.PayloadJson, newShippedCanonical);
        if (!merge.Ok || merge.MergedJson is null) return false;
        var canonical = Canonicalize(merge.MergedJson, out var error);
        if (error != null || Check(def.Kind, canonical!).Count > 0 || Hash(canonical!) == current.ContentHash) return false;
        var version = NewVersion(def.Versions.Max(v => v.VersionNumber) + 1, canonical!, "Product update: platform fields merged", now, actor, current.DefinitionVersionId);
        version.Definition = def;
        _db.DefinitionVersions.Add(version);
        await _db.SaveChangesAsync(ct);
        await ReplaceReferencesAsync(version, def.Kind, ct);
        MarkValidated(version, actor);
        await PublishVersionAsync(def, version, actor, ct);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    private const int MaxDiffEntries = 200;
    private const string ShippedSummary = "Shipped with the product";

    private static int? BasedOnShipped(Definition def, DefinitionVersion from)
    {
        var seen = new HashSet<int>();
        for (var v = from; v != null && seen.Add(v.DefinitionVersionId); v = v.ClonedFromVersionId is { } id ? def.Versions.FirstOrDefault(x => x.DefinitionVersionId == id) : null)
            if (v.ChangeSummary?.StartsWith(ShippedSummary, StringComparison.Ordinal) == true) return v.VersionNumber;
        return null;
    }

    /// <summary>Leaf-level structural difference between two JSON values: objects by property, arrays by index.</summary>
    internal static void DiffNode(string pointer, JsonNode? published, JsonNode? shipped, List<DefinitionDiffEntry> into, int cap)
    {
        if (into.Count >= cap) return;
        if (published is JsonObject po && shipped is JsonObject so)
        {
            foreach (var key in po.Select(p => p.Key).Union(so.Select(p => p.Key)).OrderBy(k => k, StringComparer.Ordinal))
                DiffNode(pointer + "/" + key.Replace("~", "~0").Replace("/", "~1"), po[key], so[key], into, cap);
            return;
        }
        if (published is JsonArray pa && shipped is JsonArray sa)
        {
            for (var i = 0; i < Math.Max(pa.Count, sa.Count); i++)
                DiffNode(pointer + "/" + i, i < pa.Count ? pa[i] : null, i < sa.Count ? sa[i] : null, into, cap);
            return;
        }
        var a = published?.ToJsonString();
        var b = shipped?.ToJsonString();
        if (a == b) return;
        into.Add(new DefinitionDiffEntry(pointer.Length == 0 ? "/" : pointer, published is null ? "added-upstream" : shipped is null ? "removed-upstream" : "changed", a, b));
    }

    public async Task<DefinitionOutcome<DefinitionDetail>> DiscardDraftAsync(int id, long expectedDraftRevision, string actor, CancellationToken ct = default)
    {
        var (def, draft, miss) = await LoadDraftAsync<DefinitionDetail>(id, ct);
        if (miss != null) return miss;
        if (draft!.Revision != expectedDraftRevision) return DefinitionOutcome<DefinitionDetail>.Stale();
        // A definition must keep at least one version: the only version (never published) cannot be discarded, delete the definition instead (not offered yet).
        if (def!.Versions.Count == 1) return DefinitionOutcome<DefinitionDetail>.Fail(DefinitionFailure.Conflict, "only-version");

        Touch(draft, expectedDraftRevision);
        Touch(def, def.Revision);
        _db.DefinitionVersions.Remove(draft);
        def.Versions.Remove(draft);
        return await SaveDetailAsync(def, ct);
    }

    // ------------------------------------------------------------------ export and import

    public async Task<DefinitionOutcome<DefinitionPackage>> ExportAsync(int id, int? version, CancellationToken ct = default)
    {
        var def = await LoadAsync(id, tracking: false, ct);
        if (def == null) return DefinitionOutcome<DefinitionPackage>.NotFound();
        var v = version is { } n ? def.Versions.FirstOrDefault(x => x.VersionNumber == n) : def.PublishedVersion;
        if (v == null) return DefinitionOutcome<DefinitionPackage>.Fail(version != null ? DefinitionFailure.NotFound : DefinitionFailure.Conflict, version != null ? "not-found" : "not-published");
        var item = new DefinitionPackageItem(def.Kind, def.Key, def.AppKey, def.TitleJson, def.DescriptionJson, v.PayloadJson, v.ContentHash, v.VersionNumber);
        return DefinitionOutcome<DefinitionPackage>.Ok(new DefinitionPackage(DefinitionPackage.CurrentFormat, DateTime.UtcNow, new[] { item }));
    }

    public async Task<DefinitionOutcome<DefinitionImportResult>> ImportAsync(DefinitionPackage package, string actor, CancellationToken ct = default)
    {
        var errors = new List<DefinitionFieldError>();
        if (package.Format != DefinitionPackage.CurrentFormat) errors.Add(Err("/format", "unsupported-format", "Def_Err_Format", $"Expected '{DefinitionPackage.CurrentFormat}'."));
        if (package.Items.Count == 0) errors.Add(Err("/items", "empty", "Def_Err_Empty", "The package has no items."));
        var prepared = new List<(DefinitionPackageItem Item, string Canonical)>();
        for (var i = 0; i < package.Items.Count; i++)
        {
            var item = package.Items[i];
            var at = $"/items/{i}";
            if (!_kinds.ContainsKey(item.Kind)) errors.Add(Err(at + "/kind", "unknown-kind", "Def_Err_UnknownKind", $"'{item.Kind}' is not a registered definition kind."));
            if (item.Key.Length is 0 or > 200 || !KeyPattern.IsMatch(item.Key)) errors.Add(Err(at + "/key", "invalid-key", "Def_Err_InvalidKey", "The key is not valid."));
            if (item.AppKey.Length is 0 or > 100 || !AppKeyPattern.IsMatch(item.AppKey)) errors.Add(Err(at + "/appKey", "invalid-app-key", "Def_Err_InvalidAppKey", "The app key is not valid."));
            if (!IsJsonValue(item.TitleJson)) errors.Add(Err(at + "/title", "invalid-json", "Def_Err_InvalidJson", "The title must be a string or a language map."));
            var canonical = Canonicalize(item.PayloadJson, out var perr);
            if (perr != null) { errors.Add(perr with { Pointer = at + "/payload" }); continue; }
            if (!string.Equals(Hash(canonical!), item.ContentHash, StringComparison.OrdinalIgnoreCase))
                errors.Add(Err(at + "/contentHash", "hash-mismatch", "Def_Err_Hash", "The payload does not match its content hash; the package was changed."));
            if (_kinds.ContainsKey(item.Kind))
                errors.AddRange(Check(item.Kind, canonical!).Select(e => e with { Pointer = at + "/payload" + e.Pointer }));
            if (package.Items.Take(i).Any(o => o.AppKey == item.AppKey && o.Kind == item.Kind && o.Key == item.Key))
                errors.Add(Err(at + "/key", "duplicate-in-package", "Def_Err_Duplicate", "The same definition appears twice in the package."));
            prepared.Add((item, canonical!));
        }
        if (errors.Count > 0) return DefinitionOutcome<DefinitionImportResult>.Invalid(errors);

        var created = 0; var updated = 0; var pending = new List<Definition>(); var now = DateTime.UtcNow;
        foreach (var (item, canonical) in prepared)
        {
            var def = await _db.Definitions.Include(d => d.Versions).FirstOrDefaultAsync(d => d.AppKey == item.AppKey && d.Kind == item.Kind && d.Key == item.Key, ct);
            if (def == null)
            {
                def = new Definition { Kind = item.Kind, Key = item.Key, AppKey = item.AppKey, Origin = DefinitionOrigin.Customer, TitleJson = item.TitleJson, DescriptionJson = item.DescriptionJson, CreatedAtUtc = now, CreatedBy = actor };
                def.Versions.Add(NewVersion(1, canonical, $"Imported (version {item.VersionNumber} of the source)", now, actor, null));
                _db.Definitions.Add(def);
                created++;
            }
            else
            {
                if (OpenDraft(def) != null) return DefinitionOutcome<DefinitionImportResult>.Invalid(new[] { Err($"/items/{prepared.FindIndex(p => p.Item == item)}/key", "draft-exists", "Def_Err_DraftExists", "This definition has an open draft; publish or discard it first.") });
                Touch(def, def.Revision);
                var draft = NewVersion(def.Versions.Max(v => v.VersionNumber) + 1, canonical, $"Imported (version {item.VersionNumber} of the source)", now, actor, null);
                draft.Definition = def;
                _db.DefinitionVersions.Add(draft);
                updated++;
            }
            pending.Add(def);
        }
        if (!await SaveAsync(ct)) return DefinitionOutcome<DefinitionImportResult>.Stale();
        return DefinitionOutcome<DefinitionImportResult>.Ok(new DefinitionImportResult(created, updated, pending.Select(d => d.DefinitionId).ToList()));
    }

    // ------------------------------------------------------------------ shipping (SCRUM-579)

    /// <summary>
    /// Writes the definitions that ship with the product as System definitions. A new key is created and published (version 1, also the shipped version); a key whose shipped content changed is upgraded to a new
    /// published version unless a customer changed it (then the customer's version stays published and the new shipped version is kept as a retired version they can compare with or roll back to); a key a
    /// customer created themselves is never touched. Running it twice changes nothing.
    /// </summary>
    public async Task<DefinitionShipResult> ShipAsync(IReadOnlyList<ShippedDefinition> items, string actor, CancellationToken ct = default)
    {
        int created = 0, upgraded = 0, unchanged = 0, kept = 0, customerOwned = 0;
        var problems = new List<string>();
        var touchedKinds = new HashSet<string>(StringComparer.Ordinal);
        var merged = 0;

        // SCRUM-515 preflight: an update that cannot keep the customer's application fields and data stops before it changes anything, with every conflict listed at once
        var conflicts = await OwnershipPreflightAsync(items, ct);
        if (conflicts.Count > 0) return new DefinitionShipResult(0, 0, 0, 0, 0, conflicts.Select(c => c.Line).ToList());

        foreach (var item in items)
        {
            var at = $"{item.Kind}/{item.AppKey}/{item.Key}";
            if (!_kinds.ContainsKey(item.Kind)) { problems.Add($"{at}: '{item.Kind}' is not a registered definition kind."); continue; }
            if (!KeyPattern.IsMatch(item.Key) || !AppKeyPattern.IsMatch(item.AppKey)) { problems.Add($"{at}: the key is not valid."); continue; }
            if (!IsJsonValue(item.TitleJson)) { problems.Add($"{at}: the title must be a string or a language map."); continue; }
            var canonical = Canonicalize(item.PayloadJson, out var perr);
            if (perr != null) { problems.Add($"{at}: {perr.Message}"); continue; }
            var invalid = Check(item.Kind, canonical!);
            if (invalid.Count > 0) { problems.Add($"{at}: {invalid[0].Pointer} {invalid[0].Message}"); continue; }
            var hash = Hash(canonical!);
            var now = DateTime.UtcNow;

            var def = await _db.Definitions.Include(d => d.Versions).FirstOrDefaultAsync(d => d.AppKey == item.AppKey && d.Kind == item.Kind && d.Key == item.Key, ct);
            if (def == null)
            {
                def = new Definition { Kind = item.Kind, Key = item.Key, AppKey = item.AppKey, Origin = DefinitionOrigin.System, TitleJson = item.TitleJson, DescriptionJson = item.DescriptionJson, CreatedAtUtc = now, CreatedBy = actor };
                var first = NewVersion(1, canonical!, "Shipped with the product", now, actor, null);
                def.Versions.Add(first);
                _db.Definitions.Add(def);
                await _db.SaveChangesAsync(ct);                       // the version needs its id before the definition can point at it
                await ReplaceReferencesAsync(first, item.Kind, ct);
                MarkValidated(first, actor);
                await PublishVersionAsync(def, first, actor, ct, envelopeAlreadyTouched: true);
                def.ShippedVersionId = first.DefinitionVersionId;
                await _db.SaveChangesAsync(ct);
                touchedKinds.Add(item.Kind);
                created++;
                continue;
            }
            if (def.Origin != DefinitionOrigin.System) { customerOwned++; continue; }

            var shipped = def.Versions.FirstOrDefault(v => v.DefinitionVersionId == def.ShippedVersionId);
            if (shipped?.ContentHash == hash) { unchanged++; continue; }

            var next = NewVersion(def.Versions.Max(v => v.VersionNumber) + 1, canonical!, "Shipped with the product (upgrade)", now, actor, shipped?.DefinitionVersionId);
            next.Definition = def;
            _db.DefinitionVersions.Add(next);
            await _db.SaveChangesAsync(ct);
            await ReplaceReferencesAsync(next, item.Kind, ct);
            MarkValidated(next, actor);
            if (def.IsCustomized)
            {
                next.Status = DefinitionStatus.Retired;                // the customer's published version stays; the new shipped one is kept beside it
                next.RetiredAtUtc = now;
                def.ShippedVersionId = next.DefinitionVersionId;
                Touch(def, def.Revision);
                await _db.SaveChangesAsync(ct);
                if (item.Kind == "form" && def.PublishedVersion is { } current && await MergePlatformFieldsAsync(def, current, canonical!, actor, now, ct)) { merged++; touchedKinds.Add(item.Kind); }
                kept++;
                continue;
            }
            def.TitleJson = item.TitleJson;
            def.DescriptionJson = item.DescriptionJson;
            await PublishVersionAsync(def, next, actor, ct);
            def.ShippedVersionId = next.DefinitionVersionId;
            def.IsCustomized = false;
            await _db.SaveChangesAsync(ct);
            touchedKinds.Add(item.Kind);
            upgraded++;
        }
        foreach (var kind in touchedKinds) _store.Evict(kind);
        return new DefinitionShipResult(created, upgraded, unchanged, kept, customerOwned, problems, merged);
    }

    // ------------------------------------------------------------------ helpers

    private async Task<Definition?> LoadAsync(int id, bool tracking, CancellationToken ct)
    {
        IQueryable<Definition> q = _db.Definitions.Include(d => d.Versions).Include(d => d.PublishedVersion);
        if (!tracking) q = q.AsNoTracking();
        return await q.FirstOrDefaultAsync(d => d.DefinitionId == id, ct);
    }

    private async Task<(Definition? Def, DefinitionVersion? Draft, DefinitionOutcome<T>? Miss)> LoadDraftAsync<T>(int id, CancellationToken ct)
    {
        var def = await LoadAsync(id, tracking: true, ct);
        if (def == null) return (null, null, DefinitionOutcome<T>.NotFound());
        var draft = OpenDraft(def);
        return draft == null ? (def, null, DefinitionOutcome<T>.Fail(DefinitionFailure.NotFound, "no-draft")) : (def, draft, null);
    }

    private static DefinitionVersion? OpenDraft(Definition d) => d.Versions.FirstOrDefault(v => v.Status is DefinitionStatus.Draft or DefinitionStatus.Validated);

    private IReadOnlyList<DefinitionFieldError> Check(string kind, string canonicalPayload) =>
        _kinds.TryGetValue(kind, out var k) ? k.Validate(canonicalPayload, _limits) : new[] { Err("/kind", "unknown-kind", "Def_Err_UnknownKind", $"'{kind}' is not a registered definition kind.") };

    private static void MarkValidated(DefinitionVersion v, string actor)
    {
        v.Status = DefinitionStatus.Validated;
        v.ValidatedAtUtc = DateTime.UtcNow;
        v.ValidatedBy = actor;
    }

    private async Task ReplaceReferencesAsync(DefinitionVersion v, string kind, CancellationToken ct)
    {
        if (v.DefinitionVersionId != 0)
            _db.DefinitionReferences.RemoveRange(await _db.DefinitionReferences.Where(r => r.FromVersionId == v.DefinitionVersionId).ToListAsync(ct));
        foreach (var r in _kinds[kind].References(v.PayloadJson).Distinct())
            v.References.Add(new DefinitionReference { ToKind = r.ToKind, ToKey = r.ToKey, Role = r.Role });
    }

    private async Task PublishVersionAsync(Definition def, DefinitionVersion next, string actor, CancellationToken ct, bool envelopeAlreadyTouched = false)
    {
        var now = DateTime.UtcNow;
        if (def.PublishedVersion is { } old)
        {
            old.Status = DefinitionStatus.Retired;
            old.RetiredAtUtc = now;
        }
        next.Status = DefinitionStatus.Published;
        next.PublishedAtUtc = now;
        next.PublishedBy = actor;
        def.PublishedVersion = next;
        if (!envelopeAlreadyTouched) Touch(def, def.Revision);
        if (def.Origin == DefinitionOrigin.System && def.ShippedVersionId is { } shipped)
        {
            var shippedHash = def.Versions.FirstOrDefault(v => v.DefinitionVersionId == shipped)?.ContentHash;
            def.IsCustomized = shippedHash != null && shippedHash != next.ContentHash;
        }
        var stamp = await _db.DefinitionStamps.FirstOrDefaultAsync(s => s.Kind == def.Kind, ct);
        if (stamp == null) _db.DefinitionStamps.Add(new DefinitionStamp { Kind = def.Kind, Value = 1 });
        else { _db.Entry(stamp).Property(s => s.Value).OriginalValue = stamp.Value; stamp.Value++; }
    }

    private void Touch(Definition d, long expected)
    {
        _db.Entry(d).Property(x => x.Revision).OriginalValue = expected;
        d.Revision = expected + 1;
    }

    private void Touch(DefinitionVersion v, long expected)
    {
        _db.Entry(v).Property(x => x.Revision).OriginalValue = expected;
        v.Revision = expected + 1;
    }

    private async Task<bool> SaveAsync(CancellationToken ct)
    {
        try { await _db.SaveChangesAsync(ct); return true; }
        catch (DbUpdateConcurrencyException) { _db.ChangeTracker.Clear(); return false; }
        catch (DbUpdateException) { _db.ChangeTracker.Clear(); return false; }
    }

    private async Task<DefinitionOutcome<DefinitionDetail>> SaveDetailAsync(Definition def, CancellationToken ct, string? evictKind = null)
    {
        if (!await SaveAsync(ct)) return DefinitionOutcome<DefinitionDetail>.Stale();
        if (evictKind != null) _store.Evict(evictKind);
        return DefinitionOutcome<DefinitionDetail>.Ok(Detail(def));
    }

    private DefinitionVersion NewVersion(int number, string canonicalPayload, string? summary, DateTime now, string actor, int? clonedFrom) => new()
    {
        VersionNumber = number,
        Status = DefinitionStatus.Draft,
        PayloadJson = canonicalPayload,
        ContentHash = Hash(canonicalPayload),
        ChangeSummary = Trim(summary, 500),
        ClonedFromVersionId = clonedFrom,
        CreatedAtUtc = now,
        CreatedBy = actor,
    };

    private static string? Trim(string? s, int max) => s == null ? null : (s.Length <= max ? s : s[..max]);

    private static DefinitionFieldError Err(string pointer, string code, string key, string message) => new(pointer, code, key, message);

    private static bool IsJsonValue(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return false;
        try { var n = JsonNode.Parse(json); return n is JsonObject or JsonValue; } catch (JsonException) { return false; }
    }

    public static string Hash(string canonical) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();

    /// <summary>Compact JSON with sorted property names; limits on size, depth and shape (must be an object).</summary>
    private string? Canonicalize(string? json, out DefinitionFieldError? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(json)) { error = Err("", "required", "Def_Err_Required", "The payload is required."); return null; }
        if (Encoding.UTF8.GetByteCount(json) > _limits.MaxPayloadBytes) { error = Err("", "too-large", "Def_Err_TooLarge", $"The payload is larger than {_limits.MaxPayloadBytes} bytes."); return null; }
        JsonNode? node;
        try { node = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { MaxDepth = _limits.MaxJsonDepth }); }
        catch (JsonException ex) { error = Err("", "malformed", "Def_Err_Malformed", ex.Message); return null; }
        if (node is not JsonObject) { error = Err("", "type", "Def_Err_Type", "The payload must be a JSON object."); return null; }
        return Sort(node)!.ToJsonString(CanonicalOptions);
    }

    private static readonly JsonSerializerOptions CanonicalOptions = new() { WriteIndented = false, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static JsonNode? Sort(JsonNode? n) => n switch
    {
        JsonObject o => new JsonObject(o.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => KeyValuePair.Create(p.Key, Sort(p.Value)))),
        JsonArray a => new JsonArray(a.Select(x => Sort(x)).ToArray()),
        null => null,
        _ => n.DeepClone(),
    };

    private static DefinitionVersionInfo Info(Definition d, DefinitionVersion v) =>
        new(v.VersionNumber, v.Status, v.ContentHash, v.ChangeSummary, v.ClonedFromVersionId is { } c ? d.Versions.FirstOrDefault(x => x.DefinitionVersionId == c)?.VersionNumber : null,
            v.CreatedAtUtc, v.ValidatedAtUtc, v.PublishedAtUtc, v.PublishedBy, v.RetiredAtUtc, v.Revision, v.CreatedBy);

    private static DefinitionVersionPayload Payload(Definition d, DefinitionVersion v) => new(Info(d, v), v.PayloadJson);

    private static DefinitionSummary Summary(Definition d, int? published, bool hasDraft) =>
        new(d.DefinitionId, d.Kind, d.Key, d.AppKey, d.Origin, d.TitleJson, d.DescriptionJson, published, hasDraft, d.IsCustomized, d.Revision, d.CreatedAtUtc);

    private static DefinitionDetail Detail(Definition d) =>
        new(Summary(d, d.PublishedVersion?.VersionNumber ?? d.Versions.FirstOrDefault(v => v.DefinitionVersionId == d.PublishedVersionId)?.VersionNumber, OpenDraft(d) != null),
            d.Versions.OrderBy(v => v.VersionNumber).Select(v => Info(d, v)).ToList());
}
