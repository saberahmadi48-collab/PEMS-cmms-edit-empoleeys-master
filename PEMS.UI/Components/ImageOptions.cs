using PEMS.UI.Abstractions;

namespace PEMS.UI.Components;

// SCRUM-729: a picture of this site (a profile photo, a stored signature). The address can come from a record binding (<c>map</c>), so only a path of this site is written; nothing else becomes an img.

/// <summary>Inputs of <see cref="PemsImage"/>.</summary>
public sealed record ImageOptions : FrameOptions
{
    /// <summary>A path of this site (starts with one slash). Empty or anything else writes nothing.</summary>
    public string? Src { get; init; }
    public TranslatableText? Alt { get; init; }
    public string? Css { get; init; }
    /// <summary>The text shown when there is no picture.</summary>
    public TranslatableText? Empty { get; init; }
    public int? Size { get; init; }
}
