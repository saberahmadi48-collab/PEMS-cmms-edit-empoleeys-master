using PEMS.Domain.Entities.Definitions;

namespace PEMS.Application.Definitions;

/// <summary>Starting values of the limits in the design document, section 6; settings <c>Definitions:*</c> in env/*.env.</summary>
public sealed class DefinitionLimits
{
    public int MaxPayloadBytes { get; init; } = 1_048_576;
    public int MaxInstancesPerPage { get; init; } = 500;
    public int MaxJsonDepth { get; init; } = 32;
}

public sealed record DefinitionFieldError(string Pointer, string Code, string MessageKey, string Message);

/// <summary>What a registered kind knows about its payload. A kind that is not registered cannot be stored.</summary>
public interface IDefinitionKind
{
    string Key { get; }
    /// <summary>Checks the payload; an empty list means valid. Pointers are JSON Pointers into the payload.</summary>
    IReadOnlyList<DefinitionFieldError> Validate(string payloadJson, DefinitionLimits limits);
    /// <summary>The definitions and components the payload points to (written to DefinitionReferences).</summary>
    IReadOnlyList<DefinitionRef> References(string payloadJson);
}

public sealed record DefinitionRef(string ToKind, string ToKey, string Role);

public enum DefinitionFailure { None, NotFound, Concurrency, Conflict, Invalid }

public sealed record DefinitionOutcome<T>(T? Value, DefinitionFailure Failure = DefinitionFailure.None, string? Code = null,
    IReadOnlyList<DefinitionFieldError>? Errors = null)
{
    public bool Success => Failure == DefinitionFailure.None;
    public static DefinitionOutcome<T> Ok(T value) => new(value);
    public static DefinitionOutcome<T> Fail(DefinitionFailure f, string code, IReadOnlyList<DefinitionFieldError>? errors = null) => new(default, f, code, errors);
    public static DefinitionOutcome<T> NotFound() => Fail(DefinitionFailure.NotFound, "not-found");
    public static DefinitionOutcome<T> Stale() => Fail(DefinitionFailure.Concurrency, "precondition-failed");
    public static DefinitionOutcome<T> Invalid(IReadOnlyList<DefinitionFieldError> errors) => Fail(DefinitionFailure.Invalid, "validation", errors);
}

public sealed record DefinitionVersionInfo(int Number, DefinitionStatus Status, string ContentHash, string? ChangeSummary, int? ClonedFromNumber,
    DateTime CreatedAtUtc, DateTime? ValidatedAtUtc, DateTime? PublishedAtUtc, string? PublishedBy, DateTime? RetiredAtUtc, long Revision, string? CreatedBy = null);

public sealed record DefinitionVersionPayload(DefinitionVersionInfo Info, string PayloadJson);

public sealed record DefinitionSummary(int Id, string Kind, string Key, string AppKey, DefinitionOrigin Origin, string TitleJson, string? DescriptionJson,
    int? PublishedVersion, bool HasDraft, bool IsCustomized, long Revision, DateTime CreatedAtUtc);

public sealed record DefinitionDetail(DefinitionSummary Summary, IReadOnlyList<DefinitionVersionInfo> Versions);

public sealed record DefinitionListQuery(string? Kind, string? AppKey, string? Q, bool? Published, IReadOnlyList<(string Field, bool Descending)> Sort, int Page, int PageSize);

public sealed record DefinitionPageResult(IReadOnlyList<DefinitionSummary> Items, int Total);

public sealed record CreateDefinition(string Kind, string Key, string AppKey, string TitleJson, string? DescriptionJson, string PayloadJson, string? ChangeSummary);

/// <summary>Export/import package: hash-checked now; the signature of the signed package comes with #23.</summary>
public sealed record DefinitionPackageItem(string Kind, string Key, string AppKey, string TitleJson, string? DescriptionJson, string PayloadJson, string ContentHash, int VersionNumber);

public sealed record DefinitionPackage(string Format, DateTime ExportedAtUtc, IReadOnlyList<DefinitionPackageItem> Items)
{
    public const string CurrentFormat = "pems.definitions/1";
}

