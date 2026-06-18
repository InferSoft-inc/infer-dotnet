using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Infersoft.Internal;

namespace Infersoft;

/// <summary>The Folders resource (<c>client.Folders</c>).</summary>
public sealed class FoldersResource
{
    private static readonly HttpMethod Patch = new("PATCH");

    private readonly RequestPipeline _http;

    internal FoldersResource(RequestPipeline http) => _http = http;

    /// <summary>Create a folder (omit <paramref name="parentId"/> to create it at the root).</summary>
    public Folder Create(string name, long? parentId = null, string? idempotencyKey = null, CancellationToken cancellationToken = default) =>
        _http.Request<Folder>(HttpMethod.Post, "/api/folders", new FolderCreateRequest { Name = name, ParentId = parentId },
            idempotencyKey: idempotencyKey ?? RequestPipeline.NewIdempotencyKey(), cancellationToken: cancellationToken)!;

    /// <inheritdoc cref="Create"/>
    public async Task<Folder> CreateAsync(string name, long? parentId = null, string? idempotencyKey = null, CancellationToken cancellationToken = default) =>
        (await _http.RequestAsync<Folder>(HttpMethod.Post, "/api/folders", new FolderCreateRequest { Name = name, ParentId = parentId },
            idempotencyKey: idempotencyKey ?? RequestPipeline.NewIdempotencyKey(), cancellationToken: cancellationToken).ConfigureAwait(false))!;

    /// <summary>Get a folder by id.</summary>
    public Folder Get(long folderId, CancellationToken cancellationToken = default) =>
        _http.Request<Folder>(HttpMethod.Get, $"/api/folders/{folderId}", cancellationToken: cancellationToken)!;

    /// <inheritdoc cref="Get"/>
    public async Task<Folder> GetAsync(long folderId, CancellationToken cancellationToken = default) =>
        (await _http.RequestAsync<Folder>(HttpMethod.Get, $"/api/folders/{folderId}", cancellationToken: cancellationToken).ConfigureAwait(false))!;

    /// <summary>Rename a folder.</summary>
    public Folder Rename(long folderId, string name, string? idempotencyKey = null, CancellationToken cancellationToken = default) =>
        _http.Request<Folder>(Patch, $"/api/folders/{folderId}", new FolderRenameRequest { Name = name },
            idempotencyKey: idempotencyKey ?? RequestPipeline.NewIdempotencyKey(), cancellationToken: cancellationToken)!;

    /// <inheritdoc cref="Rename"/>
    public async Task<Folder> RenameAsync(long folderId, string name, string? idempotencyKey = null, CancellationToken cancellationToken = default) =>
        (await _http.RequestAsync<Folder>(Patch, $"/api/folders/{folderId}", new FolderRenameRequest { Name = name },
            idempotencyKey: idempotencyKey ?? RequestPipeline.NewIdempotencyKey(), cancellationToken: cancellationToken).ConfigureAwait(false))!;

    /// <summary>List folders (omit <paramref name="parentId"/> to list root folders).</summary>
    public FolderPage Search(long? parentId = null, int page = 1, int pageSize = 50, string orderBy = "id", string orderDir = "asc", CancellationToken cancellationToken = default) =>
        _http.Request<FolderPage>(HttpMethod.Post, "/api/folders/search", BuildSearch(parentId, page, pageSize, orderBy, orderDir),
            idempotent: true, cancellationToken: cancellationToken)!;

    /// <inheritdoc cref="Search"/>
    public async Task<FolderPage> SearchAsync(long? parentId = null, int page = 1, int pageSize = 50, string orderBy = "id", string orderDir = "asc", CancellationToken cancellationToken = default) =>
        (await _http.RequestAsync<FolderPage>(HttpMethod.Post, "/api/folders/search", BuildSearch(parentId, page, pageSize, orderBy, orderDir),
            idempotent: true, cancellationToken: cancellationToken).ConfigureAwait(false))!;

