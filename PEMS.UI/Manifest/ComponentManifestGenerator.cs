using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Components;
using PEMS.UI.Abstractions;
using PEMS.UI.Rendering;

namespace PEMS.UI.Manifest;

/// <summary>Builds <see cref="ComponentManifest"/> from a component type: attribute metadata plus the JSON Schema of its parameters (System.Text.Json schema exporter, no extra library).</summary>
public static class ComponentManifestGenerator
{
    private static readonly JsonSerializerOptions SchemaOptions = Create();

    private static JsonSerializerOptions Create()
    {
        var o = new JsonSerializerOptions(PemsJson.Options) { UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };
        o.TypeInfoResolver = new DefaultJsonTypeInfoResolver();
        o.MakeReadOnly();
        return o;
    }

    private static readonly JsonNode TextSchema = JsonNode.Parse("""{"oneOf":[{"type":"string"},{"type":"object","minProperties":1,"additionalProperties":{"type":"string"}}]}""")!;

    public static string CamelCase(string name) => JsonNamingPolicy.CamelCase.ConvertName(name);

    public static JsonNode SchemaOf(Type type) => JsonSchemaExporter.GetJsonSchemaAsNode(SchemaOptions, type, new JsonSchemaExporterOptions
    {
        TreatNullObliviousAsNonNullable = true,
        TransformSchemaNode = (ctx, node) => ctx.TypeInfo.Type == typeof(TranslatableText) ? TextSchema.DeepClone() : node
    });

    public static IReadOnlyList<PropertyInfo> ParametersOf(Type component) =>
        component.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetCustomAttribute<ParameterAttribute>() is not null).OrderBy(p => p.Name, StringComparer.Ordinal).ToList();

    public static ComponentManifest Generate(string key, Type component)
    {
        var attr = component.GetCustomAttribute<PemsComponentAttribute>()
            ?? throw new InvalidOperationException($"{component.FullName} has no [PemsComponent] attribute, so it has no manifest.");
        if (!string.Equals(attr.Key, key, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"{component.FullName} is registered as '{key}' but its attribute says '{attr.Key}'.");

        var all = ParametersOf(component);
        var parameters = all.Select(p => new ManifestParameter(CamelCase(p.Name), p.PropertyType.Name, KindOf(p.PropertyType), p.GetCustomAttribute<EditorRequiredAttribute>() is not null)).ToList();
        var data = all.Where(p => KindOf(p.PropertyType) == "option" && p.GetCustomAttribute<PemsHostSetAttribute>() is null).ToList();

        // One required parameter named Options: the instance options JSON is that record itself (the common case: button, badge, states).
        string? optionsParameter = null;
        JsonNode schema;
        if (data.Count == 1 && data[0].Name == "Options")
        {
            optionsParameter = "Options";
            schema = SchemaOf(data[0].PropertyType);
        }
        else
        {
            var props = new JsonObject();
            var required = new JsonArray();
            foreach (var p in data)
            {
                props[CamelCase(p.Name)] = SchemaOf(p.PropertyType);
                if (p.GetCustomAttribute<EditorRequiredAttribute>() is not null) required.Add(CamelCase(p.Name));
            }
            var obj = new JsonObject { ["type"] = "object", ["properties"] = props, ["additionalProperties"] = false };
            if (required.Count > 0) obj["required"] = required;
            schema = obj;
        }

        return new ComponentManifest
        {
            Key = attr.Key,
            Component = component.FullName ?? component.Name,
            Category = attr.Category,
            Icon = attr.Icon,
            NameKey = "component." + attr.Key,
            OptionsParameter = optionsParameter,
            Options = schema,
            Parameters = parameters,
            Sample = ComponentSamples.Get(attr.Key)
        };
    }

    private static string KindOf(Type t) =>
        t == typeof(RenderFragment) || (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(RenderFragment<>)) ? "slot" :
        t == typeof(EventCallback) || (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(EventCallback<>)) ? "event" : "option";
}
