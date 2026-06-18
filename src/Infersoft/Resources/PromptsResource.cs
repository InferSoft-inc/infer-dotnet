using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Infersoft.Internal;

namespace Infersoft;

/// <summary>The Prompts resource (<c>client.Prompts</c>), read-only.</summary>
public sealed class PromptsResource
{
    private readonly RequestPipeline _http;

    internal PromptsResource(RequestPipeline http) => _http = http;

    /// <summary>Search extractor prompts. <paramref name="q"/> is a fuzzy match on the prompt name.</summary>
    public PromptPage Search(
        string? q = null,
        string? documentClass = null,
        bool includeDeleted = false,
        int page = 1,
        int pageSize = 50,
        string orderBy = "name",
        string orderDir = "asc",
        CancellationToken cancellationToken = default) =>
        _http.Request<PromptPage>(HttpMethod.Post, "/api/prompts/search",
            BuildSearch(q, documentClass, includeDeleted, page, pageSize, orderBy, orderDir),
            idempotent: true, cancellationToken: cancellationToken)!;

    /// <inheritdoc cref="Search"/>
    public async Task<PromptPage> SearchAsync(
        string? q = null,
        string? documentClass = null,
        bool includeDeleted = false,
        int page = 1,
        int pageSize = 50,
        string orderBy = "name",
        string orderDir = "asc",
        CancellationToken cancellationToken = default) =>
        (await _http.RequestAsync<PromptPage>(HttpMethod.Post, "/api/prompts/search",
            BuildSearch(q, documentClass, includeDeleted, page, pageSize, orderBy, orderDir),
            idempotent: true, cancellationToken: cancellationToken).ConfigureAwait(false))!;

    /// <summary>Yield every matching prompt, fetching pages on demand.</summary>
    public IEnumerable<PromptMeta> Iterate(
        string? q = null,
        string? documentClass = null,
        bool includeDeleted = false,
        int pageSize = 50,
        string orderBy = "name",
        string orderDir = "asc",
        CancellationToken cancellationToken = default)
    {
        var page = 1;
        while (true)
        {
            var result = Search(q, documentClass, includeDeleted, page, pageSize, orderBy, orderDir, cancellationToken);
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
    public async IAsyncEnumerable<PromptMeta> IterateAsync(
        string? q = null,
        string? documentClass = null,
        bool includeDeleted = false,
        int pageSize = 50,
        string orderBy = "name",
        string orderDir = "asc",
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var page = 1;
        while (true)
        {
            var result = await SearchAsync(q, documentClass, includeDeleted, page, pageSize, orderBy, orderDir, cancellationToken).ConfigureAwait(false);
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

    private static PromptSearchRequest BuildSearch(
        string? q, string? documentClass, bool includeDeleted, int page, int pageSize, string orderBy, string orderDir) =>
        new()
        {
            Q = q,
            DocumentClass = documentClass,
            IncludeDeleted = includeDeleted,
            Page = page,
            PageSize = pageSize,
            OrderBy = orderBy,
            OrderDir = orderDir,
        };
}
