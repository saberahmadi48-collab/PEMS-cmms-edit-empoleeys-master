using PEMS.UI.Abstractions;

namespace PEMS.UI.Components;

public sealed record BadgeOptions
{
    public required TranslatableText Label { get; init; }
    /// <summary>The colour family; null writes no colour class, for pages whose status helper already returns the classes (passed in <see cref="CssClass"/>).</summary>
    public Tone? Tone { get; init; } = UI.Components.Tone.Neutral;
    /// <summary>Extra classes the page needs on the element (spacing helpers such as <c>ms-1</c>).</summary>
    public string? CssClass { get; init; }
    /// <summary>Bootstrap (default) writes today's markup; Pems writes the library's own classes.</summary>
    public ComponentSkin Skin { get; init; } = ComponentSkin.Bootstrap;
    /// <summary>Other attributes of the element (title, style, data-*), written after the class.</summary>
    public IReadOnlyDictionary<string, object>? Attributes { get; init; }
}
