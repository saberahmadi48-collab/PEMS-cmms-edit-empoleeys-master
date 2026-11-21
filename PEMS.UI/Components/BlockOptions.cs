using PEMS.UI.Abstractions;

namespace PEMS.UI.Components;

/// <summary>Inputs of <see cref="PemsCard"/>: the Bootstrap card box; the page keeps the card body markup.</summary>
public sealed record CardOptions
{
    /// <summary>Extra classes the page needs on the card (shadow-sm, border-0, h-100, spacing helpers).</summary>
    public string? CssClass { get; init; }
}

/// <summary>Inputs of <see cref="PemsProgress"/>: a bar filled to Value of Max. Text between the tag helper tags is shown inside the bar.</summary>
public sealed record ProgressOptions
{
    public double Value { get; init; }
    public double Max { get; init; } = 100;
    /// <summary>The bar colour (success, danger, warning, info, primary...); null keeps the default Bootstrap bar.</summary>
    public Tone? Tone { get; init; }
    /// <summary>CSS height of the track, e.g. "8px".</summary>
    public string? Height { get; init; }
    public string? CssClass { get; init; }
    /// <summary>Extra inline style of the track (width, min-width) for layouts the classes do not cover.</summary>
    public string? Style { get; init; }
    public TranslatableText? Label { get; init; }
    public string? Title { get; init; }
    /// <summary>More bars in the same track, after the main bar (a stacked bar: present, unknown, missing). Each segment is a share of Max.</summary>
    public IReadOnlyList<ProgressSegment>? Segments { get; init; }
}

/// <summary>One extra bar of a stacked <see cref="PemsProgress"/>.</summary>
public sealed record ProgressSegment(double Value, Tone? Tone);

/// <summary>Inputs of <see cref="PemsAccordion"/>: the container whose items collapse one at a time.</summary>
public sealed record AccordionOptions
{
    public required string Id { get; init; }
    public string? CssClass { get; init; }
}

/// <summary>Inputs of <see cref="PemsAccordionItem"/>: one collapsible item; Target is the id of the collapse panel, ParentId the accordion id.</summary>
public sealed record AccordionItemOptions
{
    public required string Target { get; init; }
    public string? ParentId { get; init; }
    public string? ButtonCssClass { get; init; }
    public string? BodyCssClass { get; init; }
}

/// <summary>Inputs of <see cref="PemsListGroup"/>: the list-group box; the items stay page markup (links with their routing).</summary>
public sealed record ListGroupOptions
{
    public bool Flush { get; init; }
    public string? CssClass { get; init; }
}

/// <summary>Inputs of <see cref="PemsSpinner"/>: an inline busy indicator.</summary>
public sealed record SpinnerOptions
{
    public bool Grow { get; init; }
    public Size Size { get; init; } = Size.Medium;
    public Tone? Tone { get; init; }
    /// <summary>Extra classes (for example <c>d-none</c> for a spinner a script shows while it works).</summary>
    public string? CssClass { get; init; }
    /// <summary>The other attributes of the element (id, role, aria-label), written after the class; a page role replaces the default.</summary>
    public IReadOnlyDictionary<string, object>? Attributes { get; init; }
}

/// <summary>Inputs of <see cref="PemsInfoGrid"/>: the label / value description list; the items are <see cref="PemsInfoItem"/>s in its child content.</summary>
public sealed record InfoGridOptions
{
    /// <summary>"rows" puts each label beside its value; anything else stacks the label over the value.</summary>
    public string? Layout { get; init; }
    /// <summary>The page's own classes, kept after the component's.</summary>
    public string? CssClass { get; init; }
    /// <summary>Other attributes the page wrote on the tag (id, data-*).</summary>
    public IReadOnlyDictionary<string, object>? Attributes { get; init; }
}

/// <summary>Inputs of <see cref="PemsInfoItem"/>: one label and its value (child content; empty shows <see cref="EmptyText"/>).</summary>
public sealed record InfoItemOptions
{
    public required string Label { get; init; }
    public bool Wide { get; init; }
    public bool Num { get; init; }
    public bool Mono { get; init; }
    public bool Ltr { get; init; }
    /// <summary>danger, warning or success colours the value.</summary>
    public string? Tone { get; init; }
    public string EmptyText { get; init; } = "-";
    public string? ValueId { get; init; }
    public string? ValueClass { get; init; }
    public IReadOnlyDictionary<string, object>? Attributes { get; init; }
}

/// <summary>Inputs of <see cref="PemsKpiTile"/>: one number (child content, a dash when empty) with its label, an optional icon square, sub line, link and hint.</summary>
public sealed record KpiTileOptions
{
    public required PEMS.UI.Abstractions.TranslatableText Label { get; init; }
    /// <summary>Bootstrap icon class, e.g. bi-tools; null shows no icon square.</summary>
    public string? Icon { get; init; }
    /// <summary>The icon square colour class, already resolved (bg-icon-green).</summary>
    public string IconClass { get; init; } = "bg-icon-blue";
    public string? Sub { get; init; }
    public string? Href { get; init; }
    public string? Hint { get; init; }
    /// <summary>The number as text when the page writes no child content (a stored page sets it from a data source row, SCRUM-601).</summary>
    public string? Value { get; init; }
    public bool Compact { get; init; }
    /// <summary>danger, success or warning colours the value.</summary>
    public string? Tone { get; init; }
    public string? CssClass { get; init; }
    public IReadOnlyDictionary<string, object>? Attributes { get; init; }
}

/// <summary>The page scripts whose markup <see cref="PemsScriptTemplates"/> serves as inert templates (SCRUM-508 item 6).</summary>
public enum ScriptTemplateSet
{
    DccRegister,
    LetterCompose,
    GlobalSearch,
    OrgChart,
    OrgPositionChart,
    AccountLogin,
    ManagePasskeys,
    ProjectIntelligence,
    HelpArticle,
    HelpDrawer,
    Layout,
    CartableSettings,
    PermissionMatrix,
    UserPermissions,
    Shell,
    MaintenanceCalendar,
    ChatAssistant,
    PartCatalogue,
}

/// <summary>Inputs of <see cref="PemsScriptTemplates"/>: which script's templates to render into the page.</summary>
public sealed record ScriptTemplatesOptions
{
    public ScriptTemplateSet Set { get; init; }
}
