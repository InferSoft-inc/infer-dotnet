using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Infersoft.Internal;

namespace Infersoft;

public sealed partial class DocumentsResource
{
    private static readonly HashSet<string> KnownDocumentStatuses = new(StringComparer.Ordinal)
    {
        DocumentStatus.Uploading.Value,
        DocumentStatus.Processing.Value,
        DocumentStatus.Ready.Value,
    };

    // ----- Upload ----------------------------------------------------------------------------

    /// <summary>
    /// Upload one or more local files end to end (≤ <see cref="MaxBatchFiles"/>): plan the batch,
    /// then PUT each file's bytes to its presigned URL. With <paramref name="flatten"/> only the
    /// basename is sent (duplicate basenames throw); otherwise the path is sent verbatim. With
    /// <paramref name="wait"/>, blocks until every uploaded document is ready.
    /// </summary>
    public UploadResult Upload(
        IEnumerable<string> files,
        bool flatten = true,
        long? projectId = null,
        string? projectName = null,
        IEnumerable<long>? tagIds = null,
        IEnumerable<string>? tagNames = null,
        string? contentType = null,
        OnDuplicate onDuplicate = OnDuplicate.Allow,
        string? idempotencyKey = null,
        bool wait = false,
        TimeSpan? maxWait = null,
        TimeSpan? pollInterval = null,
        CancellationToken cancellationToken = default)
    {
        var (paths, names) = CollectForUpload(files, flatten);
        var result = UploadBatch(paths, names, projectId, projectName, tagIds, tagNames, contentType, onDuplicate,
            idempotencyKey ?? RequestPipeline.NewIdempotencyKey(), cancellationToken);
        if (wait)
        {
            WaitUntilReady(result.DocumentIds, maxWait, pollInterval, cancellationToken);
        }

        return result;
    }

