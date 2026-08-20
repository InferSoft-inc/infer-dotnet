using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Infersoft;

/// <summary>A bounding box on a page (wire fields are PascalCase).</summary>
public sealed class ExtractionTracebackBBox : InfersoftModel
{
    [JsonPropertyName("Top")]
    public double Top { get; init; }

    [JsonPropertyName("Left")]
    public double Left { get; init; }

    [JsonPropertyName("Width")]
    public double Width { get; init; }

    [JsonPropertyName("Height")]
    public double Height { get; init; }
}

/// <summary>One source location supporting an extracted value.</summary>
public sealed class ExtractionTracebackItem : InfersoftModel
{
    public ExtractionTracebackBBox? Bbox { get; init; }

    public int Page { get; init; }

    public double Confidence { get; init; }

    public IReadOnlyList<string> Issues { get; init; } = Array.Empty<string>();
}

/// <summary>An extracted value plus its provenance.</summary>
public sealed class ExtractionResultValue : InfersoftModel
{
    public string? Name { get; init; }

    public DataTypeName? DataType { get; init; }

    /// <summary>Reference-only display metadata from the prompt; <see cref="DataType"/> drives parsing.</summary>
    public string? DisplayType { get; init; }

    public string? GroupName { get; init; }

    /// <summary>The parsed value (raw JSON; its shape depends on <see cref="DataType"/>).</summary>
    public JsonElement? ParsedValue { get; init; }

    public string? RawValue { get; init; }

    public IReadOnlyList<ExtractionTracebackItem> Traceback { get; init; } = Array.Empty<ExtractionTracebackItem>();

    public IReadOnlyList<string> Issues { get; init; } = Array.Empty<string>();

    public double? Readability { get; init; }
}

/// <summary>One prompt's extracted value, returned when prompt ids are passed to document search/get.</summary>
public sealed class DocumentExtractionResultItem : InfersoftModel
{
    public long PromptId { get; init; }

    public ExtractionResultValue Value { get; init; } = new();
}

/// <summary>A short-lived signed URL for downloading a document's file.</summary>
public sealed class DownloadResponse : InfersoftModel
{
    public string Url { get; init; } = "";

    public DateTimeOffset ExpiresAt { get; init; }
}

/// <summary>A document and its metadata.</summary>
public sealed class DocumentSummary : InfersoftModel
{
    public long Id { get; init; }

    public string OrganizationId { get; init; } = "";

    public string Name { get; init; } = "";

    public DocumentStatus Status { get; init; }

    public bool IsValid { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public bool HasActiveWorkflow { get; init; }

    public long? FileSize { get; init; }

    public int? PageCount { get; init; }

    public long? SourceDocument { get; init; }

    public long? FolderId { get; init; }

    public string? DocumentClass { get; init; }

    public string? DocumentSubclass { get; init; }

    public IReadOnlyList<DocumentExtractionResultItem>? ExtractionResults { get; init; }

    /// <summary>Extraction values keyed by prompt id (populated when prompt ids were requested).</summary>
    [JsonIgnore]
    public IReadOnlyDictionary<long, ExtractionResultValue> Extractions =>
        (ExtractionResults ?? Array.Empty<DocumentExtractionResultItem>())
            .ToDictionary(item => item.PromptId, item => item.Value);
}

/// <summary>One page of document search results.</summary>
public sealed class DocumentPage : InfersoftModel
{
    public IReadOnlyList<DocumentSummary> Items { get; init; } = Array.Empty<DocumentSummary>();

    public int Page { get; init; }

    public int PageSize { get; init; }

    public bool HasMore { get; init; }
}

/// <summary>Outcome of <c>Documents.MoveToFolder</c>.</summary>
public sealed class MoveResult : InfersoftModel
{
    public int Matched { get; init; }

    public int Moved { get; init; }

    public int Skipped { get; init; }
}

/// <summary>Outcome of <c>Documents.BulkDelete</c> (nothing is deleted when <see cref="DryRun"/> is true).</summary>
public sealed class BulkDeleteResult : InfersoftModel
{
    public int Matched { get; init; }

    public bool DryRun { get; init; }
}
