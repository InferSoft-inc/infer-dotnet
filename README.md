# Infersoft .NET SDK

A .NET SDK for the Infersoft document-processing REST API.

Full endpoint reference: see [api.md](api.md).

## Requirements

- .NET 8, or any `netstandard2.0`-compatible runtime (.NET Framework 4.6.2+, etc.).

## Install

_Not yet published to NuGet._

## Authentication & configuration

The client authenticates with OAuth2 client-credentials. Pass credentials explicitly or set the
`INFERSOFT_*` environment variables (any constructor argument falls back to its env var):

| Setting | Env var | Default |
| --- | --- | --- |
| `ClientId` | `INFERSOFT_CLIENT_ID` | — (required) |
| `ClientSecret` | `INFERSOFT_CLIENT_SECRET` | — (required) |
| `BaseUrl` | `INFERSOFT_BASE_URL` | `https://api.infersoft.com` |
| `TokenUrl` | `INFERSOFT_TOKEN_URL` | the Infersoft OAuth2 token endpoint |
| `Audience` | `INFERSOFT_AUDIENCE` | `https://api.infersoft.com` |

```csharp
using Infersoft;

// From env vars:
using var client = new InfersoftClient(
    Environment.GetEnvironmentVariable("INFERSOFT_CLIENT_ID")!,
    Environment.GetEnvironmentVariable("INFERSOFT_CLIENT_SECRET")!);

// Or with full options:
using var client2 = new InfersoftClient(new InfersoftClientOptions
{
    ClientId = "…",
    ClientSecret = "…",
    Timeout = TimeSpan.FromSeconds(30),
    MaxRetries = 2,
});
```

The token is fetched lazily, cached until shortly before expiry, refreshed on a 401, and
concurrent refreshes are coalesced into a single request.

## Quickstart

```csharp
using Infersoft;

using var client = new InfersoftClient("id", "secret");

// Upload PDFs, run the extractor with two prompts, and get the values back:
ExtractResult result = await client.ExtractAsync(
    files: new[] { "invoice-1.pdf", "invoice-2.pdf" },
    prompts: new long[] { 101, 102 });

foreach (var (documentId, fields) in result.Values)
{
    Console.WriteLine($"document {documentId}: {string.Join(", ", fields.Keys)}");
}
```

## The sync + async surface, `CancellationToken`, and disposal

This is the main .NET-specific shape. Every operation has a **synchronous** member and an
**asynchronous** `…Async` member:

```csharp
DocumentSummary doc = client.Documents.Get(42);                 // sync
DocumentSummary doc = await client.Documents.GetAsync(42);      // async
```

- **Prefer the async members in servers, UI apps, and anything concurrent.** The sync members are
  for scripts, console tools, and synchronous call sites; on .NET 8 they perform real synchronous
  I/O (no thread parked on a `Task`), while on `netstandard2.0` they bridge over the async path.
- **Every async member accepts a trailing `CancellationToken`.** List iterators are
  `IAsyncEnumerable<T>`; cancel them with `await foreach (var x in client.Documents.IterateAsync().WithCancellation(ct))`.
- **One `InfersoftClient` holds pooled connections for its lifetime** — create it once and reuse
  it; dispose it when done (`using`, or `await using` for `DisposeAsync`). `WithOptions(...)` copies
  share the pool and token cache, so disposing a copy is a no-op.

## Errors

Every error derives from `InfersoftException`. Catch that as a safety net; catch a specific
subclass to react to a condition.

| Exception | When |
| --- | --- |
| `InfersoftApiException` | base for any non-2xx response; carries `StatusCode`, `Title`, `Detail`, `Type`, `Instance`, `RequestId`, `RawBody` |
| `BadRequestException` / `UnauthorizedException` / `ForbiddenException` / `NotFoundException` / `ConflictException` / `PreconditionFailedException` / `PayloadTooLargeException` / `UnprocessableEntityException` / `RateLimitException` / `ServerException` | the matching HTTP status (400/401/403/404/409/412/413/422/429/5xx) |
| `InfersoftAuthenticationException` | an OAuth2 token could not be obtained |
| `ApiConnectionException` → `ApiTimeoutException` | the request never got a response (transport error / timeout) |
| `WaitTimeoutException` | a `Wait`/`WaitUntilReady` budget elapsed |
| `CreditsLimitExceededException` | `Jobs.Run(maxCredits:)` exceeded — carries `.Estimate` (job not started) |
| `UploadManyException` | an `UploadMany` batch failed — carries `.Partial` and `.BatchesCompleted` |
| `ExtractException` | `Extract` could not complete — carries `.Job` / `.Upload` for resumption |
| `UploadTransferException` / `DownloadTransferException` | a presigned byte transfer failed |

