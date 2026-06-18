using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Infersoft;

/// <summary>
/// Base JSON converter for the extensible-enum structs: reads/writes the raw string so unknown
/// server values round-trip intact.
/// </summary>
internal abstract class ExtensibleEnumConverter<T> : JsonConverter<T>
{
    protected abstract T Create(string value);

    protected abstract string ValueOf(T member);

    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        Create(reader.TokenType == JsonTokenType.Null ? string.Empty : reader.GetString() ?? string.Empty);

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
        writer.WriteStringValue(ValueOf(value));
}
