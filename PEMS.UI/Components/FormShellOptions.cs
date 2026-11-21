using PEMS.UI.Abstractions;

namespace PEMS.UI.Components;

/// <summary>Inputs of <see cref="PemsFormSection"/>: a titled group of fields inside a form. Values and error texts come from the form state.</summary>
public sealed record FormSectionOptions
{
    public required TranslatableText Title { get; init; }
    public TranslatableText? Description { get; init; }
    public required IReadOnlyList<FieldDescriptor> Fields { get; init; }
}

/// <summary>Inputs of <see cref="PemsActionBar"/>: the buttons at the end of a form or page. Sticky keeps the bar visible at the bottom of the window.</summary>
public sealed record ActionBarOptions : FrameOptions
{
    public IReadOnlyList<ButtonOptions> Actions { get; init; } = [];
    public bool Sticky { get; init; }
    /// <summary>The page's own buttons (child content) in a plain <c>div</c> that carries only <see cref="Css"/> (SCRUM-488: <c>&lt;pems-action-bar css="mt-4"&gt;</c>); the descriptor bar keeps its classes.</summary>
    public bool Plain { get; init; }
    public string? Css { get; init; }
}
