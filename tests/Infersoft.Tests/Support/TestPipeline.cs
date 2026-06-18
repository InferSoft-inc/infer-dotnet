using System;
using System.Net.Http;
using System.Threading;
using Infersoft.Internal;

namespace Infersoft.Tests.Support;

/// <summary>Builds a <see cref="RequestPipeline"/> over a <see cref="TestHttpMessageHandler"/>.</summary>
internal sealed class TestPipeline : IDisposable
{
    private readonly HttpClient _api;
    private readonly HttpClient _upload;

    public TestPipeline(
        Func<HttpRequestMessage, int, CancellationToken, HttpResponseMessage> responder,
        int maxRetries = 2,
        int? uploadMaxRetries = null,
        TimeSpan? apiTimeout = null)
    {
        Handler = new TestHttpMessageHandler(responder);
        _api = new HttpClient(Handler) { BaseAddress = new Uri("https://api.test") };
        _upload = new HttpClient(Handler);
        Pipeline = new RequestPipeline(
            _api,
            _upload,
            maxRetries,
            uploadMaxRetries,
            apiTimeout ?? TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(30));
    }

    public RequestPipeline Pipeline { get; }

    public TestHttpMessageHandler Handler { get; }

    public void Dispose()
    {
        _api.Dispose();
        _upload.Dispose();
    }
}