## Idempotency

Every write sends an `Idempotency-Key` header (auto-generated per call) and is retried safely on
transient failures — the server dedupes the replay, so a network blip never creates duplicates.
Pass `idempotencyKey:` to control the key for exactly-once across process restarts:

```csharp
var key = "import-2026-06-17";                 // persist this, then retry with the same key
client.Documents.Upload(files, idempotencyKey: key);
```

Reusing a key with **different** parameters surfaces `UnprocessableEntityException`. Read-only
searches are never keyed. `UploadMany` derives per-batch keys (`<base>-0000`, `-0001`, …) so a
resumed bulk upload dedupes batch-by-batch.

## Retries, timeouts & rate limiting

- **Automatic retries** for transports + `408/429/5xx` on retry-eligible requests (default
  `MaxRetries = 2`), with exponential full-jitter backoff. `Retry-After` is honored and capped.
- A 429 is retried automatically; tune `MaxRetries` (and `UploadMaxRetries` for presigned
  transfers) via `InfersoftClientOptions` or per call site with `WithOptions(...)`.
- **Per-request timeout** (`Timeout`, default 30s; `UploadTimeout`, default 5 min) is applied via a
  linked `CancellationTokenSource`; a timeout surfaces as `ApiTimeoutException`, while *your*
  `CancellationToken` firing surfaces as `OperationCanceledException`.

```csharp
client.WithOptions(timeout: TimeSpan.FromMinutes(5), maxRetries: 0).Documents.BulkDelete(/* … */);
```

## Waiting & polling

`Jobs.Wait`, `Jobs.Run(wait: true)`, `Documents.WaitUntilReady`, and `Upload(wait: true)` poll to a
terminal state. Budgets are **finite by default** (30 min for jobs, 10 min for document readiness)
— a stuck resource raises `WaitTimeoutException` rather than hanging. Pass
`maxWait: Timeout.InfiniteTimeSpan` to wait without a deadline. An unknown status (one a newer
server added) is treated as non-terminal and logged once, so the wait stays diagnosable.

## Uploads & downloads

- **`flatten`** controls the name sent per file. `true` (default) sends each basename and rejects
  duplicate basenames; `false` sends the path verbatim so your local layout is replicated in the app.
- **`onDuplicate`** reacts to a server-flagged content duplicate: `Allow` (default), `Notify` (log
  and upload), or `Block` (skip the upload and delete the placeholder). Matches are always on
  `FileOutcome.Duplicates`.
- **`UploadMany` / `UploadManyFromDirectory`** upload any number of files in batches; a failing
  batch raises `UploadManyException` (earlier batches are not rolled back — see `.Partial`).
- **`Download`** fetches a signed URL and writes the file into a directory; names are reduced to
  their final component and never escape the destination, and it refuses to overwrite unless
  `overwrite: true`.

## Extract pipeline

`Extract` runs upload → readiness → estimate → start → wait → results in one call. Pass exactly one
of `files`, `documentIds`, or `selectors`. **Partial upload failures do not raise** — inspect
`result.Upload?.Failed` before treating the result as complete; only an all-failed upload or a
`failed` job raises `ExtractException` (carrying `.Upload`/`.Job` for manual resumption).

```csharp
var result = await client.ExtractAsync(files: paths, prompts: new long[] { 101 });
if (result.Upload?.Failed.Count > 0)
{
    Console.WriteLine($"{result.Upload.Failed.Count} file(s) failed to upload");
}
```

## License

[Apache-2.0](LICENSE).
