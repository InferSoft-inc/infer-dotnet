using System;
using System.Collections.Generic;

namespace Infersoft.Internal;

internal sealed class JobEstimateRequest
{
    public IReadOnlyList<string> Steps { get; set; } = Array.Empty<string>();

    public Selectors? Selectors { get; set; }

    public bool Synchronous { get; set; }

    public IReadOnlyList<long>? Prompts { get; set; }
}

internal sealed class JobStartRequest
{
    public string CreditsId { get; set; } = "";

    public long? ProjectId { get; set; }
}

internal sealed class JobSearchRequest
{
    public IReadOnlyList<string>? Statuses { get; set; }

    public string? CreatedFrom { get; set; }

    public string? CreatedTo { get; set; }

    public int Page { get; set; }

    public int PageSize { get; set; }

    public string OrderBy { get; set; } = "id";

    public string OrderDir { get; set; } = "desc";
}
