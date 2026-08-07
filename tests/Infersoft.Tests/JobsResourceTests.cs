using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Infersoft;
using Infersoft.Tests.Support;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Infersoft.Tests;

public class JobsResourceTests
{
    private static readonly TimeSpan FastPoll = TimeSpan.FromMilliseconds(1);

    private static (JobsResource Jobs, TestPipeline Pipeline) MakeJobs(
        Func<HttpRequestMessage, int, CancellationToken, HttpResponseMessage> responder, ILogger? logger = null)
    {
        var pipeline = new TestPipeline(responder);
        return (new JobsResource(pipeline.Pipeline, logger ?? NullLogger.Instance), pipeline);
    }

    private static HttpResponseMessage JobJson(string status, long id = 1) =>
        Responses.Json(HttpStatusCode.OK,
            $"{{\"id\":{id},\"organization_id\":\"o\",\"organization_name\":\"n\",\"total_docs\":1," +
            $"\"completed_docs\":1,\"error_count\":0,\"status\":\"{status}\",\"createdAt\":\"2026-01-01T00:00:00Z\"}}");

    private static HttpResponseMessage Estimate(int credits) =>
        Responses.Json(HttpStatusCode.OK, $"{{\"total_credits\":{credits},\"page_count\":5,\"id\":\"c1\"}}");

