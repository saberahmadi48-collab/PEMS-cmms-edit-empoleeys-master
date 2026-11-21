using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.Common;
using PEMS.Application.Definitions;
using PEMS.Application.Uploads;
using PEMS.Persistence.Context;
using PEMS.UI.Abstractions;

namespace PEMS.Web.Definitions;

// SCRUM-437 (the waves): service operations. A page whose actions go through a service with business rules (status sync, segregation of duties, numbering, notifications) must keep those rules, so it
// cannot be an entity write. Instead the existing service method is exposed to definitions, as it is, by an attribute on its interface method ([PemsOperation("WorkRequest.Submit", "WorkRequest.View")]):
// the catalog below finds every such method at start-up, and ONE generic handler (operation.invoke) runs it for an action definition. No adapter and no handler per operation: the service stays the only
// place of its rules, the definition says which operation a button runs and where its arguments come from, and the code checks the operation's permission, not the definition.

/// <summary>An operation found on a service interface: its model and action, the permission the code requires, and the method to call.</summary>
public sealed record OperationRegistration(string Model, string Action, string? Permission, Type ServiceType, MethodInfo Method)
{
    /// <summary>The roles that may run the operation (empty = any role that passes the permission check).</summary>
    public IReadOnlyList<string> Roles { get; init; } = [];

    public string Name => Model + "." + Action;
}

/// <summary>What a designer sees of an operation: the pair, the permission and the arguments the method takes (the actor and the cancellation token are supplied by the engine).</summary>
public sealed record OperationDescription(string Model, string Action, string? Permission, IReadOnlyList<string> Arguments, IReadOnlyList<string>? Roles = null);

public sealed class OperationCatalog
{
    private readonly Dictionary<(string Model, string Action), OperationRegistration> _operations = new();

    /// <summary>Scans the interfaces of the given assemblies for methods marked with <see cref="PemsOperationAttribute"/>.</summary>
    public static OperationCatalog Scan(params Assembly[] assemblies)
    {
        var catalog = new OperationCatalog();
        foreach (var type in assemblies.SelectMany(a => a.GetTypes()).Where(t => t.IsInterface))
            foreach (var method in type.GetMethods())
                if (method.GetCustomAttribute<PemsOperationAttribute>() is { } attr)
                {
                    if (catalog._operations.ContainsKey((attr.Model, attr.Action))) throw new InvalidOperationException($"The operation '{attr.Model}.{attr.Action}' is declared twice.");
                    catalog._operations[(attr.Model, attr.Action)] = new OperationRegistration(attr.Model, attr.Action, attr.Permission, type, method)
                    {
                        Roles = (attr.Roles ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    };
                }
        return catalog;
    }

    public OperationRegistration? Get(string? model, string? action) => model is not null && action is not null && _operations.TryGetValue((model, action), out var o) ? o : null;

    /// <summary>The models that have operations, and the actions of one model: what a designer chooses from.</summary>
    public IReadOnlyList<string> Models => _operations.Keys.Select(k => k.Model).Distinct(StringComparer.Ordinal).OrderBy(m => m, StringComparer.Ordinal).ToList();
    public IReadOnlyList<string> Actions(string model) => _operations.Keys.Where(k => k.Model == model).Select(k => k.Action).OrderBy(a => a, StringComparer.Ordinal).ToList();

    public IReadOnlyList<OperationDescription> Describe()
        => _operations.Values.OrderBy(o => o.Model, StringComparer.Ordinal).ThenBy(o => o.Action, StringComparer.Ordinal)
            .Select(o => new OperationDescription(o.Model, o.Action, o.Permission, o.Method.GetParameters().Where(p => p.ParameterType != typeof(CancellationToken) && (Nullable.GetUnderlyingType(p.ParameterType) ?? p.ParameterType) is { } t
                && (t == typeof(string) || t.IsPrimitive || t == typeof(decimal) || t == typeof(DateTime) || t.IsEnum)).Select(p => p.Name!).ToList(), o.Roles)).ToList();
}

/// <summary>
/// <c>operation.invoke</c>: runs a registered service operation. Parameters: <c>model</c> and <c>action</c> (the pair the service method is marked with) and <c>args</c> (parameter name to source: <c>field:name</c> a value of the form or button, <c>literal:text</c>, or
/// <c>context:employeeId|roles|userName|culture|ip|userAgent|userId|disciplineId</c>; a parameter without an entry reads the field of the same name). A parameter that is a record (an "actor") is built from the context by the names of its
/// constructor parameters. The method's answer <c>(bool Success, string? Error)</c>, <c>bool</c> or none is turned into the outcome of the action; a refusal shows the service's own message.
/// </summary>
public sealed class OperationInvokeAction(IServiceScopeFactory scopes, OperationCatalog catalog) : IActionHandler
{
    public string Key => "operation.invoke";
    public IReadOnlyCollection<string>? AllowedRoles => null;

