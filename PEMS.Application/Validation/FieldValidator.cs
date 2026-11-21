using System.Globalization;
using System.Text.RegularExpressions;

namespace PEMS.Application.Validation;

// SCRUM-372 (the catalog), SCRUM-373 (this engine): one type-dispatched validator for every write path. The server is the authority; the browser only repeats it for feedback. The constraint codes are the
// stable contract of SCRUM-380 (docs/architecture/FIELD_VALIDATION_CATALOG.md lists them): a form, the REST API and an import report the same code, the same JSON Pointer and the same parameters.

/// <summary>create: every required field must be present; update: only the keys that are present are checked, an omitted key never clears a stored value.</summary>
public enum ValidationMode { Create, Update }

public enum FieldType { Text, TextArea, RichText, Password, Number, Money, Date, Time, Select, Radio, Checkbox, Switch, Tags, File }

/// <summary>What a field accepts, independent of how it is shown. Built from a form field, an entity column or an import column.</summary>
public sealed record FieldConstraints
{
    public required string Name { get; init; }
    public FieldType Type { get; init; } = FieldType.Text;
    public bool Required { get; init; }
    public int? MinLength { get; init; }
    public int? MaxLength { get; init; }
    public decimal? Min { get; init; }
    public decimal? Max { get; init; }
    /// <summary>Most digits after the decimal point (number and money); 0 = whole numbers only.</summary>
    public int? Scale { get; init; }
    /// <summary>A regular expression the whole value must match (checked with a time limit).</summary>
    public string? Pattern { get; init; }
    /// <summary>The allowed values of a select or radio field; null or empty = not checked here (the options come from a data source).</summary>
    public IReadOnlyCollection<string>? Options { get; init; }
    /// <summary>Tags: least and most items (comma separated).</summary>
    public int? MinItems { get; init; }
    public int? MaxItems { get; init; }
    /// <summary>Dates: earliest and latest day, yyyy-MM-dd.</summary>
    public string? MinDate { get; init; }
    public string? MaxDate { get; init; }
    /// <summary>An advisory field warns instead of refusing: its violations are reported with <see cref="Violation.Advisory"/> and do not make the result invalid.</summary>
    public bool Advisory { get; init; }
}

/// <summary>One broken constraint: a JSON Pointer to the field, a stable code, the message key and the parameters the message needs.</summary>
public sealed record Violation(string Pointer, string Code, string MessageKey, IReadOnlyDictionary<string, object?> Params, bool Advisory = false);

public sealed record ValidationResult(IReadOnlyList<Violation> Violations, bool Truncated)
{
    /// <summary>Valid when nothing but advisory violations was found.</summary>
    public bool IsValid => Violations.All(v => v.Advisory);
}

/// <summary>Limits (settings Validation:* in env/*.env; the defaults are those of ASP.NET Core model validation).</summary>
public sealed class ValidationOptions
{
    public int MaxErrors { get; init; } = 200;
}

/// <summary>The stable constraint codes. A new constraint is a new code here, a new case in the dispatch and a row in the catalog document (a test checks that all three agree).</summary>
public static class ConstraintCodes
{
    public const string Required = "required";
    public const string MaxLength = "max-length";
    public const string MinLength = "min-length";
    public const string NotANumber = "not-a-number";
    public const string Min = "min";
    public const string Max = "max";
    public const string Scale = "scale";
    public const string NotADate = "not-a-date";
    public const string DateBefore = "date-before";
    public const string DateAfter = "date-after";
    public const string NotATime = "not-a-time";
    public const string NotAllowed = "not-allowed";
    public const string Pattern = "pattern";
    public const string MinItems = "min-items";
    public const string MaxItems = "max-items";
    // file constraints are checked by the upload step (SCRUM-596) with the same codes
    public const string FileTooLarge = "file-too-large";
    public const string FileType = "file-type";
    public const string FileCount = "file-count";

    public static readonly IReadOnlyList<string> All =
    [
        Required, MaxLength, MinLength, NotANumber, Min, Max, Scale, NotADate, DateBefore, DateAfter, NotATime, NotAllowed, Pattern, MinItems, MaxItems, FileTooLarge, FileType, FileCount,
    ];

    public static string MessageKey(string code) => "Val_" + string.Concat(code.Split('-').Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
}

public static class FieldValidator
{
    private static readonly TimeSpan PatternTimeout = TimeSpan.FromMilliseconds(250);

    /// <summary>Persian and Arabic-Indic digits become ASCII digits, so a number typed either way is the same number.</summary>
    public static string NormalizeDigits(string s)
    {
        var chars = s.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            var c = chars[i];
            if (c is >= '۰' and <= '۹') chars[i] = (char)('0' + (c - '۰'));
            else if (c is >= '٠' and <= '٩') chars[i] = (char)('0' + (c - '٠'));
            else if (c == '٫') chars[i] = '.';
        }
        return new string(chars);
    }

    public static bool TryNumber(string? raw, out decimal number)
    {
        number = 0;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        return decimal.TryParse(NormalizeDigits(raw.Trim()).Replace(",", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out number);
    }

    private static Violation V(string field, string code, bool advisory, params (string Key, object? Value)[] ps)
        => new("/" + field, code, ConstraintCodes.MessageKey(code), ps.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal), advisory);

