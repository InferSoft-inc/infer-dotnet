using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Infersoft.Internal;

namespace Infersoft;

/// <summary>
/// Client for the Infersoft REST API. Exposes both synchronous and asynchronous members; the
/// async members are awaited and the sync members run real synchronous I/O on .NET 8 (and bridge
/// over the async path on netstandard2.0). One instance holds pooled connections for its
/// lifetime — create it once and reuse it; dispose it when done.
/// </summary>
public sealed partial class InfersoftClient : IDisposable, IAsyncDisposable
{
    private const string DefaultBaseUrl = "https://api.infersoft.com";
    private const string DefaultAudience = "https://api.infersoft.com";
    private const string DefaultTokenUrl = "https://dev-noabnisxxguu0jp0.us.auth0.com/oauth/token";

    private readonly HttpClient _apiClient;
    private readonly HttpClient _uploadClient;
    private readonly ClientCredentialsHandler _authHandler;
    private readonly HttpMessageHandler _apiTransport;
    private readonly HttpMessageHandler _uploadTransport;
    private readonly ILogger _logger;
    private readonly bool _ownsHttp;
    private readonly bool _ownsTransport;
    private bool _disposed;

    /// <summary>Creates a client from explicit credentials (everything else uses defaults/env vars).</summary>
    /// <param name="clientId">OAuth2 client id.</param>
    /// <param name="clientSecret">OAuth2 client secret.</param>
    public InfersoftClient(string clientId, string clientSecret)
        : this(new InfersoftClientOptions { ClientId = clientId, ClientSecret = clientSecret })
    {
    }

    /// <summary>Creates a client from the given options, applying <c>INFERSOFT_*</c> fallbacks.</summary>
    /// <param name="options">The client options.</param>
    public InfersoftClient(InfersoftClientOptions options)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        var clientId = options.ClientId ?? Environment.GetEnvironmentVariable("INFERSOFT_CLIENT_ID");
        var clientSecret = options.ClientSecret ?? Environment.GetEnvironmentVariable("INFERSOFT_CLIENT_SECRET");
        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
        {
            throw new InfersoftException(
                "client_id and client_secret are required (pass them directly or set "
                + "INFERSOFT_CLIENT_ID / INFERSOFT_CLIENT_SECRET)");
        }

        var baseUrl = (options.BaseUrl ?? Environment.GetEnvironmentVariable("INFERSOFT_BASE_URL") ?? DefaultBaseUrl)
            .TrimEnd('/');
        var tokenUrl = options.TokenUrl ?? Environment.GetEnvironmentVariable("INFERSOFT_TOKEN_URL") ?? DefaultTokenUrl;
        var audience = options.Audience ?? Environment.GetEnvironmentVariable("INFERSOFT_AUDIENCE") ?? DefaultAudience;

        _ownsTransport = options.Transport is null;
        _apiTransport = options.Transport ?? new HttpClientHandler();
        _uploadTransport = options.Transport ?? new HttpClientHandler();

        _authHandler = new ClientCredentialsHandler(
            clientId!, clientSecret!, tokenUrl, audience, options.Scope, options.MaxRetries)
        {
            InnerHandler = _apiTransport,
        };

        _apiClient = new HttpClient(_authHandler, disposeHandler: false)
        {
            BaseAddress = new Uri(baseUrl, UriKind.Absolute),
            Timeout = System.Threading.Timeout.InfiniteTimeSpan,
        };
        _apiClient.DefaultRequestHeaders.UserAgent.ParseAdd(SdkInfo.UserAgent);
        _apiClient.DefaultRequestHeaders.Accept.ParseAdd("application/json");

        _uploadClient = new HttpClient(_uploadTransport, disposeHandler: false)
        {
            Timeout = System.Threading.Timeout.InfiniteTimeSpan,
        };

        _logger = options.LoggerFactory?.CreateLogger("Infersoft") ?? NullLogger.Instance;
        Pipeline = new RequestPipeline(
            _apiClient, _uploadClient, options.MaxRetries, options.UploadMaxRetries, options.Timeout, options.UploadTimeout);
        Documents = new DocumentsResource(Pipeline, _logger);
        Folders = new FoldersResource(Pipeline);
        Projects = new ProjectsResource(Pipeline);
        Prompts = new PromptsResource(Pipeline);
        Jobs = new JobsResource(Pipeline, _logger);
        _ownsHttp = true;
    }

    // Copy used by WithOptions: shares the pooled clients + auth handler (token cache); owns nothing.
    private InfersoftClient(InfersoftClient source, RequestPipeline pipeline)
    {
        Pipeline = pipeline;
        _logger = source._logger;
        Documents = new DocumentsResource(pipeline, _logger);
        Folders = new FoldersResource(pipeline);
        Projects = new ProjectsResource(pipeline);
        Prompts = new PromptsResource(pipeline);
        Jobs = new JobsResource(pipeline, _logger);
        _apiClient = source._apiClient;
        _uploadClient = source._uploadClient;
        _authHandler = source._authHandler;
        _apiTransport = source._apiTransport;
        _uploadTransport = source._uploadTransport;
        _ownsHttp = false;
        _ownsTransport = false;
    }

    /// <summary>The Documents resource.</summary>
    public DocumentsResource Documents { get; }

    /// <summary>The Folders resource.</summary>
    public FoldersResource Folders { get; }

    /// <summary>The Projects resource.</summary>
    public ProjectsResource Projects { get; }

    /// <summary>The Prompts resource.</summary>
    public PromptsResource Prompts { get; }

    /// <summary>The Jobs resource.</summary>
    public JobsResource Jobs { get; }

    /// <summary>The HTTP engine (internal; resources and tests use it to make requests).</summary>
    internal RequestPipeline Pipeline { get; }

    /// <summary>
    /// Returns a copy of this client with per-call-site setting overrides. The copy shares this
    /// client's connection pool and token cache (no new connections or token fetches); disposing
    /// the copy is a no-op, so it is safe to use in a <c>using</c>. The <paramref name="timeout"/>
    /// applies to API requests; OAuth token acquisition keeps its own 30s timeout.
    /// </summary>
    /// <param name="timeout">Per-request API timeout override.</param>
    /// <param name="maxRetries">Retry-budget override for API requests.</param>
    /// <param name="uploadMaxRetries">Retry-budget override for presigned transfers.</param>
    public InfersoftClient WithOptions(
        TimeSpan? timeout = null, int? maxRetries = null, int? uploadMaxRetries = null) =>
        new(this, Pipeline.WithOverrides(timeout, maxRetries, uploadMaxRetries));

    /// <summary>Releases the connection pool and token cache (no-op for <see cref="WithOptions"/> copies).</summary>
    public void Dispose()
    {
        if (_disposed || !_ownsHttp)
        {
            return;
        }

        _disposed = true;
        _apiClient.Dispose();
        _uploadClient.Dispose();
        _authHandler.Dispose(); // disposes the SemaphoreSlim and the API transport (its InnerHandler)
        if (_ownsTransport)
        {
            _uploadTransport.Dispose();
        }
    }

    /// <summary>Asynchronously releases resources (there is nothing to flush; defers to <see cref="Dispose"/>).</summary>
    public ValueTask DisposeAsync()
    {
        Dispose();
        return default;
    }
}
