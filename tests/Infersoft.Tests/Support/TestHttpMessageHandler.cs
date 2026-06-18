using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Infersoft.Tests.Support;

/// <summary>
/// Programmable transport for tests. Overrides BOTH <see cref="SendAsync"/> and (net8)
/// <see cref="Send"/> so the pipeline's sync and async shells are both exercised. The
/// responder receives the request, the zero-based attempt index, and the (linked) token.
/// </summary>
internal sealed class TestHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, int, CancellationToken, HttpResponseMessage> _responder;
    private int _calls;

    public TestHttpMessageHandler(Func<HttpRequestMessage, int, CancellationToken, HttpResponseMessage> responder) =>
        _responder = responder;

    public int CallCount => Volatile.Read(ref _calls);

    public ConcurrentQueue<RecordedRequest> Requests { get; } = new();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(Handle(request, cancellationToken));

    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Handle(request, cancellationToken);

    private HttpResponseMessage Handle(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var index = Interlocked.Increment(ref _calls) - 1;
        Requests.Enqueue(RecordedRequest.Capture(request));
        return _responder(request, index, cancellationToken);
    }
}

/// <summary>Common responder shapes.</summary>
internal static class Responders
{
    /// <summary>Returns the given responses in order; the last one repeats for further attempts.</summary>
    public static Func<HttpRequestMessage, int, CancellationToken, HttpResponseMessage> Sequence(
        params Func<HttpResponseMessage>[] responses) =>
        (_, index, _) => responses[Math.Min(index, responses.Length - 1)]();
}

internal sealed class RecordedRequest
{
    public string Method { get; init; } = "";

    public Uri? Uri { get; init; }

    public string? IdempotencyKey { get; init; }

    public string? Authorization { get; init; }

    public string? UserAgent { get; init; }

    public string? Body { get; init; }

    public static RecordedRequest Capture(HttpRequestMessage request)
    {
        var idempotencyKey = request.Headers.TryGetValues("Idempotency-Key", out var values)
            ? values.FirstOrDefault()
            : null;

        string? body = null;
        if (request.Content is not null)
        {
            var bytes = request.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            body = bytes.Length > 0 ? Encoding.UTF8.GetString(bytes) : null;
        }

        return new RecordedRequest
        {
            Method = request.Method.Method,
            Uri = request.RequestUri,
            IdempotencyKey = idempotencyKey,
            Authorization = request.Headers.Authorization?.ToString(),
            UserAgent = request.Headers.UserAgent.Count > 0 ? request.Headers.UserAgent.ToString() : null,
            Body = body,
        };
    }
}

/// <summary>Builders for canned responses.</summary>
internal static class Responses
{
    public static HttpResponseMessage Json(HttpStatusCode status, string body, params (string Name, string Value)[] headers)
    {
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        foreach (var (name, value) in headers)
        {
            response.Headers.TryAddWithoutValidation(name, value);
        }

        return response;
    }

    public static HttpResponseMessage Status(HttpStatusCode status, params (string Name, string Value)[] headers)
    {
        var response = new HttpResponseMessage(status);
        foreach (var (name, value) in headers)
        {
            response.Headers.TryAddWithoutValidation(name, value);
        }

        return response;
    }

    public static HttpResponseMessage Token() =>
        Json(HttpStatusCode.OK, "{\"access_token\":\"test-token\",\"expires_in\":3600}");
}

/// <summary>A simple response model for deserialization tests (snake_case → PascalCase).</summary>
internal sealed class Echo
{
    public string? Name { get; set; }

    public int PageCount { get; set; }
}
