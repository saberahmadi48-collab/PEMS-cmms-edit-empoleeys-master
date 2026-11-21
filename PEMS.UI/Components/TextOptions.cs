namespace PEMS.UI.Components;

// SCRUM-522: the text elements of a page (heading, paragraph, link, bullet list, divider) as registered components. They write the element with the page's classes
// and attributes unchanged; the point is that no raw h1-h6, p, a, ul/ol/li or hr is left in a view (a ratchet counter per element watches it).

/// <summary>Inputs of <see cref="PemsHeading"/>: <c>h1</c> to <c>h6</c>.</summary>
public sealed record HeadingOptions : FrameOptions
{
    /// <summary>1 to 6.</summary>
    public int Level { get; init; } = 2;
    public string? Css { get; init; }
}

/// <summary>Inputs of <see cref="PemsText"/>: a paragraph (<c>p</c>).</summary>
public sealed record TextBlockOptions : FrameOptions
{
    public string? Css { get; init; }
}

/// <summary>Inputs of <see cref="PemsLink"/>: an anchor; the href comes from the page (<c>href</c>, <c>asp-action</c> ...).</summary>
public sealed record LinkOptions : FrameOptions
{
    public string? Css { get; init; }
}

/// <summary>Inputs of <see cref="PemsBulletList"/>: <c>ul</c>, or <c>ol</c> when <see cref="Ordered"/>.</summary>
public sealed record BulletListOptions : FrameOptions
{
    public bool Ordered { get; init; }
    public string? Css { get; init; }
}

/// <summary>Inputs of <see cref="PemsBullet"/>: one <c>li</c>.</summary>
public sealed record BulletOptions : FrameOptions
{
    public string? Css { get; init; }
}

/// <summary>Inputs of <see cref="PemsDivider"/>: <c>hr</c>.</summary>
public sealed record DividerOptions : FrameOptions
{
    public string? Css { get; init; }
}

// SCRUM-486: the stage diagram (the graphic cycle of a record: circles joined by connectors) as one registered component. The page decides every state (done, current,
// pending, rejected); the component only draws. "wf" is the workflow diagram (icon, label and role), "wo" the smaller work-order diagram (no role line, no rejected state).

/// <summary>The state of one stage.</summary>
public enum StageState { Pending, Current, Done, Rejected }

/// <summary>One stage of a <see cref="PemsStageDiagram"/>: the text is already localized by the page.</summary>
public sealed record StageStep(string Label, string Icon, StageState State, string? Role = null);

/// <summary>Inputs of <see cref="PemsStageDiagram"/>. Variants: wf (default), wo and vs (the tab tracker of the VDR details: dots with label and meta line).</summary>
public sealed record StageDiagramOptions
{
    /// <summary>wf (default), wo or vs.</summary>
    public string Variant { get; init; } = "wf";
    public IReadOnlyList<StageStep>? Steps { get; init; }
}

/// <summary>Inputs of <see cref="PemsSideLink"/>: one entry of a side panel (icon, label, optional count). <see cref="Look"/> cartable (default, the mailbox rail <c>ct-folder</c>) or pems (the <c>pems-sidenav</c> item).</summary>
public sealed record SideLinkOptions : FrameOptions
{
    /// <summary>cartable (default) or pems.</summary>
    public string Look { get; init; } = "cartable";
    public bool Active { get; init; }
    /// <summary>The icon classes (<c>bi-inbox</c>); empty writes no icon.</summary>
    public string? Icon { get; init; }
    /// <summary>The count shown after the label when greater than 0.</summary>
    public int? Count { get; init; }
    /// <summary>Extra classes on the count (<c>ct-count--alert</c>).</summary>
    public string? Hot { get; init; }
}
