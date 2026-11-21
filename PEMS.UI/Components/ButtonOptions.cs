using PEMS.UI.Abstractions;

namespace PEMS.UI.Components;

public enum Tone { Neutral, Primary, Danger, Success, Warning, Info, Light, Dark }

public enum Size { Small, Medium, Large }

/// <summary>Inputs of <see cref="PemsButton"/>. Immutable named properties (object initializer); callers use <c>with</c> to vary it. The label is an input, never a default in the component.</summary>
public sealed record ButtonOptions
{
    public required TranslatableText Label { get; init; }
    /// <summary>The colour family; null writes no colour class, for the few buttons whose class the page owns (btn-pems, btn-link) and passes in <see cref="CssClass"/>.</summary>
    public Tone? Tone { get; init; } = UI.Components.Tone.Primary;
    public Size Size { get; init; } = Size.Medium;
    public bool Disabled { get; init; }
    public string Type { get; init; } = "button";
    public string? Href { get; init; }
    /// <summary>Id of a modal, drawer or confirm dialog this button opens (pems-overlay.js). The row key is copied into the dialog.</summary>
    public string? Opens { get; init; }
    public string? RowKey { get; init; }
    /// <summary>Bootstrap skin only: the outlined form (<c>btn-outline-*</c>) instead of the solid one.</summary>
    public bool Outline { get; init; }
    /// <summary>Extra classes the page needs on the element (layout helpers such as <c>w-100</c>); not a way to restyle the component.</summary>
    public string? CssClass { get; init; }
    /// <summary>Bootstrap (default) writes today's markup; Pems writes the library's own classes.</summary>
    public ComponentSkin Skin { get; init; } = ComponentSkin.Bootstrap;
}
