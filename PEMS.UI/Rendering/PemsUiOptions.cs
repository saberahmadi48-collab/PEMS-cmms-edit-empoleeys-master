namespace PEMS.UI.Rendering;

public enum CalendarMode
{
    /// <summary>Persian (Jalali) for Persian cultures, Gregorian otherwise.</summary>
    Auto,
    Gregorian,
    Persian
}

/// <summary>
/// Defaults of the locale service. The host sets them (PEMS.Web will read them from the env files when the first page uses a component,
/// SCRUM-418); nothing here is read from a file by the library itself.
/// </summary>
public sealed class PemsUiOptions
{
    /// <summary>The language used when a text has none for the current culture. The product default is Persian (Instructions rule 5).</summary>
    public string DefaultLanguage { get; set; } = "fa";

    public CalendarMode Calendar { get; set; } = CalendarMode.Auto;

    /// <summary>Show Persian digits (۱۲۳) for Persian cultures. On by default (owner decision 2026-10-27, ADR-0008); other cultures keep Latin digits.</summary>
    public bool UseNativeDigits { get; set; } = true;
}
