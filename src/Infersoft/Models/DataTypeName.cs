using System;
using System.Text.Json.Serialization;

namespace Infersoft;

/// <summary>
/// Canonical data type of a prompt value (shared by prompts and extraction). An extensible enum
/// (see <see cref="DocumentStatus"/>); the wire values are capitalized.
/// </summary>
[JsonConverter(typeof(DataTypeNameConverter))]
public readonly struct DataTypeName : IEquatable<DataTypeName>
{
    private readonly string? _value;

    public DataTypeName(string value) => _value = value ?? throw new ArgumentNullException(nameof(value));

    public static DataTypeName String { get; } = new("String");

    public static DataTypeName Number { get; } = new("Number");

    public static DataTypeName Boolean { get; } = new("Boolean");

    public static DataTypeName Date { get; } = new("Date");

    /// <summary>The raw string value.</summary>
    public string Value => _value ?? string.Empty;

    public static implicit operator DataTypeName(string value) => new(value);

    public static bool operator ==(DataTypeName left, DataTypeName right) => left.Equals(right);

    public static bool operator !=(DataTypeName left, DataTypeName right) => !left.Equals(right);

    public bool Equals(DataTypeName other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is DataTypeName other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString() => Value;
}

internal sealed class DataTypeNameConverter : ExtensibleEnumConverter<DataTypeName>
{
    protected override DataTypeName Create(string value) => new(value);

    protected override string ValueOf(DataTypeName member) => member.Value;
}
