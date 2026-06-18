using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Infersoft.Internal;

namespace Infersoft;

/// <summary>
/// The Documents resource (<c>client.Documents</c>). Every method has a synchronous and an
/// asynchronous (<c>…Async</c>) member; list endpoints expose both an explicit <see cref="Search"/>
/// page and an auto-paginating <see cref="Iterate"/>.
/// </summary>
public sealed partial class DocumentsResource
{
    /// <summary>Maximum files accepted in a single <c>Upload</c> call (use <c>UploadMany</c> for more).</summary>
    public const int MaxBatchFiles = 100;

    private readonly RequestPipeline _http;
    private readonly ILogger _logger;

    internal DocumentsResource(RequestPipeline http, ILogger logger)
    {
        _http = http;
        _logger = logger;
    }

    /// <summary>Get a single document, optionally populating extraction values for the given prompt ids.</summary>
    public DocumentSummary Get(long documentId, IEnumerable<long>? promptIds = null, CancellationToken cancellationToken = default) =>
        _http.Request<DocumentSummary>(HttpMethod.Get, GetPath(documentId, promptIds), cancellationToken: cancellationToken)!;

    /// <inheritdoc cref="Get"/>
    public async Task<DocumentSummary> GetAsync(long documentId, IEnumerable<long>? promptIds = null, CancellationToken cancellationToken = default) =>
        (await _http.RequestAsync<DocumentSummary>(HttpMethod.Get, GetPath(documentId, promptIds), cancellationToken: cancellationToken).ConfigureAwait(false))!;

    /// <summary>Search documents (one page). Target with <paramref name="selectors"/> or the <paramref name="documentIds"/> shortcut.</summary>
    public DocumentPage Search(
        Selectors? selectors = null,
        IEnumerable<long>? documentIds = null,
        IEnumerable<long>? prompts = null,
        int page = 1,
        int pageSize = 50,
        string orderBy = "id",
        string orderDir = "asc",
        CancellationToken cancellationToken = default) =>
        _http.Request<DocumentPage>(
            HttpMethod.Post, "/api/documents/search",
            BuildSearch(selectors, documentIds, prompts, page, pageSize, orderBy, orderDir),
            idempotent: true, cancellationToken: cancellationToken)!;

    /// <inheritdoc cref="Search"/>
    public async Task<DocumentPage> SearchAsync(
        Selectors? selectors = null,
        IEnumerable<long>? documentIds = null,
        IEnumerable<long>? prompts = null,
        int page = 1,
        int pageSize = 50,
        string orderBy = "id",
        string orderDir = "asc",
        CancellationToken cancellationToken = default) =>
        (await _http.RequestAsync<DocumentPage>(
            HttpMethod.Post, "/api/documents/search",
            BuildSearch(selectors, documentIds, prompts, page, pageSize, orderBy, orderDir),
            idempotent: true, cancellationToken: cancellationToken).ConfigureAwait(false))!;

