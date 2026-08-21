using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Infersoft;

/// <summary>Metadata for an extractor prompt.</summary>
public sealed class PromptMeta : InfersoftModel
{
    public long Id { get; init; }

    public string OrganizationId { get; init; } = "";

    public string Name { get; init; } = "";

    public string? Description { get; init; }

    public DataTypeName DataType { get; init; }

    public string? DisplayType { get; init; }

    public string? GroupName { get; init; }

    public JsonElement? Example { get; init; }

    public string DocumentClass { get; init; } = "";

    public bool Deleted { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>One page of prompt search results.</summary>
public sealed class PromptPage : InfersoftModel
{
    public IReadOnlyList<PromptMeta> Items { get; init; } = Array.Empty<PromptMeta>();

    public int Page { get; init; }

    public int PageSize { get; init; }

    public bool HasMore { get; init; }
}
