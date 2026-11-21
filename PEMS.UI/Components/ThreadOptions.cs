using PEMS.UI.Abstractions;

namespace PEMS.UI.Components;

// SCRUM-339 (#325): the two record-level page parts every Details page repeats: the comments thread (replies, a status per comment, an action per comment such as "answer") and the audit trail (who changed
// which field, from what to what, when). Both only draw: the page, a hydrator or the workflow mapper gives them already localized entries; the data of comments and audit stays in its own module.

/// <summary>One comment of a thread; <see cref="Replies"/> are drawn under it, one level deep.</summary>
public sealed record ThreadComment
{
    public required string Author { get; init; }
    public required string Text { get; init; }
    public DateTime When { get; init; }
    /// <summary>A short status badge text ("Open", "Answered"), localized.</summary>
    public string? Status { get; init; }
    /// <summary>success, danger, warning, info or neutral: the colour of the status.</summary>
    public string StatusTone { get; init; } = "neutral";
    /// <summary>The action of this comment (an answer form, a link); the page decides what it does, the thread writes a link to it.</summary>
    public string? ActionLabel { get; init; }
    public string? ActionUrl { get; init; }
    public IReadOnlyList<ThreadComment> Replies { get; init; } = [];
}

/// <summary>Inputs of <see cref="PemsCommentsThread"/>.</summary>
public sealed record CommentsThreadOptions : FrameOptions
{
    public string? Id { get; init; }
    public IReadOnlyList<ThreadComment> Comments { get; init; } = [];
    public required TranslatableText Empty { get; init; }
    public string? Css { get; init; }
}

/// <summary>One change of the audit trail: the field (or action), the old and the new value, who and when.</summary>
public sealed record AuditEntry
{
    public required string Field { get; init; }
    public string? Old { get; init; }
    public string? New { get; init; }
    public required string Actor { get; init; }
    public DateTime When { get; init; }
}

/// <summary>Inputs of <see cref="PemsAuditTrail"/>.</summary>
public sealed record AuditTrailOptions : FrameOptions
{
    public string? Id { get; init; }
    public IReadOnlyList<AuditEntry> Entries { get; init; } = [];
    public required TranslatableText Empty { get; init; }
    /// <summary>The column headings and the text for an empty value, in the language of the request.</summary>
    public required AuditTrailTexts Texts { get; init; }
    public string? Css { get; init; }
}

public sealed record AuditTrailTexts
{
    public required TranslatableText When { get; init; }
    public required TranslatableText Actor { get; init; }
    public required TranslatableText Field { get; init; }
    public required TranslatableText Change { get; init; }
    public required TranslatableText EmptyValue { get; init; }
}
