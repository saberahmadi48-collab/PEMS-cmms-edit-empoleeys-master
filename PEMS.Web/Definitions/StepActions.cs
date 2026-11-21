using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.Definitions;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;
using PEMS.UI.Abstractions;

namespace PEMS.Web.Definitions;

// SCRUM-593 (ordered steps in one transaction), SCRUM-594 (a step's result reaches the next steps and the redirect) and SCRUM-595 (step-up verification as a setting of the action).
// An action that writes several tables is ONE definition: <c>action.steps</c> runs registered handlers in order inside one database transaction; the first refusal or exception rolls everything back and
// later steps do not run. Handlers keep using their own services; they find the transaction because they open their scope through <see cref="ActionScope"/>, which hands them the ambient scope (and so the
// same DbContext) while a transaction is running. No handler and no controller is written per action.

/// <summary>The scope handlers work in: the ambient one of a running transaction, else a scope of their own.</summary>
public static class ActionScope
{
    private static readonly AsyncLocal<IServiceProvider?> Ambient = new();

    public static ActionScopeHandle Open(IServiceScopeFactory scopes)
    {
        if (Ambient.Value is { } provider) return new ActionScopeHandle(provider, null);
        var scope = scopes.CreateScope();
        return new ActionScopeHandle(scope.ServiceProvider, scope);
    }

    /// <summary>Makes <paramref name="provider"/> the scope of every handler started inside the returned block.</summary>
    public static IDisposable Enter(IServiceProvider provider)
    {
        var previous = Ambient.Value;
        Ambient.Value = provider;
        return new Restore(previous);
    }

    private sealed class Restore(IServiceProvider? previous) : IDisposable
    {
        public void Dispose() => Ambient.Value = previous;
    }
}

public sealed class ActionScopeHandle(IServiceProvider provider, IServiceScope? owned) : IDisposable
{
    public IServiceProvider ServiceProvider => provider;
    public void Dispose() => owned?.Dispose();
}

/// <summary>Runs a body in one transaction: it is committed when the outcome is ok and rolled back when the outcome is a refusal or the body throws.</summary>
public interface IActionTransaction
{
    Task<ActionOutcome> RunAsync(Func<Task<ActionOutcome>> body, CancellationToken ct);
}

public sealed class EfActionTransaction(IServiceScopeFactory scopes) : IActionTransaction
{
    public async Task<ActionOutcome> RunAsync(Func<Task<ActionOutcome>> body, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        using var _ = ActionScope.Enter(scope.ServiceProvider);
        var db = scope.ServiceProvider.GetRequiredService<PEMSDbContext>();
        var strategy = db.Database.CreateExecutionStrategy();
        // the whole body is the unit the strategy may repeat, so a transient failure never leaves a half-written action
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var outcome = await body();
            if (outcome.Ok) await transaction.CommitAsync(ct); else await transaction.RollbackAsync(ct);
            return outcome;
        });
    }
}

/// <summary>
/// <c>action.steps</c>. Parameters: <c>steps</c> (1 to 20 of <c>{ "as": "name", "handler": "key", "parameters": {...}, "values": { "target": "source" } }</c>) and <c>transaction</c> (default true).
/// A source is <c>field:name</c> (a posted value), <c>literal:text</c> or <c>step:as.key</c> (a value an earlier step returned). The values every step returned are returned by the action as <c>as.key</c>,
/// so the redirect of the action definition can use <c>{as.key}</c>.
/// </summary>
public sealed class StepsAction(IServiceProvider services, IActionTransaction transaction) : IActionHandler
{
    public const string HandlerKey = "action.steps";
    public const int MaxSteps = 20;
    private static readonly Regex Name = new("^[A-Za-z][A-Za-z0-9_]*$", RegexOptions.Compiled);
    private static readonly HashSet<string> StepProperties = new(StringComparer.Ordinal) { "as", "handler", "parameters", "values" };

    public string Key => HandlerKey;
    public IReadOnlyCollection<string>? AllowedRoles => null;

    private Dictionary<string, IActionHandler> Handlers()
        => services.GetServices<IActionHandler>().Where(h => h.Key != HandlerKey).ToDictionary(h => h.Key, StringComparer.Ordinal);

