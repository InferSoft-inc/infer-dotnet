using System;
using System.Net.Http;
using Microsoft.Extensions.Logging;

namespace Infersoft;

/// <summary>
/// Configuration for <see cref="InfersoftClient"/>. Any unset credential or endpoint falls back
/// to the matching <c>INFERSOFT_*</c> environment variable during construction.
/// </summary>
public sealed class InfersoftClientOptions
{
    /// <summary>OAuth2 client id. Falls back to <c>INFERSOFT_CLIENT_ID</c>.</summary>
    public string? ClientId { get; set; }

    /// <summary>OAuth2 client secret. Falls back to <c>INFERSOFT_CLIENT_SECRET</c>.</summary>
    public string? ClientSecret { get; set; }

    /// <summary>API base URL. Falls back to <c>INFERSOFT_BASE_URL</c>, then the default.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>OAuth2 token endpoint. Falls back to <c>INFERSOFT_TOKEN_URL</c>, then the default.</summary>
    public string? TokenUrl { get; set; }

    /// <summary>OAuth2 audience. Falls back to <c>INFERSOFT_AUDIENCE</c>, then the default.</summary>
    public string? Audience { get; set; }

    /// <summary>Optional OAuth2 scope.</summary>
    public string? Scope { get; set; }

    /// <summary>Per-request timeout for API calls. Default 30 seconds.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Per-request timeout for presigned uploads/downloads. Default 5 minutes.</summary>
    public TimeSpan UploadTimeout { get; set; } = TimeSpan.FromSeconds(300);

    /// <summary>Maximum retries for transient API failures. Default 2.</summary>
    public int MaxRetries { get; set; } = 2;

    /// <summary>Maximum retries for presigned transfers. Falls back to <see cref="MaxRetries"/> when null.</summary>
    public int? UploadMaxRetries { get; set; }

    /// <summary>
    /// Optional transport handler to use instead of the default. Primarily for testing
    /// (inject a mock) or advanced hosting scenarios.
    /// </summary>
    public HttpMessageHandler? Transport { get; set; }

    /// <summary>Optional logger factory; the SDK logs under the category "Infersoft".</summary>
    public ILoggerFactory? LoggerFactory { get; set; }
}
