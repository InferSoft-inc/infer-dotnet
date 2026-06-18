using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Infersoft;
using Infersoft.Tests.Support;
using Xunit;

namespace Infersoft.Tests;

public class UploadDownloadTests
{
    private static bool IsUploads(HttpRequestMessage req) => req.RequestUri!.AbsolutePath.EndsWith("/uploads", StringComparison.Ordinal);

    // Echoes a plan with one item per requested file (so the byte-PUT step runs).
    private static HttpResponseMessage Plan(HttpRequestMessage req)
    {
        var bodyText = Encoding.UTF8.GetString(req.Content!.ReadAsByteArrayAsync().GetAwaiter().GetResult());
        using var doc = JsonDocument.Parse(bodyText);
        var items = doc.RootElement.GetProperty("files").EnumerateArray().Select((f, i) =>
            $"{{\"client_file_name\":\"{f.GetProperty("file_name").GetString()}\",\"document_id\":{i + 1}," +
            "\"put_url\":\"https://s3.test/put\",\"required_headers\":{}}");
        return Responses.Json(HttpStatusCode.OK, $"{{\"items\":[{string.Join(",", items)}],\"tags\":[]}}");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Upload_uploads_a_file(bool useAsync)
    {
        using var temp = new TempDir();
        var path = temp.Write("a.pdf", new byte[] { 1, 2, 3 });
        var (client, handler) = TestClientFactory.Create((req, i, ct) =>
            req.Method == HttpMethod.Put ? Responses.Status(HttpStatusCode.OK)
            : IsUploads(req) ? Plan(req)
            : Responses.Status(HttpStatusCode.OK));
        using (client)
        {
            var result = useAsync
                ? await client.Documents.UploadAsync(new[] { path })
                : client.Documents.Upload(new[] { path });

            Assert.Single(result.Succeeded);
            Assert.Equal(new long[] { 1 }, result.DocumentIds);
            Assert.Contains(handler.Requests, r => r.Method == "PUT");
        }
    }

    [Fact]
    public void Upload_flatten_rejects_duplicate_basenames()
    {
        using var temp = new TempDir();
        var p1 = temp.Write("sub1/a.pdf", new byte[] { 1 });
        var p2 = temp.Write("sub2/a.pdf", new byte[] { 2 });
        var (client, _) = TestClientFactory.Create((req, i, ct) => Responses.Status(HttpStatusCode.OK));
        using (client)
        {
            Assert.Throws<ArgumentException>(() => client.Documents.Upload(new[] { p1, p2 }));
        }
    }

    [Fact]
    public void Upload_onDuplicate_Block_skips_and_deletes_placeholder()
    {
        using var temp = new TempDir();
        var path = temp.Write("a.pdf", new byte[] { 1 });
        var (client, handler) = TestClientFactory.Create((req, i, ct) =>
        {
            if (req.Method == HttpMethod.Delete)
            {
                return Responses.Status(HttpStatusCode.NoContent);
            }

            if (IsUploads(req))
            {
                return Responses.Json(HttpStatusCode.OK,
                    "{\"items\":[{\"client_file_name\":\"a.pdf\",\"document_id\":1,\"put_url\":\"https://s3.test/put\"," +
                    "\"duplicates\":[{\"document_id\":9,\"name\":\"a.pdf\"}]}],\"tags\":[]}");
            }

            return Responses.Status(HttpStatusCode.OK);
        });
        using (client)
        {
            var result = client.Documents.Upload(new[] { path }, onDuplicate: OnDuplicate.Block);

            Assert.Single(result.Skipped);
            Assert.DoesNotContain(handler.Requests, r => r.Method == "PUT");
            Assert.Contains(handler.Requests, r => r.Method == "DELETE");
        }
    }

    [Fact]
    public void UploadMany_batches_with_derived_keys()
    {
        using var temp = new TempDir();
        var files = new[]
        {
            temp.Write("a.pdf", new byte[] { 1 }),
            temp.Write("b.pdf", new byte[] { 2 }),
            temp.Write("c.pdf", new byte[] { 3 }),
        };
        var (client, handler) = TestClientFactory.Create((req, i, ct) =>
            req.Method == HttpMethod.Put ? Responses.Status(HttpStatusCode.OK)
            : IsUploads(req) ? Plan(req)
            : Responses.Status(HttpStatusCode.OK));
        using (client)
        {
            var result = client.Documents.UploadMany(files, batchSize: 2);

            Assert.Equal(3, result.Succeeded.Count);
            var uploadKeys = handler.Requests.Where(r => r.Uri!.AbsolutePath.EndsWith("/uploads", StringComparison.Ordinal))
                .Select(r => r.IdempotencyKey).ToList();
            Assert.Equal(2, uploadKeys.Count);
            Assert.EndsWith("-0000", uploadKeys[0]!, StringComparison.Ordinal);
            Assert.EndsWith("-0001", uploadKeys[1]!, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void UploadMany_partial_failure_carries_partial_state()
    {
        using var temp = new TempDir();
        var files = new[] { temp.Write("a.pdf", new byte[] { 1 }), temp.Write("b.pdf", new byte[] { 2 }) };
        var uploadCalls = 0;
        var (client, _) = TestClientFactory.Create(
            (req, i, ct) =>
            {
                if (req.Method == HttpMethod.Put)
                {
                    return Responses.Status(HttpStatusCode.OK);
                }

                if (IsUploads(req))
                {
                    uploadCalls++;
                    return uploadCalls == 1 ? Plan(req) : Responses.Status(HttpStatusCode.InternalServerError);
                }

                return Responses.Status(HttpStatusCode.OK);
            },
            maxRetries: 0);
        using (client)
        {
            var ex = Assert.Throws<UploadManyException>(() => client.Documents.UploadMany(files, batchSize: 1));
            Assert.Equal(1, ex.BatchesCompleted);
            Assert.Single(ex.Partial.Outcomes);
        }
    }

    [Fact]
    public void WaitUntilReady_polls_until_ready()
    {
        var searchCalls = 0;
        var (client, _) = TestClientFactory.Create((req, i, ct) =>
        {
            searchCalls++;
            var status = searchCalls == 1 ? "processing" : "ready";
            return Responses.Json(HttpStatusCode.OK,
                $"{{\"items\":[{{\"id\":1,\"organization_id\":\"o\",\"name\":\"a\",\"status\":\"{status}\"," +
                "\"is_valid\":true,\"created_at\":\"2026-01-01T00:00:00Z\",\"has_active_workflow\":false}]," +
                "\"page\":1,\"page_size\":50,\"has_more\":false}");
        });
        using (client)
        {
            var docs = client.Documents.WaitUntilReady(new long[] { 1 }, pollInterval: TimeSpan.FromMilliseconds(1));
            Assert.Single(docs);
            Assert.True(searchCalls >= 2);
        }
    }

    [Fact]
    public void DownloadUrl_returns_the_signed_url()
    {
        var (client, _) = TestClientFactory.Create((req, i, ct) =>
            Responses.Json(HttpStatusCode.OK, "{\"url\":\"https://s3.test/file\",\"expires_at\":\"2026-01-01T00:00:00Z\"}"));
        using (client)
        {
            Assert.Equal("https://s3.test/file", client.Documents.DownloadUrl(9).Url);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Download_writes_the_file_unauthenticated(bool useAsync)
    {
        using var temp = new TempDir();
        var bytes = new byte[] { 9, 8, 7 };
        var (client, handler) = TestClientFactory.Create((req, i, ct) =>
        {
            if (req.RequestUri!.AbsoluteUri == "https://s3.test/file")
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
            }

            if (req.RequestUri.AbsolutePath.EndsWith("/download", StringComparison.Ordinal))
            {
                return Responses.Json(HttpStatusCode.OK, "{\"url\":\"https://s3.test/file\",\"expires_at\":\"2026-01-01T00:00:00Z\"}");
            }

            return Responses.Json(HttpStatusCode.OK,
                "{\"id\":9,\"organization_id\":\"o\",\"name\":\"report.pdf\",\"status\":\"ready\"," +
                "\"is_valid\":true,\"created_at\":\"2026-01-01T00:00:00Z\",\"has_active_workflow\":false}");
        });
        using (client)
        {
            var target = useAsync
                ? await client.Documents.DownloadAsync(9, temp.Path)
                : client.Documents.Download(9, temp.Path);

            Assert.Equal(Path.Combine(temp.Path, "report.pdf"), target);
            Assert.Equal(bytes, File.ReadAllBytes(target));

            var signedGet = handler.Requests.Single(r => r.Uri!.AbsoluteUri == "https://s3.test/file");
            Assert.Null(signedGet.Authorization);
        }
    }

    [Fact]
    public void Download_neutralizes_a_traversal_name()
    {
        using var temp = new TempDir();
        var (client, _) = TestClientFactory.Create((req, i, ct) =>
            req.RequestUri!.AbsoluteUri == "https://s3.test/file"
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 1 }) }
                : Responses.Json(HttpStatusCode.OK, "{\"url\":\"https://s3.test/file\",\"expires_at\":\"2026-01-01T00:00:00Z\"}"));
        using (client)
        {
            var target = client.Documents.Download(9, temp.Path, fileName: "../escape.pdf");
            Assert.Equal(Path.Combine(temp.Path, "escape.pdf"), target);
        }
    }

    [Fact]
    public void Download_refuses_to_overwrite_by_default()
    {
        using var temp = new TempDir();
        temp.Write("report.pdf", new byte[] { 0 });
        var (client, _) = TestClientFactory.Create((req, i, ct) =>
            Responses.Json(HttpStatusCode.OK, "{\"url\":\"https://s3.test/file\",\"expires_at\":\"2026-01-01T00:00:00Z\"}"));
        using (client)
        {
            Assert.Throws<IOException>(() => client.Documents.Download(9, temp.Path, fileName: "report.pdf"));
        }
    }
}
