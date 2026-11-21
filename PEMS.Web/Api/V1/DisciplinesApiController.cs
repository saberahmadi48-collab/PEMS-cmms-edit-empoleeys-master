using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.Net.Http.Headers;
using PEMS.Application.Interfaces;
using PEMS.Web.Resources;

namespace PEMS.Web.Api.V1;

/// <summary>
/// Pilot resource of the REST API (SCRUM-435, ADR-0009): discipline master data. The same service as the Discipline pages does the work;
/// this controller only maps HTTP to it (no business rules here).
/// </summary>
[Route(Prefix + "/disciplines")]
public sealed class DisciplinesApiController : ApiControllerBase
{
    private const string Module = "DisciplineMaster";
    private static readonly IReadOnlySet<string> SortFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "code", "name", "createdDate" };
    private static readonly IReadOnlySet<string> FilterFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "isActive", "transmittalCode" };

    private readonly IDisciplineMasterService _service;
    private readonly IAuthorizationService _authorization;
    private readonly ApiIdempotencyStore _idempotency;
    private readonly IStringLocalizer<SharedResource> _l;

    public DisciplinesApiController(IDisciplineMasterService service, IAuthorizationService authorization, ApiIdempotencyStore idempotency,
        IStringLocalizer<SharedResource> localizer)
    {
        _service = service;
        _authorization = authorization;
        _idempotency = idempotency;
        _l = localizer;
    }

    [HttpGet]
    [ApiPermission(Module, "View")]
    [ProducesResponseType<ApiPage<DisciplineResource>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status422UnprocessableEntity, ApiProblem.ContentType)]
    [EndpointSummary("List disciplines")]
    [EndpointDescription("Pages through the discipline master data. Supports q, sort, filter[isActive], filter[transmittalCode], fields, page and pageSize (offset paging, at most 10,000 rows).")]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var errors = new List<ApiFieldError>();
        ApiListQuery.TryParse(Request.Query, out var q, out var listErrors);
        errors.AddRange(listErrors);
        ApiFields.TryParse(Request.Query, DisciplineResource.FieldNames, out var fields, out var fieldErrors);
        errors.AddRange(fieldErrors);
        foreach (var (field, _) in q.Sort.Where(s => !SortFields.Contains(s.Field)))
            errors.Add(new ApiFieldError("/sort", "unknown-field", "Api_Err_UnknownField", $"Cannot sort by '{field}'."));
        foreach (var f in q.Filters.Keys.Where(k => !FilterFields.Contains(k)))
            errors.Add(new ApiFieldError($"/filter/{f}", "unknown-field", "Api_Err_UnknownField", $"Cannot filter by '{f}'."));
        bool? isActive = null;
        if (q.Filters.TryGetValue("isActive", out var rawActive))
        {
            if (bool.TryParse(rawActive, out var b)) isActive = b;
            else errors.Add(new ApiFieldError("/filter/isActive", "wrong-type", null, "Use true or false."));
        }
        if (errors.Count > 0) return ApiProblem.Validation(errors);

        q.Filters.TryGetValue("transmittalCode", out var transmittal);
        var page = await _service.QueryAsync(new DisciplineQuery(q.Q, isActive, transmittal, q.Sort, q.Page, q.PageSize), ct);
        var allowed = await AllowedActionsAsync();
        var items = page.Items.Select(d => ApiFields.Project(DisciplineResource.From(d, allowed), fields)).ToList();
        return Ok(new ApiPage<object>(items, q.Page, q.PageSize, page.Total));
    }

    [HttpGet("{id:int}")]
    [ApiPermission(Module, "View")]
    [ProducesResponseType<DisciplineResource>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [EndpointSummary("Get one discipline")]
    [EndpointDescription("Returns one discipline with its ETag and the actions the signed-in user may take.")]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
    {
        var d = await _service.GetAsync(id, ct);
        if (d == null) return ApiProblem.NotFound();
        var etag = ApiETag.Format(d.RowVersion);
        if (Request.Headers.IfNoneMatch.ToString() == etag) return StatusCode(StatusCodes.Status304NotModified);
        Response.Headers.ETag = etag;
        return Ok(DisciplineResource.From(d, await AllowedActionsAsync()));
    }

    [HttpPost]
    [ApiPermission(Module, "Create")]
    [ProducesResponseType<DisciplineResource>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status422UnprocessableEntity, ApiProblem.ContentType)]
    [EndpointSummary("Create a discipline")]
    [EndpointDescription("Creates a discipline and answers 201 with Location and ETag. A repeated Idempotency-Key with the same body returns the first response.")]
    public async Task<IActionResult> Create([FromBody] DisciplineInput input, CancellationToken ct)
    {
        var user = User.Identity?.Name ?? "";
        var key = Request.Headers["Idempotency-Key"].ToString();
        var fingerprint = ApiIdempotencyStore.Fingerprint(JsonSerializer.Serialize(input));
        if (key.Length > 0 && _idempotency.TryGet(user, key, out var stored) && stored != null)
        {
            if (stored.Fingerprint != fingerprint)
                return ApiProblem.Validation(new[] { new ApiFieldError("", "idempotency-key-reuse", "Api_Err_IdempotencyReuse", "This Idempotency-Key was already used with a different body.") });
            Response.Headers.ETag = stored.ETag;
            Response.Headers.Location = stored.Location;
            return StatusCode(stored.Status, stored.Body);
        }

        var result = await _service.CreateAsync(input.ToWrite(), ct);
        if (!result.Success) return Problem(result.ErrorKey);

        var resource = DisciplineResource.From(result.Detail!, await AllowedActionsAsync());
        var location = $"/{Prefix}/disciplines/{resource.Id}";
        var etag = ApiETag.Format(result.Detail!.RowVersion);
        Response.Headers.ETag = etag;
        if (key.Length > 0) _idempotency.Save(user, key, new IdempotentResponse(fingerprint, StatusCodes.Status201Created, location, etag, resource));
        return Created(location, resource);
    }

    [HttpPut("{id:int}")]
    [ApiPermission(Module, "Edit")]
    [ProducesResponseType<DisciplineResource>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status412PreconditionFailed, ApiProblem.ContentType)]
    [EndpointSummary("Replace a discipline")]
    [EndpointDescription("Replaces all writable fields. Needs If-Match with the ETag of the last read.")]
    public async Task<IActionResult> Replace(int id, [FromBody] DisciplineInput input, CancellationToken ct)
    {
        var (gate, expected) = await CheckPreconditionAsync(id, ct);
        if (gate != null) return gate;
        return await WriteAsync(await _service.ReplaceAsync(id, input.ToWrite(), expected, ct));
    }

    [HttpPatch("{id:int}")]
    [Consumes("application/merge-patch+json")]
    [ApiPermission(Module, "Edit")]
    [ProducesResponseType<DisciplineResource>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status412PreconditionFailed, ApiProblem.ContentType)]
    [EndpointSummary("Change some fields of a discipline")]
    [EndpointDescription("JSON Merge Patch (application/merge-patch+json): absent fields stay, null clears a nullable field. Needs If-Match.")]
    public async Task<IActionResult> Patch(int id, [FromBody] JsonElement patch, CancellationToken ct)
    {
        var (gate, expected) = await CheckPreconditionAsync(id, ct);
        if (gate != null) return gate;
        var current = await _service.GetAsync(id, ct);
        if (!DisciplineMergePatch.TryApply(patch, current!, out var write, out var errors)) return ApiProblem.Validation(errors);
        return await WriteAsync(await _service.ReplaceAsync(id, write!, expected, ct));
    }

    [HttpDelete("{id:int}")]
    [ApiPermission(Module, "Edit")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status409Conflict, ApiProblem.ContentType)]
    [EndpointSummary("Delete a discipline")]
    [EndpointDescription("Deletes an unused discipline; one that other records use gives 409 (deactivate it with PATCH isActive=false). Needs If-Match.")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var (gate, expected) = await CheckPreconditionAsync(id, ct);
        if (gate != null) return gate;
        var result = await _service.DeleteAsync(id, expected, ct);
        return result.Success ? NoContent() : Problem(result.ErrorKey);
    }

    /// <summary>404 for a missing record, 428 without If-Match, 412 when it is stale; otherwise the row version the write must still match.</summary>
    private async Task<(IActionResult? Gate, byte[]? Expected)> CheckPreconditionAsync(int id, CancellationToken ct)
    {
        var current = await _service.GetAsync(id, ct);
        if (current == null) return (ApiProblem.NotFound(), null);
        return ApiETag.Evaluate(Request, current.RowVersion) switch
        {
            ApiETag.Check.Missing => (ApiProblem.PreconditionRequired(), null),
            ApiETag.Check.Stale => (ApiProblem.PreconditionFailed(), null),
            _ => (null, current.RowVersion),
        };
    }

    private async Task<IActionResult> WriteAsync(DisciplineOpResult result)
    {
        if (!result.Success) return Problem(result.ErrorKey);
        Response.Headers.ETag = ApiETag.Format(result.Detail!.RowVersion);
        return Ok(DisciplineResource.From(result.Detail, await AllowedActionsAsync()));
    }

    private async Task<IReadOnlyList<string>> AllowedActionsAsync()
    {
        var allowed = new List<string>();
        if ((await _authorization.AuthorizeAsync(User, null, ApiPermissionAttribute.Prefix + Module + ".Edit")).Succeeded)
            allowed.AddRange(new[] { "edit", "deactivate", "delete" });
        return allowed;
    }

    /// <summary>Maps the service's error key to the field it is about; unknown keys become a general 422.</summary>
    private IActionResult Problem(string? key)
    {
        switch (key)
        {
            case "Disc_Err_NotFound": return ApiProblem.NotFound();
            case DisciplineErrorKeys.Concurrency: return ApiProblem.PreconditionFailed();
            case DisciplineErrorKeys.InUse: return ApiProblem.Conflict("in-use", _l[key].Value);
        }
        var pointer = key switch
        {
            "Disc_Err_DuplicateCode" => "/code",
            "Disc_Err_DuplicateName" => "/name",
            "Disc_Err_AliasConflict" => "/importAliases",
            "Disc_Err_TransmittalCode" => "/transmittalCode",
            _ => "",
        };
        var code = (key ?? "Disc_Err_Generic").Replace("Disc_Err_", "");
        var kebab = string.Concat(code.Select((ch, i) => i > 0 && char.IsUpper(ch) ? "-" + char.ToLowerInvariant(ch) : char.ToLowerInvariant(ch).ToString()));
        return ApiProblem.Validation(new[] { new ApiFieldError(pointer, kebab, key, _l[key ?? "Disc_Err_Generic"].Value) });
    }
}
