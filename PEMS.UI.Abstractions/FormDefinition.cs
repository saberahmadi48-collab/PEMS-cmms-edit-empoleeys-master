namespace PEMS.UI.Abstractions;

/// <summary>
/// Describes a whole form. <see cref="Action"/> is the post target; values and validation messages are state and travel separately
/// (see <see cref="FormState"/>), so the definition stays immutable and cacheable. Later one database row holds one definition.
/// </summary>
public sealed record FormDefinition
{
    public required string Id { get; init; }
    public required TranslatableText Title { get; init; }
    /// <summary>The post target of a code-defined form; a stored form leaves it out (the form host posts to itself and runs <see cref="ActionKey"/>).</summary>
    public string Action { get; init; } = "";
    /// <summary>The key of the action definition the valid values are handed to (SCRUM-437).</summary>
    public string? ActionKey { get; init; }
    /// <summary>"Module.Action" the user needs to open the form (checked by the pipeline).</summary>
    public string? Permission { get; init; }
    /// <summary>Columns of the form grid (1 or 2); a field spans <see cref="FieldDescriptor.Span"/> of them.</summary>
    public int Columns { get; init; } = 1;
    /// <summary>Rules across fields (the end date after the start date); each names the field that shows the message.</summary>
    public IReadOnlyList<FieldRule>? Rules { get; init; }
    public required IReadOnlyList<FieldDescriptor> Fields { get; init; }
    public required TranslatableText SubmitLabel { get; init; }
    /// <summary>Ask the browser to confirm leaving the page after a value changed (pems-form.js).</summary>
    public bool WarnOnLeave { get; init; }
}

/// <summary>Per-request form state: current values (data) and validation error texts by field name.</summary>
public sealed record FormState
{
    public IReadOnlyDictionary<string, string?> Values { get; init; } = new Dictionary<string, string?>();
    public IReadOnlyDictionary<string, TranslatableText> Errors { get; init; } = new Dictionary<string, TranslatableText>();

    /// <summary>Names of the fields whose condition is false for the current values: they are rendered hidden (the browser shows them again when the values change).</summary>
    public IReadOnlyCollection<string> Hidden { get; init; } = [];
    /// <summary>Errors of the form as a whole (a rule across fields, a refused action).</summary>
    public IReadOnlyList<TranslatableText> FormErrors { get; init; } = [];

    public static FormState Empty { get; } = new();

    public string? ValueOf(string name) => Values.TryGetValue(name, out var v) ? v : null;
    public TranslatableText? ErrorOf(string name) => Errors.TryGetValue(name, out var e) ? e : null;
}