    /// <summary>Yield every matching folder, fetching pages on demand.</summary>
    public IEnumerable<Folder> Iterate(long? parentId = null, int pageSize = 50, string orderBy = "id", string orderDir = "asc", CancellationToken cancellationToken = default)
    {
        var page = 1;
        while (true)
        {
            var result = Search(parentId, page, pageSize, orderBy, orderDir, cancellationToken);
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
    public async IAsyncEnumerable<Folder> IterateAsync(long? parentId = null, int pageSize = 50, string orderBy = "id", string orderDir = "asc", [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var page = 1;
        while (true)
        {
            var result = await SearchAsync(parentId, page, pageSize, orderBy, orderDir, cancellationToken).ConfigureAwait(false);
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

    /// <summary>Move folders under a new parent (omit <paramref name="targetParentId"/> for root).</summary>
    public FolderMoveResult Move(IEnumerable<long> folderIds, long? targetParentId = null, string? idempotencyKey = null, CancellationToken cancellationToken = default) =>
        _http.Request<FolderMoveResult>(HttpMethod.Post, "/api/folders/move", new FolderMoveRequest { FolderIds = folderIds.ToList(), TargetParentId = targetParentId },
            idempotencyKey: idempotencyKey ?? RequestPipeline.NewIdempotencyKey(), cancellationToken: cancellationToken)!;

    /// <inheritdoc cref="Move"/>
    public async Task<FolderMoveResult> MoveAsync(IEnumerable<long> folderIds, long? targetParentId = null, string? idempotencyKey = null, CancellationToken cancellationToken = default) =>
        (await _http.RequestAsync<FolderMoveResult>(HttpMethod.Post, "/api/folders/move", new FolderMoveRequest { FolderIds = folderIds.ToList(), TargetParentId = targetParentId },
            idempotencyKey: idempotencyKey ?? RequestPipeline.NewIdempotencyKey(), cancellationToken: cancellationToken).ConfigureAwait(false))!;

    /// <summary>Delete folders by id.</summary>
    public FolderBulkDeleteResult BulkDelete(IEnumerable<long> folderIds, string? idempotencyKey = null, CancellationToken cancellationToken = default) =>
        _http.Request<FolderBulkDeleteResult>(HttpMethod.Post, "/api/folders/bulk_delete", new FolderBulkDeleteRequest { FolderIds = folderIds.ToList() },
            idempotencyKey: idempotencyKey ?? RequestPipeline.NewIdempotencyKey(), cancellationToken: cancellationToken)!;

    /// <inheritdoc cref="BulkDelete"/>
    public async Task<FolderBulkDeleteResult> BulkDeleteAsync(IEnumerable<long> folderIds, string? idempotencyKey = null, CancellationToken cancellationToken = default) =>
        (await _http.RequestAsync<FolderBulkDeleteResult>(HttpMethod.Post, "/api/folders/bulk_delete", new FolderBulkDeleteRequest { FolderIds = folderIds.ToList() },
            idempotencyKey: idempotencyKey ?? RequestPipeline.NewIdempotencyKey(), cancellationToken: cancellationToken).ConfigureAwait(false))!;

    /// <summary>
    /// Resolve (creating if missing) each <c>/</c>-delimited path. Get-or-create by path, so it is
    /// naturally idempotent and safe to retry without a key. Results preserve input order.
    /// </summary>
    public FolderPathsResult ResolvePaths(IEnumerable<string> paths, long? parentId = null, CancellationToken cancellationToken = default) =>
        _http.Request<FolderPathsResult>(HttpMethod.Post, "/api/folders/paths", new FolderPathsRequest { Paths = paths.ToList(), ParentId = parentId },
            idempotent: true, cancellationToken: cancellationToken)!;

    /// <inheritdoc cref="ResolvePaths"/>
    public async Task<FolderPathsResult> ResolvePathsAsync(IEnumerable<string> paths, long? parentId = null, CancellationToken cancellationToken = default) =>
        (await _http.RequestAsync<FolderPathsResult>(HttpMethod.Post, "/api/folders/paths", new FolderPathsRequest { Paths = paths.ToList(), ParentId = parentId },
            idempotent: true, cancellationToken: cancellationToken).ConfigureAwait(false))!;

    /// <summary>Get-or-create a single <c>/</c>-delimited folder path; return the leaf folder.</summary>
    public Folder EnsurePath(string path, long? parentId = null, CancellationToken cancellationToken = default) =>
        LeafOf(path, ResolvePaths(new[] { ValidatePath(path) }, parentId, cancellationToken));

    /// <inheritdoc cref="EnsurePath"/>
    public async Task<Folder> EnsurePathAsync(string path, long? parentId = null, CancellationToken cancellationToken = default) =>
        LeafOf(path, await ResolvePathsAsync(new[] { ValidatePath(path) }, parentId, cancellationToken).ConfigureAwait(false));

    private static string ValidatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Trim('/', ' ').Length == 0)
        {
            throw new ArgumentException("EnsurePath requires a non-empty folder path", nameof(path));
        }

        return path;
    }

    private static Folder LeafOf(string path, FolderPathsResult result)
    {
        var folders = result.Results.Count > 0 ? result.Results[0].Folders : Array.Empty<Folder>();
        if (folders.Count == 0)
        {
            throw new InfersoftException($"could not resolve folder path '{path}'");
        }

        return folders[folders.Count - 1];
    }

    private static FolderSearchRequest BuildSearch(long? parentId, int page, int pageSize, string orderBy, string orderDir) =>
        new() { ParentId = parentId, Page = page, PageSize = pageSize, OrderBy = orderBy, OrderDir = orderDir };
}
