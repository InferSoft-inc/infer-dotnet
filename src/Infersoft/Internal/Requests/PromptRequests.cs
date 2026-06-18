namespace Infersoft.Internal;

internal sealed class PromptSearchRequest
{
    public string? Q { get; set; }

    public string? DocumentClass { get; set; }

    public bool IncludeDeleted { get; set; }

    public int Page { get; set; }

    public int PageSize { get; set; }

    public string OrderBy { get; set; } = "name";

    public string OrderDir { get; set; } = "asc";
}
