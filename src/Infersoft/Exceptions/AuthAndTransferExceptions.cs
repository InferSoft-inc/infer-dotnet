using System;

namespace Infersoft;

/// <summary>Raised when an OAuth2 access token could not be obtained.</summary>
public sealed class InfersoftAuthenticationException : InfersoftException
{
    /// <summary>Initializes a new instance of the <see cref="InfersoftAuthenticationException"/> class.</summary>
    public InfersoftAuthenticationException()
    {
    }

    /// <summary>Initializes a new instance with the specified message.</summary>
    /// <param name="message">The error message.</param>
    public InfersoftAuthenticationException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance with the specified message and inner exception.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying cause.</param>
    public InfersoftAuthenticationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Raised when a wait helper exceeds its maximum wait budget.</summary>
public sealed class WaitTimeoutException : InfersoftException
{
    /// <summary>Initializes a new instance of the <see cref="WaitTimeoutException"/> class.</summary>
    public WaitTimeoutException()
    {
    }

    /// <summary>Initializes a new instance with the specified message.</summary>
    /// <param name="message">The error message.</param>
    public WaitTimeoutException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance with the specified message and inner exception.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying cause.</param>
    public WaitTimeoutException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Raised when uploading file bytes to a presigned URL fails.</summary>
public sealed class UploadTransferException : InfersoftException
{
    /// <summary>Initializes a new instance of the <see cref="UploadTransferException"/> class.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="statusCode">The HTTP status of the failed transfer, if any.</param>
    /// <param name="requestId">The transfer request id (e.g. the S3 <c>x-amz-*</c> id), if any.</param>
    public UploadTransferException(string message, int? statusCode = null, string? requestId = null)
        : base(message)
    {
        StatusCode = statusCode;
        RequestId = requestId;
    }

    /// <summary>Initializes a new instance with a message and inner exception.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying transport error.</param>
    public UploadTransferException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>The HTTP status of the failed transfer, if any.</summary>
    public int? StatusCode { get; }

    /// <summary>The transfer request id, if any.</summary>
    public string? RequestId { get; }
}

/// <summary>Raised when fetching file bytes from a signed download URL fails.</summary>
public sealed class DownloadTransferException : InfersoftException
{
    /// <summary>Initializes a new instance of the <see cref="DownloadTransferException"/> class.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="statusCode">The HTTP status of the failed transfer, if any.</param>
    /// <param name="requestId">The transfer request id, if any.</param>
    public DownloadTransferException(string message, int? statusCode = null, string? requestId = null)
        : base(message)
    {
        StatusCode = statusCode;
        RequestId = requestId;
    }

    /// <summary>Initializes a new instance with a message and inner exception.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying transport error.</param>
    public DownloadTransferException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>The HTTP status of the failed transfer, if any.</summary>
    public int? StatusCode { get; }

    /// <summary>The transfer request id, if any.</summary>
    public string? RequestId { get; }
}
