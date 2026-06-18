using System.Collections.Generic;

namespace Infersoft.Internal;

internal sealed class DocumentSearchRequest
{
    public int Page { get; set; }

    public int PageSize { get; set; }

    public string OrderBy { get; set; } = "id";

    public string OrderDir { get; set; } = "asc";

    public Selectors? Selectors { get; set; }

    public IReadOnlyList<long>? Prompts { get; set; }
}

internal sealed class MoveToFolderRequest
{
    public Selectors? Selectors { get; set; }

    public long? TargetFolderId { get; set; }
}

internal sealed class DocumentBulkDeleteRequest
{
    public Selectors? Selectors { get; set; }

    public bool DryRun { get; set; }
}
