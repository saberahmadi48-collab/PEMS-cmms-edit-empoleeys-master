using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using LateApexEarlySpeed.Json.Schema;
using LateApexEarlySpeed.Json.Schema.Common;
using PEMS.UI.Abstractions;
using PEMS.UI.Rendering;

namespace PEMS.UI.Manifest;

/// <summary>One field error in the shape of the API error contract (ADR-0009): JSON Pointer, constraint code, message key, parameters.</summary>
public sealed record OptionsError(string Pointer, string Code, string MessageKey, string Message);

public sealed record OptionsValidationResult(bool IsValid, IReadOnlyList<OptionsError> Errors)
{
    public static OptionsValidationResult Ok { get; } = new(true, []);
}

/// <summary>
/// The manifests of every registered component, plus validation of stored options JSON against them and conversion of valid options to
/// component parameters. The page host and the designers go through this class; components never see raw JSON.
/// </summary>
public sealed class ComponentManifestCatalog(IPemsComponentRegistry registry)
{
    private readonly ConcurrentDictionary<string, (ComponentManifest Manifest, JsonValidator Validator, Type Component)> _cache = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<ComponentManifest> All() => registry.Keys.OrderBy(k => k, StringComparer.Ordinal).Select(Get).ToList();

    public ComponentManifest Get(string key) => Entry(key).Manifest;

    private (ComponentManifest Manifest, JsonValidator Validator, Type Component) Entry(string key) => _cache.GetOrAdd(key, k =>
    {
        var type = registry.Resolve(k);
        var manifest = ComponentManifestGenerator.Generate(k, type);
        return (manifest, new JsonValidator(manifest.Options.ToJsonString()), type);
    });

    public OptionsValidationResult Validate(string key, string optionsJson)
    {
        JsonNode? instance;
        try { instance = JsonNode.Parse(optionsJson); }
        catch (JsonException ex) { return new OptionsValidationResult(false, [new OptionsError("", "malformed", "validation.malformed", ex.Message)]); }
        if (instance is null) return new OptionsValidationResult(false, [new OptionsError("", "type", "validation.type", "The options must be a JSON object.")]);

        var (manifest, validator, _) = Entry(key);
        var result = validator.Validate(optionsJson, new JsonSchemaOptions { OutputFormat = OutputFormat.List, GenerateErrorMessages = true });
        if (result.IsValid) return OptionsValidationResult.Ok;

        var errors = new List<OptionsError>();
        foreach (var e in result.ValidationErrors)
        {
            var pointer = e.InstanceLocation.ToString();
            var keyword = e.Keyword ?? "";
            if (keyword == "required" && TryMissing(manifest.Options, e, instance, pointer, out var missing))
            {
                foreach (var name in missing) errors.Add(Make(pointer + "/" + name, "required", e.ErrorMessage));
                continue;
            }
            errors.Add(Make(pointer, keyword.Length == 0 && (e.ErrorMessage ?? "").StartsWith("Boolean false", StringComparison.Ordinal) ? "unknown-property" : Code(keyword), e.ErrorMessage));
        }
        return new OptionsValidationResult(false, errors.DistinctBy(x => (x.Pointer, x.Code)).ToList());
    }

    /// <summary>Validates, then turns the options JSON into the parameter dictionary the component renderer takes.</summary>
    public IDictionary<string, object?> ToParameters(string key, string optionsJson)
    {
        var check = Validate(key, optionsJson);
        if (!check.IsValid) throw new ArgumentException("The options are not valid for '" + key + "': " + string.Join("; ", check.Errors.Select(e => e.Pointer + " " + e.Code)), nameof(optionsJson));
        var (manifest, _, type) = Entry(key);
        var result = new Dictionary<string, object?>();
        var parameters = ComponentManifestGenerator.ParametersOf(type);
        if (manifest.OptionsParameter is { } name)
        {
            var p = parameters.First(x => x.Name == name);
            result[name] = JsonSerializer.Deserialize(optionsJson, p.PropertyType, PemsJson.Options);
            return result;
        }
        var obj = JsonNode.Parse(optionsJson)!.AsObject();
        foreach (var p in parameters)
            if (obj.TryGetPropertyValue(ComponentManifestGenerator.CamelCase(p.Name), out var v) && v is not null)
                result[p.Name] = JsonSerializer.Deserialize(v.ToJsonString(), p.PropertyType, PemsJson.Options);
        return result;
    }

    private static OptionsError Make(string pointer, string code, string? detail) => new(pointer, code, "validation." + code, detail ?? "");

    private static string Code(string keyword) => keyword switch
    {
        "additionalProperties" or "unevaluatedProperties" => "unknown-property",
        "minimum" or "exclusiveMinimum" => "min",
        "maximum" or "exclusiveMaximum" => "max",
        "minLength" => "min-length",
        "maxLength" => "max-length",
        "minItems" => "min-items",
        "maxItems" => "max-items",
        "oneOf" or "anyOf" => "shape",
        "" => "invalid",
        _ => keyword
    };

    /// <summary>For a failed "required" keyword, finds which property names are absent so each missing field gets its own pointer.</summary>
    private static bool TryMissing(JsonNode schema, ValidationError error, JsonNode instance, string instancePointer, out List<string> missing)
    {
        missing = [];
        try
        {
            var target = Navigate(instance, instancePointer);
            var node = Navigate(schema, error.RelativeKeywordLocation?.ToString() ?? "");
            if (target is JsonObject obj && node is JsonArray names)
            {
                foreach (var n in names) { var s = n?.GetValue<string>(); if (s is not null && !obj.ContainsKey(s)) missing.Add(s); }
                return missing.Count > 0;
            }
        }
        catch { /* fall back to the object pointer */ }
        return false;
    }

    private static JsonNode? Navigate(JsonNode root, string pointer)
    {
        var node = root;
        foreach (var raw in pointer.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var part = raw.Replace("~1", "/").Replace("~0", "~");
            node = node switch { JsonObject o => o[part], JsonArray a => a[int.Parse(part)], _ => null };
            if (node is null) return null;
        }
        return node;
    }
}
