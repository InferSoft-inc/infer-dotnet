# API reference

Every public member of the Infersoft .NET SDK, mapped to the REST endpoint(s) it calls.
Parameter-level documentation lives in each member's XML doc comments (your IDE shows it on
hover); this file is the map of what exists and what it does on the wire.

**Every method has a synchronous and an asynchronous (`…Async`) member** with identical inputs
and semantics — only the `…Async` form is awaited (and returns `Task<T>` / `IAsyncEnumerable<T>`).
To keep this terse, the pairs are listed once.

Conventions:

- **write** — sends an `Idempotency-Key` (auto-generated per call; pass `idempotencyKey:` to
  control it) and is retried safely on transient failures.
- **read** — retried automatically; never keyed.
- **write (naturally idempotent)** — a mutation that converges on retry (a DELETE, a
  get-or-create); retried safely without a key.
- **composite** — a client-side workflow; every wire call it makes is listed.

## Client

- `new InfersoftClient(string clientId, string clientSecret)` /
  `new InfersoftClient(InfersoftClientOptions options)` — construction; credentials/endpoints fall
  back to `INFERSOFT_*` environment variables. OAuth2 client-credentials tokens are fetched lazily
  and cached.
- `WithOptions(timeout?, maxRetries?, uploadMaxRetries?) -> InfersoftClient` — a copy with
  per-call-site overrides; shares the connection pool and token cache.
- `Extract / ExtractAsync(files?, documentIds?, selectors?, prompts, projectId?, projectName?, flatten=true, maxCredits?, keyBy=Name, raiseOnFailure=true, maxWait?, pollInterval?) -> ExtractResult`
  — **composite**: the whole pipeline. Wire calls: when `files:` is used, `POST /api/uploads` +
  presigned PUTs + a polled `POST /api/documents/search` readiness wait; then always
  `POST /api/jobs/credits/estimate`, `POST /api/jobs/start`, polled `GET /api/jobs/{id}`, and paged
  `POST /api/documents/search` with `prompts` (results).
- `Dispose() / DisposeAsync()` — release the pooled connections (also usable via `using`).

## Documents (`client.Documents`)

- `Get(documentId, promptIds?) -> DocumentSummary` — **read**: `GET /api/documents/{id}`
  (`?prompt_ids=` populates `ExtractionResults`).
- `Search(selectors?, documentIds?, prompts?, page=1, pageSize=50, orderBy="id", orderDir="asc") -> DocumentPage`
  — **read**: `POST /api/documents/search`.
- `Iterate(...) -> IEnumerable<DocumentSummary>` / `IterateAsync(...) -> IAsyncEnumerable<DocumentSummary>`
  — **read**: paged `POST /api/documents/search`, fetched on demand.
- `GetValues(prompts, selectors?, documentIds?, keyBy=Name) -> IReadOnlyDictionary<long, IReadOnlyDictionary<object, object?>>`
  — values are typed: `decimal` for Number when the server's decimal string parses, otherwise that string unchanged; `DateTime` for Date, `bool` for Boolean, `string` for String; `null` when the raw text could not be typed; `JsonElement` only from servers that predate the typed fields.
  — **read/composite**: paged `POST /api/documents/search` with `prompts`, flattened to
  `{documentId: {field: value}}`.
- `Upload(files, flatten=true, projectId?, projectName?, tagIds?, tagNames?, contentType?, onDuplicate=Allow, idempotencyKey?, wait=false, maxWait?, pollInterval?) -> UploadResult`
  — **write/composite**: `POST /api/uploads` (plans the batch, ≤ `MaxBatchFiles`=100), then one
  presigned `PUT` per file (unauthenticated, to S3); with `wait: true`, polled
  `POST /api/documents/search` until every document is `ready`.
- `UploadMany(files, …) -> UploadResult` / `UploadManyFromDirectory(directory, searchPattern="*", …) -> UploadResult`
  — **write/composite**: bulk uploads of any size; one `Upload` batch per `batchSize` files with
  deterministic per-batch keys derived from the base `idempotencyKey`; `onBatch` progress callback.
- `WaitUntilReady(documentIds, maxWait?, pollInterval?) -> IReadOnlyList<DocumentSummary>`
  — **read**: polled `POST /api/documents/search` until all are `ready`.
- `DownloadUrl(documentId) -> DownloadResponse` — **read**: `GET /api/documents/{id}/download`
  (short-lived signed URL + `ExpiresAt`).
- `Download(documentId, destDir, fileName?, overwrite=false) -> string` — **composite**:
  `GET /api/documents/{id}` (name; skipped when `fileName:` given) + `GET /api/documents/{id}/download`
  + a signed `GET` for the bytes (unauthenticated); writes the file into `destDir`.
- `MoveToFolder(selectors?, documentIds?, targetFolderId?, idempotencyKey?) -> MoveResult`
  — **write**: `POST /api/documents/move-to-folder`.
- `BulkDelete(selectors?, documentIds?, dryRun=false, idempotencyKey?) -> BulkDeleteResult`
  — **write**: `POST /api/documents/bulk_delete`.
