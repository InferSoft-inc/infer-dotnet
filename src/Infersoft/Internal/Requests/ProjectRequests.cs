namespace Infersoft.Internal;

internal sealed class ProjectCreateRequest
{
    public string Name { get; set; } = "";
}

internal sealed class ProjectSearchRequest
{
    public string? Q { get; set; }

    public int Page { get; set; }

    public int PageSize { get; set; }

    public string OrderBy { get; set; } = "id";

    public string OrderDir { get; set; } = "asc";
}

internal sealed class AssignDocumentsRequest
{
    public Selectors? Selectors { get; set; }

    public long? ProjectId { get; set; }

    public string? ProjectName { get; set; }
}
