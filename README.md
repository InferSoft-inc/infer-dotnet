# InferSoft .NET SDK

A .NET SDK for the [InferSoft](https://platform.infersoft.com) document-processing REST API.

Full endpoint reference: [api.md](https://github.com/InferSoft-inc/infer-dotnet/blob/main/api.md), every member mapped to the REST calls it makes.
The REST API itself is documented at [api.infersoft.com/documentation](https://api.infersoft.com/documentation/).

## Requirements

- .NET 8, or any `netstandard2.0`-compatible runtime (.NET Framework 4.6.2+, etc.).

## Install

The package is [`Infersoft` on NuGet](https://www.nuget.org/packages/Infersoft):

```bash
dotnet add package Infersoft
```

## Authentication and configuration

The client authenticates with OAuth2 client credentials; create them for your organization at
[platform.infersoft.com](https://platform.infersoft.com). Pass credentials explicitly or set the
`INFERSOFT_*` environment variables (any unset credential or endpoint falls back to its environment variable):

| Setting | Environment variable | Default |
| --- | --- | --- |
| `ClientId` | `INFERSOFT_CLIENT_ID` | none (required) |
| `ClientSecret` | `INFERSOFT_CLIENT_SECRET` | none (required) |
| `BaseUrl` | `INFERSOFT_BASE_URL` | `https://api.infersoft.com` |
| `TokenUrl` | `INFERSOFT_TOKEN_URL` | `https://dev-noabnisxxguu0jp0.us.auth0.com/oauth/token` |
| `Audience` | `INFERSOFT_AUDIENCE` | `https://api.infersoft.com` |
| `Scope` | (options only) | none (optional OAuth scopes) |

`InfersoftClientOptions` also carries `Timeout` (30 s), `UploadTimeout` (5 minutes), `MaxRetries` (2),
`UploadMaxRetries` (falls back to `MaxRetries`), `LoggerFactory` and `Transport` (a custom
`HttpMessageHandler`, mainly for tests).

```csharp
using Infersoft;

// Credentials from INFERSOFT_CLIENT_ID / INFERSOFT_CLIENT_SECRET:
using var client = new InfersoftClient(new InfersoftClientOptions());

// Or explicit, with options:
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

// Upload PDFs, run the extractor with two prompts, and get the values back.
// 101 and 102 stand for your prompt IDs; list them as shown below.
ExtractResult result = await client.ExtractAsync(
    files: new[] { "invoice-1.pdf", "invoice-2.pdf" },
    prompts: new long[] { 101, 102 });

foreach (var pair in result.Values)
{
    Console.WriteLine($"document {pair.Key}: {string.Join(", ", pair.Value.Keys)}");
}
```

A **prompt** is a field extractor configured in your InferSoft workspace. List your prompts and
their IDs with:

```csharp
foreach (var prompt in client.Prompts.Iterate())
{
    Console.WriteLine($"{prompt.Id} {prompt.Name}");
}
```

Runnable samples (extract, search and export, bulk import, download, concurrent reads) live in
[examples/Infersoft.Examples](https://github.com/InferSoft-inc/infer-dotnet/tree/main/examples/Infersoft.Examples).

## Sync and async members, `CancellationToken` and disposal

Every operation has a **synchronous** member and an **asynchronous** `…Async` member:

```csharp
DocumentSummary doc = client.Documents.Get(42);                  // sync
DocumentSummary same = await client.Documents.GetAsync(42);      // async
```

- **Prefer the async members in servers, UI apps, and anything concurrent.** The sync members are
  for scripts, console tools, and synchronous call sites; on .NET 8 they perform real synchronous
  I/O (no thread parked on a `Task`), while on `netstandard2.0` they bridge over the async path.
- **Every async member accepts a trailing `CancellationToken`.** List iterators are
  `IAsyncEnumerable<T>`; cancel them with `await foreach (var x in client.Documents.IterateAsync().WithCancellation(ct))`.
- **One `InfersoftClient` holds pooled connections for its lifetime**: create it once and reuse
  it; dispose it when done (`using`, or `await using` for `DisposeAsync`). `WithOptions(...)` copies
  share the pool and token cache, so disposing a copy is a no-op.

## Errors

Every error derives from `InfersoftException`. Catch that as a safety net; catch a specific
subclass to react to a condition. Usage errors throw the standard .NET exceptions instead
(`ArgumentException` for bad arguments, `FileNotFoundException` / `DirectoryNotFoundException` for
missing local paths, `IOException` when a download target already exists).

| Exception | When |
| --- | --- |
| `InfersoftApiException` | base for any non-2xx response; carries `StatusCode`, `Title`, `Detail`, `Type`, `Instance`, `RequestId`, `RawBody` |
| `BadRequestException` / `UnauthorizedException` / `ForbiddenException` / `NotFoundException` / `ConflictException` / `PreconditionFailedException` / `PayloadTooLargeException` / `UnprocessableEntityException` / `RateLimitException` / `ServerException` | the matching HTTP status (400/401/403/404/409/412/413/422/429/5xx) |
| `InfersoftAuthenticationException` | an OAuth2 token could not be obtained |
| `ApiConnectionException` → `ApiTimeoutException` | the request never got a response (transport error / timeout) |
| `WaitTimeoutException` | a `Wait`/`WaitUntilReady` budget elapsed |
| `CreditsLimitExceededException` | `Jobs.Run(maxCredits:)` exceeded; carries `.Estimate` and `.MaxCredits` (job not started) |
| `UploadManyException` | an `UploadMany` batch failed; carries `.Partial` and `.BatchesCompleted` |
| `ExtractException` | `Extract` could not complete; carries `.Job` / `.Upload` for resumption |
| `UploadTransferException` / `DownloadTransferException` | a presigned byte transfer failed; carries `StatusCode`, `RequestId` |

## Idempotency

Every member with an `idempotencyKey:` parameter sends an `Idempotency-Key` header (auto-generated
per call) and is retried safely on transient failures: the server records the first response and
replays it to a retry carrying the same key, so a network blip never creates duplicates. Pass
`idempotencyKey:` to control the key for exactly-once across process restarts:

```csharp
var key = "import-2026-06-17";                 // persist this, then retry with the same key
client.Documents.Upload(files, idempotencyKey: key);
```

Reusing a key with **different** parameters surfaces `UnprocessableEntityException`. If a retry
arrives while the original request is still running, the API answers with a transient 409 that the
SDK waits out and retries. Read-only searches are never keyed; `Delete` and
`ResolvePaths`/`EnsurePath` converge on retry without a key. `UploadMany` derives per-batch keys
(`<base>-0000`, `-0001`, …) so a resumed bulk upload deduplicates batch-by-batch.

## Retries, timeouts and rate limiting

- **Automatic retries** for transport errors and `408/429/500/502/503/504` on retry-eligible
  requests (default `MaxRetries = 2`), with exponential full-jitter backoff. A numeric
  `Retry-After` is honored, capped at 60 seconds.
- The API rate-limits per organization and answers excess traffic with 429 plus `Retry-After`;
  the SDK retries those within its budget and then throws `RateLimitException`. Tune `MaxRetries`
  (and `UploadMaxRetries` for presigned transfers) via `InfersoftClientOptions` or per call site
  with `WithOptions(...)`.
- **Per-request timeout** (`Timeout`, default 30 s; `UploadTimeout`, default 5 minutes) is applied via a
  linked `CancellationTokenSource`; a timeout surfaces as `ApiTimeoutException`, while *your*
  `CancellationToken` firing surfaces as `OperationCanceledException`. OAuth token requests keep
  their own 30 s timeout.

```csharp
client.WithOptions(timeout: TimeSpan.FromMinutes(5), maxRetries: 0).Documents.BulkDelete(/* … */);
```

## Waiting and polling

`Jobs.Wait`, `Jobs.Run(wait: true)`, `Documents.WaitUntilReady`, and `Upload(wait: true)` poll to a
terminal state every 2 seconds by default (`pollInterval:`). Budgets are **finite by default** (30
minutes for jobs, 10 minutes for document readiness): a stuck resource raises `WaitTimeoutException`
rather than hanging, and the server-side work is not canceled. Pass
`maxWait: Timeout.InfiniteTimeSpan` to wait without a deadline. An unknown status (one a newer
server added) is treated as non-terminal and logged once, so the wait stays diagnosable.

## Uploads and downloads

- **`flatten`** controls the name sent per file. `true` (default) sends each basename and rejects
  duplicate basenames; `false` sends the path verbatim so your local layout is replicated in the app.
- **`Upload`** takes up to `DocumentsResource.MaxBatchFiles` (100) files per call and sends an MD5
  hash of each file so the server can flag content it already has.
- **`onDuplicate`** reacts to a server-flagged content duplicate: `Allow` (default), `Notify` (log
  and upload), or `Block` (skip the upload and delete the placeholder; those files land on
  `result.Skipped`). Matches are always on `FileOutcome.Duplicates`.
- **`UploadMany` / `UploadManyFromDirectory`** upload any number of files in batches; a failing
  batch raises `UploadManyException` (earlier batches are not rolled back; see `.Partial`).
  `UploadManyFromDirectory` searches subdirectories with `searchPattern` and skips files whose name
  starts with `.`.
- **`Download`** fetches a signed URL and writes the file into a directory; names are reduced to
  their final component and never escape the destination, and it refuses to overwrite unless
  `overwrite: true`.

## Selectors

`Documents.Search`/`Iterate`/`GetValues`/`BulkDelete`/`MoveToFolder`, `Projects.AssignDocuments`,
`Jobs.Estimate`/`Quote`/`Run` and `Extract` take a `Selectors` value. Build it from the `Selector`
factories:

```csharp
var page = client.Documents.Search(Selectors.Build(
    include: new[] { Selector.Folder(5), Selector.Tag(new long[] { 10 }) },
    exclude: new[] { Selector.Name("draft") }));
```

Every method that takes `selectors:` also accepts `documentIds:` as a shortcut; pass one or the
other. The full factory list is in [api.md](https://github.com/InferSoft-inc/infer-dotnet/blob/main/api.md#selectors).

## Extract pipeline

`Extract` runs upload → readiness → estimate → start → wait → results in one call. Pass exactly one
of `files`, `documentIds`, or `selectors`. **Partial upload failures do not raise**: inspect
`result.Upload?.Failed` before treating the result as complete; only an all-failed upload or a
`failed` job raises `ExtractException` (carrying `.Upload`/`.Job` for manual resumption).
`maxWait` applies to each waiting phase separately; when unset, upload readiness uses the 10-minute
default and the job wait the 30-minute default.

```csharp
var result = await client.ExtractAsync(files: paths, prompts: new long[] { 101 });
if (result.Upload?.Failed.Count > 0)
{
    Console.WriteLine($"{result.Upload.Failed.Count} file(s) failed to upload");
}
```

`Jobs.Quote` prices a job without reserving anything, which is useful while composing one;
`Jobs.Run(maxCredits:)` estimates first and refuses to start when the estimate is over budget.

## Extraction results

Pass prompt IDs to `Documents.Get`/`Search`/`Iterate` (or use `GetValues`, `Jobs.Results`,
`Extract`) to get typed `ExtractionResultValue`s. `Value` returns the typed value: `decimal` for
Number (the server's decimal string when it does not fit a `decimal`), `DateTime` for Date, `bool`
for Boolean, `string` for String. When no typed property is set, for example because the raw text
could not be typed, `Value` falls back to the deprecated `ParsedValue` (a `JsonElement?`).

While a running workflow includes a prompt, its value has `IsProcessing` set. A prompt with no
result yet still gets an item, with `IsProcessing` true and no value properties. `GetValues` and
`ExtractResult.Values` leave those items out; the documents returned by `Get`, `Search`, `Iterate`
and `Jobs.Results` keep them.

## Logging

Pass `LoggerFactory` in `InfersoftClientOptions` to receive the SDK's warnings (an
`OnDuplicate.Notify` match, an unknown status seen while waiting) under the category `Infersoft`.

## Development

How the SDK is built, kept in sync with the REST API, tested and released to NuGet:
[docs/MAINTAINING.md](https://github.com/InferSoft-inc/infer-dotnet/blob/main/docs/MAINTAINING.md).
Release history: [CHANGELOG.md](https://github.com/InferSoft-inc/infer-dotnet/blob/main/CHANGELOG.md).

## License

[Apache-2.0](https://github.com/InferSoft-inc/infer-dotnet/blob/main/LICENSE).
