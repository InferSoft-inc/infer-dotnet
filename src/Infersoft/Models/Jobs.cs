using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Infersoft;

/// <summary>A credit estimate for a planned job. Its <see cref="Id"/> is the credits id used to start the job.</summary>
public sealed class CreditsEstimate : InfersoftModel
{
    public int TotalCredits { get; init; }

    public int PageCount { get; init; }

    public string Id { get; init; } = "";
}

/// <summary>A processing job.</summary>
public sealed class Job : InfersoftModel
{
    public long Id { get; init; }

    public string OrganizationId { get; init; } = "";

    public string OrganizationName { get; init; } = "";

    public int TotalDocs { get; init; }

    public int CompletedDocs { get; init; }

    public int ErrorCount { get; init; }

    public JobStatus Status { get; init; }

    public IReadOnlyList<string> Stages { get; init; } = Array.Empty<string>();

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; init; }

    public long? ProjectId { get; init; }

    /// <summary>The raw selectors the job ran with, if any.</summary>
    public JsonElement? Selectors { get; init; }

    public int? Credits { get; init; }
}

/// <summary>One page of job search results.</summary>
public sealed class JobPage : InfersoftModel
{
    public IReadOnlyList<Job> Items { get; init; } = Array.Empty<Job>();

    public int Page { get; init; }

    public int PageSize { get; init; }

    public bool HasMore { get; init; }
}
