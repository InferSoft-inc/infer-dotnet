# Changelog

All notable changes to this project are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- Initial release of the Infersoft .NET SDK, targeting `net8.0` and `netstandard2.0`.
- `InfersoftClient` exposing synchronous and asynchronous method pairs, with OAuth2
  client-credentials authentication (lazy token fetch, caching, single-flight refresh, refresh on
  401), automatic retries with jittered backoff and idempotency keys, `Retry-After` handling, and
  per-request timeouts. Per-call-site overrides via `WithOptions(...)`.
- **Documents** — get, search, iterate, `GetValues`; `Upload` / `UploadMany` /
  `UploadManyFromDirectory` (batching, `OnDuplicate` handling, partial-failure semantics);
  `WaitUntilReady`; `DownloadUrl` / `Download`; `MoveToFolder`; `BulkDelete`; `Delete`.
- **Jobs** — `Estimate`, `Quote` (read-only credit calculation, nothing reserved), `Start`,
  `Run` (with a `maxCredits` budget guard), `Get`, `Wait`, `Results`, `Search`, `Iterate`,
  with finite-by-default waiting/polling.
- **Projects** — `Create`, `Get`, `GetOrCreate`, `Search`, `Iterate`, `AssignDocuments`.
- **Folders** — `Create`, `Get`, `Rename`, `Search`, `Iterate`, `Move`, `BulkDelete`,
  `ResolvePaths`, `EnsurePath`.
- **Prompts** — `Search`, `Iterate`.
- The end-to-end `Extract` / `ExtractAsync` pipeline, typed `Selector` builders, forward-compatible
  response models (extensible enums + preserved unknown fields), and a full exception hierarchy
  rooted at `InfersoftException`.
