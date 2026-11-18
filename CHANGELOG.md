# Changelog

All notable changes to this project are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.1.2] - 2026-10-21

### Added
- `ExtractionResultValue.IsProcessing`: `true` while a workflow running on the document includes
  the prompt. Document get/search/iterate now also return an item for each requested prompt that is
  still processing and has no result yet, with `IsProcessing` set and no value properties.

### Changed
- `GetValues` and `ExtractResult.Values` leave out prompts that are still
  processing without a result, so their output is unchanged by the new items.

## [0.1.1] - 2026-09-28

### Added
- `Jobs.Quote` / `QuoteAsync`: read-only credit calculation via `POST /api/jobs/credits/quote`
  (nothing reserved, no ID returned), returning the new `CreditsQuote` model (`TotalCredits`,
  `PageCount`, `DocumentCount`).
- `ExtractionResultValue.ValueText` / `ValueNumber` (decimal string, full precision, plus
  `ValueNumberDecimal`) / `ValueBool` / `ValueDate`: the typed value the server derived from
  `RawValue`, at most one set per item (none when the raw value could not be typed; `RawValue` still
  carries the text), and `Value` returning whichever is set (falling back to the
  deprecated `ParsedValue` against older servers).
- `PromptMeta.Example`, and `DisplayType` / `GroupName` on `PromptMeta` and `ExtractionResultValue`.
- `Selector.FolderSubtree(folderId)` and `Selector.HasClassification()`.

### Changed
- `GetValues` and `ExtractResult.Values` now carry the typed `Value` (`object?`) instead of the raw
  `JsonElement?` parsed value.

## [0.1.0] - 2026-06-18

### Added
- Initial release of the InferSoft .NET SDK, targeting `net8.0` and `netstandard2.0`.
- `InfersoftClient` exposing synchronous and asynchronous method pairs, with OAuth2
  client-credentials authentication (lazy token fetch, caching, single-flight refresh, refresh on
  401), automatic retries with jittered backoff and idempotency keys, `Retry-After` handling, and
  per-request timeouts. Per-call-site overrides via `WithOptions(...)`.
- **Documents**: get, search, iterate, `GetValues`; `Upload` / `UploadMany` /
  `UploadManyFromDirectory` (batching, `OnDuplicate` handling, partial-failure semantics);
  `WaitUntilReady`; `DownloadUrl` / `Download`; `MoveToFolder`; `BulkDelete`; `Delete`.
- **Jobs**: `Estimate`, `Start`, `Run` (with a `maxCredits` budget guard), `Get`, `Wait`,
  `Results`, `Search`, `Iterate`, with finite-by-default waiting/polling.
- **Projects**: `Create`, `Get`, `GetOrCreate`, `Search`, `Iterate`, `AssignDocuments`.
- **Folders**: `Create`, `Get`, `Rename`, `Search`, `Iterate`, `Move`, `BulkDelete`,
  `ResolvePaths`, `EnsurePath`.
- **Prompts**: `Search`, `Iterate`.
- The end-to-end `Extract` / `ExtractAsync` pipeline, typed `Selector` builders, forward-compatible
  response models (extensible enums + preserved unknown fields), and a full exception hierarchy
  rooted at `InfersoftException`.