    public IReadOnlyList<DefinitionFieldError> ValidateParameters(JsonObject? parameters)
    {
        var errors = new List<DefinitionFieldError>();
        if (parameters?["steps"] is not JsonArray steps || steps.Count == 0) return [new("/steps", "required", "Def_Err_Required", "steps is a list of at least one step.")];
        if (steps.Count > MaxSteps) errors.Add(new("/steps", "too-many", "Def_Err_Range", $"An action has at most {MaxSteps} steps."));
        if (parameters["transaction"] is { } t && !(t is JsonValue tv && tv.TryGetValue<bool>(out _))) errors.Add(new("/transaction", "wrong-type", "Def_Err_Type", "transaction is true or false."));

        var handlers = Handlers();
        var earlier = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < steps.Count; i++)
        {
            var at = $"/steps/{i}";
            if (steps[i] is not JsonObject step) { errors.Add(new(at, "wrong-type", "Def_Err_Type", "A step is an object.")); continue; }
            foreach (var p in step) if (!StepProperties.Contains(p.Key)) errors.Add(new($"{at}/{p.Key}", "unknown-property", "Def_Err_UnknownProperty", $"'{p.Key}' is not a property of a step."));
            var handlerKey = DataSourceDefinitionKind.Str(step["handler"]);
            if (handlerKey == HandlerKey) errors.Add(new($"{at}/handler", "nested-steps", "Def_Err_Type", "A step cannot be another action.steps."));
            else if (string.IsNullOrEmpty(handlerKey)) errors.Add(new($"{at}/handler", "required", "Def_Err_Required", "A step needs a handler key."));
            else if (!handlers.TryGetValue(handlerKey, out var handler)) errors.Add(new($"{at}/handler", "unknown-handler", "Def_Err_UnknownHandler", $"'{handlerKey}' is not a registered action handler."));
            else errors.AddRange(handler.ValidateParameters(step["parameters"] as JsonObject).Select(e => e with { Pointer = $"{at}/parameters" + e.Pointer }));

            if (step["values"] is JsonObject values)
                foreach (var (target, source) in values)
                {
                    var text = DataSourceDefinitionKind.Str(source) ?? "";
                    if (text.StartsWith("step:", StringComparison.Ordinal))
                    {
                        var from = text[5..];
                        var name = from.Contains('.') ? from[..from.IndexOf('.')] : from;
                        if (!from.Contains('.') || from.EndsWith('.')) errors.Add(new($"{at}/values/{target}", "wrong-source", "Def_Err_Type", "A step source is step:name.key."));
                        else if (!earlier.Contains(name)) errors.Add(new($"{at}/values/{target}", "unknown-step", "Def_Err_UnknownField", $"'{name}' is not an earlier step (a step can only use the result of a step before it)."));
                    }
                    else if (!(text.StartsWith("field:", StringComparison.Ordinal) && text.Length > 6 || text.StartsWith("literal:", StringComparison.Ordinal)))
                        errors.Add(new($"{at}/values/{target}", "wrong-source", "Def_Err_Type", "A source is field:name, literal:text or step:name.key."));
                }
            else if (step["values"] is not null) errors.Add(new($"{at}/values", "wrong-type", "Def_Err_Type", "values is an object."));

            if (step["as"] is { } alias)
            {
                var asName = DataSourceDefinitionKind.Str(alias);
                if (asName is null || !Name.IsMatch(asName)) errors.Add(new($"{at}/as", "invalid-name", "Def_Err_Type", "as is a name of letters, digits and _, starting with a letter."));
                else if (!earlier.Add(asName)) errors.Add(new($"{at}/as", "duplicate", "Def_Err_Duplicate", $"The step name '{asName}' is used twice."));
            }
        }
        return errors;
    }

    public async Task<ActionOutcome> ExecuteAsync(ActionInvocation invocation, CancellationToken ct)
    {
        var steps = invocation.Parameters?["steps"] as JsonArray ?? [];
        var useTransaction = invocation.Parameters?["transaction"] is not JsonValue tv || !tv.TryGetValue<bool>(out var flag) || flag;
        var handlers = Handlers();

        async Task<ActionOutcome> RunSteps()
        {
            var returned = new Dictionary<string, string?>(StringComparer.Ordinal);
            ActionOutcome? last = null;
            foreach (var node in steps.OfType<JsonObject>())
            {
                var handler = handlers[DataSourceDefinitionKind.Str(node["handler"])!];
                if (handler.AllowedRoles is not null && !handler.AllowedRoles.Any(r => invocation.Roles.Contains(r, StringComparer.OrdinalIgnoreCase)))
                    return ActionOutcome.Refused(TranslatableText.Of(("en", "You may not do this."), ("fa", "اجازه‌ی انجام این کار را ندارید.")));

                var values = new Dictionary<string, string?>(invocation.Values, StringComparer.Ordinal);
                foreach (var (target, source) in node["values"] as JsonObject ?? [])
                {
                    var text = DataSourceDefinitionKind.Str(source) ?? "";
                    values[target] = text.StartsWith("literal:", StringComparison.Ordinal) ? text[8..]
                        : text.StartsWith("step:", StringComparison.Ordinal) ? returned.GetValueOrDefault(text[5..])
                        : invocation.Values.GetValueOrDefault(text[6..]);
                }
                var outcome = await handler.ExecuteAsync(invocation with { Values = values, Parameters = node["parameters"] as JsonObject }, ct);
                if (!outcome.Ok) return outcome;                      // the first refusal stops everything; the transaction is rolled back by the runner
                var alias = DataSourceDefinitionKind.Str(node["as"]);
                if (alias is not null && outcome.Returned is not null)
                    foreach (var (key, value) in outcome.Returned) returned[alias + "." + key] = value;
                last = outcome;
            }
            return ActionOutcome.Done(last?.Message, last?.Redirect, returned);
        }

        return useTransaction ? await transaction.RunAsync(RunSteps, ct) : await RunSteps();
    }
}

