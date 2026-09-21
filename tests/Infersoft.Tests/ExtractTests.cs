using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Infersoft;
using Infersoft.Tests.Support;
using Xunit;

namespace Infersoft.Tests;

public class ExtractTests
{
    private const string ResultsPage =
        "{\"items\":[{\"id\":5,\"organization_id\":\"o\",\"name\":\"a\",\"status\":\"ready\"," +
        "\"is_valid\":true,\"created_at\":\"2026-01-01T00:00:00Z\",\"has_active_workflow\":false," +
        "\"extraction_results\":[" +
        "{\"prompt_id\":10,\"value\":{\"name\":\"total\",\"parsed_value\":42}}," +
        "{\"prompt_id\":11,\"value\":{\"name\":\"royalty\",\"data_type\":\"Number\"," +
        "\"value_number\":\"0.125\",\"raw_value\":\"1/8\"}}," +
        "{\"prompt_id\":12,\"value\":{\"name\":\"executed_on\",\"data_type\":\"Date\"," +
        "\"raw_value\":\"Not Found\"}}]}]," +
        "\"page\":1,\"page_size\":50,\"has_more\":false}";

    private static HttpResponseMessage JobJson(string status, long id) =>
        Responses.Json(HttpStatusCode.OK,
            $"{{\"id\":{id},\"organization_id\":\"o\",\"organization_name\":\"n\",\"total_docs\":1," +
            $"\"completed_docs\":1,\"error_count\":0,\"status\":\"{status}\",\"createdAt\":\"2026-01-01T00:00:00Z\"}}");

    private static HttpResponseMessage Pipeline(HttpRequestMessage req, string terminalStatus)
    {
        var path = req.RequestUri!.AbsolutePath;
        if (path.EndsWith("/credits/estimate", StringComparison.Ordinal))
        {
            return Responses.Json(HttpStatusCode.OK, "{\"total_credits\":10,\"page_count\":1,\"id\":\"c1\"}");
        }

        if (path.EndsWith("/jobs/start", StringComparison.Ordinal))
        {
            return JobJson("running", 7);
        }

        if (path.EndsWith("/documents/search", StringComparison.Ordinal))
        {
            return Responses.Json(HttpStatusCode.OK, ResultsPage);
        }

        if (path.StartsWith("/api/jobs/", StringComparison.Ordinal))
        {
            return JobJson(terminalStatus, 7);
        }

        return Responses.Status(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Extract_runs_the_pipeline_over_document_ids(bool useAsync)
    {
        var (client, _) = TestClientFactory.Create((req, i, ct) => Pipeline(req, "completed"));
        using (client)
        {
            var result = useAsync
                ? await client.ExtractAsync(documentIds: new long[] { 5 }, prompts: new long[] { 10 })
                : client.Extract(documentIds: new long[] { 5 }, prompts: new long[] { 10 });

            Assert.Equal(JobStatus.Completed, result.Job.Status);
            Assert.Null(result.Upload);
            Assert.Equal(42, ((JsonElement)result.Values[5]["total"]!).GetInt32());
            Assert.Equal(0.125m, Assert.IsType<decimal>(result.Values[5]["royalty"]));
            Assert.Null(result.Values[5]["executed_on"]);
        }
    }

    [Fact]
    public void Extract_failed_job_raises_with_the_job_attached()
    {
        var (client, _) = TestClientFactory.Create((req, i, ct) => Pipeline(req, "failed"));
        using (client)
        {
            var ex = Assert.Throws<ExtractException>(
                () => client.Extract(documentIds: new long[] { 5 }, prompts: new long[] { 10 }));
            Assert.NotNull(ex.Job);
            Assert.Equal(JobStatus.Failed, ex.Job!.Status);
        }
    }

    [Fact]
    public void Extract_aborts_when_every_file_fails_to_upload()
    {
        using var temp = new TempDir();
        var path = temp.Write("a.pdf", new byte[] { 1 });
        var (client, _) = TestClientFactory.Create((req, i, ct) =>
            req.RequestUri!.AbsolutePath.EndsWith("/uploads", StringComparison.Ordinal)
                ? Responses.Json(HttpStatusCode.OK, "{\"items\":[{\"client_file_name\":\"a.pdf\",\"error\":{\"code\":\"bad\",\"message\":\"nope\"}}],\"tags\":[]}")
                : Responses.Status(HttpStatusCode.OK));
        using (client)
        {
            var ex = Assert.Throws<ExtractException>(
                () => client.Extract(files: new[] { path }, prompts: new long[] { 10 }));
            Assert.NotNull(ex.Upload);
            Assert.Single(ex.Upload!.Failed);
        }
    }

    [Fact]
    public void Extract_validates_its_inputs()
    {
        var (client, _) = TestClientFactory.Create((req, i, ct) => Responses.Status(HttpStatusCode.OK));
        using (client)
        {
            // Zero inputs.
            Assert.Throws<ArgumentException>(() => client.Extract(prompts: new long[] { 1 }));
            // Two inputs.
            Assert.Throws<ArgumentException>(
                () => client.Extract(documentIds: new long[] { 1 }, selectors: Selectors.Build(), prompts: new long[] { 1 }));
            // projectName without files.
            Assert.Throws<ArgumentException>(
                () => client.Extract(documentIds: new long[] { 1 }, prompts: new long[] { 1 }, projectName: "x"));
            // Missing prompts.
            Assert.Throws<ArgumentException>(() => client.Extract(documentIds: new long[] { 1 }));
        }
    }
}