- `Delete(documentId) -> void` — **write (naturally idempotent)**: `DELETE /api/documents/{id}`.

## Folders (`client.Folders`)

- `Create(name, parentId?, idempotencyKey?) -> Folder` — **write**: `POST /api/folders`.
- `Get(folderId) -> Folder` — **read**: `GET /api/folders/{id}`.
- `Rename(folderId, name, idempotencyKey?) -> Folder` — **write**: `PATCH /api/folders/{id}`.
- `Search(parentId?, page=1, …) -> FolderPage` / `Iterate(…)` — **read**: `POST /api/folders/search`.
- `Move(folderIds, targetParentId?, idempotencyKey?) -> FolderMoveResult` — **write**: `POST /api/folders/move`.
- `BulkDelete(folderIds, idempotencyKey?) -> FolderBulkDeleteResult` — **write**: `POST /api/folders/bulk_delete`.
- `ResolvePaths(paths, parentId?) -> FolderPathsResult` — **write (naturally idempotent)**:
  `POST /api/folders/paths` — get-or-create by path.
- `EnsurePath(path, parentId?) -> Folder` — **composite** over `ResolvePaths`; returns the leaf folder.

## Projects (`client.Projects`)

- `Create(name, idempotencyKey?) -> Project` — **write**: `POST /api/projects`.
- `Get(projectId) -> Project` — **read**: `GET /api/projects/{id}`.
- `GetOrCreate(name, idempotencyKey?) -> Project` — **composite**: `POST /api/projects/search`
  (exact-name match), then `POST /api/projects` when absent (recovers from a creation race).
- `Search(q?, page=1, …) -> ProjectPage` / `Iterate(q?, …)` — **read**: `POST /api/projects/search`.
- `AssignDocuments(selectors?, documentIds?, projectId?, projectName?, idempotencyKey?) -> AssignDocumentsResult`
  — **write**: `POST /api/projects/assign-documents`.

## Prompts (`client.Prompts`)

- `Search(q?, documentClass?, includeDeleted=false, page=1, …) -> PromptPage` / `Iterate(…)`
  — **read**: `POST /api/prompts/search`.

## Jobs (`client.Jobs`)

- `Estimate(step, selectors?, documentIds?, prompts?, synchronous=false, idempotencyKey?) -> CreditsEstimate`
  — **write**: `POST /api/jobs/credits/estimate`.
- `Quote(step, selectors?, documentIds?, prompts?, synchronous=false) -> CreditsQuote`
  — **read**: `POST /api/jobs/credits/quote`. Same formula as `Estimate` but
  nothing is reserved and no id is returned; use it while composing a job.
- `Start(creditsId, projectId?, idempotencyKey?) -> Job` — **write**: `POST /api/jobs/start`.
- `Run(step, selectors?, documentIds?, prompts?, synchronous=false, projectId?, maxCredits?, wait=false, maxWait?, pollInterval?) -> Job`
  — **composite**: `POST /api/jobs/credits/estimate` + `POST /api/jobs/start` (gated by
  `maxCredits`); with `wait: true`, polled `GET /api/jobs/{id}`.
- `Get(jobId) -> Job` — **read**: `GET /api/jobs/{id}`.
- `Wait(jobId | job, maxWait?, pollInterval?) -> Job` — **read**: polled `GET /api/jobs/{id}` until
  a terminal status.
- `Results(jobId | job, prompts?, …) -> IEnumerable<DocumentSummary>` — **read/composite**: paged
  `POST /api/documents/search` with a job selector (and `prompts` to populate extraction values).
- `Search(statuses?, createdFrom?, createdTo?, page=1, …, orderDir="desc") -> JobPage` / `Iterate(…)`
  — **read**: `POST /api/jobs/search`.

## Selectors

Static factories that build the selectors accepted by every `selectors:` parameter; combine them
with `Selectors.Build(include:, exclude:)`:

```csharp
var selectors = Selectors.Build(
    include: new[] { Selector.Folder(7), Selector.CreatedAt(createdFrom: "2026-01-01") });
```

- `Selectors.Build(include?, exclude?) -> Selectors`
- `Selector.File(ids)` · `Selector.Folder(folderId)` · `Selector.Name(value)` ·
  `Selector.DocumentClass(classes)` · `Selector.Tag(ids, taggedFrom?, taggedTo?)` ·
  `Selector.CreatedAt(createdFrom?, createdTo?)` · `Selector.Size(sizeFrom?, sizeTo?)` ·
  `Selector.PageCount(pageCountFrom?, pageCountTo?)` · `Selector.SourceDocument(ids)` ·
  `Selector.Project(projectId, addedFrom?, addedTo?)` · `Selector.Job(ids)` ·
  `Selector.FolderSubtree(folderId)` ·
  `Selector.IsValid()` · `Selector.HasChildren()` · `Selector.HasRunningWorkflow()` ·
  `Selector.HasClassification()` ·
  `Selector.Raw(type, fields)` (forward-compat escape hatch for not-yet-typed selectors).

Anywhere a method takes `selectors:`, it also accepts a `documentIds:` shortcut (wrapped into a
file selector) — pass one or the other.