    /// <inheritdoc cref="Upload"/>
    public async Task<UploadResult> UploadAsync(
        IEnumerable<string> files,
        bool flatten = true,
        long? projectId = null,
        string? projectName = null,
        IEnumerable<long>? tagIds = null,
        IEnumerable<string>? tagNames = null,
        string? contentType = null,
        OnDuplicate onDuplicate = OnDuplicate.Allow,
        string? idempotencyKey = null,
        bool wait = false,
        TimeSpan? maxWait = null,
        TimeSpan? pollInterval = null,
        CancellationToken cancellationToken = default)
    {
        var (paths, names) = CollectForUpload(files, flatten);
        var result = await UploadBatchAsync(paths, names, projectId, projectName, tagIds, tagNames, contentType, onDuplicate,
            idempotencyKey ?? RequestPipeline.NewIdempotencyKey(), cancellationToken).ConfigureAwait(false);
        if (wait)
        {
            await WaitUntilReadyAsync(result.DocumentIds, maxWait, pollInterval, cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    // ----- UploadMany ------------------------------------------------------------------------

    /// <summary>Upload any number of explicit files, in batches of up to <paramref name="batchSize"/>.</summary>
    public UploadResult UploadMany(
        IEnumerable<string> files,
        bool flatten = true,
        int batchSize = MaxBatchFiles,
        long? projectId = null,
        string? projectName = null,
        IEnumerable<long>? tagIds = null,
        IEnumerable<string>? tagNames = null,
        string? contentType = null,
        OnDuplicate onDuplicate = OnDuplicate.Allow,
        string? idempotencyKey = null,
        Action<UploadResult>? onBatch = null,
        bool wait = false,
        TimeSpan? maxWait = null,
        TimeSpan? pollInterval = null,
        CancellationToken cancellationToken = default)
    {
        var (paths, names) = CollectFromList(files, flatten);
        return RunUploadMany(paths, names, batchSize, projectId, projectName, tagIds, tagNames, contentType, onDuplicate,
            idempotencyKey, onBatch, wait, maxWait, pollInterval, cancellationToken);
    }

    /// <summary>Upload every file under a directory (recursively; dotfiles skipped), in batches.</summary>
    public UploadResult UploadManyFromDirectory(
        string directory,
        string searchPattern = "*",
        bool flatten = true,
        int batchSize = MaxBatchFiles,
        long? projectId = null,
        string? projectName = null,
        IEnumerable<long>? tagIds = null,
        IEnumerable<string>? tagNames = null,
        string? contentType = null,
        OnDuplicate onDuplicate = OnDuplicate.Allow,
        string? idempotencyKey = null,
        Action<UploadResult>? onBatch = null,
        bool wait = false,
        TimeSpan? maxWait = null,
        TimeSpan? pollInterval = null,
        CancellationToken cancellationToken = default)
    {
        var (paths, names) = CollectFromDirectory(directory, searchPattern, flatten);
        return RunUploadMany(paths, names, batchSize, projectId, projectName, tagIds, tagNames, contentType, onDuplicate,
            idempotencyKey, onBatch, wait, maxWait, pollInterval, cancellationToken);
    }

    /// <inheritdoc cref="UploadMany"/>
    public Task<UploadResult> UploadManyAsync(
        IEnumerable<string> files,
        bool flatten = true,
        int batchSize = MaxBatchFiles,
        long? projectId = null,
        string? projectName = null,
        IEnumerable<long>? tagIds = null,
        IEnumerable<string>? tagNames = null,
        string? contentType = null,
        OnDuplicate onDuplicate = OnDuplicate.Allow,
        string? idempotencyKey = null,
        Action<UploadResult>? onBatch = null,
        bool wait = false,
        TimeSpan? maxWait = null,
        TimeSpan? pollInterval = null,
        CancellationToken cancellationToken = default)
    {
        var (paths, names) = CollectFromList(files, flatten);
        return RunUploadManyAsync(paths, names, batchSize, projectId, projectName, tagIds, tagNames, contentType, onDuplicate,
            idempotencyKey, onBatch, wait, maxWait, pollInterval, cancellationToken);
    }

    /// <inheritdoc cref="UploadManyFromDirectory"/>
    public Task<UploadResult> UploadManyFromDirectoryAsync(
        string directory,
        string searchPattern = "*",
        bool flatten = true,
        int batchSize = MaxBatchFiles,
        long? projectId = null,
        string? projectName = null,
        IEnumerable<long>? tagIds = null,
        IEnumerable<string>? tagNames = null,
        string? contentType = null,
        OnDuplicate onDuplicate = OnDuplicate.Allow,
        string? idempotencyKey = null,
        Action<UploadResult>? onBatch = null,
        bool wait = false,
        TimeSpan? maxWait = null,
        TimeSpan? pollInterval = null,
        CancellationToken cancellationToken = default)
    {
        var (paths, names) = CollectFromDirectory(directory, searchPattern, flatten);
        return RunUploadManyAsync(paths, names, batchSize, projectId, projectName, tagIds, tagNames, contentType, onDuplicate,
            idempotencyKey, onBatch, wait, maxWait, pollInterval, cancellationToken);
    }

    // ----- Readiness wait --------------------------------------------------------------------

    /// <summary>Poll until every given document reaches <c>ready</c> (default budget 10 minutes).</summary>
    public IReadOnlyList<DocumentSummary> WaitUntilReady(
        IEnumerable<long> documentIds, TimeSpan? maxWait = null, TimeSpan? pollInterval = null, CancellationToken cancellationToken = default)
    {
        var ids = documentIds.ToList();
        if (ids.Count == 0)
        {
            return Array.Empty<DocumentSummary>();
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var budget = maxWait ?? Polling.DefaultDocumentWait;
        return Polling.PollUntil(
            () => Iterate(documentIds: ids, cancellationToken: cancellationToken).ToList(),
            docs => AllReady(docs, ids, seen),
            budget,
            pollInterval ?? Polling.DefaultPollInterval,
            ReadyTimeoutMessage(ids, budget),
            cancellationToken);
    }

    /// <inheritdoc cref="WaitUntilReady"/>
    public Task<List<DocumentSummary>> WaitUntilReadyAsync(
        IEnumerable<long> documentIds, TimeSpan? maxWait = null, TimeSpan? pollInterval = null, CancellationToken cancellationToken = default)
    {
        var ids = documentIds.ToList();
        if (ids.Count == 0)
        {
            return Task.FromResult(new List<DocumentSummary>());
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var budget = maxWait ?? Polling.DefaultDocumentWait;
        return Polling.PollUntilAsync(
            async token =>
            {
                var docs = new List<DocumentSummary>();
                await foreach (var doc in IterateAsync(documentIds: ids, cancellationToken: token).ConfigureAwait(false))
                {
                    docs.Add(doc);
                }

                return docs;
            },
            docs => AllReady(docs, ids, seen),
            budget,
            pollInterval ?? Polling.DefaultPollInterval,
            ReadyTimeoutMessage(ids, budget),
            cancellationToken);
    }

    // ----- Download --------------------------------------------------------------------------

    /// <summary>Get a short-lived signed URL for a document's file (no bytes; fetch before it expires).</summary>
    public DownloadResponse DownloadUrl(long documentId, CancellationToken cancellationToken = default) =>
        _http.Request<DownloadResponse>(HttpMethod.Get, $"/api/documents/{documentId}/download", cancellationToken: cancellationToken)!;

    /// <inheritdoc cref="DownloadUrl"/>
    public async Task<DownloadResponse> DownloadUrlAsync(long documentId, CancellationToken cancellationToken = default) =>
        (await _http.RequestAsync<DownloadResponse>(HttpMethod.Get, $"/api/documents/{documentId}/download", cancellationToken: cancellationToken).ConfigureAwait(false))!;

    /// <summary>
    /// Download a document's file into <paramref name="destDir"/> and return the written path.
    /// The name defaults to the document's server name (one extra GET); names are reduced to their
    /// final component and never escape <paramref name="destDir"/>. Existing files throw unless
    /// <paramref name="overwrite"/>.
    /// </summary>
    public string Download(long documentId, string destDir, string? fileName = null, bool overwrite = false, CancellationToken cancellationToken = default)
    {
        var directory = RequireDirectory(destDir);
        fileName ??= Get(documentId, cancellationToken: cancellationToken).Name;
        var target = ResolveDownloadTarget(directory, fileName);
        GuardOverwrite(target, overwrite);

        var signed = DownloadUrl(documentId, cancellationToken);
        var data = _http.GetPresigned(signed.Url, cancellationToken);
        File.WriteAllBytes(target, data);
        return target;
    }

    /// <inheritdoc cref="Download"/>
    public async Task<string> DownloadAsync(long documentId, string destDir, string? fileName = null, bool overwrite = false, CancellationToken cancellationToken = default)
    {
        var directory = RequireDirectory(destDir);
        fileName ??= (await GetAsync(documentId, cancellationToken: cancellationToken).ConfigureAwait(false)).Name;
        var target = ResolveDownloadTarget(directory, fileName);
        GuardOverwrite(target, overwrite);

        var signed = await DownloadUrlAsync(documentId, cancellationToken).ConfigureAwait(false);
        var data = await _http.GetPresignedAsync(signed.Url, cancellationToken).ConfigureAwait(false);
        await Task.Run(() => File.WriteAllBytes(target, data), cancellationToken).ConfigureAwait(false);
        return target;
    }

    // ----- Batch core ------------------------------------------------------------------------

    private UploadResult UploadBatch(
        List<string> paths, List<string> names, long? projectId, string? projectName,
        IEnumerable<long>? tagIds, IEnumerable<string>? tagNames, string? contentType, OnDuplicate onDuplicate,
        string idempotencyKey, CancellationToken cancellationToken)
    {
        var byName = new Dictionary<string, string>(StringComparer.Ordinal);
        var requestFiles = new List<UploadFileRequest>(paths.Count);
        for (var i = 0; i < paths.Count; i++)
        {
            byName[names[i]] = paths[i];
            requestFiles.Add(new UploadFileRequest
            {
                FileName = names[i],
                ContentType = contentType ?? MimeTypes.Guess(paths[i]),
                Size = new FileInfo(paths[i]).Length,
                Md5 = Md5Hex(paths[i]),
            });
        }

        var response = _http.Request<BatchUploadResponse>(HttpMethod.Post, "/api/uploads",
            BuildBatchBody(requestFiles, projectId, projectName, tagIds, tagNames), idempotencyKey: idempotencyKey, cancellationToken: cancellationToken)!;

        var outcomes = new List<FileOutcome>(response.Items.Count);
        foreach (var item in response.Items)
        {
            var outcome = new FileOutcome { FileName = item.ClientFileName, DocumentId = item.DocumentId, Duplicates = item.Duplicates };
            if (item.PutUrl is not null)
            {
                if (item.Duplicates.Count > 0 && onDuplicate == OnDuplicate.Block)
                {
                    if (item.DocumentId is long id)
                    {
                        Delete(id, cancellationToken);
                    }

                    outcome.SkippedAsDuplicate = true;
                }
                else if (byName.TryGetValue(item.ClientFileName, out var src))
                {
                    NotifyDuplicate(item, onDuplicate);
                    _http.PutPresigned(item.PutUrl, File.ReadAllBytes(src), item.RequiredHeaders, cancellationToken);
                    outcome.Uploaded = true;
                }
            }
            else
            {
                outcome.Error = item.Error;
            }

            outcomes.Add(outcome);
        }

        return new UploadResult { Outcomes = outcomes, Project = response.Project, Tags = response.Tags };
    }

    private async Task<UploadResult> UploadBatchAsync(
        List<string> paths, List<string> names, long? projectId, string? projectName,
        IEnumerable<long>? tagIds, IEnumerable<string>? tagNames, string? contentType, OnDuplicate onDuplicate,
        string idempotencyKey, CancellationToken cancellationToken)
    {
        var byName = new Dictionary<string, string>(StringComparer.Ordinal);
        var requestFiles = new List<UploadFileRequest>(paths.Count);
        for (var i = 0; i < paths.Count; i++)
        {
            byName[names[i]] = paths[i];
            var path = paths[i];
            requestFiles.Add(new UploadFileRequest
            {
                FileName = names[i],
                ContentType = contentType ?? MimeTypes.Guess(path),
                Size = new FileInfo(path).Length,
                Md5 = await Task.Run(() => Md5Hex(path), cancellationToken).ConfigureAwait(false),
            });
        }

        var response = (await _http.RequestAsync<BatchUploadResponse>(HttpMethod.Post, "/api/uploads",
            BuildBatchBody(requestFiles, projectId, projectName, tagIds, tagNames), idempotencyKey: idempotencyKey, cancellationToken: cancellationToken).ConfigureAwait(false))!;

        var outcomes = new List<FileOutcome>(response.Items.Count);
        foreach (var item in response.Items)
        {
            var outcome = new FileOutcome { FileName = item.ClientFileName, DocumentId = item.DocumentId, Duplicates = item.Duplicates };
            if (item.PutUrl is not null)
            {
                if (item.Duplicates.Count > 0 && onDuplicate == OnDuplicate.Block)
                {
                    if (item.DocumentId is long id)
                    {
                        await DeleteAsync(id, cancellationToken).ConfigureAwait(false);
                    }

                    outcome.SkippedAsDuplicate = true;
                }
                else if (byName.TryGetValue(item.ClientFileName, out var src))
                {
                    NotifyDuplicate(item, onDuplicate);
                    var bytes = await Task.Run(() => File.ReadAllBytes(src), cancellationToken).ConfigureAwait(false);
                    await _http.PutPresignedAsync(item.PutUrl, bytes, item.RequiredHeaders, cancellationToken).ConfigureAwait(false);
                    outcome.Uploaded = true;
                }
            }
            else
            {
                outcome.Error = item.Error;
            }

            outcomes.Add(outcome);
        }

        return new UploadResult { Outcomes = outcomes, Project = response.Project, Tags = response.Tags };
    }

    private UploadResult RunUploadMany(
        List<string> paths, List<string> names, int batchSize, long? projectId, string? projectName,
        IEnumerable<long>? tagIds, IEnumerable<string>? tagNames, string? contentType, OnDuplicate onDuplicate,
        string? idempotencyKey, Action<UploadResult>? onBatch, bool wait, TimeSpan? maxWait, TimeSpan? pollInterval, CancellationToken cancellationToken)
    {
        ValidateBatchSize(batchSize);
        var baseKey = idempotencyKey ?? RequestPipeline.NewIdempotencyKey();
        var state = new UploadManyState(projectId, projectName, tagIds, tagNames);

        for (var batchNo = 0; batchNo * batchSize < paths.Count; batchNo++)
        {
            var (batchPaths, batchNames) = Slice(paths, names, batchNo, batchSize);
            UploadResult result;
            try
            {
                result = UploadBatch(batchPaths, batchNames, state.ProjectId, state.ProjectName, state.TagIds, state.TagNames,
                    contentType, onDuplicate, $"{baseKey}-{batchNo:D4}", cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new UploadManyException(
                    $"UploadMany batch {batchNo + 1} failed; {batchNo} batch(es) were already uploaded (see Partial)",
                    ex, state.ToResult(), batchNo);
            }

            state.Absorb(result);
            onBatch?.Invoke(result);
        }

        var total = state.ToResult();
        if (wait)
        {
            WaitUntilReady(total.DocumentIds, maxWait, pollInterval, cancellationToken);
        }

        return total;
    }

    private async Task<UploadResult> RunUploadManyAsync(
        List<string> paths, List<string> names, int batchSize, long? projectId, string? projectName,
        IEnumerable<long>? tagIds, IEnumerable<string>? tagNames, string? contentType, OnDuplicate onDuplicate,
        string? idempotencyKey, Action<UploadResult>? onBatch, bool wait, TimeSpan? maxWait, TimeSpan? pollInterval, CancellationToken cancellationToken)
    {
        ValidateBatchSize(batchSize);
        var baseKey = idempotencyKey ?? RequestPipeline.NewIdempotencyKey();
        var state = new UploadManyState(projectId, projectName, tagIds, tagNames);

        for (var batchNo = 0; batchNo * batchSize < paths.Count; batchNo++)
        {
            var (batchPaths, batchNames) = Slice(paths, names, batchNo, batchSize);
            UploadResult result;
            try
            {
                result = await UploadBatchAsync(batchPaths, batchNames, state.ProjectId, state.ProjectName, state.TagIds, state.TagNames,
                    contentType, onDuplicate, $"{baseKey}-{batchNo:D4}", cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new UploadManyException(
                    $"UploadMany batch {batchNo + 1} failed; {batchNo} batch(es) were already uploaded (see Partial)",
                    ex, state.ToResult(), batchNo);
            }

            state.Absorb(result);
            onBatch?.Invoke(result);
        }

        var total = state.ToResult();
        if (wait)
        {
            await WaitUntilReadyAsync(total.DocumentIds, maxWait, pollInterval, cancellationToken).ConfigureAwait(false);
        }

        return total;
    }

    // ----- Helpers ---------------------------------------------------------------------------

    private void NotifyDuplicate(UploadItem item, OnDuplicate onDuplicate)
    {
        if (item.Duplicates.Count > 0 && onDuplicate == OnDuplicate.Notify)
        {
            _logger.LogWarning("upload '{File}' matches existing document(s) {Ids}",
                item.ClientFileName, string.Join(",", item.Duplicates.Select(d => d.DocumentId)));
        }
    }

    private bool AllReady(IReadOnlyList<DocumentSummary> docs, IReadOnlyList<long> ids, ISet<string> seen)
    {
        var ready = new HashSet<long>();
        foreach (var doc in docs)
        {
            Polling.WarnOnceUnknownStatus(_logger, seen, doc.Status.Value, KnownDocumentStatuses, "document");
            if (doc.Status == DocumentStatus.Ready)
            {
                ready.Add(doc.Id);
            }
        }

        return ids.All(ready.Contains);
    }

    private static BatchUploadRequest BuildBatchBody(
        IReadOnlyList<UploadFileRequest> files, long? projectId, string? projectName, IEnumerable<long>? tagIds, IEnumerable<string>? tagNames) =>
        new()
        {
            Files = files,
            ProjectId = projectId,
            ProjectName = projectName,
            TagIds = tagIds?.ToList(),
            TagNames = tagNames?.ToList(),
        };

    private static (List<string> Paths, List<string> Names) CollectForUpload(IEnumerable<string> files, bool flatten)
    {
        var paths = files.ToList();
        if (paths.Count == 0)
        {
            throw new ArgumentException("Upload requires at least one file", nameof(files));
        }

        if (paths.Count > MaxBatchFiles)
        {
            throw new ArgumentException(
                $"Upload accepts at most {MaxBatchFiles} files per call (got {paths.Count}); use UploadMany for more.", nameof(files));
        }

        RequireFilesExist(paths);
        return (paths, DeriveRemoteNames(paths, paths, flatten));
    }

    private static (List<string> Paths, List<string> Names) CollectFromList(IEnumerable<string> files, bool flatten)
    {
        var paths = files.ToList();
        if (paths.Count == 0)
        {
            throw new ArgumentException("UploadMany requires at least one file", nameof(files));
        }

        RequireFilesExist(paths);
        return (paths, DeriveRemoteNames(paths, paths, flatten));
    }

    private static (List<string> Paths, List<string> Names) CollectFromDirectory(string directory, string searchPattern, bool flatten)
    {
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"directory does not exist: {directory}");
        }

        var basePath = Path.GetFullPath(directory);
        var paths = Directory.EnumerateFiles(basePath, searchPattern, SearchOption.AllDirectories)
            .Where(p => !Path.GetFileName(p).StartsWith(".", StringComparison.Ordinal))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
        if (paths.Count == 0)
        {
            throw new ArgumentException($"no files matching '{searchPattern}' under {basePath}", nameof(directory));
        }

        var names = flatten
            ? DeriveRemoteNames(paths, paths, flatten: true)
            : paths.Select(p => RelativePosix(basePath, p)).ToList();
        return (paths, names);
    }

    private static List<string> DeriveRemoteNames(IReadOnlyList<string> paths, IReadOnlyList<string> rawInputs, bool flatten)
    {
        if (!flatten)
        {
            return rawInputs.ToList();
        }

        var basenames = paths.Select(p => Path.GetFileName(p)).ToList();
        var duplicates = basenames
            .GroupBy(n => n, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
        if (duplicates.Count > 0)
        {
            throw new ArgumentException(
                $"Upload(flatten: true) requires unique file names, but these basenames repeat: {string.Join(", ", duplicates)}. " +
                "Pass flatten: false to send each file's full path instead.", nameof(flatten));
        }

        return basenames;
    }

    private static (List<string> Paths, List<string> Names) Slice(List<string> paths, List<string> names, int batchNo, int batchSize)
    {
        var start = batchNo * batchSize;
        var count = Math.Min(batchSize, paths.Count - start);
        var batchPaths = new List<string>(count);
        var batchNames = new List<string>(count);
        for (var i = start; i < start + count; i++)
        {
            batchPaths.Add(paths[i]);
            batchNames.Add(names[i]);
        }

        return (batchPaths, batchNames);
    }

    private static void ValidateBatchSize(int batchSize)
    {
        if (batchSize < 1 || batchSize > MaxBatchFiles)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize), batchSize, $"batchSize must be between 1 and {MaxBatchFiles}");
        }
    }

    private static void RequireFilesExist(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("file not found", path);
            }
        }
    }

    private static DirectoryInfo RequireDirectory(string destDir)
    {
        var directory = new DirectoryInfo(destDir);
        if (!directory.Exists)
        {
            throw new DirectoryNotFoundException($"dest directory does not exist: {destDir}");
        }

        return directory;
    }

    private static void GuardOverwrite(string target, bool overwrite)
    {
        if (!overwrite && File.Exists(target))
        {
            throw new IOException($"file already exists: {target}");
        }
    }

    private static string ResolveDownloadTarget(DirectoryInfo directory, string fileName)
    {
        var slash = fileName.LastIndexOf('/');
        var baseName = slash >= 0 ? fileName.Substring(slash + 1) : fileName;
        if (baseName.Length == 0 || baseName == "." || baseName == "..")
        {
            throw new ArgumentException($"cannot derive a file name from '{fileName}'", nameof(fileName));
        }

        var dirFull = directory.FullName;
        var target = Path.GetFullPath(Path.Combine(dirFull, baseName));
        var prefix = dirFull[dirFull.Length - 1] == Path.DirectorySeparatorChar ? dirFull : dirFull + Path.DirectorySeparatorChar;
        if (target != dirFull && !target.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new ArgumentException($"refusing to write outside {dirFull}: '{fileName}'", nameof(fileName));
        }

        return target;
    }

    private static string RelativePosix(string baseDir, string fullPath)
    {
        var relative = fullPath.Substring(baseDir.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return relative.Replace('\\', '/');
    }

    private static string Md5Hex(string path)
    {
        using var md5 = MD5.Create();
        using var stream = File.OpenRead(path);
        var hash = md5.ComputeHash(stream);
        var builder = new StringBuilder(hash.Length * 2);
        foreach (var b in hash)
        {
            builder.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    private static string ReadyTimeoutMessage(IReadOnlyList<long> ids, TimeSpan budget) =>
        string.Format(CultureInfo.InvariantCulture, "documents [{0}] did not all become ready within {1}", string.Join(",", ids), budget);

    // Tracks project/tag pinning + accumulated outcomes across UploadMany batches.
    private sealed class UploadManyState
    {
        private readonly List<FileOutcome> _outcomes = new();
        private readonly Dictionary<long, Tag> _tagsById = new();
        private Project? _project;

        public UploadManyState(long? projectId, string? projectName, IEnumerable<long>? tagIds, IEnumerable<string>? tagNames)
        {
            ProjectId = projectId;
            ProjectName = projectName;
            TagIds = tagIds?.ToList();
            TagNames = tagNames?.ToList();
        }

        public long? ProjectId { get; private set; }

        public string? ProjectName { get; private set; }

        public IReadOnlyList<long>? TagIds { get; private set; }

        public IReadOnlyList<string>? TagNames { get; private set; }

        public void Absorb(UploadResult result)
        {
            _outcomes.AddRange(result.Outcomes);
            if (result.Project is not null)
            {
                _project = result.Project;
                ProjectId = result.Project.Id; // pin later batches to the created project
                ProjectName = null;
            }

            if (result.Tags.Count > 0)
            {
                foreach (var tag in result.Tags)
                {
                    _tagsById[tag.Id] = tag;
                }

                TagIds = _tagsById.Keys.ToList();
                TagNames = null;
            }
        }

        public UploadResult ToResult() =>
            new() { Outcomes = _outcomes.ToList(), Project = _project, Tags = _tagsById.Values.ToList() };
    }
}
