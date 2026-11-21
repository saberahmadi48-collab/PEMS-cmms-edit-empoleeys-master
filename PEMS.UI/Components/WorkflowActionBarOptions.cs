using PEMS.UI.Abstractions;

namespace PEMS.UI.Components;

// SCRUM-58 (#58): the workflow action bar of a Details page: one comment box and one button per action the engine offers to this user. Denied actions are shown disabled with
// the reason as the tooltip, so the user sees why. The form posts actionType and comment (and the hidden fields, such as the record id) to the module's workflow action.

/// <summary>One action of the bar, already localized.</summary>
public sealed record WorkflowActionItem
{
    public required string ActionType { get; init; }
    public required string Label { get; init; }
    /// <summary>primary, success, danger, warning or secondary.</summary>
    public string Tone { get; init; } = "primary";
    public bool Allowed { get; init; } = true;
    public string? DeniedReason { get; init; }
    /// <summary>Asks before sending (reject, cancel).</summary>
    public string? Confirm { get; init; }
    /// <summary>The stage the action leads to, shown after the label.</summary>
    public string? Target { get; init; }
    /// <summary>The comment must be filled before the action is sent (reject, return, hold).</summary>
    public bool RequireComment { get; init; }
}

/// <summary>Inputs of <see cref="PemsWorkflowActionBar"/>.</summary>
public sealed record WorkflowActionBarOptions : FrameOptions
{
    public required string Action { get; init; }
    public AntiforgeryField? Antiforgery { get; init; }
    /// <summary>Hidden fields posted with the action (the record id).</summary>
    public IReadOnlyDictionary<string, string> Fields { get; init; } = new Dictionary<string, string>();
    public IReadOnlyList<WorkflowActionItem> Actions { get; init; } = [];
    public required TranslatableText CommentLabel { get; init; }
    /// <summary>The placeholder of the comment box (what is required and when).</summary>
    public TranslatableText? CommentHint { get; init; }
    /// <summary>Shown when the engine offers this user no action.</summary>
    public required TranslatableText None { get; init; }
    public string? Css { get; init; }
}
