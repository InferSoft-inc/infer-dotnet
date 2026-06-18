using System.Globalization;

namespace Infersoft;

/// <summary>
/// Raised for a non-2xx response from the Infersoft API. Carries the parsed RFC 9457 problem
/// details when present, plus the request id from the response headers (when the server
/// provides one). The concrete subclass reflects the HTTP status (see <see cref="BadRequestException"/>,
/// <see cref="NotFoundException"/>, and so on).
/// </summary>
public class InfersoftApiException : InfersoftException
{
    /// <summary>Initializes a new instance of the <see cref="InfersoftApiException"/> class.</summary>
    /// <param name="statusCode">The HTTP status code of the response.</param>
    /// <param name="title">The problem title (RFC 9457), if any.</param>
    /// <param name="detail">The problem detail, if any.</param>
    /// <param name="type">The problem type URI, if any.</param>
    /// <param name="instance">The problem instance URI, if any.</param>
    /// <param name="requestId">The server request/trace id, if any.</param>
    /// <param name="rawBody">The raw response body, if any.</param>
    public InfersoftApiException(
        int statusCode,
        string? title = null,
        string? detail = null,
        string? type = null,
        string? instance = null,
        string? requestId = null,
        string? rawBody = null)
        : base(BuildMessage(statusCode, title, detail, requestId))
    {
        StatusCode = statusCode;
        Title = title;
        Detail = detail;
        Type = type;
        Instance = instance;
        RequestId = requestId;
        RawBody = rawBody;
    }

    /// <summary>The HTTP status code of the response.</summary>
    public int StatusCode { get; }

    /// <summary>The problem title (RFC 9457), if the server supplied one.</summary>
    public string? Title { get; }

    /// <summary>The problem detail, if the server supplied one.</summary>
    public string? Detail { get; }

    /// <summary>The problem type URI, if the server supplied one.</summary>
    public string? Type { get; }

    /// <summary>The problem instance URI, if the server supplied one.</summary>
    public string? Instance { get; }

    /// <summary>The server request/trace id read from the response headers, if any.</summary>
    public string? RequestId { get; }

    /// <summary>The raw response body, if any (snapshotted before the response was disposed).</summary>
    public string? RawBody { get; }

    private static string BuildMessage(int statusCode, string? title, string? detail, string? requestId)
    {
        var message = statusCode.ToString(CultureInfo.InvariantCulture);
        if (!string.IsNullOrEmpty(title))
        {
            message += " " + title;
        }

        if (!string.IsNullOrEmpty(detail))
        {
            message += ": " + detail;
        }

        if (!string.IsNullOrEmpty(requestId))
        {
            message += " (request_id=" + requestId + ")";
        }

        return message;
    }
}

/// <summary>Raised for HTTP 400 (Bad Request).</summary>
public sealed class BadRequestException : InfersoftApiException
{
    /// <summary>Initializes a new instance of the <see cref="BadRequestException"/> class.</summary>
    public BadRequestException(int statusCode, string? title = null, string? detail = null, string? type = null, string? instance = null, string? requestId = null, string? rawBody = null)
        : base(statusCode, title, detail, type, instance, requestId, rawBody)
    {
    }
}

/// <summary>Raised for HTTP 401 (Unauthorized).</summary>
public sealed class UnauthorizedException : InfersoftApiException
{
    /// <summary>Initializes a new instance of the <see cref="UnauthorizedException"/> class.</summary>
    public UnauthorizedException(int statusCode, string? title = null, string? detail = null, string? type = null, string? instance = null, string? requestId = null, string? rawBody = null)
        : base(statusCode, title, detail, type, instance, requestId, rawBody)
    {
    }
}

/// <summary>Raised for HTTP 403 (Forbidden).</summary>
public sealed class ForbiddenException : InfersoftApiException
{
    /// <summary>Initializes a new instance of the <see cref="ForbiddenException"/> class.</summary>
    public ForbiddenException(int statusCode, string? title = null, string? detail = null, string? type = null, string? instance = null, string? requestId = null, string? rawBody = null)
        : base(statusCode, title, detail, type, instance, requestId, rawBody)
    {
    }
}

/// <summary>Raised for HTTP 404 (Not Found).</summary>
public sealed class NotFoundException : InfersoftApiException
{
    /// <summary>Initializes a new instance of the <see cref="NotFoundException"/> class.</summary>
    public NotFoundException(int statusCode, string? title = null, string? detail = null, string? type = null, string? instance = null, string? requestId = null, string? rawBody = null)
        : base(statusCode, title, detail, type, instance, requestId, rawBody)
    {
    }
}

/// <summary>Raised for HTTP 409 (Conflict). Untyped 409s are deterministic conflicts.</summary>
public sealed class ConflictException : InfersoftApiException
{
    /// <summary>Initializes a new instance of the <see cref="ConflictException"/> class.</summary>
    public ConflictException(int statusCode, string? title = null, string? detail = null, string? type = null, string? instance = null, string? requestId = null, string? rawBody = null)
        : base(statusCode, title, detail, type, instance, requestId, rawBody)
    {
    }
}

/// <summary>Raised for HTTP 412 (Precondition Failed).</summary>
public sealed class PreconditionFailedException : InfersoftApiException
{
    /// <summary>Initializes a new instance of the <see cref="PreconditionFailedException"/> class.</summary>
    public PreconditionFailedException(int statusCode, string? title = null, string? detail = null, string? type = null, string? instance = null, string? requestId = null, string? rawBody = null)
        : base(statusCode, title, detail, type, instance, requestId, rawBody)
    {
    }
}

/// <summary>Raised for HTTP 413 (Payload Too Large).</summary>
public sealed class PayloadTooLargeException : InfersoftApiException
{
    /// <summary>Initializes a new instance of the <see cref="PayloadTooLargeException"/> class.</summary>
    public PayloadTooLargeException(int statusCode, string? title = null, string? detail = null, string? type = null, string? instance = null, string? requestId = null, string? rawBody = null)
        : base(statusCode, title, detail, type, instance, requestId, rawBody)
    {
    }
}

/// <summary>Raised for HTTP 422 (Unprocessable Entity) — e.g. reusing an Idempotency-Key with different parameters.</summary>
public sealed class UnprocessableEntityException : InfersoftApiException
{
    /// <summary>Initializes a new instance of the <see cref="UnprocessableEntityException"/> class.</summary>
    public UnprocessableEntityException(int statusCode, string? title = null, string? detail = null, string? type = null, string? instance = null, string? requestId = null, string? rawBody = null)
        : base(statusCode, title, detail, type, instance, requestId, rawBody)
    {
    }
}

/// <summary>Raised for HTTP 429 (Too Many Requests).</summary>
public sealed class RateLimitException : InfersoftApiException
{
    /// <summary>Initializes a new instance of the <see cref="RateLimitException"/> class.</summary>
    public RateLimitException(int statusCode, string? title = null, string? detail = null, string? type = null, string? instance = null, string? requestId = null, string? rawBody = null)
        : base(statusCode, title, detail, type, instance, requestId, rawBody)
    {
    }
}

/// <summary>Raised for HTTP 5xx (server errors).</summary>
public sealed class ServerException : InfersoftApiException
{
    /// <summary>Initializes a new instance of the <see cref="ServerException"/> class.</summary>
    public ServerException(int statusCode, string? title = null, string? detail = null, string? type = null, string? instance = null, string? requestId = null, string? rawBody = null)
        : base(statusCode, title, detail, type, instance, requestId, rawBody)
    {
    }
}
