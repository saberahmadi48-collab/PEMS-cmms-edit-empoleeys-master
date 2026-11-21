using PEMS.UI.Abstractions;

namespace PEMS.UI.Components;

// SCRUM-437 (#437): a button bound to a stored action definition. It is a small form that posts the values to /a/{key} (the action host runs the action definition with the rights of the user), so a page
// definition can place a button that does something with nothing but the key of the action.

/// <summary>Inputs of <see cref="PemsActionButton"/>.</summary>
public sealed record ActionButtonOptions : FrameOptions
{
    /// <summary>The key of the action definition.</summary>
    public required string Action { get; init; }
    public required TranslatableText Label { get; init; }
    /// <summary>primary, success, danger, warning or secondary.</summary>
    public string Tone { get; init; } = "primary";
    public string? Icon { get; init; }
    /// <summary>The question asked before the action runs (pems-confirm.js); none = no question.</summary>
    public TranslatableText? Confirm { get; init; }
    /// <summary>The values the action receives (the record id).</summary>
    public IReadOnlyDictionary<string, string> Values { get; init; } = new Dictionary<string, string>();
    /// <summary>A path of this site to return to when the action names no redirect of its own.</summary>
    public string? ReturnUrl { get; init; }
    public AntiforgeryField? Antiforgery { get; init; }
    public string? Css { get; init; }
}