    private static bool IsSimple(Type t)
    {
        t = Nullable.GetUnderlyingType(t) ?? t;
        return t == typeof(string) || t == typeof(int) || t == typeof(long) || t == typeof(decimal) || t == typeof(double) || t == typeof(bool) || t == typeof(DateTime) || t.IsEnum;
    }

    /// <summary>SCRUM-635: the upload profiles an operation file argument may name (the checks of the upload guard: allow-list, size, content, malware hook).</summary>
    private static readonly IReadOnlyDictionary<string, UploadProfile> UploadProfiles = new Dictionary<string, UploadProfile>(StringComparer.Ordinal)
    {
        ["attachment"] = UploadProfile.Attachment, ["image"] = UploadProfile.Image, ["spreadsheet"] = UploadProfile.Spreadsheet, ["csv"] = UploadProfile.Csv, ["pdf"] = UploadProfile.Pdf, ["licenceFile"] = UploadProfile.LicenceFile, ["migrationPackage"] = UploadProfile.MigrationPackage
    };

    private static readonly HashSet<string> ContextNames = new(StringComparer.OrdinalIgnoreCase) { "employeeId", "roles", "userName", "culture", "ip", "userAgent", "userId", "disciplineId" };

    /// <summary>A plain options object (settable simple properties, no constructor arguments), such as a completion-data record: its properties are bound from the form values of the same name (first letter lower-case), or from the sources the definition lists for them.</summary>
    /// <summary>A record whose constructor takes only simple values (a positional "input" record): built from the sources the definition lists for each constructor parameter (default: the form field of the same name).</summary>
    private static bool IsInput(Type t) => t.IsClass && t != typeof(string) && !IsTextList(t) && t.GetConstructors().OrderByDescending(c => c.GetParameters().Length).FirstOrDefault() is { } c
        && c.GetParameters().Length > 0 && c.GetParameters().All(cp => IsSimple(cp.ParameterType)) && !c.GetParameters().All(cp => ContextNames.Contains(cp.Name!));

    private static bool IsBag(Type t) => t.IsClass && t != typeof(string) && t.GetConstructor(Type.EmptyTypes) is not null
        && t.GetProperties().Any(pr => pr.CanWrite && IsSimple(pr.PropertyType));

    private static string CamelCase(string name) => char.ToLowerInvariant(name[0]) + name[1..];

    private static bool IsSource(string s) => s.StartsWith("field:") && s.Length > 6 || s.StartsWith("file:") && s.Length > 5 || s.StartsWith("literal:") || s.StartsWith("context:") && ContextNames.Contains(s[8..]);

    private static bool IsTextList(Type t) => t != typeof(string) && typeof(IEnumerable<string>).IsAssignableFrom(t);

