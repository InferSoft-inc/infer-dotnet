using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Infersoft;
using Infersoft.Tests.Support;
using Xunit;

namespace Infersoft.Tests;

public class PipelineTests
{
    private const string EchoJson = "{\"name\":\"hi\",\"page_count\":5}";

    private static Task<T?> Request<T>(
        TestPipeline tp, bool useAsync, HttpMethod method, string path,
        object? body = null, bool? idempotent = null, string? idempotencyKey = null) =>
        useAsync
            ? tp.Pipeline.RequestAsync<T>(method, path, body, idempotent, idempotencyKey)
            : Task.FromResult(tp.Pipeline.Request<T>(method, path, body, idempotent, idempotencyKey));

    private static async Task<Exception> Capture<T>(
        TestPipeline tp, bool useAsync, HttpMethod method, string path,
        object? body = null, bool? idempotent = null, string? idempotencyKey = null)
    {
        try
        {
            await Request<T>(tp, useAsync, method, path, body, idempotent, idempotencyKey).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return ex;
        }

        Assert.Fail("expected an exception");
        return null!; // unreachable
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Retries_a_transient_status_then_succeeds(bool useAsync)
    {
        using var tp = new TestPipeline(Responders.Sequence(
            () => Responses.Status(HttpStatusCode.ServiceUnavailable, ("Retry-After", "0")),
            () => Responses.Json(HttpStatusCode.OK, EchoJson)));

        var result = await Request<Echo>(tp, useAsync, HttpMethod.Get, "/x");

        Assert.Equal("hi", result!.Name);
        Assert.Equal(5, result.PageCount);
        Assert.Equal(2, tp.Handler.CallCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Retries_a_transport_error_then_succeeds(bool useAsync)
    {
        using var tp = new TestPipeline((req, i, ct) =>
            i == 0 ? throw new HttpRequestException("boom") : Responses.Json(HttpStatusCode.OK, EchoJson));

        var result = await Request<Echo>(tp, useAsync, HttpMethod.Get, "/x");

        Assert.Equal("hi", result!.Name);
        Assert.Equal(2, tp.Handler.CallCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Wraps_transport_error_on_exhaustion(bool useAsync)
    {
        using var tp = new TestPipeline((req, i, ct) => throw new HttpRequestException("down"), maxRetries: 2);

        var ex = await Capture<Echo>(tp, useAsync, HttpMethod.Get, "/x");

        Assert.IsType<ApiConnectionException>(ex);
        Assert.Equal(3, tp.Handler.CallCount); // 1 + 2 retries
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Maps_status_to_the_right_exception(bool useAsync)
    {
        using var tp = new TestPipeline((req, i, ct) => Responses.Json(
            HttpStatusCode.NotFound,
            "{\"title\":\"Not Found\",\"detail\":\"no such document\",\"type\":\"about:blank\"}"));

        var ex = await Capture<Echo>(tp, useAsync, HttpMethod.Get, "/x");

        var apiEx = Assert.IsType<NotFoundException>(ex);
        Assert.Equal(404, apiEx.StatusCode);
        Assert.Equal("no such document", apiEx.Detail);
        Assert.Equal(1, tp.Handler.CallCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Does_not_retry_a_non_idempotent_post_without_a_key(bool useAsync)
    {
        using var tp = new TestPipeline((req, i, ct) => Responses.Status(HttpStatusCode.ServiceUnavailable));

        var ex = await Capture<Echo>(tp, useAsync, HttpMethod.Post, "/x", body: new { foo = "bar" });

        Assert.IsType<ServerException>(ex);
        Assert.Equal(1, tp.Handler.CallCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Reuses_the_idempotency_key_across_retries(bool useAsync)
    {
        using var tp = new TestPipeline(Responders.Sequence(
            () => Responses.Status(HttpStatusCode.ServiceUnavailable, ("Retry-After", "0")),
            () => Responses.Json(HttpStatusCode.OK, EchoJson)));

        await Request<Echo>(tp, useAsync, HttpMethod.Post, "/x", body: new { a = 1 }, idempotencyKey: "key-123");

        Assert.Equal(2, tp.Handler.CallCount);
        var keys = tp.Handler.Requests.Select(r => r.IdempotencyKey).ToArray();
        Assert.All(keys, k => Assert.Equal("key-123", k));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Retries_the_in_flight_409_then_succeeds(bool useAsync)
    {
        using var tp = new TestPipeline(Responders.Sequence(
            () => Responses.Json(
                HttpStatusCode.Conflict,
                "{\"type\":\"https://api.infersoft.com/problems/request-in-progress\"}",
                ("Retry-After", "0")),
            () => Responses.Json(HttpStatusCode.OK, EchoJson)));

        var result = await Request<Echo>(tp, useAsync, HttpMethod.Post, "/x", idempotencyKey: "k");

        Assert.Equal("hi", result!.Name);
        Assert.Equal(2, tp.Handler.CallCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Does_not_retry_a_plain_409(bool useAsync)
    {
        using var tp = new TestPipeline((req, i, ct) => Responses.Json(
            HttpStatusCode.Conflict, "{\"title\":\"Conflict\"}"));

        var ex = await Capture<Echo>(tp, useAsync, HttpMethod.Post, "/x", idempotencyKey: "k");

        Assert.IsType<ConflictException>(ex);
        Assert.Equal(1, tp.Handler.CallCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Returns_default_for_no_content(bool useAsync)
    {
        using var tp = new TestPipeline((req, i, ct) => Responses.Status(HttpStatusCode.NoContent));

        var result = await Request<Echo>(tp, useAsync, HttpMethod.Delete, "/x/1");

        Assert.Null(result);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Wraps_a_timeout_as_ApiTimeoutException(bool useAsync)
    {
        using var tp = new TestPipeline(
            (req, i, ct) =>
            {
                ct.WaitHandle.WaitOne();
                ct.ThrowIfCancellationRequested();
                return Responses.Status(HttpStatusCode.OK);
            },
            maxRetries: 0,
            apiTimeout: TimeSpan.FromMilliseconds(100));

        var ex = await Capture<Echo>(tp, useAsync, HttpMethod.Get, "/slow");

        Assert.IsType<ApiTimeoutException>(ex);
    }

    [Fact]
    public async Task Caller_cancellation_propagates_unwrapped()
    {
        using var tp = new TestPipeline(
            (req, i, ct) =>
            {
                ct.WaitHandle.WaitOne();
                ct.ThrowIfCancellationRequested();
                return Responses.Status(HttpStatusCode.OK);
            },
            apiTimeout: TimeSpan.FromSeconds(30));

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => tp.Pipeline.RequestAsync<Echo>(HttpMethod.Get, "/slow", cancellationToken: cts.Token));
    }
}
