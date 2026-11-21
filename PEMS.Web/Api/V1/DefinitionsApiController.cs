using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PEMS.Application.Definitions;
using PEMS.Web.Security;

namespace PEMS.Web.Api.V1;

/// <summary>
/// The definition store REST API (SCRUM-434, ADR-0009/0010/0011): create, edit with If-Match, validate, publish, roll back, export and import of UI definitions.
/// Designers and integrations are ordinary clients of it. The lifecycle rules live in <see cref="IDefinitionService"/>; every data change is audited by the
/// DbContext audit trail; publish and rollback need the permission "Approve" of the module "Definitions".
/// </summary>
[Route(Prefix + "/definitions")]
public sealed class DefinitionsApiController : ApiControllerBase
{
    private const string Module = "Definitions";
    private static readonly IReadOnlySet<string> SortFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "kind", "key", "createdDate" };
    private static readonly IReadOnlySet<string> FilterFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "kind", "appKey", "published" };

    private readonly IDefinitionService _service;
    private readonly IDefinitionImpact _impact;
    private readonly PEMS.Persistence.Services.DefinitionPublishGuard _guard;
    private readonly IAuthorizationService _authorization;
    private readonly ICurrentUserService _currentUser;

    public DefinitionsApiController(IDefinitionService service, IDefinitionImpact impact, PEMS.Persistence.Services.DefinitionPublishGuard guard, IAuthorizationService authorization, ICurrentUserService currentUser)
    {
        _service = service;
        _impact = impact;
        _guard = guard;
        _authorization = authorization;
        _currentUser = currentUser;
    }

    private string Actor => _currentUser.UserName ?? User.Identity?.Name ?? "unknown";

    // ------------------------------------------------------------------ reads

    [HttpGet]
    [ApiPermission(Module, "View")]
    [ProducesResponseType<ApiPage<DefinitionResource>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status422UnprocessableEntity, ApiProblem.ContentType)]
    [EndpointSummary("List definitions")]
    [EndpointDescription("Pages through definitions. Supports q, sort, filter[kind], filter[appKey], filter[published], fields, page and pageSize.")]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var errors = new List<ApiFieldError>();
        ApiListQuery.TryParse(Request.Query, out var q, out var listErrors);
        errors.AddRange(listErrors);
        ApiFields.TryParse(Request.Query, DefinitionResource.FieldNames, out var fields, out var fieldErrors);
        errors.AddRange(fieldErrors);
        foreach (var (field, _) in q.Sort.Where(s => !SortFields.Contains(s.Field))) errors.Add(new ApiFieldError("/sort", "unknown-field", "Api_Err_UnknownField", $"Cannot sort by '{field}'."));
        foreach (var f in q.Filters.Keys.Where(k => !FilterFields.Contains(k))) errors.Add(new ApiFieldError($"/filter/{f}", "unknown-field", "Api_Err_UnknownField", $"Cannot filter by '{f}'."));
        bool? published = null;
        if (q.Filters.TryGetValue("published", out var rawPublished))
        {
            if (bool.TryParse(rawPublished, out var b)) published = b;
            else errors.Add(new ApiFieldError("/filter/published", "wrong-type", null, "Use true or false."));
        }
        if (errors.Count > 0) return ApiProblem.Validation(errors);

        q.Filters.TryGetValue("kind", out var kind);
        q.Filters.TryGetValue("appKey", out var appKey);
        var page = await _service.ListAsync(new DefinitionListQuery(kind, appKey, q.Q, published, q.Sort, q.Page, q.PageSize), ct);
        var items = page.Items.Select(s => ApiFields.Project(DefinitionResource.From(s, null, Array.Empty<string>()), fields)).ToList();
        return Ok(new ApiPage<object>(items, q.Page, q.PageSize, page.Total));
    }

    [HttpGet("{id:int}")]
    [ApiPermission(Module, "View")]
    [ProducesResponseType<DefinitionResource>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [EndpointSummary("Get one definition")]
    [EndpointDescription("Returns the envelope with its versions and the actions the user may take; the ETag is the envelope revision.")]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
    {
        var r = await _service.GetAsync(id, ct);
        if (!r.Success) return Problem(r);
        SetRevisionETag(r.Value!.Summary.Revision);
        return Ok(await ResourceAsync(r.Value));
    }

    [HttpGet("{id:int}/versions/{number:int}")]
    [ApiPermission(Module, "View")]
    [ProducesResponseType<DefinitionVersionPayloadResource>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [EndpointSummary("Get one version with its payload")]
    [EndpointDescription("Returns any version (draft, published or retired) with its payload.")]
    public async Task<IActionResult> GetVersion(int id, int number, CancellationToken ct)
    {
        var r = await _service.GetVersionAsync(id, number, ct);
        if (!r.Success) return Problem(r);
        SetRevisionETag(r.Value!.Info.Revision);
        return Ok(DefinitionVersionPayloadResource.From(r.Value));
    }

    [HttpGet("{id:int}/draft")]
    [ApiPermission(Module, "View")]
    [ProducesResponseType<DefinitionVersionPayloadResource>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [EndpointSummary("Get the open draft")]
    [EndpointDescription("Returns the open draft (Draft or Validated) with its payload; the ETag is the draft revision.")]
    public async Task<IActionResult> GetDraft(int id, CancellationToken ct)
    {
        var r = await _service.GetDraftAsync(id, ct);
        if (!r.Success) return Problem(r);
        SetRevisionETag(r.Value!.Info.Revision);
        return Ok(DefinitionVersionPayloadResource.From(r.Value));
    }

    [HttpGet("{id:int}/export")]
    [ApiPermission(Module, "View")]
    [ProducesResponseType<DefinitionPackageResource>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [EndpointSummary("Export a definition")]
    [EndpointDescription("Returns a hash-checked package of the published version, or of the version given in the query.")]
    public async Task<IActionResult> Export(int id, [FromQuery] int? version, CancellationToken ct)
    {
        var r = await _service.ExportAsync(id, version, ct);
        if (!r.Success) return Problem(r);
        return Ok(new DefinitionPackageResource(r.Value!.Format, DefinitionVersionResource.Utc(r.Value.ExportedAtUtc), r.Value.Items.Select(i => new DefinitionPackageItemResource(
            i.Kind, i.Key, i.AppKey, DefinitionResource.Parse(i.TitleJson)!.Value, i.DescriptionJson == null ? null : DefinitionResource.Parse(i.DescriptionJson),
            DefinitionResource.Parse(i.PayloadJson)!.Value, i.ContentHash, i.VersionNumber)).ToList()));
    }

    [HttpGet("{id:int}/upstream")]
    [ApiPermission(Module, "View")]
    [ProducesResponseType<DefinitionUpstreamResource>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [EndpointSummary("Compare a customised shipped definition with the shipped one")]
    [EndpointDescription("For a definition that ships with the product: whether the customer changed it, whether a newer shipped version exists, and the differences between the published payload and the shipped payload.")]
    public async Task<IActionResult> GetUpstream(int id, CancellationToken ct)
    {
        var r = await _service.GetUpstreamAsync(id, ct);
        return r.Success ? Ok(DefinitionUpstreamResource.From(r.Value!)) : Problem(r);
    }

    [HttpGet("{id:int}/used-by")]
    [ApiPermission(Module, "View")]
    [ProducesResponseType<DefinitionUsedByResource>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [EndpointSummary("Which published definitions use this one")]
    [EndpointDescription("The published definitions whose published version points at this definition (a page that renders a component definition, a form that uses a data source, an action a form submits to). Read only.")]
    public async Task<IActionResult> UsedBy(int id, CancellationToken ct)
    {
        var d = await _service.GetAsync(id, ct);
        if (!d.Success) return Problem(d);
        var users = await _impact.UsedByAsync(d.Value!.Summary.Kind, d.Value.Summary.Key, ct);
        return Ok(new DefinitionUsedByResource(users.Select(u => new DefinitionUsageResource(u.Kind, u.Key, u.Role)).ToList()));
    }

    [HttpGet("{id:int}/references")]
    [ApiPermission(Module, "View")]
    [ProducesResponseType<DefinitionReferencesResource>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [EndpointSummary("What this definition points at")]
    [EndpointDescription("The definitions the published version points at, each with whether it exists and is published. A target that does not exist stops a publish of a draft that points at it. Read only.")]
    public async Task<IActionResult> References(int id, CancellationToken ct)
    {
        var d = await _service.GetAsync(id, ct);
        if (!d.Success) return Problem(d);
        var targets = await _impact.ReferencesAsync(id, ct);
        return Ok(new DefinitionReferencesResource(targets.Select(t => new DefinitionTargetResource(t.Kind, t.Key, t.Role, t.Exists)).ToList()));
    }

    [HttpPost("{id:int}/actions/precheck-publish")]
    [ApiPermission(Module, "View")]
    [ProducesResponseType<ImpactReportResource>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [EndpointSummary("What publishing the draft would do")]
    [EndpointDescription("The verdict (allowed, warnings or blocked) and the findings of publishing the open draft: missing or unpublished references and changes that would drop stored data. Changes nothing.")]
    public async Task<IActionResult> PrecheckPublish(int id, CancellationToken ct)
    {
        var d = await _service.GetAsync(id, ct);
        return d.Success ? Ok(ImpactReportResource.From(await _impact.PrecheckPublishAsync(id, ct))) : Problem(d);
    }

    [HttpPost("{id:int}/actions/precheck-rollback")]
    [ApiPermission(Module, "View")]
    [ProducesResponseType<ImpactReportResource>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [EndpointSummary("Whether an older version can be published again")]
    [EndpointDescription("The verdict and the findings of publishing the version toVersion again, against today's definitions: allowed, warnings or blocked with the reasons. Changes nothing.")]
    public async Task<IActionResult> PrecheckRollback(int id, [FromBody] RollbackInput input, CancellationToken ct)
    {
        var d = await _service.GetAsync(id, ct);
        if (!d.Success) return Problem(d);
        if (input.ToVersion is null or < 1) return ApiProblem.Validation(new[] { new ApiFieldError("/toVersion", "required", "Def_Err_Required", "Say which version to check.") });
        return Ok(ImpactReportResource.From(await _impact.PrecheckRollbackAsync(id, input.ToVersion.Value, ct)));
    }

    // ------------------------------------------------------------------ writes

    [HttpPost]
    [ApiPermission(Module, "Create")]
    [ProducesResponseType<DefinitionResource>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status422UnprocessableEntity, ApiProblem.ContentType)]
    [EndpointSummary("Create a definition as a draft")]
    [EndpointDescription("Creates the envelope and a draft version 1. The kind must be registered; the payload may be incomplete until validation.")]
    public async Task<IActionResult> Create([FromBody] DefinitionCreateInput input, CancellationToken ct)
    {
        var missing = Missing(("title", input.Title), ("payload", input.Payload));
        if (missing.Count > 0) return ApiProblem.Validation(missing);
        var r = await _service.CreateAsync(new CreateDefinition(input.Kind ?? "", input.Key ?? "", input.AppKey ?? "core", input.Title.GetRawText(),
            input.Description is { ValueKind: not JsonValueKind.Null } d ? d.GetRawText() : null, input.Payload.GetRawText(), input.ChangeSummary), Actor, ct);
        if (!r.Success) return Problem(r);
        SetRevisionETag(r.Value!.Summary.Revision);
        return Created($"/{Prefix}/definitions/{r.Value.Summary.Id}", await ResourceAsync(r.Value));
    }

    [HttpPut("{id:int}")]
    [ApiPermission(Module, "Edit")]
    [ProducesResponseType<DefinitionResource>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status412PreconditionFailed, ApiProblem.ContentType)]
    [EndpointSummary("Change title and description")]
    [EndpointDescription("Changes the title and description of the envelope. Needs If-Match with the envelope ETag.")]
    public async Task<IActionResult> UpdateEnvelope(int id, [FromBody] DefinitionEnvelopeInput input, CancellationToken ct)
    {
        var (gate, revision) = await EnvelopeGateAsync(id, ct);          // 404 and the precondition come before the body is judged
        if (gate != null) return gate;
        var missing = Missing(("title", input.Title));
        if (missing.Count > 0) return ApiProblem.Validation(missing);
        var r = await _service.UpdateEnvelopeAsync(id, input.Title.GetRawText(), input.Description is { ValueKind: not JsonValueKind.Null } d ? d.GetRawText() : null, revision, Actor, ct);
        return await DetailResultAsync(r);
    }

    [HttpPut("{id:int}/draft")]
    [ApiPermission(Module, "Edit")]
    [ProducesResponseType<DefinitionVersionPayloadResource>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status412PreconditionFailed, ApiProblem.ContentType)]
    [EndpointSummary("Edit the draft")]
    [EndpointDescription("Replaces the payload of the open draft and sets it back to Draft status. Needs If-Match with the draft ETag.")]
    public async Task<IActionResult> SaveDraft(int id, [FromBody] DefinitionDraftInput input, CancellationToken ct)
    {
        var (gate, revision) = await DraftGateAsync(id, ct);
        if (gate != null) return gate;
        var missing = Missing(("payload", input.Payload));
        if (missing.Count > 0) return ApiProblem.Validation(missing);
        return VersionResult(await _service.SaveDraftAsync(id, input.Payload.GetRawText(), input.ChangeSummary, revision, Actor, ct));
    }

    [HttpDelete("{id:int}/draft")]
    [ApiPermission(Module, "Edit")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status412PreconditionFailed, ApiProblem.ContentType)]
    [EndpointSummary("Discard the draft")]
    [EndpointDescription("Deletes the open draft; the only version of a definition cannot be discarded. Needs If-Match.")]
    public async Task<IActionResult> DiscardDraft(int id, CancellationToken ct)
    {
        var (gate, revision) = await DraftGateAsync(id, ct);
        if (gate != null) return gate;
        var r = await _service.DiscardDraftAsync(id, revision, Actor, ct);
        return r.Success ? NoContent() : Problem(r);
    }

    [HttpPost("{id:int}/actions/create-draft")]
    [ApiPermission(Module, "Edit")]
    [ProducesResponseType<DefinitionVersionPayloadResource>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status409Conflict, ApiProblem.ContentType)]
    [EndpointSummary("Open a new draft")]
    [EndpointDescription("Clones the published version (or the given version) into a new draft. Needs If-Match with the envelope ETag.")]
    public async Task<IActionResult> CreateDraft(int id, [FromBody] CreateDraftInput? input, CancellationToken ct)
    {
        var (gate, revision) = await EnvelopeGateAsync(id, ct);
        if (gate != null) return gate;
        var r = await _service.CreateDraftAsync(id, input?.FromVersion, revision, Actor, ct);
        if (!r.Success) return Problem(r);
        SetRevisionETag(r.Value!.Info.Revision);
        return Created($"/{Prefix}/definitions/{id}/draft", DefinitionVersionPayloadResource.From(r.Value));
    }

    [HttpPost("{id:int}/actions/validate")]
    [ApiPermission(Module, "Edit")]
    [ProducesResponseType<DefinitionVersionPayloadResource>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status422UnprocessableEntity, ApiProblem.ContentType)]
    [EndpointSummary("Validate the draft")]
    [EndpointDescription("Checks the draft against its kind (components, options, limits) and records its references; answers 422 with pointers when invalid. Needs If-Match with the draft ETag.")]
    public async Task<IActionResult> Validate(int id, CancellationToken ct)
    {
        var (gate, revision) = await DraftGateAsync(id, ct);
        if (gate != null) return gate;
        return VersionResult(await _service.ValidateAsync(id, revision, Actor, ct));
    }

    [HttpPost("{id:int}/actions/publish")]
    [ApiPermission(Module, "Approve")]
    [ProducesResponseType<DefinitionResource>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status422UnprocessableEntity, ApiProblem.ContentType)]
    [EndpointSummary("Publish the draft")]
    [EndpointDescription("Checks the references first (a missing or unpublished target answers 422 with the pointer), then validates and publishes the draft as the current version; the previous version is retired. A change that only warns (a field changes type, a field becomes required) needs ?acknowledge=true and answers 409 without it. After the publish a smoke check runs; when it fails the previous version is published again automatically (409 auto-rolled-back, change summary 'auto-rollback: reason'). Needs permission Approve and If-Match with the draft ETag.")]
    public async Task<IActionResult> Publish(int id, [FromQuery] bool acknowledge, CancellationToken ct)
    {
        var (gate, revision) = await DraftGateAsync(id, ct);
        if (gate != null) return gate;
        var result = await _guard.PublishAsync(id, revision, Actor, acknowledge, ct);
        switch (result.Stage)
        {
            case PEMS.Persistence.Services.PublishStages.Blocked:
                return ApiProblem.Validation(result.Report.Items.Where(i => i.Severity == "blocked").Select(i => new ApiFieldError(i.Path, "impact-blocked", "Def_Err_Impact", i.Message)).ToList());
            case PEMS.Persistence.Services.PublishStages.NeedsSecondApprover:
                return ApiProblem.Build(409, "second-approver-required", "A second approver is required.", detail: result.Reason);
            case PEMS.Persistence.Services.PublishStages.NeedsAcknowledgement:
                return ApiProblem.Build(409, "breaking-changes-not-acknowledged", "The change has warnings.", detail: result.Reason + " Publish again with acknowledge=true to go on.");
            case PEMS.Persistence.Services.PublishStages.Failed:
                return Problem(result.Outcome!);
            case PEMS.Persistence.Services.PublishStages.RolledBack:
                return ApiProblem.Build(409, "auto-rolled-back", "The published version failed the smoke check and the previous version was published again.", detail: result.Reason);
            case PEMS.Persistence.Services.PublishStages.RollbackStopped:
                return ApiProblem.Build(409, "smoke-check-failed", "The published version failed the smoke check and could not be rolled back automatically.", detail: result.Reason);
            default:
                return await DetailResultAsync(result.Outcome!);
        }
    }

    [HttpPost("{id:int}/actions/rollback")]
    [ApiPermission(Module, "Approve")]
    [ProducesResponseType<DefinitionResource>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status409Conflict, ApiProblem.ContentType)]
    [EndpointSummary("Roll back to an older version")]
    [EndpointDescription("Publishes an older version again as a new version number; history is never rewritten. Needs permission Approve and If-Match with the envelope ETag.")]
    public async Task<IActionResult> Rollback(int id, [FromBody] RollbackInput input, CancellationToken ct)
    {
        var (gate, revision) = await EnvelopeGateAsync(id, ct);
        if (gate != null) return gate;
        if (input.ToVersion is null or < 1) return ApiProblem.Validation(new[] { new ApiFieldError("/toVersion", "required", "Def_Err_Required", "Say which version to publish again.") });
        return await DetailResultAsync(await _service.RollbackAsync(id, input.ToVersion.Value, revision, Actor, ct));
    }

    [HttpPost("{id:int}/actions/reset-to-shipped")]
    [ApiPermission(Module, "Approve")]
    [ProducesResponseType<DefinitionResource>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status409Conflict, ApiProblem.ContentType)]
    [EndpointSummary("Reset a definition to the shipped version")]
    [EndpointDescription("Publishes the shipped payload again as a new version; the customer's versions stay in the history. Needs permission Approve and If-Match with the envelope ETag.")]
    public async Task<IActionResult> ResetToShipped(int id, CancellationToken ct)
    {
        var (gate, revision) = await EnvelopeGateAsync(id, ct);
        if (gate != null) return gate;
        return await DetailResultAsync(await _service.ResetToShippedAsync(id, revision, Actor, ct));
    }

    [HttpPost("import")]
    [ApiPermission(Module, "Create")]
    [ProducesResponseType<DefinitionImportResource>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status422UnprocessableEntity, ApiProblem.ContentType)]
    [EndpointSummary("Import definitions from a package")]
    [EndpointDescription("Checks every item (kind, payload, content hash) and creates drafts; nothing is published.")]
    public async Task<IActionResult> Import([FromBody] DefinitionPackageResource package, CancellationToken ct)
    {
        var r = await _service.ImportAsync(package.ToPackage(), Actor, ct);
        if (!r.Success) return Problem(r);
        return StatusCode(StatusCodes.Status201Created, new DefinitionImportResource(r.Value!.Created, r.Value.Updated, r.Value.DefinitionIds));
    }

    // ------------------------------------------------------------------ helpers

    private static List<ApiFieldError> Missing(params (string Name, JsonElement Value)[] required) =>
        required.Where(r => r.Value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            .Select(r => new ApiFieldError("/" + r.Name, "required", "Def_Err_Required", $"{r.Name} is required.")).ToList();

    private void SetRevisionETag(long revision) => Response.Headers.ETag = ApiETag.Format(BitConverter.GetBytes(revision));

    /// <summary>404, then 428 without If-Match, then 412 when stale; otherwise the envelope revision the write must still match.</summary>
    private async Task<(IActionResult? Gate, long Revision)> EnvelopeGateAsync(int id, CancellationToken ct)
    {
        var r = await _service.GetAsync(id, ct);
        if (!r.Success) return (Problem(r), 0);
        return (Precondition(r.Value!.Summary.Revision), r.Value.Summary.Revision);
    }

    private async Task<(IActionResult? Gate, long Revision)> DraftGateAsync(int id, CancellationToken ct)
    {
        var r = await _service.GetDraftAsync(id, ct);
        if (!r.Success) return (Problem(r), 0);
        return (Precondition(r.Value!.Info.Revision), r.Value.Info.Revision);
    }

    private IActionResult? Precondition(long currentRevision) =>
        ApiETag.Evaluate(Request, BitConverter.GetBytes(currentRevision)) switch
        {
            ApiETag.Check.Missing => ApiProblem.PreconditionRequired(),
            ApiETag.Check.Stale => ApiProblem.PreconditionFailed(),
            _ => null,
        };

    private IActionResult VersionResult(DefinitionOutcome<DefinitionVersionPayload> r)
    {
        if (!r.Success) return Problem(r);
        SetRevisionETag(r.Value!.Info.Revision);
        return Ok(DefinitionVersionPayloadResource.From(r.Value));
    }

    private async Task<IActionResult> DetailResultAsync(DefinitionOutcome<DefinitionDetail> r)
    {
        if (!r.Success) return Problem(r);
        SetRevisionETag(r.Value!.Summary.Revision);
        return Ok(await ResourceAsync(r.Value));
    }

    private async Task<DefinitionResource> ResourceAsync(DefinitionDetail d)
    {
        var allowed = new List<string>();
        var canEdit = (await _authorization.AuthorizeAsync(User, null, ApiPermissionAttribute.Prefix + Module + ".Edit")).Succeeded;
        var canPublish = (await _authorization.AuthorizeAsync(User, null, ApiPermissionAttribute.Prefix + Module + ".Approve")).Succeeded;
        var s = d.Summary;
        if (canEdit)
        {
            allowed.Add("edit");
            if (s.HasDraft) { allowed.Add("save-draft"); allowed.Add("validate"); if (d.Versions.Count > 1) allowed.Add("discard-draft"); }
            else if (s.PublishedVersion != null) allowed.Add("create-draft");
        }
        if (canPublish)
        {
            if (s.HasDraft) allowed.Add("publish");
            if (d.Versions.Count(v => v.Status is PEMS.Domain.Entities.Definitions.DefinitionStatus.Published or PEMS.Domain.Entities.Definitions.DefinitionStatus.Retired) > 1) allowed.Add("rollback");
        }
        return DefinitionResource.From(s, d.Versions, allowed);
    }

    private IActionResult Problem<T>(DefinitionOutcome<T> r) => r.Failure switch
    {
        DefinitionFailure.NotFound => ApiProblem.Build(404, r.Code ?? "not-found", "The resource was not found."),
        DefinitionFailure.Concurrency => ApiProblem.PreconditionFailed(),
        DefinitionFailure.Conflict => ApiProblem.Conflict(r.Code ?? "conflict"),
        DefinitionFailure.Invalid => ApiProblem.Validation((r.Errors ?? Array.Empty<DefinitionFieldError>())
            .Select(e => new ApiFieldError(e.Pointer, e.Code, e.MessageKey, e.Message)).ToList()),
        _ => ApiProblem.Server(),
    };
}