    private static bool IsEstimate(HttpRequestMessage req) => req.RequestUri!.AbsolutePath.EndsWith("/estimate", StringComparison.Ordinal);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Estimate_sends_steps_and_idempotency_key(bool useAsync)
    {
        var (jobs, tp) = MakeJobs((req, i, ct) => Estimate(100));
        using (tp)
        {
            var estimate = useAsync
                ? await jobs.EstimateAsync("extractor", documentIds: new long[] { 1 })
                : jobs.Estimate("extractor", documentIds: new long[] { 1 });

            Assert.Equal(100, estimate.TotalCredits);
            Assert.Equal("c1", estimate.Id);

            var request = tp.Handler.Requests.Single();
            Assert.False(string.IsNullOrEmpty(request.IdempotencyKey));
            using var body = JsonDocument.Parse(request.Body!);
            Assert.Equal("extractor", body.RootElement.GetProperty("steps")[0].GetString());
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Quote_is_read_only_and_sends_estimate_shaped_body(bool useAsync)
    {
        var (jobs, tp) = MakeJobs((req, i, ct) => Responses.Json(HttpStatusCode.OK,
            "{\"total_credits\":17,\"page_count\":9,\"document_count\":4}"));
        using (tp)
        {
            var quote = useAsync
                ? await jobs.QuoteAsync("extractor", documentIds: new long[] { 1 }, prompts: new long[] { 5 }, synchronous: true)
                : jobs.Quote("extractor", documentIds: new long[] { 1 }, prompts: new long[] { 5 }, synchronous: true);

            Assert.Equal(17, quote.TotalCredits);
            Assert.Equal(9, quote.PageCount);
            Assert.Equal(4, quote.DocumentCount);

            var request = tp.Handler.Requests.Single();
            Assert.EndsWith("/api/jobs/credits/quote", request.Uri!.AbsolutePath, StringComparison.Ordinal);
            Assert.True(string.IsNullOrEmpty(request.IdempotencyKey));
            using var body = JsonDocument.Parse(request.Body!);
            Assert.Equal("extractor", body.RootElement.GetProperty("steps")[0].GetString());
            Assert.True(body.RootElement.GetProperty("synchronous").GetBoolean());
            Assert.Equal(5, body.RootElement.GetProperty("prompts")[0].GetInt64());
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Run_throws_when_estimate_exceeds_max_credits(bool useAsync)
    {
        var (jobs, tp) = MakeJobs((req, i, ct) => IsEstimate(req) ? Estimate(100) : JobJson("running"));
        using (tp)
        {
            var ex = useAsync
                ? await Record.ExceptionAsync(() => jobs.RunAsync("extractor", documentIds: new long[] { 1 }, maxCredits: 50))
                : Record.Exception(() => jobs.Run("extractor", documentIds: new long[] { 1 }, maxCredits: 50));

            var credits = Assert.IsType<CreditsLimitExceededException>(ex);
            Assert.Equal(100, credits.Estimate.TotalCredits);
            Assert.Equal(50, credits.MaxCredits);
            Assert.DoesNotContain(tp.Handler.Requests, r => r.Uri!.AbsolutePath.EndsWith("/start", StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Run_starts_when_under_budget(bool useAsync)
    {
        var (jobs, tp) = MakeJobs((req, i, ct) => IsEstimate(req) ? Estimate(100) : JobJson("running", id: 42));
        using (tp)
        {
            var job = useAsync
                ? await jobs.RunAsync("extractor", documentIds: new long[] { 1 }, maxCredits: 200)
                : jobs.Run("extractor", documentIds: new long[] { 1 }, maxCredits: 200);

            Assert.Equal(42, job.Id);
            Assert.Contains(tp.Handler.Requests, r => r.Uri!.AbsolutePath.EndsWith("/start", StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Wait_polls_until_terminal(bool useAsync)
    {
        var (jobs, tp) = MakeJobs((req, i, ct) => JobJson(i == 0 ? "running" : "completed"));
        using (tp)
        {
            var job = useAsync
                ? await jobs.WaitAsync(1, pollInterval: FastPoll)
                : jobs.Wait(1, pollInterval: FastPoll);

            Assert.Equal(JobStatus.Completed, job.Status);
        }
    }

    [Fact]
    public void Wait_returns_a_failed_job_rather_than_throwing()
    {
        var (jobs, tp) = MakeJobs((req, i, ct) => JobJson("failed"));
        using (tp)
        {
            var job = jobs.Wait(1);
            Assert.Equal(JobStatus.Failed, job.Status);
        }
    }

    [Fact]
    public async Task Wait_times_out_on_a_stuck_job()
    {
        var (jobs, tp) = MakeJobs((req, i, ct) => JobJson("running"));
        using (tp)
        {
            await Assert.ThrowsAsync<WaitTimeoutException>(
                () => jobs.WaitAsync(1, maxWait: TimeSpan.FromMilliseconds(50), pollInterval: FastPoll));
        }
    }

    [Fact]
    public async Task Wait_with_infinite_budget_keeps_polling()
    {
        var (jobs, tp) = MakeJobs((req, i, ct) => JobJson(i < 3 ? "running" : "completed"));
        using (tp)
        {
            var job = await jobs.WaitAsync(1, maxWait: Timeout.InfiniteTimeSpan, pollInterval: FastPoll);
            Assert.Equal(JobStatus.Completed, job.Status);
        }
    }

    [Fact]
    public async Task Unknown_status_warns_once_and_keeps_polling()
    {
        var logger = new ListLogger();
        var (jobs, tp) = MakeJobs((req, i, ct) => JobJson("frobnicate"), logger);
        using (tp)
        {
            await Assert.ThrowsAsync<WaitTimeoutException>(
                () => jobs.WaitAsync(1, maxWait: TimeSpan.FromMilliseconds(50), pollInterval: FastPoll));

            Assert.Single(logger.Warnings);
            Assert.Contains("frobnicate", logger.Warnings[0], StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Iterate_fetches_all_pages()
    {
        var (jobs, tp) = MakeJobs((req, i, ct) => Responses.Json(
            HttpStatusCode.OK,
            i == 0
                ? "{\"items\":[" + RawJob(1) + "],\"page\":1,\"page_size\":50,\"has_more\":true}"
                : "{\"items\":[" + RawJob(2) + "],\"page\":2,\"page_size\":50,\"has_more\":false}"));
        using (tp)
        {
            var ids = jobs.Iterate().Select(j => j.Id).ToList();
            Assert.Equal(new long[] { 1, 2 }, ids);
        }
    }

    [Fact]
    public void Results_searches_documents_by_job_selector()
    {
        const string docPage =
            "{\"items\":[{\"id\":5,\"organization_id\":\"o\",\"name\":\"a\",\"status\":\"ready\"," +
            "\"is_valid\":true,\"created_at\":\"2026-01-01T00:00:00Z\",\"has_active_workflow\":false}]," +
            "\"page\":1,\"page_size\":50,\"has_more\":false}";
        var (jobs, tp) = MakeJobs((req, i, ct) => Responses.Json(HttpStatusCode.OK, docPage));
        using (tp)
        {
            var docs = jobs.Results(7).ToList();
            Assert.Single(docs);
            Assert.Equal(5, docs[0].Id);

            var request = tp.Handler.Requests.Single();
            using var body = JsonDocument.Parse(request.Body!);
            var type = body.RootElement.GetProperty("selectors").GetProperty("include")[0].GetProperty("type").GetString();
            Assert.Equal("jobSelector", type);
        }
    }

    private static string RawJob(long id) =>
        $"{{\"id\":{id},\"organization_id\":\"o\",\"organization_name\":\"n\",\"total_docs\":1," +
        $"\"completed_docs\":1,\"error_count\":0,\"status\":\"completed\",\"createdAt\":\"2026-01-01T00:00:00Z\"}}";
}