    public static bool IsEmpty(FieldConstraints f, string? value)
        => string.IsNullOrWhiteSpace(value) || (f.Type is FieldType.Checkbox or FieldType.Switch && string.Equals(value, "false", StringComparison.OrdinalIgnoreCase));

    /// <summary>Every violation of one field's value (a non-empty value; emptiness and required are decided by the caller or by <see cref="Validate"/>), in the order max-length, min-length, type, range, pattern.</summary>
    public static IReadOnlyList<Violation> CheckValue(FieldConstraints f, string value)
    {
        var found = new List<Violation>();
        var a = f.Advisory;
        if (f.MaxLength is { } max && value.Length > max) found.Add(V(f.Name, ConstraintCodes.MaxLength, a, ("max", max)));
        if (f.MinLength is { } min && value.Length < min) found.Add(V(f.Name, ConstraintCodes.MinLength, a, ("min", min)));
        switch (f.Type)
        {
            case FieldType.Number or FieldType.Money:
                if (!TryNumber(value, out var n)) { found.Add(V(f.Name, ConstraintCodes.NotANumber, a)); break; }
                if (f.Min is { } lo && n < lo) found.Add(V(f.Name, ConstraintCodes.Min, a, ("min", lo)));
                if (f.Max is { } hi && n > hi) found.Add(V(f.Name, ConstraintCodes.Max, a, ("max", hi)));
                if (f.Scale is { } scale && Decimals(n) > scale) found.Add(V(f.Name, ConstraintCodes.Scale, a, ("scale", scale)));
                break;
            case FieldType.Date:
                var day = NormalizeDigits(value);
                if (!DateTime.TryParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) { found.Add(V(f.Name, ConstraintCodes.NotADate, a)); break; }
                if (f.MinDate is { } from && string.CompareOrdinal(day, from) < 0) found.Add(V(f.Name, ConstraintCodes.DateBefore, a, ("min", from)));
                if (f.MaxDate is { } to && string.CompareOrdinal(day, to) > 0) found.Add(V(f.Name, ConstraintCodes.DateAfter, a, ("max", to)));
                break;
            case FieldType.Time:
                if (!TimeSpan.TryParseExact(NormalizeDigits(value), ["hh\\:mm", "hh\\:mm\\:ss"], CultureInfo.InvariantCulture, out _)) found.Add(V(f.Name, ConstraintCodes.NotATime, a));
                break;
            case FieldType.Select or FieldType.Radio:
                if (f.Options is { Count: > 0 } opts && !opts.Contains(value)) found.Add(V(f.Name, ConstraintCodes.NotAllowed, a));
                break;
            case FieldType.Tags:
                var items = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Length;
                if (f.MinItems is { } mi && items < mi) found.Add(V(f.Name, ConstraintCodes.MinItems, a, ("min", mi)));
                if (f.MaxItems is { } ma && items > ma) found.Add(V(f.Name, ConstraintCodes.MaxItems, a, ("max", ma)));
                break;
        }
        if (!string.IsNullOrEmpty(f.Pattern))
        {
            try { if (!Regex.IsMatch(value, "^(?:" + f.Pattern + ")$", RegexOptions.CultureInvariant, PatternTimeout)) found.Add(V(f.Name, ConstraintCodes.Pattern, a)); }
            catch (RegexMatchTimeoutException) { found.Add(V(f.Name, ConstraintCodes.Pattern, a)); }   // a pattern that cannot be decided in time is a refusal, never an allow
        }
        return found;
    }

    private static int Decimals(decimal n)
    {
        var text = n.ToString(CultureInfo.InvariantCulture);
        var dot = text.IndexOf('.');
        return dot < 0 ? 0 : text[(dot + 1)..].TrimEnd('0').Length;
    }

    /// <summary>
    /// All violations of a record: create requires every required field, update checks only the keys present in <paramref name="values"/>; whitespace-only counts as empty. The list is cut at
    /// <see cref="ValidationOptions.MaxErrors"/> (the result says so).
    /// </summary>
    public static ValidationResult Validate(IReadOnlyList<FieldConstraints> fields, IReadOnlyDictionary<string, string?> values, ValidationMode mode, ValidationOptions? options = null)
    {
        var cap = (options ?? new ValidationOptions()).MaxErrors;
        var all = new List<Violation>();
        foreach (var f in fields)
        {
            var present = values.TryGetValue(f.Name, out var raw);
            if (mode == ValidationMode.Update && !present) continue;      // an omitted key never clears a value and is never "missing"
            var value = f.Type is FieldType.Password or FieldType.RichText ? raw : raw?.Trim();
            if (IsEmpty(f, value))
            {
                if (f.Required) all.Add(V(f.Name, ConstraintCodes.Required, f.Advisory));
                continue;
            }
            all.AddRange(CheckValue(f, value!));
            if (all.Count > cap) break;
        }
        var truncated = all.Count > cap;
        return new ValidationResult(truncated ? all.Take(cap).ToList() : all, truncated);
    }
}
