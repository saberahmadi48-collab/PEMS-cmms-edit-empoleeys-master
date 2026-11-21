namespace PEMS.UI.Binding;

/// <summary>
/// Turns what a Persian or Arabic user may type into what the parsers understand: Persian (۰-۹) and Arabic-Indic (٠-٩) digits become
/// Latin digits, the Persian decimal mark (٫) becomes a dot and the Persian thousands mark (٬) is dropped. Input only; display formatting
/// is <c>IPemsLocale</c> (ADR-0008).
/// </summary>
public static class PemsDigits
{
    public static string ToLatin(string value)
    {
        var needsWork = false;
        foreach (var c in value)
            if (c is >= '٠' and <= '٩' or >= '۰' and <= '۹' or '٫' or '٬') { needsWork = true; break; }
        if (!needsWork) return value;

        var o = new System.Text.StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (c is >= '٠' and <= '٩') o.Append((char)('0' + (c - '٠')));
            else if (c is >= '۰' and <= '۹') o.Append((char)('0' + (c - '۰')));
            else if (c == '٫') o.Append('.');
            else if (c == '٬') continue;
            else o.Append(c);
        }
        return o.ToString();
    }
}
