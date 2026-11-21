namespace PEMS.UI.Abstractions;

/// <summary>The input kinds a form field can take. A kind maps to exactly one component.</summary>
public enum FieldKind
{
    Text,
    TextArea,
    Number,
    Date,
    Select,
    Checkbox,
    File,
    /// <summary>
    /// Formatted text (HTML) edited with the shared rich-text editor (SCRUM-59). The posted HTML is untrusted: the server must run it through
    /// <c>FormValueSanitizer</c> / <c>IRichTextSanitizerService</c> before it is stored or shown.
    /// </summary>
    RichText,
    /// <summary>Secret text: never rendered back into the page, the browser is told not to autofill an old password.</summary>
    Password,
    /// <summary>Decimal amount typed with the decimal keypad; the value is data, formatting for display is the locale's job.</summary>
    Money,
    Time,
    /// <summary>One choice out of <see cref="FieldDescriptor.Options"/>, shown as a radio group.</summary>
    Radio,
    /// <summary>A boolean shown as a switch (a checkbox with the switch role).</summary>
    Switch,
    /// <summary>Free tags typed as one comma separated text; works without script and posts one value.</summary>
    Tags,
    /// <summary>A grid of checkboxes: <see cref="FieldDescriptor.Options"/> are the rows and <see cref="FieldDescriptor.Columns"/> the columns; it posts one list of "row|column" values.</summary>
    CheckboxMatrix
}

/// <summary>
/// Who owns a field (SCRUM-515): <see cref="Platform"/> fields are delivered and changed only by product updates and are read-only for the customer; <see cref="Application"/> fields (the default) belong to the customer
/// and are never touched by an update.
/// </summary>
public enum FieldOwner { Application, Platform }

/// <summary>One choice of a <see cref="FieldKind.Select"/> field. <see cref="Value"/> is data and is never translated.</summary>
public sealed record FieldOption
{
    public required string Value { get; init; }
    public required TranslatableText Label { get; init; }
}

/// <summary>
/// Describes one form field as data. Code-defined pages and the metadata engine build the same type, so there is one rendering path.
/// Every user-visible text is a <see cref="TranslatableText"/> input; the component holds none (ADR-0008).
/// </summary>
public sealed record FieldDescriptor
{
    public required string Name { get; init; }
    public required TranslatableText Label { get; init; }
    public FieldKind Kind { get; init; } = FieldKind.Text;
    public bool Required { get; init; }
    public bool ReadOnly { get; init; }
    /// <summary>Platform (ours, read-only in the designer, changed by product updates) or application (the customer's, the default). See <c>FormOwnership</c>.</summary>
    public FieldOwner Owner { get; init; } = FieldOwner.Application;
    public TranslatableText? HelpText { get; init; }
    public int? MaxLength { get; init; }
    public IReadOnlyList<FieldOption>? Options { get; init; }
    /// <summary>The columns of a <see cref="FieldKind.CheckboxMatrix"/>.</summary>
    public IReadOnlyList<FieldOption>? Columns { get; init; }
    public TranslatableText? Placeholder { get; init; }

    // SCRUM-578 (form engine): what a stored form says about a field besides its look. Every condition is an expression (ADR-0015) that reads only the roots values, user and request.
    /// <summary>The field is shown (and its value is used) only while this condition is true; no condition = always.</summary>
    public string? Visible { get; init; }
    /// <summary>The field is required while this condition is true (in addition to <see cref="Required"/>).</summary>
    public string? RequiredWhen { get; init; }
    /// <summary>The field is read only while this condition is true (in addition to <see cref="ReadOnly"/>); a read-only value posted by the browser is ignored.</summary>
    public string? ReadOnlyWhen { get; init; }
    public int? MinLength { get; init; }
    public decimal? Min { get; init; }
    public decimal? Max { get; init; }
    /// <summary>A regular expression the whole value must match (checked with a time limit).</summary>
    public string? Pattern { get; init; }
    /// <summary>Extra rules: each expression must be true, else the message is the error of the field.</summary>
    public IReadOnlyList<FieldRule>? Rules { get; init; }
    /// <summary>Replaces the built-in message of a check (required, maxLength, ...), in both languages.</summary>
    public FieldMessages? Messages { get; init; }
    /// <summary>The key of a data-source definition that gives the options of a Select or Radio field (resolved for the request by the pipeline).</summary>
    public string? OptionsFrom { get; init; }
    /// <summary>Columns this field takes in the form grid (1 to the form's <c>columns</c>).</summary>
    public int? Span { get; init; }
    /// <summary>Shows the value grouped as the user types (SCRUM-386): <c>9</c> a digit, <c>A</c> a letter, <c>*</c> a letter or digit, any other character a separator ("9999-9999-9999-9999", "AA99 9999"). The value that is posted and stored has no separators (<see cref="PEMS.UI.Abstractions.FieldMask"/>).</summary>
    public string? Mask { get; init; }
    /// <summary>The on-screen keyboard hint (SCRUM-374): numeric, decimal, tel, email, url or text; money fields and all-digit masks get one by themselves.</summary>
    public string? InputMode { get; init; }
}

/// <summary>A rule of a field or of the whole form: <see cref="Expression"/> must be true, otherwise <see cref="Message"/> is shown (on <see cref="Field"/> of a form rule, else on the form).</summary>
public sealed record FieldRule
{
    public required string Expression { get; init; }
    public required TranslatableText Message { get; init; }
    /// <summary>Form rules only: the field that shows the message.</summary>
    public string? Field { get; init; }
}

/// <summary>Messages a field overrides; a missing one keeps the engine's built-in text.</summary>
public sealed record FieldMessages
{
    public TranslatableText? Required { get; init; }
    public TranslatableText? MaxLength { get; init; }
    public TranslatableText? MinLength { get; init; }
    public TranslatableText? Pattern { get; init; }
    public TranslatableText? Min { get; init; }
    public TranslatableText? Max { get; init; }
    public TranslatableText? Invalid { get; init; }
    public TranslatableText? NotAllowed { get; init; }
}
