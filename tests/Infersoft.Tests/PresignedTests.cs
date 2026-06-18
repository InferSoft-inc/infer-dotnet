using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Infersoft;
using Infersoft.Tests.Support;
using Xunit;

namespace Infersoft.Tests;

public class PresignedTests
{
    private static readonly byte[] Payload = Encoding.UTF8.GetBytes("file-bytes");

    private static Task Put(TestPipeline tp, bool useAsync) =>
        useAsync
            ? tp.Pipeline.PutPresignedAsync("https://s3.test/put", Payload, null, CancellationToken.None)
            : RunSync(() => tp.Pipeline.PutPresigned("https://s3.test/put", Payload, null, CancellationToken.None));

    private static Task<byte[]> Get(TestPipeline tp, bool useAsync) =>
        useAsync
            ? tp.Pipeline.GetPresignedAsync("https://s3.test/get", CancellationToken.None)
            : Task.FromResult(tp.Pipeline.GetPresigned("https://s3.test/get", CancellationToken.None));

    private static Task RunSync(Action action)
    {
        action();
        return Task.CompletedTask;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Put_retries_a_transient_status_then_succeeds(bool useAsync)
    {
        using var tp = new TestPipeline(Responders.Sequence(
            () => Responses.Status(HttpStatusCode.ServiceUnavailable, ("Retry-After", "0")),
            () => Responses.Status(HttpStatusCode.OK)));

        await Put(tp, useAsync);

        Assert.Equal(2, tp.Handler.CallCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Put_retries_a_transport_error_then_succeeds(bool useAsync)
    {
        using var tp = new TestPipeline((req, i, ct) =>
            i == 0 ? throw new HttpRequestException("reset") : Responses.Status(HttpStatusCode.OK));

        await Put(tp, useAsync);

        Assert.Equal(2, tp.Handler.CallCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Put_fails_immediately_on_403(bool useAsync)
    {
        using var tp = new TestPipeline((req, i, ct) => Responses.Status(HttpStatusCode.Forbidden));

        var ex = useAsync
            ? await Record.ExceptionAsync(() => Put(tp, useAsync))
            : Record.Exception(() => tp.Pipeline.PutPresigned("https://s3.test/put", Payload, null, CancellationToken.None));

        var transfer = Assert.IsType<UploadTransferException>(ex);
        Assert.Equal(403, transfer.StatusCode);
        Assert.Equal(1, tp.Handler.CallCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Put_wraps_exhaustion_as_UploadTransferException(bool useAsync)
    {
        using var tp = new TestPipeline(
            (req, i, ct) => Responses.Status(HttpStatusCode.ServiceUnavailable, ("Retry-After", "0")),
            uploadMaxRetries: 1);

        var ex = useAsync
            ? await Record.ExceptionAsync(() => Put(tp, useAsync))
            : Record.Exception(() => tp.Pipeline.PutPresigned("https://s3.test/put", Payload, null, CancellationToken.None));

        Assert.IsType<UploadTransferException>(ex);
        Assert.Equal(2, tp.Handler.CallCount); // 1 + 1 retry
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Get_returns_the_bytes(bool useAsync)
    {
        using var tp = new TestPipeline((req, i, ct) =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Payload) });

        var bytes = await Get(tp, useAsync);

        Assert.Equal(Payload, bytes);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Get_fails_immediately_on_404(bool useAsync)
    {
        using var tp = new TestPipeline((req, i, ct) => Responses.Status(HttpStatusCode.NotFound));

        var ex = useAsync
            ? await Record.ExceptionAsync(() => Get(tp, useAsync))
            : Record.Exception(() => tp.Pipeline.GetPresigned("https://s3.test/get", CancellationToken.None));

        Assert.IsType<DownloadTransferException>(ex);
        Assert.Equal(1, tp.Handler.CallCount);
    }
}
