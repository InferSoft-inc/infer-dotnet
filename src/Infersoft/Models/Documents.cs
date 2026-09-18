using System;
using System.Collections.Generic;
using System.Globalization;
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

/// <summary>
/// An extracted value plus its provenance. Exactly one of <see cref="ValueText"/>,
/// <see cref="ValueNumber"/>, <see cref="ValueBool"/> and <see cref="ValueDate"/> is set when
/// the raw value could be typed, chosen by <see cref="DataType"/>; none is set when it could not
/// (<see cref="RawValue"/> still carries the text). Read <see cref="Value"/> for the typed value.
/// </summary>
public sealed class ExtractionResultValue : InfersoftModel
{
    public string? Name { get; init; }

    public DataTypeName? DataType { get; init; }

    public string? DisplayType { get; init; }

    public string? GroupName { get; init; }

    /// <summary>
    /// Deprecated: read <see cref="Value"/> or the <c>Value*</c> properties instead. Numbers arrive
    /// here as JSON numbers and may lose precision; dates arrive as <c>YYYY-MM-DD</c> strings.
    /// </summary>
    public JsonElement? ParsedValue { get; init; }

    /// <summary>Typed value when <see cref="DataType"/> is <c>String</c>.</summary>
    public string? ValueText { get; init; }

    /// <summary>
    /// Typed value when <see cref="DataType"/> is <c>Number</c>, as the server's decimal string with
    /// full precision. See <see cref="ValueNumberDecimal"/> for a <see cref="decimal"/>.
    /// </summary>
    public string? ValueNumber { get; init; }

    /// <summary>Typed value when <see cref="DataType"/> is <c>Boolean</c>.</summary>
    public bool? ValueBool { get; init; }

    /// <summary>Typed value when <see cref="DataType"/> is <c>Date</c> (date part only, <see cref="DateTimeKind.Unspecified"/>).</summary>
    public DateTime? ValueDate { get; init; }

    /// <summary>
    /// <see cref="ValueNumber"/> as a <see cref="decimal"/>, or <c>null</c> when it is unset or
    /// exceeds the range of <see cref="decimal"/>.
    /// </summary>
    [JsonIgnore]
    public decimal? ValueNumberDecimal =>
        ValueNumber is not null &&
        decimal.TryParse(ValueNumber, NumberStyles.Number | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out var d)
            ? d
            : null;

    /// <summary>
    /// The typed value: a <see cref="string"/>, <see cref="decimal"/> (or the decimal string when it
    /// exceeds <see cref="decimal"/>), <see cref="bool"/> or <see cref="DateTime"/>, whichever
    /// <c>Value*</c> property is set; falls back to <see cref="ParsedValue"/> against servers that
    /// predate the typed fields.
    /// </summary>
    [JsonIgnore]
    public object? Value
    {
        get
        {
            if (ValueText is not null) return ValueText;
            if (ValueNumber is not null) return (object?)ValueNumberDecimal ?? ValueNumber;
            if (ValueBool is not null) return ValueBool;
            if (ValueDate is not null) return ValueDate;
            return ParsedValue;
        }
    }

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
