using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using PEMS.Application.Definitions;
using PEMS.UI.Abstractions;
using PEMS.UI.Rendering;
using PEMS.Web.Definitions;
using PEMS.Web.Security;

namespace PEMS.Web.Controllers;

/// <summary>
/// The one generic host of the form engine (SCRUM-578, ADR-0014): <c>/f/{key}</c> runs the pipeline for a form definition (resolve, authorize, hydrate), evaluates its conditions (ADR-0015) and renders it
/// with the form component; the post validates the values from the definition (both languages) and hands the valid ones to the action definition named by the form (SCRUM-437). Nothing about a form lives
/// in code: a changed definition is a changed form.
/// </summary>
[Route("f/{key}")]
public sealed class FormHostController(DefinitionPipeline pipeline, ActionExecutor actions, ICurrentUserService currentUser, IAntiforgery antiforgery, IPemsComponentRenderer renderer, ILogger<FormHostController> log, EntityRecordLoader? records = null, PEMS.Application.Definitions.PipelineBudgets? budgets = null) : Controller
{
    private static readonly Regex KeyShape = new("^[a-z0-9][a-z0-9._-]{0,80}$", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions Strict = new(PemsJson.Options);

    /// <summary>An edit form is a form whose action names an entity: <c>/f/{key}/{id}</c> loads the row (with the rights of the user) into the fields the action maps, and the post carries the id to the action.</summary>
    [HttpGet("{id?}")]
    public async Task<IActionResult> Index(string key, string? id, CancellationToken ct)
    {
        var loaded = await LoadAsync(key, id, ct);
        if (loaded.Early is not null) return loaded.Early;
        var values = loaded.Form!.Fields.Where(f => !string.IsNullOrEmpty(f.Name)).ToDictionary(f => f.Name, f => (string?)null, StringComparer.Ordinal);
        if (id is not null)
        {
            var current = loaded.Form.ActionKey is null || records is null ? null : await records.LoadAsync(await actions.ParametersAsync(loaded.Form.ActionKey, "core", ct), id, loaded.Context.Roles, ct);
            if (current is null) return NotFound();
            foreach (var (name, v) in current) if (values.ContainsKey(name)) values[name] = v;
        }
        foreach (var (name, v) in Request.Query) if (values.ContainsKey(name)) values[name] = v.ToString();   // a link may pre-fill a field
        return await Show(key, loaded, values, new Dictionary<string, TranslatableText>(), []);
    }

    [HttpPost("{id?}")]
    [PEMS.Web.Security.Uploads.DefinitionUploadPolicy]   // SCRUM-596: a file field of a form is checked by the file.upload action with the profile the definition names
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(string key, string? id, CancellationToken ct)
    {
        var loaded = await LoadAsync(key, id, ct);
        if (loaded.Early is not null) return loaded.Early;
        var form = loaded.Form!;
        var posted = form.Fields.ToDictionary(f => f.Name, f => (string?)(Request.Form.TryGetValue(f.Name, out var v) ? v.ToString() : null), StringComparer.Ordinal);   // only the declared names are read
        foreach (var f in form.Fields.Where(f => f.Mask is not null)) posted[f.Name] = FieldMask.Canonical(posted[f.Name]);   // a masked value is stored without separators (SCRUM-386)
        // the choices a Select accepts are the ones the data source gave this user, so the post is checked against the form as it was shown
        foreach (var fileField in form.Fields.Where(f => f.Kind == FieldKind.File))
            if (Request.HasFormContentType && Request.Form.Files.GetFile(fileField.Name) is { } uploaded) posted[fileField.Name] = uploaded.FileName;   // SCRUM-596: a chosen file is the value of its field (required, rules)
        var shown = FormEngine.Effective(form, FormEngine.Evaluate(form, posted, loaded.Context), loaded.Options);
        var validation = FormEngine.Validate(shown, posted, loaded.Context);
        if (!validation.IsValid) return await Show(key, loaded, posted, validation.Errors, validation.FormErrors);

        if (string.IsNullOrEmpty(form.ActionKey)) return await Show(key, loaded, posted, validation.Errors, [TranslatableText.Of(("en", "This form has no action."), ("fa", "این فرم کنشی ندارد."))]);
        var result = await actions.RunAsync(form.ActionKey, new ActionInvocation(id is null ? validation.Values : new Dictionary<string, string?>(validation.Values) { ["id"] = id }, CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, loaded.Context.Roles, loaded.Context.UserName, null, HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString(), HostFiles.From(Request)), "core", ct);
        switch (result.Status)
        {
            case ActionStatus.Forbidden: return Forbid();
            case ActionStatus.NotFound: log.LogWarning("Form definition {Key}: the action \"{Action}\" does not exist.", key, form.ActionKey); return NotFound();
            case ActionStatus.StepUpRequired:
            case ActionStatus.Refused:
                return await Show(key, loaded, posted, result.Outcome!.FieldErrors ?? new Dictionary<string, TranslatableText>(), result.Outcome.FormError is { } fe ? [fe] : []);
        }
        if (result.Outcome?.Secret is { } secret)
        {
            // SCRUM-816: a value shown once is rendered in this answer: no redirect, no TempData, nothing cached
            Response.Headers.CacheControl = "no-store, no-cache";
            Response.Headers.Pragma = "no-cache";
            return View("Secret", secret);
        }
        if (result.Message is { IsEmpty: false } message) TempData["Message"] = message.Resolve(CultureInfo.CurrentUICulture);
        var target = result.Redirect;
        return target is not null && Url.IsLocalUrl(target) ? LocalRedirect(target) : RedirectToAction(nameof(Index), new { key, id });
    }

    private sealed record Loaded(IActionResult? Early, FormDefinition? Form, FormRequestContext Context, IReadOnlyDictionary<string, IReadOnlyList<FieldOption>> Options, string? Title);

    private async Task<Loaded> LoadAsync(string key, string? id, CancellationToken ct)
    {
        var context = new FormRequestContext(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, key,
            User.Claims.Where(c => c.Type == System.Security.Claims.ClaimTypes.Role).Select(c => c.Value).ToList(), currentUser.UserName);
        if (!KeyShape.IsMatch(key)) return new Loaded(NotFound(), null, context, new Dictionary<string, IReadOnlyList<FieldOption>>(), null);

        var request = new PipelineRequest("form", key, "core", context.Culture, context.Roles, context.UserName);
        var result = await pipeline.RunAsync(request, ct);
        if (result.Status == PipelineStatus.NotFound) return new Loaded(NotFound(), null, context, new Dictionary<string, IReadOnlyList<FieldOption>>(), null);
        if (result.Status == PipelineStatus.Forbidden) return new Loaded(Forbid(), null, context, new Dictionary<string, IReadOnlyList<FieldOption>>(), null);

        HostDiagnostics.Report(log, HttpContext.TraceIdentifier, "form", key, result.Timings, result.Counts, budgets);   // SCRUM-577
        var form = result.Resolved!.Payload.Deserialize<FormDefinition>(Strict)!;
        var options = result.Resolved.Hydrated.TryGetValue(FormOptionsHydrator.OptionsKey, out var o) && o is IReadOnlyDictionary<string, IReadOnlyList<FieldOption>> map ? map : new Dictionary<string, IReadOnlyList<FieldOption>>();
        if (result.Resolved.Hydrated.TryGetValue(FormOptionsHydrator.ProblemsKey, out var p) && p is IEnumerable<string> problems)
            foreach (var problem in problems) log.LogWarning("Form definition {Key}: {Problem}", key, problem);
        return new Loaded(null, form with { Id = key, Action = Url.Action(nameof(Submit), new { key, id })! }, context, options, PageRenderer.TitleOf(result.Resolved.Source.TitleJson, CultureInfo.CurrentUICulture));
    }

    private async Task<IActionResult> Show(string key, Loaded loaded, IReadOnlyDictionary<string, string?> values, IReadOnlyDictionary<string, TranslatableText> errors, IReadOnlyList<TranslatableText> formErrors)
    {
        var conditions = FormEngine.Evaluate(loaded.Form!, values, loaded.Context);
        var shown = FormEngine.Effective(loaded.Form!, conditions, loaded.Options);
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        var hidden = conditions.Where(c => !c.Value.Visible).Select(c => c.Key).ToList();
        var rules = JsonSerializer.Serialize(new
        {
            user = new { name = loaded.Context.UserName, roles = loaded.Context.Roles },
            request = new { culture = loaded.Context.Culture, key },
            fields = shown.Fields.Select(f => new { name = f.Name, kind = f.Kind.ToString().ToLowerInvariant(), visible = f.Visible, requiredWhen = f.RequiredWhen, readOnlyWhen = f.ReadOnlyWhen, required = loaded.Form!.Fields.First(x => x.Name == f.Name).Required, readOnly = loaded.Form.Fields.First(x => x.Name == f.Name).ReadOnly })
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
        var state = new FormState { Values = values, Errors = errors, Hidden = hidden, FormErrors = formErrors };
        var html = await renderer.RenderAsync("form", new Dictionary<string, object?> { ["Definition"] = shown, ["State"] = state, ["RequestToken"] = tokens.RequestToken, ["RequestTokenName"] = tokens.FormFieldName ?? "__RequestVerificationToken", ["RulesJson"] = rules }, CultureInfo.CurrentUICulture);
        ViewData["Title"] = loaded.Title ?? key;
        return View("~/Views/PageHost/Index.cshtml", new RenderedPage(html, loaded.Title, []));
    }
}

/// <summary>A stored action on its own (SCRUM-437): <c>POST /a/{key}</c> with the values of the action as form fields runs the action definition, for a button on a page that is not a form. Same rights, same handler.</summary>
[Route("a/{key}")]
public sealed class ActionHostController(ActionExecutor actions, ICurrentUserService currentUser) : Controller
{
    private static readonly Regex KeyShape = new("^[a-z0-9][a-z0-9._-]{0,80}$", RegexOptions.Compiled);

    [HttpPost("")]
    [PEMS.Web.Security.Uploads.DefinitionUploadPolicy]   // SCRUM-596: files of an action are checked by file.upload with the profile the definition names
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Run(string key, string? returnUrl, CancellationToken ct)
    {
        if (!KeyShape.IsMatch(key)) return NotFound();
        // the values of the action: form fields, or a JSON object (a kanban card move or a calendar drag posts { "id": ..., "to": ... }); only scalars, every value as text (SCRUM-620/772)
        var values = Request.HasJsonContentType()
            ? ActionValues.FromJson(await ReadJsonAsync(ct))
            : Request.Form.Where(p => p.Key != "__RequestVerificationToken" && p.Key != "returnUrl").ToDictionary(p => p.Key, p => (string?)p.Value.ToString(), StringComparer.Ordinal);
        var roles = User.Claims.Where(c => c.Type == System.Security.Claims.ClaimTypes.Role).Select(c => c.Value).ToList();
        var result = await actions.RunAsync(key, new ActionInvocation(values, CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, roles, currentUser.UserName, null, HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString(), HostFiles.From(Request)), "core", ct);
        if (result.Status == ActionStatus.Ok && result.Outcome?.File is { } produced) return File(produced.Bytes, produced.ContentType, produced.FileName);   // SCRUM-615: an export or a download
        if (Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase))
        {
            // a grid row action asks for JSON: the outcome and the text to show, no redirect
            var text = (result.Status == ActionStatus.Ok ? result.Message : result.Outcome?.FormError ?? result.Outcome?.FieldErrors?.Values.FirstOrDefault())?.Resolve(CultureInfo.CurrentUICulture);
            return result.Status switch
            {
                ActionStatus.Ok => Json(new { ok = true, message = text }),
                ActionStatus.Refused => BadRequest(new { ok = false, error = text, errors = ErrorShape.FromOutcome(result.Outcome!, CultureInfo.CurrentUICulture.TwoLetterISOLanguageName) }),   // SCRUM-380: the same field-error shape as the API (error stays for the scripts that read it)
                ActionStatus.StepUpRequired => StatusCode(428, new { ok = false, stepUp = true, options = result.StepUpOptionsJson, error = text }),   // SCRUM-595: run the security-key ceremony with these options, then post again with the answer
                ActionStatus.Forbidden => StatusCode(403, new { ok = false, error = (string?)null }),
                _ => NotFound(new { ok = false, error = (string?)null })
            };
        }
        switch (result.Status)
        {
            case ActionStatus.NotFound: return NotFound();
            case ActionStatus.Forbidden: return Forbid();
            case ActionStatus.StepUpRequired:
            case ActionStatus.Refused:
                TempData["Error"] = (result.Outcome!.FormError ?? result.Outcome.FieldErrors?.Values.FirstOrDefault())?.Resolve(CultureInfo.CurrentUICulture);
                break;
            default:
                if (result.Message is { IsEmpty: false } m) TempData["Message"] = m.Resolve(CultureInfo.CurrentUICulture);
                break;
        }
        var target = result.Redirect ?? returnUrl;
        return !string.IsNullOrEmpty(target) && Url.IsLocalUrl(target) ? LocalRedirect(target) : Redirect("~/");
    }

    private async Task<System.Text.Json.JsonElement?> ReadJsonAsync(CancellationToken ct)
    {
        try
        {
            using var limited = new MemoryStream();
            await Request.Body.CopyToAsync(limited, ct);
            if (limited.Length is 0 or > 65536) return null;
            limited.Position = 0;
            return (await System.Text.Json.JsonDocument.ParseAsync(limited, cancellationToken: ct)).RootElement.Clone();
        }
        catch (System.Text.Json.JsonException) { return null; }
    }
}

/// <summary>The values of an action posted as JSON: the scalars of the top-level object as text (objects and arrays are ignored, never flattened), at most 100 names.</summary>
public static class ActionValues
{
    public static Dictionary<string, string?> FromJson(System.Text.Json.JsonElement? body)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (body is not { ValueKind: System.Text.Json.JsonValueKind.Object } obj) return values;
        foreach (var p in obj.EnumerateObject().Take(100))
        {
            if (p.Name is "__RequestVerificationToken" or "returnUrl") continue;
            values[p.Name] = p.Value.ValueKind switch
            {
                System.Text.Json.JsonValueKind.String => p.Value.GetString(),
                System.Text.Json.JsonValueKind.Number => p.Value.GetRawText(),
                System.Text.Json.JsonValueKind.True => "true",
                System.Text.Json.JsonValueKind.False => "false",
                System.Text.Json.JsonValueKind.Null => null,
                _ => null
            };
            if (p.Value.ValueKind is System.Text.Json.JsonValueKind.Object or System.Text.Json.JsonValueKind.Array) values.Remove(p.Name);
        }
        return values;
    }
}