// ---- SCRUM-595: step-up verification ---------------------------------------------------------------------------------------------------------------------------------------------------

public enum StepUpStatus { Passed, Challenge, Denied }

/// <summary>What the host needs to decide a step-up: who, which protected action (for the audit), and the answer of the security key if the browser already did the ceremony.</summary>
public sealed record StepUpRequest(string? UserName, string Resource, string Action, string? AssertionJson, string? OptionsJson, string? IpAddress);

public sealed record StepUpResult(StepUpStatus Status, string? OptionsJson = null, string? Error = null);

public interface IStepUpVerifier
{
    Task<StepUpResult> VerifyAsync(StepUpRequest request, CancellationToken ct);
}

/// <summary>
/// WebAuthn step-up: without an answer the host gives the challenge to run in the browser (<see cref="StepUpStatus.Challenge"/>); with an answer it is verified; a missing key, a wrong answer or an
/// unknown user is a denial and is audited. Never an allow on an error.
/// </summary>
public sealed class WebAuthnStepUpVerifier(IServiceScopeFactory scopes) : IStepUpVerifier
{
    public async Task<StepUpResult> VerifyAsync(StepUpRequest request, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PEMSDbContext>();
        var webauthn = scope.ServiceProvider.GetRequiredService<IWebAuthnService>();
        var userId = request.UserName is null ? null : await db.Users.Where(u => u.UserName == request.UserName).Select(u => (int?)u.Id).FirstOrDefaultAsync(ct);
        if (userId is null) return await DenyAsync(scope.ServiceProvider, null, request, "The signed-in user could not be identified.");

        if (string.IsNullOrWhiteSpace(request.AssertionJson) || string.IsNullOrWhiteSpace(request.OptionsJson))
        {
            if (!await webauthn.HasAnyCredentialAsync(userId.Value)) return await DenyAsync(scope.ServiceProvider, userId, request, "No security key is registered for the user.");
            var options = await webauthn.GetAssertionOptionsJsonAsync(userId.Value);
            return options is null ? await DenyAsync(scope.ServiceProvider, userId, request, "No challenge could be created.") : new StepUpResult(StepUpStatus.Challenge, options);
        }
        try
        {
            var (success, error, _) = await webauthn.CompleteAssertionAsync(userId.Value, request.AssertionJson, request.OptionsJson);
            return success ? new StepUpResult(StepUpStatus.Passed) : await DenyAsync(scope.ServiceProvider, userId, request, error ?? "The security key answer was not accepted.");
        }
        catch (Exception) { return await DenyAsync(scope.ServiceProvider, userId, request, "The security key answer could not be verified."); }
    }

    private static async Task<StepUpResult> DenyAsync(IServiceProvider services, int? userId, StepUpRequest request, string reason)
    {
        try { await services.GetRequiredService<IAccessControlService>().RecordDenialAsync(userId, request.Resource, request.Action, null, request.IpAddress, PEMS.Application.DTOs.AccessDecisionReason.DeniedByPolicy, "Step-up: " + reason); }
        catch (Exception) { /* an audit failure never becomes an allow */ }
        return new StepUpResult(StepUpStatus.Denied, Error: reason);
    }
}
