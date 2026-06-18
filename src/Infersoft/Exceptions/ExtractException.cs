using System;

namespace Infersoft;

/// <summary>
/// Raised when the <c>client.Extract</c> pipeline cannot complete. Carries whatever partial state
/// exists so the caller can resume manually: <see cref="Upload"/> (set when local files were
/// uploaded — those documents are NOT rolled back) and <see cref="Job"/> (set when an extraction
/// job was started; resume with <c>Jobs.Wait(error.Job)</c> / <c>Jobs.Results(error.Job)</c>).
/// </summary>
public sealed class ExtractException : InfersoftException
{
    /// <summary>Initializes a new instance of the <see cref="ExtractException"/> class.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="job">The started job, if any.</param>
    /// <param name="upload">The upload result, if local files were uploaded.</param>
    public ExtractException(string message, Job? job = null, UploadResult? upload = null)
        : base(message)
    {
        Job = job;
        Upload = upload;
    }

    /// <summary>Initializes a new instance with a message and inner exception.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying cause.</param>
    public ExtractException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>The started extraction job, if any (resume with <c>Jobs.Wait</c>/<c>Jobs.Results</c>).</summary>
    public Job? Job { get; }

    /// <summary>The upload result, if local files were uploaded (those documents are not rolled back).</summary>
    public UploadResult? Upload { get; }
}
