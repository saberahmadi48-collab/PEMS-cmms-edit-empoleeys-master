namespace PEMS.UI.Components;

/// <summary>Everything the button markup prints, computed from the inputs. Plain C#, unit-testable without rendering.</summary>
public sealed record ButtonState(string Css, bool AsLink)
{
    public static ButtonState From(ButtonOptions o) => new(
        o.Skin == ComponentSkin.Bootstrap ? BootstrapCss(o) : With($"pems-btn{(o.Tone is { } t ? $" pems-btn--{t.ToString().ToLowerInvariant()}" : "")} pems-btn--{o.Size.ToString().ToLowerInvariant()}", o.CssClass),
        o.Href is not null && !o.Disabled);

    private static string BootstrapCss(ButtonOptions o)
    {
        var size = o.Size switch { Size.Small => " btn-sm", Size.Large => " btn-lg", _ => "" };
        if (o.Tone is not { } tone) return With($"btn{size}", o.CssClass);
        var name = tone switch { Tone.Neutral => "secondary", Tone.Primary when !o.Outline => "pems-solid", Tone.Primary => "primary", _ => tone.ToString().ToLowerInvariant() };
        return With($"btn{size} btn-{(o.Outline ? "outline-" : "")}{name}", o.CssClass);
    }

    private static string With(string css, string? extra) => string.IsNullOrWhiteSpace(extra) ? css : css + " " + extra.Trim();
}