    /// <summary>A list of text named "...Roles" is the roles of the user (context:roles); any other optional list of text is not settable from a definition and is passed as null.</summary>
    private static bool IsRoles(ParameterInfo p) => p.Name!.EndsWith("Roles", StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<DefinitionFieldError> ValidateParameters(JsonObject? parameters)
    {
        var model = EntityJson.Str(parameters?["model"]);
        if (catalog.Get(model, EntityJson.Str(parameters?["action"])) is not { } op)
        {
            if (model is null || !catalog.Models.Contains(model)) return [new("/model", "unknown-model", "Def_Err_UnknownOperation", $"parameters.model must be one of: {string.Join(", ", catalog.Models)}.")];
            return [new("/action", "unknown-action", "Def_Err_UnknownOperation", $"parameters.action of {model} must be one of: {string.Join(", ", catalog.Actions(model))}.")];
        }
        var errors = new List<DefinitionFieldError>();
        if (parameters!["guard"] is { } guardNode)
        {
            if (guardNode is not JsonObject g) errors.Add(new("/guard", "wrong-guard", "Def_Err_Type", "guard is an object: { resource, action, resourceId, mode }."));
            else
            {
                foreach (var key in g.Select(kv => kv.Key).Where(k => k is not ("resource" or "action" or "resourceId" or "mode" or "message")))
                    errors.Add(new("/guard/" + key, "unknown-parameter", "Def_Err_UnknownField", $"'{key}' is not a guard setting (resource, action, resourceId, mode, message)."));
                if (string.IsNullOrWhiteSpace(EntityJson.Str(g["resource"])) || string.IsNullOrWhiteSpace(EntityJson.Str(g["action"])))
                    errors.Add(new("/guard", "wrong-guard", "Def_Err_Required", "A guard names the resource and the action of the access decision (for example Production and Approve)."));
                if (g["resourceId"] is not null && !IsSource(EntityJson.Str(g["resourceId"]) ?? "")) errors.Add(new("/guard/resourceId", "wrong-source", "Def_Err_Type", "A source is field:name, literal:text or context:employeeId|roles|userName|culture."));
                if (EntityJson.Str(g["mode"]) is { } mode && mode is not ("sod" or "strict")) errors.Add(new("/guard/mode", "wrong-guard", "Def_Err_Type", "mode is sod (only a separation-of-duties refusal stops the action) or strict (anything but an explicit allow stops it)."));
            }
        }
        // SCRUM-781: require = { field: message } must be true before the operation runs; precondition = another operation ({ model, action, args }) that must succeed first
        if (parameters["require"] is not null and not JsonObject) errors.Add(new("/require", "wrong-require", "Def_Err_Type", "require is an object: { fieldName: { en, fa } }."));
        if (parameters["precondition"] is JsonObject pre) errors.AddRange(ValidateParameters(pre).Select(e => e with { Pointer = "/precondition" + e.Pointer }));
        else if (parameters["precondition"] is not null) errors.Add(new("/precondition", "wrong-precondition", "Def_Err_Type", "precondition is an operation: { model, action, args }."));
        errors.AddRange(OperationSectionGuard.Validate(parameters["sectionGuard"] as JsonObject, IsSource));
        if (parameters["sectionGuard"] is not null and not JsonObject) errors.Add(new("/sectionGuard", "wrong-guard", "Def_Err_Type", "sectionGuard is an object: { section, action, workOrderId, reason, recordRef, audit }."));
        var args = parameters["args"] as JsonObject ?? [];
        var names = op.Method.GetParameters().Select(p => p.Name!).ToHashSet(StringComparer.Ordinal);
        foreach (var (name, source) in args)
        {
            var s = EntityJson.Str(source) ?? "";
            if (!names.Contains(name)) errors.Add(new("/args/" + name, "unknown-parameter", "Def_Err_UnknownField", $"'{name}' is not a parameter of {op.Name}."));
            else if (source is JsonObject inputMap && IsInput(op.Method.GetParameters().First(pp => pp.Name == name).ParameterType))
            {
                var ctor = op.Method.GetParameters().First(pp => pp.Name == name).ParameterType.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
                foreach (var (property, propertySource) in inputMap)
                    if (ctor.GetParameters().All(cp => !string.Equals(cp.Name, property, StringComparison.OrdinalIgnoreCase))) errors.Add(new($"/args/{name}/{property}", "unknown-parameter", "Def_Err_UnknownField", $"'{property}' is not a value of {name}."));
                    else if (!IsSource(EntityJson.Str(propertySource) ?? "")) errors.Add(new($"/args/{name}/{property}", "wrong-source", "Def_Err_Type", "A source is field:name, literal:text or context:employeeId|roles|userName|culture."));
            }
            else if (source is JsonObject bag)
            {
                var type = op.Method.GetParameters().First(pp => pp.Name == name).ParameterType;
                if (!IsBag(type)) errors.Add(new("/args/" + name, "wrong-source", "Def_Err_Type", $"'{name}' of {op.Name} is not an options object."));
                else
                    foreach (var (property, propertySource) in bag)
                    {
                        if (!type.GetProperties().Any(pr => pr.Name == property && pr.CanWrite && IsSimple(pr.PropertyType))) errors.Add(new($"/args/{name}/{property}", "unknown-parameter", "Def_Err_UnknownField", $"'{property}' is not a property of {name}."));
                        else if (!IsSource(EntityJson.Str(propertySource) ?? "")) errors.Add(new($"/args/{name}/{property}", "wrong-source", "Def_Err_Type", "A source is field:name, literal:text or context:employeeId|roles|userName|culture."));
                    }
            }
            else if (!IsSource(s)) errors.Add(new("/args/" + name, "wrong-source", "Def_Err_Type", "A source is field:name, literal:text or context:employeeId|roles|userName|culture."));
        }
        foreach (var p in op.Method.GetParameters())
        {
            if (p.ParameterType == typeof(CancellationToken)) continue;
            if (IsSimple(p.ParameterType)) continue;
            if (p.ParameterType == typeof(byte[]))
            {
                // SCRUM-635: the bytes of an uploaded file: args names the form field as file:field and the action names the upload profile
                if (!(EntityJson.Str(args[p.Name!]) ?? "").StartsWith("file:")) errors.Add(new("/args/" + p.Name, "wrong-source", "Def_Err_Type", $"The file parameter '{p.Name}' of {op.Name} takes file:field."));
                if (parameters["upload"] is not JsonObject up || !UploadProfiles.ContainsKey(EntityJson.Str(up["profile"]) ?? "")) errors.Add(new("/upload", "required", "Def_Err_Required", $"upload: {{ profile, maxFileMb }} is required for a file parameter; profile is one of: {string.Join(", ", UploadProfiles.Keys)}."));
                continue;
            }
            if (IsTextList(p.ParameterType))
            {
                if (!IsRoles(p)) { if (args.ContainsKey(p.Name!)) errors.Add(new("/args/" + p.Name, "wrong-source", "Def_Err_Type", $"The list '{p.Name}' of {op.Name} cannot be set from a definition; leave it out.")); continue; }
                if (EntityJson.Str(args[p.Name!]) is not "context:roles") errors.Add(new("/args/" + p.Name, "wrong-source", "Def_Err_Type", $"The list parameter '{p.Name}' of {op.Name} is the roles of the user: set it to context:roles."));
                continue;
            }
            if (IsBag(p.ParameterType) || IsInput(p.ParameterType)) continue;
            var ctor = p.ParameterType.GetConstructors().OrderByDescending(c => c.GetParameters().Length).FirstOrDefault();
            if (ctor is null || ctor.GetParameters().Any(cp => !ContextNames.Contains(cp.Name!)))
                errors.Add(new("/operation", "unsupported-parameter", "Def_Err_Type", $"The parameter '{p.Name}' of {op.Name} is neither a simple value nor an object built from the context (employeeId, roles, userName, culture)."));
        }
        return errors;
    }

    /// <summary>
    /// The access-control decision of the old controllers, as a setting of the action: <c>guard: { resource, action, resourceId, mode }</c>. It asks IAccessControlService (scope + separation of duties,
    /// one audit row) before the service is called. mode "sod" (default, the behaviour of the Production Order controller) stops only a separation-of-duties refusal; mode "strict" (the
    /// ProtectedActionGuard of procurement) stops everything except an explicit allow, and a user without identity. Null = the action may go on.
    /// </summary>
    private static async Task<ActionOutcome?> EvaluateGuardAsync(IServiceProvider services, JsonObject guard, ActionInvocation invocation, Dictionary<string, object?> context, int? userId, CancellationToken ct)
    {
        var access = services.GetRequiredService<PEMS.Application.Interfaces.IAccessControlService>();
        var resource = EntityJson.Str(guard["resource"])!;
        var action = EntityJson.Str(guard["action"])!;
        var strict = EntityJson.Str(guard["mode"]) == "strict";
        var source = EntityJson.Str(guard["resourceId"]);
        var resourceId = source is null ? null : source.StartsWith("literal:") ? source[8..] : source.StartsWith("context:") ? EntityJson.Show(context[source[8..]]) : invocation.Values.GetValueOrDefault(source[6..]);
        var refused = TranslatableText.Of(("en", "You may not do this."), ("fa", "اجازه‌ی انجام این کار را ندارید."));

        if (userId is null)
        {
            if (!strict) return null;                    // the old controller let an unidentified caller through to the permission check
            try { await access.RecordDenialAsync(null, resource, action, resourceId, invocation.IpAddress, PEMS.Application.DTOs.AccessDecisionReason.DeniedByPolicy, "The signed-in user could not be identified."); } catch (Exception) { /* an audit failure never becomes an allow */ }
            return ActionOutcome.Refused(refused);
        }

        PEMS.Application.DTOs.AccessDecision? decision;
        try
        {
            decision = await access.AuthorizeAsync(new PEMS.Application.DTOs.AccessControlRequest { UserId = userId.Value, ResourceName = resource, ActionName = action, ResourceId = resourceId, IpAddress = invocation.IpAddress });
        }
        catch (Exception) when (strict) { return ActionOutcome.Refused(refused); }

        if (decision is null) return strict ? ActionOutcome.Refused(refused) : null;
        if (decision.Reason == PEMS.Application.DTOs.AccessDecisionReason.DeniedBySoD)
        {
            var message = string.IsNullOrWhiteSpace(decision.Detail)
                ? TranslatableText.Of(("en", "Under the separation-of-duties policy you may not do this."), ("fa", "طبق سیاست تفکیک وظایف، این اقدام برای شما مجاز نیست."))
                : TranslatableText.Literal(decision.Detail);
            return ActionOutcome.Refused(message);
        }
        if (strict && !(decision.Allowed && decision.Reason == PEMS.Application.DTOs.AccessDecisionReason.Allow)) return ActionOutcome.Refused(refused);
        return null;
    }

    /// <summary>A result object of the shape { Success, Error, Args } (the Po result of procurement and its kind): the refusal text is the Error, a resource key when the shared resources know it (filled with Args), in both languages.</summary>
    /// <summary>SCRUM-816: a result object with SecretValue (and SecretLabel, SecretTitle, SecretNote, BackUrl, BackLabel as resource keys) is a value to show once; a result with Success false is a refusal.</summary>
    private static ActionSecret? SecretOf(IServiceProvider services, object answer)
    {
        var t = answer.GetType();
        if (t.GetProperty("SecretValue")?.GetValue(answer) is not string value || value.Length == 0) return null;
        if (t.GetProperty("Success")?.GetValue(answer) is false) return null;
        string? Key(string name) => t.GetProperty(name)?.GetValue(answer) as string;
        TranslatableText Text(string? key, string fallback) => ResultMessage(services, (true, string.IsNullOrWhiteSpace(key) ? fallback : key, []));
        var note = Key("SecretNote");
        return new ActionSecret(Text(Key("SecretTitle"), "Secret"), string.IsNullOrWhiteSpace(note) ? null : Text(note, note), [new SecretItem(Text(Key("SecretLabel"), "Secret"), value)], Key("BackUrl"), Text(Key("BackLabel"), "Back"));
    }

    /// <summary>A result object with Bytes (and FileName, ContentType) is a file for the user to download; a result with Success false is a refusal, not a file (SCRUM-635).</summary>
    private static FileResult? FileOf(object answer)
    {
        var t = answer.GetType();
        if (t.GetProperty("Bytes")?.GetValue(answer) is not byte[] bytes) return null;
        if (t.GetProperty("Success")?.GetValue(answer) is false) return null;
        return new FileResult(bytes, t.GetProperty("FileName")?.GetValue(answer) as string ?? "download", t.GetProperty("ContentType")?.GetValue(answer) as string ?? "application/octet-stream");
    }

    private static (bool Success, string? Error, object?[] Args)? ResultOf(object answer)
    {
        var t = answer.GetType();
        if (t.GetProperty("Success")?.GetValue(answer) is not bool ok)
            return t.GetProperty("Succeeded")?.GetValue(answer) is bool done ? (done, t.GetProperty("Message")?.GetValue(answer) as string, [])
                : t.GetProperty("Message")?.GetValue(answer) is string text && t.IsClass && t != typeof(string) ? (true, text, []) : null;
        var args = t.GetProperty("Args")?.GetValue(answer) as object?[] ?? [];
        var error = t.GetProperty("Error")?.GetValue(answer) as string ?? t.GetProperty("ErrorKey")?.GetValue(answer) as string;
        var detail = t.GetProperty("Detail")?.GetValue(answer) as string;
        if (!ok && !string.IsNullOrWhiteSpace(detail) && args.Length == 0) args = [detail];      // the detail is the first argument of the message ({0}) when there is no other
        return (ok, error, args);
    }

    private static TranslatableText ResultMessage(IServiceProvider services, (bool Success, string? Error, object?[] Args) result)
    {
        var key = result.Error ?? "The operation was refused.";
        var localizer = services.GetService<Microsoft.Extensions.Localization.IStringLocalizer<PEMS.Web.Resources.SharedResource>>();
        string In(string culture)
        {
            var previous = CultureInfo.CurrentUICulture;
            try { CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture); var s = localizer?[key, result.Args!]; return s is { ResourceNotFound: false } ? s.Value : key; }
            finally { CultureInfo.CurrentUICulture = previous; }
        }
        return TranslatableText.Of(("en", In("en")), ("fa", In("fa")));
    }

