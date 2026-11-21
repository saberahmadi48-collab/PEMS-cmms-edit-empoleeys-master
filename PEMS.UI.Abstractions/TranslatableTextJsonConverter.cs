using System.Text.Json;
using System.Text.Json.Serialization;

namespace PEMS.UI.Abstractions;

/// <summary>
/// JSON shape of <see cref="TranslatableText"/>: an object of language slug to text. When reading, a plain string is accepted as the
/// language-neutral text and null as empty; when writing, the object form is always used so stored data has one shape.
/// </summary>
public sealed class TranslatableTextJsonConverter : JsonConverter<TranslatableText>
{
    public override bool HandleNull => true;

    public override TranslatableText Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return TranslatableText.Empty;
            case JsonTokenType.String:
                return TranslatableText.Literal(reader.GetString());
            case JsonTokenType.StartObject:
                var pairs = new List<KeyValuePair<string, string?>>();
                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    var slug = reader.GetString() ?? "";
                    reader.Read();
                    if (reader.TokenType is not (JsonTokenType.String or JsonTokenType.Null))
                        throw new JsonException($"The text for language '{slug}' must be a string.");
                    pairs.Add(KeyValuePair.Create(slug, reader.GetString()));
                }
                try { return TranslatableText.From(pairs); }
                catch (ArgumentException ex) { throw new JsonException(ex.Message, ex); }
            default:
                throw new JsonException("A translatable text must be a JSON object of language to text, a string, or null.");
        }
    }

    public override void Write(Utf8JsonWriter writer, TranslatableText value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var kv in (value ?? TranslatableText.Empty).Texts) writer.WriteString(kv.Key, kv.Value);
        writer.WriteEndObject();
    }
}
