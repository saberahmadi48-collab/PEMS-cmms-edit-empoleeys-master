using System.Globalization;
using System.Text.RegularExpressions;
using PEMS.Application.Definitions;
using PEMS.Application.Expressions;
using PEMS.Application.Validation;
using PEMS.UI.Abstractions;

namespace PEMS.Web.Definitions;

// SCRUM-578 (#578, ADR-0014): the form engine. A form is a definition; this class is everything that is decided about it at run time and nothing else: which fields are visible, required or read
// only (conditions in the expression language, ADR-0015), whether the posted values are valid (built-in checks, the field rules and the form rules, every message in both languages from the
// definition), and the cleaned values the action receives. The server is the authority; the browser (pems-form-rules.js) only repeats the same conditions so the form reacts while typing.

/// <summary>What the form needs to know about the request to evaluate its conditions.</summary>
public sealed record FormRequestContext(string Culture, string Key, IReadOnlyCollection<string> Roles, string? UserName);

/// <summary>The state of one field after the conditions were evaluated for the current values.</summary>
public sealed record FieldCondition(bool Visible, bool Required, bool ReadOnly);

/// <summary>The outcome of validating a post: errors by field, errors of the form as a whole, and the cleaned values (visible, writable fields only).</summary>
public sealed record FormValidation(IReadOnlyDictionary<string, TranslatableText> Errors, IReadOnlyList<TranslatableText> FormErrors, IReadOnlyDictionary<string, string?> Values)
{
    public bool IsValid => Errors.Count == 0 && FormErrors.Count == 0;
}

public static class FormEngine
{
    /// <summary>The variables an expression of a form may read.</summary>
    public static readonly string[] ExpressionRoots = ["values", "user", "request"];

    private static readonly TimeSpan PatternTimeout = TimeSpan.FromMilliseconds(250);

    // ---- values as the expression language sees them -------------------------------------------------------------------------------------------------------------------------------

    /// <summary>Persian and Arabic-Indic digits become ASCII digits, so a number typed in either way is the same number.</summary>
    public static string NormalizeDigits(string s) => FieldValidator.NormalizeDigits(s);

    public static bool TryNumber(string? raw, out decimal number) => FieldValidator.TryNumber(raw, out number);

