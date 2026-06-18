using System;

namespace Infersoft;

/// <summary>
/// Raised when an <c>UploadMany</c> batch fails partway through. Earlier batches are <b>not</b>
/// rolled back: <see cref="Partial"/> aggregates everything uploaded before the failure and
/// <see cref="BatchesCompleted"/> counts the batches that succeeded. The failing batch's error is
/// the <see cref="Exception.InnerException"/>.
/// </summary>
public sealed class UploadManyException : InfersoftException
{
    /// <summary>Initializes a new instance of the <see cref="UploadManyException"/> class.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The failing batch's underlying error.</param>
    /// <param name="partial">Everything uploaded before the failure.</param>
    /// <param name="batchesCompleted">The number of batches that completed successfully.</param>
    public UploadManyException(string message, Exception innerException, UploadResult partial, int batchesCompleted)
        : base(message, innerException)
    {
        Partial = partial;
        BatchesCompleted = batchesCompleted;
    }

    /// <summary>Everything uploaded before the failure.</summary>
    public UploadResult Partial { get; }

    /// <summary>The number of batches that completed successfully.</summary>
    public int BatchesCompleted { get; }
}
