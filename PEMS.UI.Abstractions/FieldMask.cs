namespace PEMS.UI.Abstractions;

/// <summary>The mask of a field (SCRUM-386): the same rule shapes the text in the browser (pems-mask.js) and cleans the value on the server, so what is stored never carries separators.</summary>
public static class FieldMask
{
    /// <summary>The value as stored: Persian and Arabic digits become Latin, then only letters and digits are kept.</summary>
    public static string Canonical(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value ?? "";
        var s = value.Normalize(System.Text.NormalizationForm.FormKC);
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (c is >= '\u06F0' and <= '\u06F9') sb.Append((char)('0' + (c - '\u06F0')));
            else if (c is >= '\u0660' and <= '\u0669') sb.Append((char)('0' + (c - '\u0660')));
            else if (char.IsLetterOrDigit(c)) sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>The value shown in the mask: slots are filled in order, a character that does not fit its slot is skipped, separators are written between filled slots.</summary>
    public static string Format(string? canonical, string mask)
    {
        var raw = Canonical(canonical);
        var sb = new System.Text.StringBuilder();
        var i = 0;
        foreach (var m in mask)
        {
            if (i >= raw.Length) break;
            if (m is '9' or 'A' or '*')
            {
                while (i < raw.Length && !Fits(raw[i], m)) i++;
                if (i >= raw.Length) break;
                sb.Append(raw[i++]);
            }
            else sb.Append(m);
        }
        return sb.ToString().TrimEnd(mask.Where(c => c is not ('9' or 'A' or '*')).Distinct().ToArray());
    }

    public static bool IsAllDigits(string mask) => mask.Length > 0 && mask.All(c => c == '9' || !char.IsLetterOrDigit(c) && c != 'A' && c != '*');

    private static bool Fits(char c, char slot) => slot switch { '9' => c is >= '0' and <= '9', 'A' => char.IsLetter(c), _ => char.IsLetterOrDigit(c) };
}
