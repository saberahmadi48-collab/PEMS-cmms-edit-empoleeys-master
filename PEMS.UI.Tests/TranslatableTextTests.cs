using System.Globalization;
using System.Text.Json;
using PEMS.UI.Abstractions;

namespace PEMS.UI.Tests;

public sealed class TranslatableTextTests
{
    private static readonly TranslatableText Save = TranslatableText.Of(("fa", "ذخیره"), ("en", "Save"));

    [Theory]
    [InlineData("fa", "ذخیره")]
    [InlineData("fa-IR", "ذخیره")]          // the culture's parent "fa" is tried after "fa-ir"
    [InlineData("en", "Save")]
    [InlineData("en-US", "Save")]
    public void Resolve_picks_the_culture_then_its_parent(string culture, string expected) =>
        Assert.Equal(expected, Save.Resolve(new CultureInfo(culture), defaultLanguage: "en"));

    [Fact]
    public void Resolve_prefers_the_specific_region_over_the_language()
    {
        var text = TranslatableText.Of(("en", "colour"), ("en-us", "color"));
        Assert.Equal("color", text.Resolve(new CultureInfo("en-US")));
        Assert.Equal("colour", text.Resolve(new CultureInfo("en-GB")));
    }

    [Fact]
    public void Resolve_falls_back_to_the_default_language_then_neutral_then_the_first()
    {
        var onlyEn = TranslatableText.Of(("en", "Save"), ("de", "Speichern"));
        Assert.Equal("Save", onlyEn.Resolve(new CultureInfo("fa"), defaultLanguage: "en"));               // default language
        Assert.Equal("Speichern", onlyEn.Resolve(new CultureInfo("fa"), defaultLanguage: "fr"));          // first by slug (de < en)

        var neutral = TranslatableText.Literal("PT-0001").With("en", "Tag");
        Assert.Equal("PT-0001", neutral.Resolve(new CultureInfo("fa"), defaultLanguage: "fr"));           // neutral before first
        Assert.Equal("Tag", neutral.Resolve(new CultureInfo("en")));                                       // a real language wins over neutral
    }

    [Fact]
    public void Resolve_of_nothing_is_an_empty_string()
    {
        Assert.Equal("", TranslatableText.Empty.Resolve(new CultureInfo("fa")));
        Assert.Equal("", ((TranslatableText)(string?)null).Resolve(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Slugs_are_case_insensitive_and_validated()
    {
        Assert.Equal("Save", TranslatableText.Of(("EN", "Save")).Texts["en"]);
        Assert.Throws<ArgumentException>(() => TranslatableText.Of(("english", "Save")));
        Assert.Throws<ArgumentException>(() => TranslatableText.Of(("e", "Save")));
        Assert.Throws<ArgumentException>(() => TranslatableText.Of(("en", "a"), ("EN", "b")));            // duplicate after normalising
    }

    [Fact]
    public void Blank_texts_count_as_absent()
    {
        var t = TranslatableText.Of(("fa", " "), ("en", "Save"), ("de", null));
        Assert.Single(t.Texts);
        Assert.True(TranslatableText.Literal("  ").IsEmpty);
        Assert.True(Save.With("fa", "").Texts.ContainsKey("fa") == false);
    }

    [Fact]
    public void A_string_converts_to_a_language_neutral_text()
    {
        TranslatableText t = "PT-0001";
        Assert.Equal("PT-0001", t.Texts[TranslatableText.NeutralKey]);
        Assert.Equal("PT-0001", t.Resolve(new CultureInfo("fa")));
    }

    [Fact]
    public void MissingLanguages_reports_what_a_designer_still_has_to_translate()
    {
        Assert.Equal(["de"], Save.MissingLanguages(["fa", "en", "de"]));
        Assert.Empty(Save.MissingLanguages(["fa", "en"]));
        Assert.Empty(TranslatableText.Literal("PT").MissingLanguages(["fa", "en"]));                      // neutral satisfies every language
        Assert.Equal(["en", "fa"], TranslatableText.Empty.MissingLanguages(["fa", "en"]));
    }

    [Fact]
    public void Equality_is_by_content()
    {
        Assert.Equal(Save, TranslatableText.Of(("en", "Save"), ("fa", "ذخیره")));
        Assert.Equal(Save.GetHashCode(), TranslatableText.Of(("en", "Save"), ("fa", "ذخیره")).GetHashCode());
        Assert.NotEqual(Save, Save.With("en", "Store"));
    }

    // ---- JSON: the shape stored in the database form table
    [Fact]
    public void Json_writes_an_object_of_slug_to_text_in_slug_order()
    {
        var json = JsonSerializer.Serialize(Save, PemsJson.Options);
        Assert.Equal("""{"en":"Save","fa":"ذخیره"}""", json);
        Assert.Equal(Save, JsonSerializer.Deserialize<TranslatableText>(json, PemsJson.Options));
    }

    [Fact]
    public void Json_reads_a_plain_string_as_neutral_and_null_as_empty()
    {
        Assert.Equal(TranslatableText.Literal("x"), JsonSerializer.Deserialize<TranslatableText>("\"x\"", PemsJson.Options));
        Assert.True(JsonSerializer.Deserialize<TranslatableText>("null", PemsJson.Options)!.IsEmpty);
        Assert.Equal("""{"*":"x"}""", JsonSerializer.Serialize(TranslatableText.Literal("x"), PemsJson.Options));      // one stored shape
        Assert.Equal("{}", JsonSerializer.Serialize(TranslatableText.Empty, PemsJson.Options));
    }

    [Theory]
    [InlineData("""{"english":"x"}""")]          // invalid slug
    [InlineData("""{"en":5}""")]                  // not a string
    [InlineData("[1]")]                           // wrong shape
    public void Json_rejects_a_wrong_shape_with_a_JsonException(string json) =>
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<TranslatableText>(json, PemsJson.Options));
}
