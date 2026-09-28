using System.Collections.Generic;
using System.Text.Json;

namespace Infersoft;

/// <summary>
/// The outcome of the end-to-end <c>client.Extract</c> pipeline. <see cref="Values"/> is the
/// happy-path payload (<c>{documentId: {field: value}}</c>, typed values); <see cref="Documents"/> keeps
/// the full typed extraction items, and <see cref="Upload"/> is present only when local files were
/// uploaded. Files that failed to upload are skipped, not raised — check <c>Upload.Failed</c>
/// before treating the result as complete.
/// </summary>
public sealed class ExtractResult
{
    internal ExtractResult(
        Job job,
        UploadResult? upload,
        IReadOnlyList<DocumentSummary> documents,
        IReadOnlyDictionary<long, IReadOnlyDictionary<object, object?>> values)
    {
        Job = job;
        Upload = upload;
        Documents = documents;
        Values = values;
    }

    /// <summary>The finished extraction job.</summary>
    public Job Job { get; }

    /// <summary>The upload result, when local files were uploaded (else null).</summary>
    public UploadResult? Upload { get; }

    /// <summary>The documents the job touched, with their typed extraction items.</summary>
    public IReadOnlyList<DocumentSummary> Documents { get; }

    /// <summary>
    /// Extraction values flattened to <c>{documentId: {field: value}}</c>. Each value is the typed
    /// <see cref="ExtractionResultValue.Value"/>: a <see cref="string"/>, <see cref="bool"/>,
    /// <see cref="System.DateTime"/>, or for Number a <see cref="decimal"/> when the server's decimal string
    /// parses, otherwise that string unchanged; <c>null</c> when the raw text could not be typed.
    /// </summary>
    public IReadOnlyDictionary<long, IReadOnlyDictionary<object, object?>> Values { get; }
}
