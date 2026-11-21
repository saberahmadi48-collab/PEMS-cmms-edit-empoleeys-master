using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using PEMS.Domain.Entities;

namespace PEMS.Application.Numbering;

/// <summary>Values that a pattern can place into a number (project and discipline codes of the document).</summary>
public sealed record NumberingContext(string? ProjectCode = null, string? DisciplineCode = null)
{
    public static readonly NumberingContext Empty = new();
}

/// <summary>
/// SCRUM-40: the pattern language of <see cref="NumberingScheme"/>. Pure functions (no database) so they can be unit tested:
/// validation, the counter scope (which numbers share one counter) and formatting.
/// Tokens: {YYYY} {YY} (year in the scheme's calendar), {PROJECT}, {DISC}, {SEQ} (the counter, padded).
/// </summary>
public static partial class NumberingPattern
{
    public const string Year4 = "{YYYY}", Year2 = "{YY}", Project = "{PROJECT}", Discipline = "{DISC}", Seq = "{SEQ}";
    public static readonly IReadOnlyList<string> Tokens = new[] { Year4, Year2, Project, Discipline, Seq };

    public const int MaxPatternLength = 60;
    public const int MinPad = 1, MaxPad = 8;

    [GeneratedRegex(@"\{[^}]*\}")]
    private static partial Regex TokenRegex();

    // literal text: letters, digits and the usual separators; no spaces, no slashes in numbers used in URLs and file names
    [GeneratedRegex(@"^[A-Za-z0-9\-_.]*$")]
    private static partial Regex LiteralRegex();

    /// <summary>Returns a resource key describing the first problem, or null when the scheme is valid.</summary>
    public static string? Validate(string? pattern, int padWidth, string? calendar, string? resetPeriod)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return "Num_Err_PatternRequired";
        if (pattern.Length > MaxPatternLength) return "Num_Err_PatternTooLong";
        var tokens = TokenRegex().Matches(pattern).Select(m => m.Value).ToList();
        if (tokens.Any(t => !Tokens.Contains(t))) return "Num_Err_UnknownToken";
        if (tokens.Count(t => t == Seq) != 1) return "Num_Err_SeqOnce";
        if (!LiteralRegex().IsMatch(TokenRegex().Replace(pattern, ""))) return "Num_Err_Literal";
        if (padWidth < MinPad || padWidth > MaxPad) return "Num_Err_Pad";
        if (calendar == null || !NumberingCalendars.All.Contains(calendar)) return "Num_Err_Calendar";
        if (resetPeriod == null || !NumberingResetPeriods.All.Contains(resetPeriod)) return "Num_Err_Reset";
        if (resetPeriod == NumberingResetPeriods.Yearly && !pattern.Contains(Year4) && !pattern.Contains(Year2))
            return "Num_Err_YearlyNeedsYear";   // otherwise two years would print the same numbers
        return null;
    }

    public static int Year(DateTime when, string calendar) =>
        calendar == NumberingCalendars.Jalali ? new PersianCalendar().GetYear(when) : when.Year;

    /// <summary>
    /// The counter scope: numbers that differ only in {SEQ} share one counter. A yearly reset adds the year; {PROJECT}/{DISC}
    /// add their codes. Prefixed with "S|" so it never meets the scopes of the built-in formats in NumberingSequences.
    /// </summary>
    public static string ScopeKey(NumberingScheme scheme, DateTime when, NumberingContext ctx)
    {
        var sb = new StringBuilder("S|");
        if (scheme.ResetPeriod == NumberingResetPeriods.Yearly) sb.Append(Year(when, scheme.Calendar).ToString(CultureInfo.InvariantCulture));
        sb.Append('|');
        if (scheme.Pattern.Contains(Project)) sb.Append(Code(ctx.ProjectCode));
        sb.Append('|');
        if (scheme.Pattern.Contains(Discipline)) sb.Append(Code(ctx.DisciplineCode));
        var key = sb.ToString();
        return key.Length <= 100 ? key : key[..100];
    }

    public static string Format(NumberingScheme scheme, DateTime when, NumberingContext ctx, int value)
    {
        var year = Year(when, scheme.Calendar).ToString(CultureInfo.InvariantCulture);
        return scheme.Pattern
            .Replace(Year4, year)
            .Replace(Year2, year[^2..])
            .Replace(Project, Code(ctx.ProjectCode))
            .Replace(Discipline, Code(ctx.DisciplineCode))
            .Replace(Seq, value.ToString(CultureInfo.InvariantCulture).PadLeft(scheme.PadWidth, '0'));
    }

    /// <summary>The shape of a number before the codes are known: the year filled in, codes left as tokens, the counter as #.</summary>
    public static string Shape(NumberingScheme scheme, DateTime when)
    {
        var year = Year(when, scheme.Calendar).ToString(CultureInfo.InvariantCulture);
        return scheme.Pattern.Replace(Year4, year).Replace(Year2, year[^2..]).Replace(Seq, new string('#', scheme.PadWidth));
    }

    /// <summary>Codes are upper-cased and stripped to the literal alphabet; a missing code prints "X" so the number stays readable.</summary>
    public static string Code(string? code)
    {
        var cleaned = new string((code ?? "").Trim().ToUpperInvariant().Where(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.').ToArray());
        return cleaned.Length == 0 ? "X" : cleaned.Length > 20 ? cleaned[..20] : cleaned;
    }
}
