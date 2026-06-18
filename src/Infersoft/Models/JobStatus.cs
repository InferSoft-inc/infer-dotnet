using System;
using System.Text.Json.Serialization;

namespace Infersoft;

/// <summary>
/// Lifecycle status of a job. An extensible enum (see <see cref="DocumentStatus"/>): unknown
/// server values round-trip intact.
/// </summary>
[JsonConverter(typeof(JobStatusConverter))]
public readonly struct JobStatus : IEquatable<JobStatus>
{
    private readonly string? _value;

    public JobStatus(string value) => _value = value ?? throw new ArgumentNullException(nameof(value));

    public static JobStatus Running { get; } = new("running");

    public static JobStatus Completed { get; } = new("completed");

    public static JobStatus Failed { get; } = new("failed");

    public static JobStatus PartialSuccess { get; } = new("partial_success");

    public static JobStatus CreatingWorkflows { get; } = new("creating_workflows");

    /// <summary>The raw string value.</summary>
    public string Value => _value ?? string.Empty;

    public static implicit operator JobStatus(string value) => new(value);

    public static bool operator ==(JobStatus left, JobStatus right) => left.Equals(right);

    public static bool operator !=(JobStatus left, JobStatus right) => !left.Equals(right);

    public bool Equals(JobStatus other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is JobStatus other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString() => Value;
}

internal sealed class JobStatusConverter : ExtensibleEnumConverter<JobStatus>
{
    protected override JobStatus Create(string value) => new(value);

    protected override string ValueOf(JobStatus member) => member.Value;
}