public sealed record DefinitionImportResult(int Created, int Updated, IReadOnlyList<int> DefinitionIds);

/// <summary>One difference between the customer's published payload and the shipped one (SCRUM-448): a JSON Pointer, what happened to it, and both values (null where absent).</summary>
public sealed record DefinitionDiffEntry(string Pointer, string Change, string? Published, string? Shipped);

/// <summary>
/// What a product update did to a definition the customer changed (SCRUM-448): <see cref="UpstreamChanged"/> is true when the shipped version is newer than the one the customer's published version
/// was made from; <see cref="Diff"/> lists the differences between the published payload and the current shipped payload (capped; <see cref="Truncated"/> says so).
/// </summary>
public sealed record DefinitionUpstream(bool Customized, bool UpstreamChanged, int? ShippedVersion, int? BasedOnShippedVersion, int? PublishedVersion, IReadOnlyList<DefinitionDiffEntry> Diff, bool Truncated);

/// <summary>Write side (designers and integrations through the REST API). Every method that changes data takes the signed-in user's name.</summary>
public interface IDefinitionService
{
    Task<DefinitionPageResult> ListAsync(DefinitionListQuery query, CancellationToken ct = default);
    Task<DefinitionOutcome<DefinitionDetail>> GetAsync(int id, CancellationToken ct = default);
    Task<DefinitionOutcome<DefinitionVersionPayload>> GetVersionAsync(int id, int versionNumber, CancellationToken ct = default);
    Task<DefinitionOutcome<DefinitionVersionPayload>> GetDraftAsync(int id, CancellationToken ct = default);

    Task<DefinitionOutcome<DefinitionDetail>> CreateAsync(CreateDefinition create, string actor, CancellationToken ct = default);
    Task<DefinitionOutcome<DefinitionDetail>> UpdateEnvelopeAsync(int id, string titleJson, string? descriptionJson, long expectedRevision, string actor, CancellationToken ct = default);
    /// <summary>Opens a draft cloned from the published version (or from <paramref name="fromVersion"/>); one open draft per definition.</summary>
    Task<DefinitionOutcome<DefinitionVersionPayload>> CreateDraftAsync(int id, int? fromVersion, long expectedEnvelopeRevision, string actor, CancellationToken ct = default);
    Task<DefinitionOutcome<DefinitionVersionPayload>> SaveDraftAsync(int id, string payloadJson, string? changeSummary, long expectedDraftRevision, string actor, CancellationToken ct = default);
    Task<DefinitionOutcome<DefinitionVersionPayload>> ValidateAsync(int id, long expectedDraftRevision, string actor, CancellationToken ct = default);
    Task<DefinitionOutcome<DefinitionDetail>> PublishAsync(int id, long expectedDraftRevision, string actor, CancellationToken ct = default);
    /// <param name="changeSummary">The change summary of the new version; default "Rollback to version N" (the smoke check writes "auto-rollback: reason", SCRUM-788).</param>
    Task<DefinitionOutcome<DefinitionDetail>> RollbackAsync(int id, int toVersion, long expectedEnvelopeRevision, string actor, CancellationToken ct = default, string? changeSummary = null);
    Task<DefinitionOutcome<DefinitionDetail>> DiscardDraftAsync(int id, long expectedDraftRevision, string actor, CancellationToken ct = default);

    /// <summary>SCRUM-448: the difference between a customised System definition and what the product ships now; NotFound for a definition that is not shipped.</summary>
    Task<DefinitionOutcome<DefinitionUpstream>> GetUpstreamAsync(int id, CancellationToken ct = default);
    /// <summary>SCRUM-448: republishes the shipped payload as a new version (the customer's version stays in the history and can be rolled back to). Conflict "not-shipped" for a definition without a shipped version.</summary>
    Task<DefinitionOutcome<DefinitionDetail>> ResetToShippedAsync(int id, long expectedEnvelopeRevision, string actor, CancellationToken ct = default);

    /// <summary>Exports the published version, or <paramref name="version"/> when given.</summary>
    Task<DefinitionOutcome<DefinitionPackage>> ExportAsync(int id, int? version, CancellationToken ct = default);
    Task<DefinitionOutcome<DefinitionImportResult>> ImportAsync(DefinitionPackage package, string actor, CancellationToken ct = default);
}

