using System.Globalization;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace PEMS.UI.Abstractions;

/// <summary>
/// A user-visible text in several languages: a map from a language slug ("fa", "en", "fa-ir") to the text in that language.
/// It is the only way a PEMS.UI component receives text (ADR-0008): components hold no literal text, so the same value can come from code,
/// from JSON, or from a form row of the database. It serialises as <c>{"fa":"ذخیره","en":"Save"}</c>.
/// </summary>
/// <remarks>
/// Immutable. Slugs are BCP 47 style, compared case-insensitively (stored lower case). The slug <see cref="NeutralKey"/> ("*") holds a
/// language-neutral text such as a code or a symbol; a plain string converts to it. A blank text counts as absent, so "missing" has one meaning.
/// </remarks>
[JsonConverter(typeof(TranslatableTextJsonConverter))]
public sealed class TranslatableText : IEquatable<TranslatableText>
{
    /// <summary>The slug of a text that is the same in every language.</summary>
    public const string NeutralKey = "*";

    private static readonly Regex SlugRx = new("^[a-z]{2,3}(-[a-z0-9]{2,8})*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly SortedDictionary<string, string> _texts;

    public static TranslatableText Empty { get; } = new(new SortedDictionary<string, string>(StringComparer.Ordinal));

    private TranslatableText(SortedDictionary<string, string> texts) => _texts = texts;

    /// <summary>Language slug to text, ordered by slug. Never contains blank texts.</summary>
    public IReadOnlyDictionary<string, string> Texts => _texts;

    public bool IsEmpty => _texts.Count == 0;

    /// <summary>A text that is the same in every language. Null or blank gives <see cref="Empty"/>.</summary>
    public static TranslatableText Literal(string? text) =>
        string.IsNullOrWhiteSpace(text) ? Empty : new(new SortedDictionary<string, string>(StringComparer.Ordinal) { [NeutralKey] = text });

    /// <summary>Builds a text from (language, text) pairs. Throws <see cref="ArgumentException"/> for an invalid slug or a duplicate.</summary>
    public static TranslatableText Of(params (string Language, string? Text)[] texts) =>
        From(texts.Select(t => KeyValuePair.Create(t.Language, t.Text)));

    public static TranslatableText From(IEnumerable<KeyValuePair<string, string?>> texts)
    {
        var map = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (language, text) in texts)
        {
            var slug = NormalizeSlug(language);
            if (map.ContainsKey(slug)) throw new ArgumentException($"Language '{slug}' appears twice.", nameof(texts));
            if (!string.IsNullOrWhiteSpace(text)) map[slug] = text;
        }
        return map.Count == 0 ? Empty : new(map);
    }

    public static implicit operator TranslatableText(string? text) => Literal(text);

    /// <summary>Returns a copy with the text of one language added or replaced (blank removes it).</summary>
    public TranslatableText With(string language, string? text)
    {
        var slug = NormalizeSlug(language);
        var map = new SortedDictionary<string, string>(_texts, StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(text)) map.Remove(slug); else map[slug] = text;
        return map.Count == 0 ? Empty : new(map);
    }

    /// <summary>
    /// Picks the text to show. Order: the culture and its parents ("fa-IR", then "fa"), then <paramref name="defaultLanguage"/> and its
    /// parents, then the language-neutral text, then the first text by slug (so a page never shows nothing while any text exists).
    /// Returns an empty string for <see cref="Empty"/>.
    /// </summary>
    public string Resolve(CultureInfo culture, string? defaultLanguage = null)
    {
        if (_texts.Count == 0) return "";
        for (var c = culture; c is not null && c.Name.Length > 0; c = c.Parent)
            if (_texts.TryGetValue(c.Name.ToLowerInvariant(), out var hit)) return hit;
        if (!string.IsNullOrWhiteSpace(defaultLanguage))
            for (var slug = defaultLanguage.Trim().ToLowerInvariant(); slug.Length > 0; slug = Parent(slug))
                if (_texts.TryGetValue(slug, out var hit)) return hit;
        if (_texts.TryGetValue(NeutralKey, out var neutral)) return neutral;
        return _texts.Values.First();
    }

    /// <summary>The languages in <paramref name="required"/> that have no text here (for the designers; the neutral text satisfies every language).</summary>
    public IReadOnlyList<string> MissingLanguages(IEnumerable<string> required)
    {
        if (_texts.ContainsKey(NeutralKey)) return [];
        return required.Select(NormalizeSlug).Distinct().Where(l => !_texts.ContainsKey(l)).OrderBy(l => l, StringComparer.Ordinal).ToList();
    }

    internal static string NormalizeSlug(string? language)
    {
        var slug = (language ?? "").Trim().ToLowerInvariant();
        if (slug == NeutralKey || SlugRx.IsMatch(slug)) return slug;
        throw new ArgumentException($"'{language}' is not a language slug (expected something like 'fa', 'en' or 'fa-ir', or '*').", nameof(language));
    }

    private static string Parent(string slug)
    {
        var i = slug.LastIndexOf('-');
        return i < 0 ? "" : slug[..i];
    }

    public bool Equals(TranslatableText? other) =>
        other is not null && _texts.Count == other._texts.Count && _texts.All(kv => other._texts.TryGetValue(kv.Key, out var v) && v == kv.Value);

    public override bool Equals(object? obj) => Equals(obj as TranslatableText);

    public override int GetHashCode()
    {
        var h = new HashCode();
        foreach (var kv in _texts) { h.Add(kv.Key); h.Add(kv.Value); }
        return h.ToHashCode();
    }

    /// <summary>The text for the invariant culture (neutral text, or the first). For logs and tests; pages use <see cref="Resolve"/>.</summary>
    public override string ToString() => Resolve(CultureInfo.InvariantCulture);
}
