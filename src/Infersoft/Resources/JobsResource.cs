using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Infersoft.Internal;

namespace Infersoft;

/// <summary>
/// The Jobs resource (<c>client.Jobs</c>): credit estimation, starting jobs, waiting for
/// completion, and reading results. Every method has a synchronous and an asynchronous member.
/// </summary>
public sealed class JobsResource
{
    private static readonly HashSet<string> KnownJobStatuses = new(StringComparer.Ordinal)
    {
        JobStatus.Running.Value,
        JobStatus.Completed.Value,
        JobStatus.Failed.Value,
        JobStatus.PartialSuccess.Value,
        JobStatus.CreatingWorkflows.Value,
    };

    private static readonly HashSet<string> TerminalJobStatuses = new(StringComparer.Ordinal)
    {
        JobStatus.Completed.Value,
        JobStatus.Failed.Value,
        JobStatus.PartialSuccess.Value,
    };

    private readonly RequestPipeline _http;
    private readonly ILogger _logger;
    private readonly DocumentsResource _documents;

    internal JobsResource(RequestPipeline http, ILogger logger)
    {
        _http = http;
        _logger = logger;
        _documents = new DocumentsResource(http, logger);
    }

    /// <summary>Estimate the credits a job would cost.</summary>
    public CreditsEstimate Estimate(
        string step,
        Selectors? selectors = null,
        IEnumerable<long>? documentIds = null,
        IEnumerable<long>? prompts = null,
        bool synchronous = false,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        _http.Request<CreditsEstimate>(
            HttpMethod.Post, "/api/jobs/credits/estimate",
            BuildEstimate(step, selectors, documentIds, prompts, synchronous),
            idempotencyKey: idempotencyKey ?? RequestPipeline.NewIdempotencyKey(),
            cancellationToken: cancellationToken)!;

    /// <inheritdoc cref="Estimate"/>
    public async Task<CreditsEstimate> EstimateAsync(
        string step,
        Selectors? selectors = null,
        IEnumerable<long>? documentIds = null,
        IEnumerable<long>? prompts = null,
        bool synchronous = false,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        (await _http.RequestAsync<CreditsEstimate>(
            HttpMethod.Post, "/api/jobs/credits/estimate",
            BuildEstimate(step, selectors, documentIds, prompts, synchronous),
            idempotencyKey: idempotencyKey ?? RequestPipeline.NewIdempotencyKey(),
            cancellationToken: cancellationToken).ConfigureAwait(false))!;

    /// <summary>
    /// Price a workflow without reserving anything. Same request shape and formula as
    /// <see cref="Estimate"/>, but read-only: no documents are reserved and no id is
    /// returned, so the result cannot be passed to <see cref="Start"/>. Use it while
    /// composing a job; call <see cref="Estimate"/> when ready to start.
    /// </summary>
    public CreditsQuote Quote(
        string step,
        Selectors? selectors = null,
        IEnumerable<long>? documentIds = null,
        IEnumerable<long>? prompts = null,
        bool synchronous = false,
        CancellationToken cancellationToken = default) =>
        _http.Request<CreditsQuote>(
            HttpMethod.Post, "/api/jobs/credits/quote",
            BuildEstimate(step, selectors, documentIds, prompts, synchronous),
            idempotent: true, cancellationToken: cancellationToken)!;

    /// <inheritdoc cref="Quote"/>
    public async Task<CreditsQuote> QuoteAsync(
        string step,
        Selectors? selectors = null,
        IEnumerable<long>? documentIds = null,
        IEnumerable<long>? prompts = null,
        bool synchronous = false,
        CancellationToken cancellationToken = default) =>
        (await _http.RequestAsync<CreditsQuote>(
            HttpMethod.Post, "/api/jobs/credits/quote",
            BuildEstimate(step, selectors, documentIds, prompts, synchronous),
            idempotent: true, cancellationToken: cancellationToken).ConfigureAwait(false))!;

    /// <summary>Start a job from a credits estimate id.</summary>
    public Job Start(string creditsId, long? projectId = null, string? idempotencyKey = null, CancellationToken cancellationToken = default) =>
        _http.Request<Job>(
            HttpMethod.Post, "/api/jobs/start",
            new JobStartRequest { CreditsId = creditsId, ProjectId = projectId },
            idempotencyKey: idempotencyKey ?? RequestPipeline.NewIdempotencyKey(),
            cancellationToken: cancellationToken)!;

    /// <inheritdoc cref="Start"/>
    public async Task<Job> StartAsync(string creditsId, long? projectId = null, string? idempotencyKey = null, CancellationToken cancellationToken = default) =>
        (await _http.RequestAsync<Job>(
            HttpMethod.Post, "/api/jobs/start",
            new JobStartRequest { CreditsId = creditsId, ProjectId = projectId },
            idempotencyKey: idempotencyKey ?? RequestPipeline.NewIdempotencyKey(),
            cancellationToken: cancellationToken).ConfigureAwait(false))!;

    /// <summary>
    /// Estimate then immediately start the job. <paramref name="maxCredits"/> guards the budget
    /// (a <see cref="CreditsLimitExceededException"/> is thrown and the job is not started). With
    /// <paramref name="wait"/>, blocks until the job reaches a terminal status.
    /// </summary>
    public Job Run(
        string step,
        Selectors? selectors = null,
        IEnumerable<long>? documentIds = null,
        IEnumerable<long>? prompts = null,
        bool synchronous = false,
        long? projectId = null,
        int? maxCredits = null,
        bool wait = false,
        TimeSpan? maxWait = null,
        TimeSpan? pollInterval = null,
        CancellationToken cancellationToken = default)
    {
        var resolved = SelectorResolver.Resolve(selectors, documentIds, required: true);
        var estimate = Estimate(step, resolved, prompts: prompts, synchronous: synchronous, cancellationToken: cancellationToken);
        if (maxCredits is int cap && estimate.TotalCredits > cap)
        {
            throw new CreditsLimitExceededException(estimate, cap);
        }

        var job = Start(estimate.Id, projectId: projectId, cancellationToken: cancellationToken);
        return wait ? Wait(job.Id, maxWait, pollInterval, cancellationToken) : job;
    }

    /// <inheritdoc cref="Run"/>
    public async Task<Job> RunAsync(
        string step,
        Selectors? selectors = null,
        IEnumerable<long>? documentIds = null,
        IEnumerable<long>? prompts = null,
        bool synchronous = false,
        long? projectId = null,
        int? maxCredits = null,
        bool wait = false,
        TimeSpan? maxWait = null,
        TimeSpan? pollInterval = null,
        CancellationToken cancellationToken = default)
    {
        var resolved = SelectorResolver.Resolve(selectors, documentIds, required: true);
        var estimate = await EstimateAsync(step, resolved, prompts: prompts, synchronous: synchronous, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (maxCredits is int cap && estimate.TotalCredits > cap)
        {
            throw new CreditsLimitExceededException(estimate, cap);
        }

        var job = await StartAsync(estimate.Id, projectId: projectId, cancellationToken: cancellationToken).ConfigureAwait(false);
        return wait ? await WaitAsync(job.Id, maxWait, pollInterval, cancellationToken).ConfigureAwait(false) : job;
    }

    /// <summary>Get a job by id.</summary>
    public Job Get(long jobId, CancellationToken cancellationToken = default) =>
        _http.Request<Job>(HttpMethod.Get, $"/api/jobs/{jobId}", cancellationToken: cancellationToken)!;

    /// <inheritdoc cref="Get"/>
    public async Task<Job> GetAsync(long jobId, CancellationToken cancellationToken = default) =>
        (await _http.RequestAsync<Job>(HttpMethod.Get, $"/api/jobs/{jobId}", cancellationToken: cancellationToken).ConfigureAwait(false))!;

    /// <summary>
    /// Poll a job until it reaches a terminal status (<c>completed</c>/<c>failed</c>/<c>partial_success</c>)
    /// and return it — a finished-but-failed job is returned, not thrown. Default budget is 30 minutes;
    /// pass <see cref="Timeout.InfiniteTimeSpan"/> to wait without a deadline.
    /// </summary>
    public Job Wait(long jobId, TimeSpan? maxWait = null, TimeSpan? pollInterval = null, CancellationToken cancellationToken = default)
    {
        var budget = maxWait ?? Polling.DefaultJobWait;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return Polling.PollUntil(
            () => Get(jobId, cancellationToken),
            job => IsTerminal(job, seen),
            budget,
            pollInterval ?? Polling.DefaultPollInterval,
            TimeoutMessage(jobId, budget),
            cancellationToken);
    }

    /// <summary>Poll the given job until it reaches a terminal status and return it.</summary>
    public Job Wait(Job job, TimeSpan? maxWait = null, TimeSpan? pollInterval = null, CancellationToken cancellationToken = default) =>
        Wait(job.Id, maxWait, pollInterval, cancellationToken);

    /// <inheritdoc cref="Wait(long, System.TimeSpan?, System.TimeSpan?, System.Threading.CancellationToken)"/>
    public Task<Job> WaitAsync(long jobId, TimeSpan? maxWait = null, TimeSpan? pollInterval = null, CancellationToken cancellationToken = default)
    {
        var budget = maxWait ?? Polling.DefaultJobWait;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return Polling.PollUntilAsync(
            token => GetAsync(jobId, token),
            job => IsTerminal(job, seen),
            budget,
            pollInterval ?? Polling.DefaultPollInterval,
            TimeoutMessage(jobId, budget),
            cancellationToken);
    }

    /// <inheritdoc cref="WaitAsync(long, System.TimeSpan?, System.TimeSpan?, System.Threading.CancellationToken)"/>
    public Task<Job> WaitAsync(Job job, TimeSpan? maxWait = null, TimeSpan? pollInterval = null, CancellationToken cancellationToken = default) =>
        WaitAsync(job.Id, maxWait, pollInterval, cancellationToken);

    /// <summary>Yield the documents a job touched (pass <paramref name="prompts"/> to populate extraction values).</summary>
    public IEnumerable<DocumentSummary> Results(
        long jobId, IEnumerable<long>? prompts = null, int pageSize = 50, string orderBy = "id", string orderDir = "asc", CancellationToken cancellationToken = default) =>
        _documents.Iterate(JobSelector(jobId), prompts: prompts, pageSize: pageSize, orderBy: orderBy, orderDir: orderDir, cancellationToken: cancellationToken);

    /// <inheritdoc cref="Results(long, System.Collections.Generic.IEnumerable{long}, int, string, string, System.Threading.CancellationToken)"/>
    public IEnumerable<DocumentSummary> Results(
        Job job, IEnumerable<long>? prompts = null, int pageSize = 50, string orderBy = "id", string orderDir = "asc", CancellationToken cancellationToken = default) =>
        Results(job.Id, prompts, pageSize, orderBy, orderDir, cancellationToken);

    /// <inheritdoc cref="Results(long, System.Collections.Generic.IEnumerable{long}, int, string, string, System.Threading.CancellationToken)"/>
    public IAsyncEnumerable<DocumentSummary> ResultsAsync(
        long jobId, IEnumerable<long>? prompts = null, int pageSize = 50, string orderBy = "id", string orderDir = "asc", CancellationToken cancellationToken = default) =>
        _documents.IterateAsync(JobSelector(jobId), prompts: prompts, pageSize: pageSize, orderBy: orderBy, orderDir: orderDir, cancellationToken: cancellationToken);

    /// <inheritdoc cref="ResultsAsync(long, System.Collections.Generic.IEnumerable{long}, int, string, string, System.Threading.CancellationToken)"/>
    public IAsyncEnumerable<DocumentSummary> ResultsAsync(
        Job job, IEnumerable<long>? prompts = null, int pageSize = 50, string orderBy = "id", string orderDir = "asc", CancellationToken cancellationToken = default) =>
        ResultsAsync(job.Id, prompts, pageSize, orderBy, orderDir, cancellationToken);

    /// <summary>Search jobs (one page).</summary>
    public JobPage Search(
        IEnumerable<string>? statuses = null,
        string? createdFrom = null,
        string? createdTo = null,
        int page = 1,
        int pageSize = 50,
        string orderBy = "id",
        string orderDir = "desc",
        CancellationToken cancellationToken = default) =>
        _http.Request<JobPage>(
            HttpMethod.Post, "/api/jobs/search",
            BuildSearch(statuses, createdFrom, createdTo, page, pageSize, orderBy, orderDir),
            idempotent: true, cancellationToken: cancellationToken)!;

    /// <inheritdoc cref="Search"/>
    public async Task<JobPage> SearchAsync(
        IEnumerable<string>? statuses = null,
        string? createdFrom = null,
        string? createdTo = null,
        int page = 1,
        int pageSize = 50,
        string orderBy = "id",
        string orderDir = "desc",
        CancellationToken cancellationToken = default) =>
        (await _http.RequestAsync<JobPage>(
            HttpMethod.Post, "/api/jobs/search",
            BuildSearch(statuses, createdFrom, createdTo, page, pageSize, orderBy, orderDir),
            idempotent: true, cancellationToken: cancellationToken).ConfigureAwait(false))!;

    /// <summary>Yield every matching job, fetching pages on demand.</summary>
    public IEnumerable<Job> Iterate(
        IEnumerable<string>? statuses = null,
        string? createdFrom = null,
        string? createdTo = null,
        int pageSize = 50,
        string orderBy = "id",
        string orderDir = "desc",
        CancellationToken cancellationToken = default)
    {
        var page = 1;
        while (true)
        {
            var result = Search(statuses, createdFrom, createdTo, page, pageSize, orderBy, orderDir, cancellationToken);
            foreach (var item in result.Items)
            {
                yield return item;
            }

            if (!result.HasMore)
            {
                yield break;
            }

            page++;
        }
    }

    /// <inheritdoc cref="Iterate"/>
    public async IAsyncEnumerable<Job> IterateAsync(
        IEnumerable<string>? statuses = null,
        string? createdFrom = null,
        string? createdTo = null,
        int pageSize = 50,
        string orderBy = "id",
        string orderDir = "desc",
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var page = 1;
        while (true)
        {
            var result = await SearchAsync(statuses, createdFrom, createdTo, page, pageSize, orderBy, orderDir, cancellationToken).ConfigureAwait(false);
            foreach (var item in result.Items)
            {
                yield return item;
            }

            if (!result.HasMore)
            {
                yield break;
            }

            page++;
        }
    }

    private static Selectors JobSelector(long jobId) =>
        Selectors.Build(include: new[] { Selector.Job(new[] { jobId }) });

    private static string TimeoutMessage(long jobId, TimeSpan budget) =>
        string.Format(CultureInfo.InvariantCulture, "job {0} did not reach a terminal status within {1}", jobId, budget);

    private static JobEstimateRequest BuildEstimate(
        string step, Selectors? selectors, IEnumerable<long>? documentIds, IEnumerable<long>? prompts, bool synchronous) =>
        new()
        {
            Steps = new[] { step },
            Selectors = SelectorResolver.Resolve(selectors, documentIds, required: true),
            Synchronous = synchronous,
            Prompts = prompts?.ToList(),
        };

    private static JobSearchRequest BuildSearch(
        IEnumerable<string>? statuses, string? createdFrom, string? createdTo, int page, int pageSize, string orderBy, string orderDir) =>
        new()
        {
            Statuses = statuses?.ToList(),
            CreatedFrom = createdFrom,
            CreatedTo = createdTo,
            Page = page,
            PageSize = pageSize,
            OrderBy = orderBy,
            OrderDir = orderDir,
        };

    private bool IsTerminal(Job job, ISet<string> seen)
    {
        Polling.WarnOnceUnknownStatus(_logger, seen, job.Status.Value, KnownJobStatuses, "job");
        return TerminalJobStatuses.Contains(job.Status.Value);
    }
}