/// <param name="TitleJson">The title as TranslatableText JSON ({"fa": "...", "en": "..."}); null when the owner does not carry one.</param>
public sealed record PublishedDefinition(string Kind, string Key, string AppKey, int VersionNumber, string PayloadJson, string ContentHash, string? TitleJson = null);

/// <summary>Read side for the runtime (page host, renderers): the published version only, cached and refreshed by the per-kind stamp.</summary>
public interface IDefinitionStore
{
    Task<PublishedDefinition?> GetPublishedAsync(string kind, string key, string appKey = "core", CancellationToken ct = default);

    /// <summary>Every published definition of a kind (the route table and the entity registry read whole kinds); stores that cannot list answer nothing.</summary>
    Task<IReadOnlyList<PublishedDefinition>> ListPublishedAsync(string kind, string appKey = "core", CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<PublishedDefinition>>([]);
}

/// <summary>Where the definition files live and how they are read (SCRUM-572; settings Definitions:Files:* in env/*.env).</summary>
public sealed class DefinitionFileOptions
{
    /// <summary>The folder with {kind}/{appKey}/{key}.json; empty means "definitions" next to the application.</summary>
    public string? Folder { get; init; }
    /// <summary>True: an invalid file stops the start-up (the default); false: it is skipped and listed in the problems.</summary>
    public bool FailOnInvalid { get; init; } = true;
    /// <summary>Watch the folder and reload on change (hot reload).</summary>
    public bool Watch { get; init; } = true;
}

public sealed record DefinitionFileProblem(string File, string Pointer, string Message);

/// <summary>A definition that ships with the product (SCRUM-579): read from the system package, written into the definition tables as a System definition.</summary>
public sealed record ShippedDefinition(string Kind, string Key, string AppKey, string TitleJson, string? DescriptionJson, string PayloadJson);

/// <summary>What shipping did: new definitions, upgraded ones, ones already current, ones a customer changed (kept as they are), keys a customer created themselves (never touched), and what could not be shipped.</summary>
public sealed record DefinitionShipResult(int Created, int Upgraded, int Unchanged, int KeptCustomized, int CustomerOwned, IReadOnlyList<string> Problems, int PlatformFieldsMerged = 0)
{
    public bool Ok => Problems.Count == 0;
}

/// <summary>Writes the shipped definitions into the tables (ADR-0010 system origin, ADR-0014 cutover); idempotent, so a fresh install and every upgrade run the same code.</summary>
public interface IDefinitionShipper
{
    Task<DefinitionShipResult> ShipAsync(IReadOnlyList<ShippedDefinition> items, string actor, CancellationToken ct = default);
}

/// <summary>One reason a product update cannot be applied to a customised form (SCRUM-515, 797): the form, the field, the code and the readable reason.</summary>
public sealed record OwnershipProblem(string Kind, string AppKey, string Key, string Field, string Code, string Message)
{
    /// <summary>The line the shipping result and the logs use.</summary>
    public string Line => $"{Kind}/{AppKey}/{Key}: field '{Field}' ({Code}) {Message}";
}

/// <summary>The ownership preflight of an update without writing anything: which customised forms the shipped package would collide with (SCRUM-797).</summary>
public interface IDefinitionPreflight
{
    Task<IReadOnlyList<OwnershipProblem>> CheckOwnershipAsync(IReadOnlyList<ShippedDefinition> items, CancellationToken ct = default);
}

/// <summary>The definitions that ship as JSON files in the data layer (ADR-0014): read once, validated, reloaded when a file changes.</summary>
public interface IDefinitionFiles
{
    IReadOnlyList<PublishedDefinition> All { get; }
    /// <summary>What is wrong with the files of the last load (empty when all are valid).</summary>
    IReadOnlyList<DefinitionFileProblem> Problems { get; }
    /// <summary>A number that grows with every reload (caches compare it).</summary>
    long Generation { get; }
    event Action? Reloaded;
    /// <summary>Reads the folder again now (the watcher calls it after a short pause).</summary>
    void Reload();
}