    /// <summary>Yield every matching document, fetching pages on demand.</summary>
    public IEnumerable<DocumentSummary> Iterate(
        Selectors? selectors = null,
        IEnumerable<long>? documentIds = null,
        IEnumerable<long>? prompts = null,
        int pageSize = 50,
        string orderBy = "id",
        string orderDir = "asc",
        CancellationToken cancellationToken = default)
    {
        var resolved = SelectorResolver.Resolve(selectors, documentIds, required: false);
        var promptList = prompts?.ToList();
        var page = 1;
        while (true)
        {
            var result = Search(resolved, prompts: promptList, page: page, pageSize: pageSize, orderBy: orderBy, orderDir: orderDir, cancellationToken: cancellationToken);
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
    public async IAsyncEnumerable<DocumentSummary> IterateAsync(
        Selectors? selectors = null,
        IEnumerable<long>? documentIds = null,
        IEnumerable<long>? prompts = null,
        int pageSize = 50,
        string orderBy = "id",
        string orderDir = "asc",
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var resolved = SelectorResolver.Resolve(selectors, documentIds, required: false);
        var promptList = prompts?.ToList();
        var page = 1;
        while (true)
        {
            var result = await SearchAsync(resolved, prompts: promptList, page: page, pageSize: pageSize, orderBy: orderBy, orderDir: orderDir, cancellationToken: cancellationToken).ConfigureAwait(false);
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

    /// <summary>Fetch extraction values as plain dictionaries: <c>{documentId: {field: parsedValue}}</c>.</summary>
    public IReadOnlyDictionary<long, IReadOnlyDictionary<object, JsonElement?>> GetValues(
        IEnumerable<long> prompts,
        Selectors? selectors = null,
        IEnumerable<long>? documentIds = null,
        ExtractionKey keyBy = ExtractionKey.Name,
        CancellationToken cancellationToken = default)
    {
        var output = new Dictionary<long, IReadOnlyDictionary<object, JsonElement?>>();
        foreach (var doc in Iterate(selectors, documentIds, prompts, cancellationToken: cancellationToken))
        {
            output[doc.Id] = ExtractionFlattener.Flatten(doc, keyBy);
        }

        return output;
    }

    /// <inheritdoc cref="GetValues"/>
    public async Task<IReadOnlyDictionary<long, IReadOnlyDictionary<object, JsonElement?>>> GetValuesAsync(
        IEnumerable<long> prompts,
        Selectors? selectors = null,
        IEnumerable<long>? documentIds = null,
        ExtractionKey keyBy = ExtractionKey.Name,
        CancellationToken cancellationToken = default)
    {
        var output = new Dictionary<long, IReadOnlyDictionary<object, JsonElement?>>();
        await foreach (var doc in IterateAsync(selectors, documentIds, prompts, cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            output[doc.Id] = ExtractionFlattener.Flatten(doc, keyBy);
        }

        return output;
    }

    /// <summary>Move documents into a folder (omit <paramref name="targetFolderId"/> for the root).</summary>
    public MoveResult MoveToFolder(
        Selectors? selectors = null,
        IEnumerable<long>? documentIds = null,
        long? targetFolderId = null,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        _http.Request<MoveResult>(
            HttpMethod.Post, "/api/documents/move-to-folder",
            BuildMove(selectors, documentIds, targetFolderId),
            idempotencyKey: idempotencyKey ?? RequestPipeline.NewIdempotencyKey(),
            cancellationToken: cancellationToken)!;

    /// <inheritdoc cref="MoveToFolder"/>
    public async Task<MoveResult> MoveToFolderAsync(
        Selectors? selectors = null,
        IEnumerable<long>? documentIds = null,
        long? targetFolderId = null,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        (await _http.RequestAsync<MoveResult>(
            HttpMethod.Post, "/api/documents/move-to-folder",
            BuildMove(selectors, documentIds, targetFolderId),
            idempotencyKey: idempotencyKey ?? RequestPipeline.NewIdempotencyKey(),
            cancellationToken: cancellationToken).ConfigureAwait(false))!;

    /// <summary>Delete documents matched by <paramref name="selectors"/> (or <paramref name="documentIds"/>); <paramref name="dryRun"/> returns the matched count only.</summary>
    public BulkDeleteResult BulkDelete(
        Selectors? selectors = null,
        IEnumerable<long>? documentIds = null,
        bool dryRun = false,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        _http.Request<BulkDeleteResult>(
            HttpMethod.Post, "/api/documents/bulk_delete",
            BuildBulkDelete(selectors, documentIds, dryRun),
            idempotencyKey: idempotencyKey ?? RequestPipeline.NewIdempotencyKey(),
            cancellationToken: cancellationToken)!;

    /// <inheritdoc cref="BulkDelete"/>
    public async Task<BulkDeleteResult> BulkDeleteAsync(
        Selectors? selectors = null,
        IEnumerable<long>? documentIds = null,
        bool dryRun = false,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        (await _http.RequestAsync<BulkDeleteResult>(
            HttpMethod.Post, "/api/documents/bulk_delete",
            BuildBulkDelete(selectors, documentIds, dryRun),
            idempotencyKey: idempotencyKey ?? RequestPipeline.NewIdempotencyKey(),
            cancellationToken: cancellationToken).ConfigureAwait(false))!;

    /// <summary>Delete a single document (idempotent).</summary>
    public void Delete(long documentId, CancellationToken cancellationToken = default) =>
        _http.Request<object>(HttpMethod.Delete, $"/api/documents/{documentId}", cancellationToken: cancellationToken);

    /// <inheritdoc cref="Delete"/>
    public Task DeleteAsync(long documentId, CancellationToken cancellationToken = default) =>
        _http.RequestAsync<object>(HttpMethod.Delete, $"/api/documents/{documentId}", cancellationToken: cancellationToken);

    private static string GetPath(long documentId, IEnumerable<long>? promptIds)
    {
        var path = $"/api/documents/{documentId}";
        if (promptIds is not null)
        {
            var joined = string.Join(",", promptIds);
            if (joined.Length > 0)
            {
                path += "?prompt_ids=" + joined;
            }
        }

        return path;
    }

    private static DocumentSearchRequest BuildSearch(
        Selectors? selectors, IEnumerable<long>? documentIds, IEnumerable<long>? prompts,
        int page, int pageSize, string orderBy, string orderDir) =>
        new()
        {
            Page = page,
            PageSize = pageSize,
            OrderBy = orderBy,
            OrderDir = orderDir,
            Selectors = SelectorResolver.Resolve(selectors, documentIds, required: false),
            Prompts = prompts?.ToList(),
        };

    private static MoveToFolderRequest BuildMove(Selectors? selectors, IEnumerable<long>? documentIds, long? targetFolderId) =>
        new()
        {
            Selectors = SelectorResolver.Resolve(selectors, documentIds, required: true),
            TargetFolderId = targetFolderId,
        };

    private static DocumentBulkDeleteRequest BuildBulkDelete(Selectors? selectors, IEnumerable<long>? documentIds, bool dryRun) =>
        new()
        {
            Selectors = SelectorResolver.Resolve(selectors, documentIds, required: true),
            DryRun = dryRun,
        };
}