    public async Task<ActionOutcome> ExecuteAsync(ActionInvocation invocation, CancellationToken ct)
    {
        var op = catalog.Get(EntityJson.Str(invocation.Parameters?["model"]), EntityJson.Str(invocation.Parameters?["action"]));
        if (op is null) return ActionOutcome.Refused(TranslatableText.Of(("en", "The action is not set up."), ("fa", "کنش درست تنظیم نشده است.")));
        using var scope = ActionScope.Open(scopes);
        if (!await scope.ServiceProvider.GetRequiredService<IDefinitionAccess>().CanOpenAsync(op.Permission, invocation.Roles, ct)
            || op.Roles.Count > 0 && !invocation.Roles.Any(r => op.Roles.Contains(r, StringComparer.OrdinalIgnoreCase)))      // SCRUM-773: an operation may also name the roles that can run it
            return ActionOutcome.Refused(TranslatableText.Of(("en", "You may not do this."), ("fa", "اجازه‌ی انجام این کار را ندارید.")));

        // SCRUM-781: fields that must be ticked (a confirmation), then a precondition operation of a service (entitlement, state) that must succeed
        if (invocation.Parameters?["require"] is JsonObject required)
            foreach (var (field, message) in required)
                if (invocation.Values.GetValueOrDefault(field) is not ("true" or "on" or "1" or "True"))
                    return ActionOutcome.Refused(message is JsonObject mo
                        ? TranslatableText.Of(("en", EntityJson.Str(mo["en"]) ?? "Confirm first."), ("fa", EntityJson.Str(mo["fa"]) ?? "ابتدا تأیید کنید."))
                        : TranslatableText.Literal(EntityJson.Str(message) ?? "Confirm first."));
        if (invocation.Parameters?["precondition"] is JsonObject precondition)
        {
            var check = await ExecuteAsync(invocation with { Parameters = precondition }, ct);
            if (!check.Ok) return check;
        }

        var db = scope.ServiceProvider.GetRequiredService<PEMSDbContext>();
        var identity = invocation.UserName is null ? null : await db.Users.Where(u => u.UserName == invocation.UserName).Select(u => new { u.Id, u.EmployeeId }).FirstOrDefaultAsync(ct);
        var employeeId = identity?.EmployeeId;
        var disciplineId = employeeId is null ? null : await db.Employees.Where(e => e.EmployeeId == employeeId).Select(e => (int?)e.DisciplineId).FirstOrDefaultAsync(ct);
        var context = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            { ["employeeId"] = employeeId, ["roles"] = invocation.Roles.ToList(), ["userName"] = invocation.UserName, ["culture"] = invocation.Culture, ["userId"] = identity?.Id, ["disciplineId"] = disciplineId, ["ip"] = invocation.IpAddress, ["userAgent"] = invocation.UserAgent };
        var args = invocation.Parameters?["args"] as JsonObject ?? [];

        if (invocation.Parameters?["guard"] is JsonObject guard)
        {
            var refusal = await EvaluateGuardAsync(scope.ServiceProvider, guard, invocation, context, identity?.Id, ct);
            if (refusal is not null) return refusal;
        }

        string? Resolve(string? source) => source is null ? null : source.StartsWith("literal:") ? source[8..] : source.StartsWith("context:") ? EntityJson.Show(context[source[8..]]) : invocation.Values.GetValueOrDefault(source[6..]);
        var caller = new PEMS.Application.Interfaces.WoCaller(identity?.Id, invocation.UserName, employeeId, invocation.Roles.ToList(), invocation.IpAddress);
        SectionGuardPlan? sectionPlan = null;
        if (invocation.Parameters?["sectionGuard"] is JsonObject sectionGuard)
        {
            var (refusal, plan) = await OperationSectionGuard.AuthorizeAsync(scope.ServiceProvider, sectionGuard, Resolve, invocation.Culture, caller);
            if (refusal is not null) return refusal;
            sectionPlan = plan;
        }

        var values = new List<object?>();
        foreach (var p in op.Method.GetParameters())
        {
            if (p.ParameterType == typeof(CancellationToken)) { values.Add(ct); continue; }
            if (p.ParameterType == typeof(byte[]))
            {
                var source = EntityJson.Str(args[p.Name!]) ?? string.Empty;
                var fileField = source.StartsWith("file:") ? source[5..] : p.Name!;
                var file = invocation.Files?.FirstOrDefault(f => f.Field == fileField);
                if (file is null) return ActionOutcome.Refused(fileField, TranslatableText.Of(("en", "Choose a file."), ("fa", "یک فایل انتخاب کنید.")));
                var upload = invocation.Parameters?["upload"] as JsonObject;
                var uploadGuard = scope.ServiceProvider.GetService<IUploadGuard>();
                if (uploadGuard is null || upload is null || !UploadProfiles.TryGetValue(EntityJson.Str(upload["profile"]) ?? "", out var profile))
                    return ActionOutcome.Refused(TranslatableText.Of(("en", "The action is not set up."), ("fa", "کنش درست تنظیم نشده است.")));
                var limitMb = upload["maxFileMb"] is JsonValue lv && lv.TryGetValue<int>(out var mb) ? mb : 0;
                if (limitMb > 0 && file.Length > limitMb * 1024L * 1024L) return ActionOutcome.Refused(fileField, TranslatableText.Of(("en", $"File {file.FileName} is larger than {limitMb} MB."), ("fa", $"حجم فایل {file.FileName} بیش از {limitMb} مگابایت است.")));
                var check = await uploadGuard.CheckAsync(profile, file.FileName, file.ContentType, file.Length, file.Open, ct);
                if (!check.Ok) return ActionOutcome.Refused(fileField, TranslatableText.Of(("en", $"File {file.FileName} was refused ({check.Error})."), ("fa", $"فایل {file.FileName} رد شد ({check.Error}).")));
                await using var stream = file.Open();
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer, ct);
                values.Add(buffer.ToArray());
                continue;
            }
            if (IsTextList(p.ParameterType))
            {
                values.Add(IsRoles(p) ? invocation.Roles.ToList() : null);
                continue;
            }
            if (IsSimple(p.ParameterType))
            {
                var source = EntityJson.Str(args[p.Name!]) ?? "field:" + p.Name;
                var text = source.StartsWith("literal:") ? source[8..] : source.StartsWith("context:") ? EntityJson.Show(context[source[8..]]) : invocation.Values.GetValueOrDefault(source[6..]);
                var converted = EntityJson.Convert(p.ParameterType, text);
                if (converted is EntityJson.NotConvertible) return ActionOutcome.Refused(TranslatableText.Of(("en", $"The value of '{p.Name}' is not valid."), ("fa", $"مقدار «{p.Name}» معتبر نیست.")));
                var plainValue = p.ParameterType.IsValueType && Nullable.GetUnderlyingType(p.ParameterType) is null && p.ParameterType != typeof(bool);   // a missing checkbox is false, a missing number is missing
                if (plainValue && !p.HasDefaultValue && string.IsNullOrWhiteSpace(text))
                    return ActionOutcome.Refused(TranslatableText.Of(("en", $"'{p.Name}' is required."), ("fa", $"«{p.Name}» الزامی است.")));
                values.Add(converted);
            }
            else if (IsInput(p.ParameterType))
            {
                var ctor = p.ParameterType.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
                var map = args[p.Name!] as JsonObject ?? [];
                var built = new List<object?>();
                foreach (var cp in ctor.GetParameters())
                {
                    var source = EntityJson.Str(map.FirstOrDefault(kv => string.Equals(kv.Key, cp.Name, StringComparison.OrdinalIgnoreCase)).Value) ?? "field:" + CamelCase(cp.Name!);
                    var text = source.StartsWith("literal:") ? source[8..] : source.StartsWith("context:") ? EntityJson.Show(context[source[8..]]) : invocation.Values.GetValueOrDefault(source[6..]);
                    var converted = EntityJson.Convert(cp.ParameterType, text);
                    if (converted is EntityJson.NotConvertible) return ActionOutcome.Refused(TranslatableText.Of(("en", $"The value of '{cp.Name}' is not valid."), ("fa", $"مقدار «{cp.Name}» معتبر نیست.")));
                    var plainValue = cp.ParameterType.IsValueType && Nullable.GetUnderlyingType(cp.ParameterType) is null && cp.ParameterType != typeof(bool);
                    if (plainValue && string.IsNullOrWhiteSpace(text)) return ActionOutcome.Refused(TranslatableText.Of(("en", $"'{cp.Name}' is required."), ("fa", $"«{cp.Name}» الزامی است.")));
                    built.Add(converted);
                }
                values.Add(ctor.Invoke(built.ToArray()));
            }
            else if (IsBag(p.ParameterType))
            {
                // only built when the definition names it: an optional options object stays null otherwise
                if (!args.ContainsKey(p.Name!)) { values.Add(p.HasDefaultValue ? p.DefaultValue : null); continue; }
                var bag = Activator.CreateInstance(p.ParameterType)!;
                var map = args[p.Name!] as JsonObject ?? [];
                foreach (var property in p.ParameterType.GetProperties().Where(pr => pr.CanWrite && IsSimple(pr.PropertyType)))
                {
                    var source = EntityJson.Str(map[property.Name]) ?? "field:" + CamelCase(property.Name);
                    var text = source.StartsWith("literal:") ? source[8..] : source.StartsWith("context:") ? EntityJson.Show(context[source[8..]]) : invocation.Values.GetValueOrDefault(source[6..]);
                    var converted = EntityJson.Convert(property.PropertyType, text);
                    if (converted is EntityJson.NotConvertible) return ActionOutcome.Refused(TranslatableText.Of(("en", $"The value of '{property.Name}' is not valid."), ("fa", $"مقدار «{property.Name}» معتبر نیست.")));
                    property.SetValue(bag, converted);
                }
                values.Add(bag);
            }
            else
            {
                var ctor = p.ParameterType.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
                values.Add(ctor.Invoke(ctor.GetParameters().Select(cp => context[cp.Name!] is List<string> roles && cp.ParameterType != typeof(List<string>) ? (object)roles.AsReadOnly() : context[cp.Name!]).ToArray()));
            }
        }

