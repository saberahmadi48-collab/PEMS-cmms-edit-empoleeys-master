using System.Text.Json.Nodes;

namespace PEMS.UI.Manifest;

/// <summary>One component parameter. <see cref="Kind"/> is "option" (data), "slot" (RenderFragment) or "event" (EventCallback).</summary>
public sealed record ManifestParameter(string Name, string Type, string Kind, bool Required);

/// <summary>
/// Machine-readable description of a component, generated from the code (never written by hand): what a designer needs to offer the
/// component, edit its options and validate stored JSON. <see cref="Options"/> is the JSON Schema of an instance's options object.
/// </summary>
public sealed record ComponentManifest
{
    public required string Key { get; init; }
    public required string Component { get; init; }
    public string Category { get; init; } = "general";
    public string Icon { get; init; } = "";
    /// <summary>Message key of the palette name; the translations live in the message catalog, not in the component.</summary>
    public required string NameKey { get; init; }
    /// <summary>Name of the single <c>Options</c> parameter whose type IS the instance options JSON, or null when the options object holds the parameters.</summary>
    public string? OptionsParameter { get; init; }
    public int ManifestVersion { get; init; } = 1;
    public required JsonNode Options { get; init; }
    public IReadOnlyList<ManifestParameter> Parameters { get; init; } = [];
    public JsonNode? Sample { get; init; }
}