    private static object? Typed(FieldDescriptor f, string? raw)
    {
        switch (f.Kind)
        {
            case FieldKind.Checkbox or FieldKind.Switch: return string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase);
            case FieldKind.Number or FieldKind.Money: return TryNumber(raw, out var n) ? (double)n : null;
            default: return string.IsNullOrWhiteSpace(raw) ? null : raw;
        }
    }

    public static ExpressionContext Context(FormDefinition form, IReadOnlyDictionary<string, string?> raw, FormRequestContext request)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var f in form.Fields) values[f.Name] = Typed(f, raw.TryGetValue(f.Name, out var v) ? v : null);
        return new ExpressionContext(new Dictionary<string, object?>
        {
            ["values"] = values,
            ["user"] = new Dictionary<string, object?> { ["name"] = request.UserName, ["roles"] = request.Roles.ToList() },
            ["request"] = new Dictionary<string, object?> { ["culture"] = request.Culture, ["key"] = request.Key }
        }, DateTime.Today);
    }

    private static bool Holds(string? expression, ExpressionContext ctx, bool whenBroken = false)
    {
        if (string.IsNullOrWhiteSpace(expression)) return whenBroken;
        try { return ExpressionCompiler.Compile(expression, ExpressionRoots).EvaluateCondition(ctx); }
        catch (ExpressionException) { return whenBroken; }
    }

    // ---- conditions ----------------------------------------------------------------------------------------------------------------------------------------------------------------

    /// <summary>Visible, required and read only of every field for these values. A field without a condition keeps what its descriptor says.</summary>
    public static IReadOnlyDictionary<string, FieldCondition> Evaluate(FormDefinition form, IReadOnlyDictionary<string, string?> raw, FormRequestContext request)
    {
        var ctx = Context(form, raw, request);
        var result = new Dictionary<string, FieldCondition>(StringComparer.Ordinal);
        foreach (var f in form.Fields)
            result[f.Name] = new FieldCondition(
                Visible: string.IsNullOrWhiteSpace(f.Visible) || Holds(f.Visible, ctx),
                Required: f.Required || Holds(f.RequiredWhen, ctx),
                ReadOnly: f.ReadOnly || Holds(f.ReadOnlyWhen, ctx));
        return result;
    }

    /// <summary>The form as it is shown for these values: the required and read only flags the conditions gave, and the options the data sources resolved for this request.</summary>
    public static FormDefinition Effective(FormDefinition form, IReadOnlyDictionary<string, FieldCondition> conditions, IReadOnlyDictionary<string, IReadOnlyList<FieldOption>> options)
        => form with
        {
            Fields = form.Fields.Select(f =>
            {
                var c = conditions.TryGetValue(f.Name, out var s) ? s : new FieldCondition(true, f.Required, f.ReadOnly);
                return f with { Required = c.Required, ReadOnly = c.ReadOnly, Options = options.TryGetValue(f.Name, out var o) ? o : f.Options };
            }).ToList()
        };

    // ---- messages -------------------------------------------------------------------------------------------------------------------------------------------------------------------

    private static TranslatableText Msg(string en, string fa, params object[] args)
        => TranslatableText.Of(("en", string.Format(CultureInfo.InvariantCulture, en, args)), ("fa", string.Format(CultureInfo.InvariantCulture, fa, args)));

    private static TranslatableText Pick(TranslatableText? custom, TranslatableText builtIn) => custom is { IsEmpty: false } ? custom : builtIn;

    // ---- validation -----------------------------------------------------------------------------------------------------------------------------------------------------------------

    /// <param name="raw">The posted values by field name.</param>
    /// <param name="current">The stored values of the record (edit forms): a read-only field keeps its current value, a posted one is ignored.</param>
    public static FormValidation Validate(FormDefinition form, IReadOnlyDictionary<string, string?> raw, FormRequestContext request, IReadOnlyDictionary<string, string?>? current = null)
    {
        var conditions = Evaluate(form, raw, request);
        var ctx = Context(form, raw, request);
        var errors = new Dictionary<string, TranslatableText>(StringComparer.Ordinal);
        var cleaned = new Dictionary<string, string?>(StringComparer.Ordinal);

        foreach (var f in form.Fields)
        {
            var c = conditions[f.Name];
            if (!c.Visible) continue;   // a hidden field is not part of the post
            if (c.ReadOnly) { if (current is not null && current.TryGetValue(f.Name, out var kept)) cleaned[f.Name] = kept; continue; }

            raw.TryGetValue(f.Name, out var posted);
            var value = f.Kind is FieldKind.Password or FieldKind.RichText ? posted : posted?.Trim();
            if (f.Kind is FieldKind.Checkbox or FieldKind.Switch) value = string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ? "true" : "false";
            var empty = string.IsNullOrWhiteSpace(value) || (f.Kind is FieldKind.Checkbox or FieldKind.Switch && value == "false");
            cleaned[f.Name] = value;

            var m = f.Messages;
            TranslatableText? error = null;
            if (empty)
            {
                if (c.Required) error = Pick(m?.Required, ValidationMessages.Text(ConstraintCodes.Required));
            }
            else error = CheckValue(f, value!, m);

            if (error is null && !empty && f.Rules is { Count: > 0 })
                foreach (var rule in f.Rules)
                    if (!Holds(rule.Expression, ctx, whenBroken: false)) { error = rule.Message; break; }
            if (error is not null) errors[f.Name] = error;
        }

        var formErrors = new List<TranslatableText>();
        foreach (var rule in form.Rules ?? [])
        {
            if (Holds(rule.Expression, ctx, whenBroken: false)) continue;
            if (!string.IsNullOrEmpty(rule.Field) && form.Fields.Any(x => x.Name == rule.Field)) errors.TryAdd(rule.Field, rule.Message);
            else formErrors.Add(rule.Message);
        }
        return new FormValidation(errors, formErrors, cleaned);
    }

    // SCRUM-373: the built-in checks are the one validator of PEMS.Application (stable constraint codes); the form shows the first violation of the field, in the definition's own text when it has one.
    private static TranslatableText? CheckValue(FieldDescriptor f, string value, FieldMessages? m)
    {
        var first = FieldValidator.CheckValue(ToConstraints(f), value).FirstOrDefault();
        if (first is null) return null;
        var custom = first.Code switch
        {
            ConstraintCodes.MaxLength => m?.MaxLength,
            ConstraintCodes.MinLength => m?.MinLength,
            ConstraintCodes.NotANumber or ConstraintCodes.NotADate or ConstraintCodes.NotATime => m?.Invalid,
            ConstraintCodes.Min => m?.Min,
            ConstraintCodes.Max => m?.Max,
            ConstraintCodes.NotAllowed => m?.NotAllowed,
            ConstraintCodes.Pattern => m?.Pattern,
            _ => null,
        };
        return Pick(custom, ValidationMessages.Text(first.Code, first.Params));
    }

    /// <summary>What a form field accepts, as the validator sees it (the same constraints an entity column or an import column is checked with).</summary>
    public static FieldConstraints ToConstraints(FieldDescriptor f) => new()
    {
        Name = f.Name,
        Type = f.Kind switch
        {
            FieldKind.TextArea => FieldType.TextArea, FieldKind.Number => FieldType.Number, FieldKind.Date => FieldType.Date, FieldKind.Select => FieldType.Select, FieldKind.Checkbox => FieldType.Checkbox,
            FieldKind.File => FieldType.File, FieldKind.RichText => FieldType.RichText, FieldKind.Password => FieldType.Password, FieldKind.Money => FieldType.Money, FieldKind.Time => FieldType.Time,
            FieldKind.Radio => FieldType.Radio, FieldKind.Switch => FieldType.Switch, FieldKind.Tags => FieldType.Tags, _ => FieldType.Text,
        },
        Required = f.Required, MinLength = f.MinLength, MaxLength = f.MaxLength, Min = f.Min, Max = f.Max, Pattern = f.Pattern,
        Options = f.Options?.Select(o => o.Value).ToList(),
    };

    // ---- checks at load -------------------------------------------------------------------------------------------------------------------------------------------------------------

    /// <summary>What is wrong with a form definition as a definition (not with a post): unknown kinds of rule, expressions that do not compile or read another variable, a missing translation, an unknown field in a rule, a pattern that is not a regular expression.</summary>
    public static IReadOnlyList<DefinitionFieldError> CheckDefinition(FormDefinition form)
    {
        var errors = new List<DefinitionFieldError>();
        var names = form.Fields.Select(f => f.Name).ToHashSet(StringComparer.Ordinal);
        if (form.Columns is < 1 or > 2) errors.Add(new("/columns", "range", "Def_Err_Range", "columns is 1 or 2."));

        void Expr(string? source, string at)
        {
            if (string.IsNullOrWhiteSpace(source)) return;
            if (!ExpressionCompiler.TryCompile(source, out var compiled, out var ex, ExpressionRoots)) { errors.Add(new(at, ex!.Code, "Def_Err_Expression", ex.Message)); return; }
        }
        void Text(TranslatableText? text, string at)
        {
            if (text is null) return;
            if (text.MissingLanguages(["fa", "en"]).Count > 0) errors.Add(new(at, "missing-translation", "Def_Err_Translation", "A text needs both fa and en."));
        }
        void Rules(IReadOnlyList<FieldRule>? rules, string at)
        {
            for (var i = 0; i < (rules?.Count ?? 0); i++)
            {
                Expr(rules![i].Expression, $"{at}/{i}/expression");
                Text(rules[i].Message, $"{at}/{i}/message");
                if (rules[i].Field is { } rf && !names.Contains(rf)) errors.Add(new($"{at}/{i}/field", "unknown-field", "Def_Err_UnknownField", $"The form has no field '{rf}'."));
            }
        }

        for (var i = 0; i < form.Fields.Count; i++)
        {
            var f = form.Fields[i];
            var at = $"/fields/{i}";
            Text(f.Label, at + "/label");
            Text(f.HelpText, at + "/helpText");
            Text(f.Placeholder, at + "/placeholder");
            Expr(f.Visible, at + "/visible"); Expr(f.RequiredWhen, at + "/requiredWhen"); Expr(f.ReadOnlyWhen, at + "/readOnlyWhen");
            Rules(f.Rules, at + "/rules");
            if (f.Rules?.Any(r => !string.IsNullOrEmpty(r.Field)) == true) errors.Add(new(at + "/rules", "field-not-allowed", "Def_Err_Type", "A field rule shows its message on the field itself; 'field' belongs to form rules."));
            foreach (var (k, t) in new (string, TranslatableText?)[] { ("required", f.Messages?.Required), ("maxLength", f.Messages?.MaxLength), ("minLength", f.Messages?.MinLength), ("pattern", f.Messages?.Pattern), ("min", f.Messages?.Min), ("max", f.Messages?.Max), ("invalid", f.Messages?.Invalid), ("notAllowed", f.Messages?.NotAllowed) })
                Text(t, $"{at}/messages/{k}");
            foreach (var (o, j) in (f.Options ?? []).Select((o, j) => (o, j))) Text(o.Label, $"{at}/options/{j}/label");
            if (f.Kind is FieldKind.Select or FieldKind.Radio && (f.Options is null || f.Options.Count == 0) && string.IsNullOrEmpty(f.OptionsFrom))
                errors.Add(new(at + "/options", "required", "Def_Err_Required", "A Select or Radio field needs options or optionsFrom."));
            if (!string.IsNullOrEmpty(f.OptionsFrom) && f.Options is { Count: > 0 }) errors.Add(new(at + "/optionsFrom", "overlap", "Def_Err_Overlap", "A field takes its options from one place: options or optionsFrom, not both."));
            if (f.Span is { } span && (span < 1 || span > Math.Max(form.Columns, 1))) errors.Add(new(at + "/span", "range", "Def_Err_Range", "span is between 1 and the form's columns."));
            if (!string.IsNullOrEmpty(f.Pattern))
            {
                try { _ = new Regex(f.Pattern, RegexOptions.CultureInvariant, PatternTimeout); }
                catch (ArgumentException) { errors.Add(new(at + "/pattern", "invalid-pattern", "Def_Err_Pattern", "The pattern is not a regular expression.")); }
            }
            if (f.Min is { } lo && f.Max is { } hi && lo > hi) errors.Add(new(at + "/min", "range", "Def_Err_Range", "min is larger than max."));
        }
        Rules(form.Rules, "/rules");
        Text(form.Title, "/title");
        Text(form.SubmitLabel, "/submitLabel");
        return errors;
    }
}
