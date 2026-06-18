using System;
using System.Text.Json.Serialization;

namespace Infersoft;

/// <summary>
/// Processing status of a document. An extensible enum: known values are static members, but a
/// value the server adds after this build round-trips intact instead of failing the response.
/// Compare with <c>==</c> (also against a raw string).
/// </summary>
[JsonConverter(typeof(DocumentStatusConverter))]
public readonly struct DocumentStatus : IEquatable<DocumentStatus>
{
    private readonly string? _value;

    public DocumentStatus(string value) => _value = value ?? throw new ArgumentNullException(nameof(value));

    public static DocumentStatus Uploading { get; } = new("uploading");

    public static DocumentStatus Processing { get; } = new("processing");

    public static DocumentStatus Ready { get; } = new("ready");

    /// <summary>The raw string value.</summary>
    public string Value => _value ?? string.Empty;

    public static implicit operator DocumentStatus(string value) => new(value);

    public static bool operator ==(DocumentStatus left, DocumentStatus right) => left.Equals(right);

    public static bool operator !=(DocumentStatus left, DocumentStatus right) => !left.Equals(right);

    public bool Equals(DocumentStatus other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is DocumentStatus other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString() => Value;
}

internal sealed class DocumentStatusConverter : ExtensibleEnumConverter<DocumentStatus>
{
    protected override DocumentStatus Create(string value) => new(value);

    protected override string ValueOf(DocumentStatus member) => member.Value;
}
