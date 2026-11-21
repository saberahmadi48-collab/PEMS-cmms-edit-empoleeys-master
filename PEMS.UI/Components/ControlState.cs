namespace PEMS.UI.Components;

/// <summary>Which control the page draws: a text-like input, a select list or a check box / radio / switch input.</summary>
public enum ControlKind { Text, Select, Check }

/// <summary>Inputs of the bare form controls (<c>pems-input</c>, <c>pems-select</c>, <c>pems-textarea</c>). The control only: the label, the layout and the error text stay in the page.</summary>
public sealed record ControlOptions
{
    public ControlKind Kind { get; init; } = ControlKind.Text;
    public Size Size { get; init; } = Size.Medium;
    /// <summary>Extra classes the page needs on the control (layout helpers, a script hook such as <c>pems-employee-search</c>).</summary>
    public string? CssClass { get; init; }
}

/// <summary>The Bootstrap classes of a bare form control, computed from the inputs. Plain C#, unit-testable without rendering.</summary>
public static class ControlState
{
    public static string Css(ControlOptions o)
    {
        var css = o.Kind switch
        {
            ControlKind.Select => "form-select" + (o.Size == Size.Small ? " form-select-sm" : o.Size == Size.Large ? " form-select-lg" : ""),
            ControlKind.Check => "form-check-input",
            _ => "form-control" + (o.Size == Size.Small ? " form-control-sm" : o.Size == Size.Large ? " form-control-lg" : "")
        };
        return string.IsNullOrWhiteSpace(o.CssClass) ? css : css + " " + o.CssClass.Trim();
    }

    /// <summary>The class of a form label: <c>form-label</c> plus what the page needs (<c>small</c>, <c>fw-bold</c>, <c>mb-0</c>).</summary>
    public static string LabelCss(string? cssClass) => string.IsNullOrWhiteSpace(cssClass) ? "form-label" : "form-label " + cssClass.Trim();
}
