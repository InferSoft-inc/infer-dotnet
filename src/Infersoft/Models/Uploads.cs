using System;
using System.Collections.Generic;
using System.Linq;

namespace Infersoft;

/// <summary>A tag attached to documents.</summary>
public sealed class Tag : InfersoftModel
{
    public long Id { get; init; }

    public string Name { get; init; } = "";
}

/// <summary>Why an upload item could not be planned.</summary>
public sealed class UploadErrorInfo : InfersoftModel
{
    public string Code { get; init; } = "";

    public string Message { get; init; } = "";

    public long? FileSize { get; init; }

    public long? MaxSize { get; init; }

    public bool? Retryable { get; init; }
}

/// <summary>An existing document whose content md5 matches an upload item (advisory only).</summary>
public sealed class UploadDuplicate : InfersoftModel
{
    public long DocumentId { get; init; }

    public string Name { get; init; } = "";

    public long? FileSize { get; init; }

    public int? PageCount { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }
}

/// <summary>One planned upload entry returned by <c>POST /api/uploads</c>.</summary>
public sealed class UploadItem : InfersoftModel
{
    public string ClientFileName { get; init; } = "";

    public long? DocumentId { get; init; }

    public string? UploadMode { get; init; }

    public string? PutUrl { get; init; }

    public IReadOnlyDictionary<string, string>? RequiredHeaders { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }

    public UploadErrorInfo? Error { get; init; }

    public IReadOnlyList<UploadDuplicate> Duplicates { get; init; } = Array.Empty<UploadDuplicate>();
}

/// <summary>A project (a named grouping of documents).</summary>
public sealed class Project : InfersoftModel
{
    public long Id { get; init; }

    public string OrganizationId { get; init; } = "";

    public string Name { get; init; } = "";

    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>The raw response of <c>POST /api/uploads</c>.</summary>
public sealed class BatchUploadResponse : InfersoftModel
{
    public IReadOnlyList<UploadItem> Items { get; init; } = Array.Empty<UploadItem>();

    public Project? Project { get; init; }

    public IReadOnlyList<Tag> Tags { get; init; } = Array.Empty<Tag>();
}

/// <summary>Per-file result of a high-level <c>Documents.Upload</c> call.</summary>
public sealed class FileOutcome
{
    public string FileName { get; internal set; } = "";

    public long? DocumentId { get; internal set; }

    public bool Uploaded { get; internal set; }

    public UploadErrorInfo? Error { get; internal set; }

    /// <summary>Existing documents whose content md5 matched this file (advisory hint).</summary>
    public IReadOnlyList<UploadDuplicate> Duplicates { get; internal set; } = Array.Empty<UploadDuplicate>();

    /// <summary>True when <c>onDuplicate=Block</c> skipped this file as a known duplicate.</summary>
    public bool SkippedAsDuplicate { get; internal set; }
}

/// <summary>The outcome of a high-level <c>Documents.Upload</c> / <c>UploadMany</c> call.</summary>
public sealed class UploadResult
{
    public IReadOnlyList<FileOutcome> Outcomes { get; internal set; } = Array.Empty<FileOutcome>();

    public Project? Project { get; internal set; }

    public IReadOnlyList<Tag> Tags { get; internal set; } = Array.Empty<Tag>();

    /// <summary>The ids of the documents created by this upload.</summary>
    public IReadOnlyList<long> DocumentIds =>
        Outcomes.Where(o => o.DocumentId.HasValue).Select(o => o.DocumentId!.Value).ToList();

    /// <summary>Items that uploaded successfully.</summary>
    public IReadOnlyList<FileOutcome> Succeeded => Outcomes.Where(o => o.Uploaded).ToList();

    /// <summary>Items that errored (excludes ones skipped as duplicates).</summary>
    public IReadOnlyList<FileOutcome> Failed =>
        Outcomes.Where(o => !o.Uploaded && !o.SkippedAsDuplicate).ToList();

    /// <summary>Items skipped because they duplicated existing content (<c>onDuplicate=Block</c>).</summary>
    public IReadOnlyList<FileOutcome> Skipped => Outcomes.Where(o => o.SkippedAsDuplicate).ToList();
}
