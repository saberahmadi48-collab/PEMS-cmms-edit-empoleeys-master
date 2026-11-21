using PEMS.UI.Abstractions;

namespace PEMS.UI.Components;

// SCRUM-58 (#58): the history of a workflow instance as a timeline (newest first). The page (or the workflow mapper) gives the entries already
// localized; the component only draws, so every module shows approvals, returns and comments the same way.

/// <summary>One step of the history: what happened, who did it, when, and the comment.</summary>
public sealed record WorkflowHistoryEntry
{
    public required string Title { get; init; }
    /// <summary>The route of the step ("Review → Approval"), shown under the title.</summary>
    public string? Detail { get; init; }
    public string? Actor { get; init; }
    public string? Comment { get; init; }
    public DateTime When { get; init; }
    /// <summary>success, danger, warning, info or neutral: the colour of the dot.</summary>
    public string Tone { get; init; } = "neutral";
}

/// <summary>Inputs of <see cref="PemsWorkflowHistory"/>.</summary>
public sealed record WorkflowHistoryOptions : FrameOptions
{
    public IReadOnlyList<WorkflowHistoryEntry> Entries { get; init; } = [];
    public string? Id { get; init; }
    public required TranslatableText Empty { get; init; }
    public string? Css { get; init; }
}
