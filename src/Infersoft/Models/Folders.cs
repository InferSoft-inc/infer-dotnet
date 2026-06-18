using System;
using System.Collections.Generic;

namespace Infersoft;

/// <summary>A folder in the document hierarchy.</summary>
public sealed class Folder : InfersoftModel
{
    public long Id { get; init; }

    public string OrganizationId { get; init; } = "";

    public string Name { get; init; } = "";

    public long? ParentId { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    public bool HasChildren { get; init; }
}

/// <summary>One page of folder search results.</summary>
public sealed class FolderPage : InfersoftModel
{
    public IReadOnlyList<Folder> Items { get; init; } = Array.Empty<Folder>();

    public int Page { get; init; }

    public int PageSize { get; init; }

    public bool HasMore { get; init; }
}

/// <summary>Outcome of <c>Folders.Move</c>; the skipped lists explain non-moves.</summary>
public sealed class FolderMoveResult : InfersoftModel
{
    public int Matched { get; init; }

    public IReadOnlyList<long> MovedIds { get; init; } = Array.Empty<long>();

    public IReadOnlyList<long> SkippedAlreadyInTargetIds { get; init; } = Array.Empty<long>();

    public IReadOnlyList<long> SkippedNameConflictIds { get; init; } = Array.Empty<long>();

    public IReadOnlyList<long> SkippedDuplicateInSelectionIds { get; init; } = Array.Empty<long>();

    public IReadOnlyList<long> SkippedInvalidParentIds { get; init; } = Array.Empty<long>();
}

/// <summary>Outcome of <c>Folders.BulkDelete</c>.</summary>
public sealed class FolderBulkDeleteResult : InfersoftModel
{
    public int Requested { get; init; }

    public int Deleted { get; init; }

    public IReadOnlyList<long> DeletedFolderIds { get; init; } = Array.Empty<long>();

    public IReadOnlyList<long> NotFoundFolderIds { get; init; } = Array.Empty<long>();
}

/// <summary>One resolved path: its folders ordered from the base parent to the leaf.</summary>
public sealed class FolderPathResult : InfersoftModel
{
    public IReadOnlyList<Folder> Folders { get; init; } = Array.Empty<Folder>();
}

/// <summary>Outcome of <c>Folders.ResolvePaths</c>; <see cref="Results"/> preserves input order.</summary>
public sealed class FolderPathsResult : InfersoftModel
{
    public IReadOnlyList<FolderPathResult> Results { get; init; } = Array.Empty<FolderPathResult>();
}
