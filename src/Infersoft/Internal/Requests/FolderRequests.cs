using System;
using System.Collections.Generic;

namespace Infersoft.Internal;

internal sealed class FolderCreateRequest
{
    public string Name { get; set; } = "";

    public long? ParentId { get; set; }
}

internal sealed class FolderRenameRequest
{
    public string Name { get; set; } = "";
}

internal sealed class FolderSearchRequest
{
    public long? ParentId { get; set; }

    public int Page { get; set; }

    public int PageSize { get; set; }

    public string OrderBy { get; set; } = "id";

    public string OrderDir { get; set; } = "asc";
}

internal sealed class FolderMoveRequest
{
    public IReadOnlyList<long> FolderIds { get; set; } = Array.Empty<long>();

    public long? TargetParentId { get; set; }
}

internal sealed class FolderBulkDeleteRequest
{
    public IReadOnlyList<long> FolderIds { get; set; } = Array.Empty<long>();
}

internal sealed class FolderPathsRequest
{
    public IReadOnlyList<string> Paths { get; set; } = Array.Empty<string>();

    public long? ParentId { get; set; }
}