        var service = scope.ServiceProvider.GetRequiredService(op.ServiceType);
        object? answer;
        try
        {
            var task = op.Method.Invoke(service, values.ToArray())!;
            await (Task)task;
            answer = task.GetType().IsGenericType ? task.GetType().GetProperty("Result")!.GetValue(task) : null;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException or UnauthorizedAccessException or DbUpdateConcurrencyException || ex.InnerException?.GetType() == typeof(Exception))   // a business rule thrown as a plain Exception (MOC) is shown; a crash is not
        {
            return ActionOutcome.Refused(TranslatableText.Literal(ex.InnerException.Message));
        }

        // (bool Success, string? Error) or bool; anything else is a success
        var outcome = answer switch
        {
            bool ok => ok ? ActionOutcome.Done() : ActionOutcome.Refused(TranslatableText.Of(("en", "The operation was refused."), ("fa", "عملیات پذیرفته نشد."))),
            ITuple t when t.Length >= 2 && t[0] is bool success => success ? ActionOutcome.Done() : ActionOutcome.Refused(TranslatableText.Literal(t[1] as string ?? "The operation was refused.")),
            _ when answer is not null && SecretOf(scope.ServiceProvider, answer) is { } secret => ActionOutcome.Revealed(secret),
            _ when answer is not null && FileOf(answer) is { } produced => ActionOutcome.Download(produced),
            _ when answer is not null && ResultOf(answer) is { } result => result.Success ? ActionOutcome.Done(result.Error is null ? null : ResultMessage(scope.ServiceProvider, result)) : ActionOutcome.Refused(ResultMessage(scope.ServiceProvider, result)),
            _ => ActionOutcome.Done()
        };
        if (outcome.Ok && sectionPlan is not null)
        {
            var arguments = op.Method.GetParameters().Zip(values, (p, v) => (p.Name!, v)).ToDictionary(x => x.Item1, x => x.v, StringComparer.OrdinalIgnoreCase);
            await OperationSectionGuard.RecordAsync(scope.ServiceProvider, sectionPlan, caller, arguments, answer);
        }
        return outcome;
    }
}
