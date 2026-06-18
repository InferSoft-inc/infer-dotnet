using System;
using System.Collections.Generic;

namespace Infersoft;

/// <summary>One page of project search results.</summary>
public sealed class ProjectPage : InfersoftModel
{
    public IReadOnlyList<Project> Items { get; init; } = Array.Empty<Project>();

    public int Page { get; init; }

    public int PageSize { get; init; }

    public bool HasMore { get; init; }
}

/// <summary>Outcome of <c>Projects.AssignDocuments</c>.</summary>
public sealed class AssignDocumentsResult : InfersoftModel
{
    public Project Project { get; init; } = new();

    public int Matched { get; init; }

    public int Added { get; init; }

    public int Skipped { get; init; }
}
