using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Infersoft.Internal;

namespace Infersoft;

/// <summary>The Projects resource (<c>client.Projects</c>).</summary>
public sealed class ProjectsResource
{
    private readonly RequestPipeline _http;

    internal ProjectsResource(RequestPipeline http) => _http = http;

    /// <summary>Create a project.</summary>
    public Project Create(string name, string? idempotencyKey = null, CancellationToken cancellationToken = default) =>
        _http.Request<Project>(HttpMethod.Post, "/api/projects", new ProjectCreateRequest { Name = name },
            idempotencyKey: idempotencyKey ?? RequestPipeline.NewIdempotencyKey(), cancellationToken: cancellationToken)!;

    /// <inheritdoc cref="Create"/>
    public async Task<Project> CreateAsync(string name, string? idempotencyKey = null, CancellationToken cancellationToken = default) =>
        (await _http.RequestAsync<Project>(HttpMethod.Post, "/api/projects", new ProjectCreateRequest { Name = name },
            idempotencyKey: idempotencyKey ?? RequestPipeline.NewIdempotencyKey(), cancellationToken: cancellationToken).ConfigureAwait(false))!;

    /// <summary>Get a project by id.</summary>
    public Project Get(long projectId, CancellationToken cancellationToken = default) =>
        _http.Request<Project>(HttpMethod.Get, $"/api/projects/{projectId}", cancellationToken: cancellationToken)!;

    /// <inheritdoc cref="Get"/>
    public async Task<Project> GetAsync(long projectId, CancellationToken cancellationToken = default) =>
        (await _http.RequestAsync<Project>(HttpMethod.Get, $"/api/projects/{projectId}", cancellationToken: cancellationToken).ConfigureAwait(false))!;

    /// <summary>
    /// Return the project whose name equals <paramref name="name"/> exactly, creating it if absent.
    /// Recovers from a creation race (a concurrent creator) by re-fetching.
    /// </summary>
    public Project GetOrCreate(string name, string? idempotencyKey = null, CancellationToken cancellationToken = default)
    {
        var existing = FindByExactName(name, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            return Create(name, idempotencyKey, cancellationToken);
        }
        catch (ConflictException)
        {
            // Lost a creation race; the project exists now.
            var raced = FindByExactName(name, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }

            throw;
        }
    }

    /// <inheritdoc cref="GetOrCreate"/>
    public async Task<Project> GetOrCreateAsync(string name, string? idempotencyKey = null, CancellationToken cancellationToken = default)
    {
        var existing = await FindByExactNameAsync(name, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            return await CreateAsync(name, idempotencyKey, cancellationToken).ConfigureAwait(false);
        }
        catch (ConflictException)
        {
            var raced = await FindByExactNameAsync(name, cancellationToken).ConfigureAwait(false);
            if (raced is not null)
            {
                return raced;
            }

            throw;
        }
    }

    /// <summary>Search projects (one page). <paramref name="q"/> is a fuzzy name match.</summary>
    public ProjectPage Search(string? q = null, int page = 1, int pageSize = 50, string orderBy = "id", string orderDir = "asc", CancellationToken cancellationToken = default) =>
        _http.Request<ProjectPage>(HttpMethod.Post, "/api/projects/search", BuildSearch(q, page, pageSize, orderBy, orderDir),
            idempotent: true, cancellationToken: cancellationToken)!;

    /// <inheritdoc cref="Search"/>
    public async Task<ProjectPage> SearchAsync(string? q = null, int page = 1, int pageSize = 50, string orderBy = "id", string orderDir = "asc", CancellationToken cancellationToken = default) =>
        (await _http.RequestAsync<ProjectPage>(HttpMethod.Post, "/api/projects/search", BuildSearch(q, page, pageSize, orderBy, orderDir),
            idempotent: true, cancellationToken: cancellationToken).ConfigureAwait(false))!;

    /// <summary>Yield every matching project, fetching pages on demand.</summary>
    public IEnumerable<Project> Iterate(string? q = null, int pageSize = 50, string orderBy = "id", string orderDir = "asc", CancellationToken cancellationToken = default)
    {
        var page = 1;
        while (true)
        {
            var result = Search(q, page, pageSize, orderBy, orderDir, cancellationToken);
            foreach (var item in result.Items)
            {
                yield return item;
            }

            if (!result.HasMore)
            {
                yield break;
            }

            page++;
        }
    }

    /// <inheritdoc cref="Iterate"/>
    public async IAsyncEnumerable<Project> IterateAsync(string? q = null, int pageSize = 50, string orderBy = "id", string orderDir = "asc", [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var page = 1;
        while (true)
        {
            var result = await SearchAsync(q, page, pageSize, orderBy, orderDir, cancellationToken).ConfigureAwait(false);
            foreach (var item in result.Items)
            {
                yield return item;
            }

            if (!result.HasMore)
            {
                yield break;
            }

            page++;
        }
    }

    /// <summary>Assign documents (matched by <paramref name="selectors"/> or <paramref name="documentIds"/>) to a project.</summary>
    public AssignDocumentsResult AssignDocuments(
        Selectors? selectors = null, IEnumerable<long>? documentIds = null, long? projectId = null, string? projectName = null, string? idempotencyKey = null, CancellationToken cancellationToken = default) =>
        _http.Request<AssignDocumentsResult>(HttpMethod.Post, "/api/projects/assign-documents", BuildAssign(selectors, documentIds, projectId, projectName),
            idempotencyKey: idempotencyKey ?? RequestPipeline.NewIdempotencyKey(), cancellationToken: cancellationToken)!;

    /// <inheritdoc cref="AssignDocuments"/>
    public async Task<AssignDocumentsResult> AssignDocumentsAsync(
        Selectors? selectors = null, IEnumerable<long>? documentIds = null, long? projectId = null, string? projectName = null, string? idempotencyKey = null, CancellationToken cancellationToken = default) =>
        (await _http.RequestAsync<AssignDocumentsResult>(HttpMethod.Post, "/api/projects/assign-documents", BuildAssign(selectors, documentIds, projectId, projectName),
            idempotencyKey: idempotencyKey ?? RequestPipeline.NewIdempotencyKey(), cancellationToken: cancellationToken).ConfigureAwait(false))!;

    private Project? FindByExactName(string name, CancellationToken cancellationToken)
    {
        foreach (var project in Iterate(name, cancellationToken: cancellationToken))
        {
            if (string.Equals(project.Name, name, StringComparison.Ordinal))
            {
                return project;
            }
        }

        return null;
    }

    private async Task<Project?> FindByExactNameAsync(string name, CancellationToken cancellationToken)
    {
        await foreach (var project in IterateAsync(name, cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            if (string.Equals(project.Name, name, StringComparison.Ordinal))
            {
                return project;
            }
        }

        return null;
    }

    private static ProjectSearchRequest BuildSearch(string? q, int page, int pageSize, string orderBy, string orderDir) =>
        new() { Q = q, Page = page, PageSize = pageSize, OrderBy = orderBy, OrderDir = orderDir };

    private static AssignDocumentsRequest BuildAssign(Selectors? selectors, IEnumerable<long>? documentIds, long? projectId, string? projectName) =>
        new()
        {
            Selectors = SelectorResolver.Resolve(selectors, documentIds, required: true),
            ProjectId = projectId,
            ProjectName = projectName,
        };
}
