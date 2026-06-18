using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Infersoft.Internal;

namespace Infersoft;

public sealed partial class InfersoftClient
{
    private const string ExtractorStep = "extractor";

    /// <summary>
    /// Run the whole extraction pipeline in one call. Pass exactly one of <paramref name="files"/>
    /// (uploaded first and waited until ready), <paramref name="documentIds"/>, or
    /// <paramref name="selectors"/>; the extractor job is run with <paramref name="prompts"/> and
    /// waited to completion. Upload failures do NOT raise — inspect <c>result.Upload.Failed</c>;
    /// a <c>failed</c> job raises <see cref="ExtractException"/> unless
    /// <paramref name="raiseOnFailure"/> is false. <paramref name="maxWait"/> is a per-phase budget.
    /// </summary>
    public ExtractResult Extract(
        IEnumerable<string>? files = null,
        IEnumerable<long>? documentIds = null,
        Selectors? selectors = null,
        IEnumerable<long>? prompts = null,
        long? projectId = null,
        string? projectName = null,
        bool flatten = true,
        int? maxCredits = null,
        ExtractionKey keyBy = ExtractionKey.Name,
        bool raiseOnFailure = true,
        TimeSpan? maxWait = null,
        TimeSpan? pollInterval = null,
        CancellationToken cancellationToken = default)
    {
        var promptList = ValidateExtract(files, documentIds, selectors, prompts, projectName);

        UploadResult? upload = null;
        IReadOnlyList<long>? ids = documentIds?.ToList();
        if (files is not null)
        {
            upload = Documents.Upload(files, flatten: flatten, projectId: projectId, projectName: projectName,
                wait: true, maxWait: maxWait, pollInterval: pollInterval, cancellationToken: cancellationToken);
            ids = upload.DocumentIds;
            if (ids.Count == 0)
            {
                throw new ExtractException("Extract aborted: no documents to process (every file failed to upload)", upload: upload);
            }
        }

        var job = Jobs.Run(ExtractorStep, selectors: selectors, documentIds: ids, prompts: promptList, maxCredits: maxCredits,
            projectId: files is not null ? null : projectId, wait: true, maxWait: maxWait, pollInterval: pollInterval, cancellationToken: cancellationToken);
        if (raiseOnFailure && job.Status == JobStatus.Failed)
        {
            throw new ExtractException($"extraction job {job.Id} failed", job, upload);
        }

        var documents = Jobs.Results(job, prompts: promptList, cancellationToken: cancellationToken).ToList();
        return new ExtractResult(job, upload, documents, FlattenAll(documents, keyBy));
    }

    /// <inheritdoc cref="Extract"/>
    public async Task<ExtractResult> ExtractAsync(
        IEnumerable<string>? files = null,
        IEnumerable<long>? documentIds = null,
        Selectors? selectors = null,
        IEnumerable<long>? prompts = null,
        long? projectId = null,
        string? projectName = null,
        bool flatten = true,
        int? maxCredits = null,
        ExtractionKey keyBy = ExtractionKey.Name,
        bool raiseOnFailure = true,
        TimeSpan? maxWait = null,
        TimeSpan? pollInterval = null,
        CancellationToken cancellationToken = default)
    {
        var promptList = ValidateExtract(files, documentIds, selectors, prompts, projectName);

        UploadResult? upload = null;
        IReadOnlyList<long>? ids = documentIds?.ToList();
        if (files is not null)
        {
            upload = await Documents.UploadAsync(files, flatten: flatten, projectId: projectId, projectName: projectName,
                wait: true, maxWait: maxWait, pollInterval: pollInterval, cancellationToken: cancellationToken).ConfigureAwait(false);
            ids = upload.DocumentIds;
            if (ids.Count == 0)
            {
                throw new ExtractException("Extract aborted: no documents to process (every file failed to upload)", upload: upload);
            }
        }

        var job = await Jobs.RunAsync(ExtractorStep, selectors: selectors, documentIds: ids, prompts: promptList, maxCredits: maxCredits,
            projectId: files is not null ? null : projectId, wait: true, maxWait: maxWait, pollInterval: pollInterval, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (raiseOnFailure && job.Status == JobStatus.Failed)
        {
            throw new ExtractException($"extraction job {job.Id} failed", job, upload);
        }

        var documents = new List<DocumentSummary>();
        await foreach (var doc in Jobs.ResultsAsync(job, prompts: promptList, cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            documents.Add(doc);
        }

        return new ExtractResult(job, upload, documents, FlattenAll(documents, keyBy));
    }

    private static List<long> ValidateExtract(
        IEnumerable<string>? files, IEnumerable<long>? documentIds, Selectors? selectors, IEnumerable<long>? prompts, string? projectName)
    {
        var provided = (files is not null ? 1 : 0) + (documentIds is not null ? 1 : 0) + (selectors is not null ? 1 : 0);
        if (provided != 1)
        {
            throw new ArgumentException("Extract requires exactly one of files, documentIds, or selectors");
        }

        if (files is null && projectName is not null)
        {
            throw new ArgumentException(
                "projectName requires files (the upload creates the project); pass projectId instead", nameof(projectName));
        }

        if (prompts is null)
        {
            throw new ArgumentException("prompts is required", nameof(prompts));
        }

        return prompts.ToList();
    }

    private static Dictionary<long, IReadOnlyDictionary<object, JsonElement?>> FlattenAll(
        IReadOnlyList<DocumentSummary> documents, ExtractionKey keyBy) =>
        documents.ToDictionary(doc => doc.Id, doc => ExtractionFlattener.Flatten(doc, keyBy));
}
