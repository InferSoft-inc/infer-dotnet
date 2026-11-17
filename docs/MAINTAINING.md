# Maintaining infer-dotnet

`infer-dotnet` is the .NET client for the InferSoft public REST API ([infer-rest](https://github.com/InferSoft-inc/infer-rest)), published to NuGet as [`Infersoft`](https://www.nuget.org/packages/Infersoft). It is hand-written: there is no code generator, and every model, resource member and selector factory was written against infer-rest's OpenAPI spec (`rest-api/openapi.yaml` in infer-rest). The Python SDK, [infer-python](https://github.com/InferSoft-inc/infer-python), exposes the same surface, so an API change normally lands in both SDKs.

The customer-facing docs are [README.md](../README.md) (behavior and examples), [api.md](../api.md) (every public member and the REST calls it makes) and [CHANGELOG.md](../CHANGELOG.md). Keep all three accurate in the same PR as a code change. README.md is packed into the NuGet package (`PackageReadmeFile` in [`Infersoft.csproj`](../src/Infersoft/Infersoft.csproj)) and shown on nuget.org, which is why its links are absolute GitHub URLs.

## What the SDK talks to

```mermaid
flowchart LR
    app["Customer code<br/>InfersoftClient"]
    auth0["Auth0 token endpoint<br/>dev-noabnisxxguu0jp0.us.auth0.com/oauth/token"]
    rest["infer-rest public API<br/>https://api.infersoft.com /api/*"]
    s3["CloudFront-signed file URLs<br/>(signed by infer-facade, returned by infer-rest,<br/>served from S3)"]
    app -->|"POST client_credentials grant"| auth0
    app -->|"Bearer token, JSON"| rest
    app -->|"PUT file bytes (upload)<br/>GET file bytes (download)"| s3
```

| Dependency | How the SDK reaches it | Code |
|---|---|---|
| Auth0 token endpoint | `ClientCredentialsHandler` (a `DelegatingHandler` on the API `HttpClient`) fetches a client-credentials token, caches it until 60 s before expiry, refreshes single-flight and retries once with a fresh token on a 401. Token requests have their own 30 s timeout. | [`Internal/ClientCredentialsHandler.cs`](../src/Infersoft/Internal/ClientCredentialsHandler.cs) |
| infer-rest | The API `HttpClient` (`BaseAddress` = base URL, `User-Agent: infersoft-dotnet/<version>`), driven by `RequestPipeline` for timeouts, retries and error mapping. | [`InfersoftClient.cs`](../src/Infersoft/InfersoftClient.cs), [`Internal/RequestPipeline.cs`](../src/Infersoft/Internal/RequestPipeline.cs) |
| CloudFront-signed file URLs | A second `HttpClient` without the auth handler (a bearer token would break the signature), using `UploadTimeout` and `UploadMaxRetries`. | [`Internal/RequestPipeline.cs`](../src/Infersoft/Internal/RequestPipeline.cs) (`PutPresigned*` / `GetPresigned*`) |

The defaults (`https://api.infersoft.com` for base URL and audience, the `dev-noabnisxxguu0jp0` Auth0 tenant for the token URL) are the production values: infer-rest's `ProdInferRESTStack` is deployed with `domainName: 'api.infersoft.com'` and `auth0Domain: 'dev-noabnisxxguu0jp0.us.auth0.com'`, and the API validates the token audience against `https://<domainName>`.

### Pointing the SDK at the dev environment

infer-rest's `DevInferRESTStack` runs on a different domain and Auth0 tenant. Override all three endpoints (environment variables or `InfersoftClientOptions`) and use credentials from the dev tenant:

| Environment variable | Option | Dev value |
|---|---|---|
| `INFERSOFT_BASE_URL` | `BaseUrl` | `https://dev-api.infersoft.com` |
| `INFERSOFT_TOKEN_URL` | `TokenUrl` | `https://dev-infersoft.us.auth0.com/oauth/token` |
| `INFERSOFT_AUDIENCE` | `Audience` | `https://dev-api.infersoft.com` |

The interactive API docs and `/openapi.yaml` are only served by the production API (`mountDocs` runs only when `ENVIRONMENT=production`): [api.infersoft.com/documentation](https://api.infersoft.com/documentation/).

## Local development

These are the commands CI runs (requires the .NET 8 SDK from `global.json`):

```bash
dotnet restore
dotnet build --no-restore -c Release
dotnet test --no-build -c Release
dotnet format --verify-no-changes
```

Run one sample against a real account (names: `extract`, `search`, `bulk`, `download`, `concurrent`). The samples read credentials from `INFERSOFT_CLIENT_ID` and `INFERSOFT_CLIENT_SECRET`, plus the [dev endpoint overrides](#pointing-the-sdk-at-the-dev-environment) when targeting dev:

```bash
dotnet run --project examples/Infersoft.Examples -- extract
```

The samples have fixed inputs in [`Program.cs`](../examples/Infersoft.Examples/Program.cs); edit them for the account first. `extract` uploads `invoice-1.pdf` and `invoice-2.pdf` from the working directory and extracts prompts 101 and 102, `search` reads prompts 101 and 102 for folder 7, `bulk` uploads `./pdfs/*.pdf` into the project `Imports`, `download` writes every document of the organization into `./downloads`, and `concurrent` reads documents 1, 2 and 3. `extract` and `bulk` create documents and spend credits.

## CI

[`.github/workflows/ci.yml`](../.github/workflows/ci.yml) runs on every pull request and on pushes to `main`: restore, Release build of both target frameworks (warnings as errors, public-API analyzer), tests, and `dotnet format --verify-no-changes`. It needs no secrets. Actions are pinned to commit SHAs.

`main` is the default branch and the only release branch. [CODEOWNERS](../.github/CODEOWNERS) assigns every path to the `InferSoft-inc/infersoft-it` team.

## Releasing to NuGet

Versions follow Semantic Versioning. The version lives only in `<Version>` in [`Directory.Build.props`](../Directory.Build.props); it also becomes the `User-Agent` suffix (read from the assembly's informational version in [`Internal/SdkInfo.cs`](../src/Infersoft/Internal/SdkInfo.cs)).

```mermaid
sequenceDiagram
    participant Dev as Maintainer
    participant GH as GitHub (main)
    participant Build as release.yml build job
    participant Pub as release.yml publish job (env nuget)
    participant NuGet as nuget.org
    Dev->>GH: PR bumps Version and CHANGELOG, merge to main
    Dev->>GH: push tag vX.Y.Z on the merge commit
    GH->>Build: on push tags v*
    Build->>Build: tag must equal Version
    Build->>Build: restore, build, test, format check on the tagged commit
    Build->>Build: dotnet pack (nupkg + snupkg), upload artifact nupkg
    Build->>Pub: needs build
    Pub->>NuGet: NuGet/login (OIDC) then dotnet nuget push --skip-duplicate
```

1. In a PR to `main`, set `<Version>` in `Directory.Build.props` and move the `## [Unreleased]` notes in CHANGELOG.md under `## [X.Y.Z] - <date>`. Merge it.
2. Tag the merge commit by its SHA (a bare `git tag` would tag whatever is checked out locally) and push the tag:
   ```bash
   git fetch origin
   git tag vX.Y.Z <merge commit SHA>
   git push origin vX.Y.Z
   ```
3. [`.github/workflows/release.yml`](../.github/workflows/release.yml) builds and publishes:
   - **build & verify**: fails unless the tag without its `v` equals `<Version>`, reruns the CI gates with `ContinuousIntegrationBuild=true`, packs `src/Infersoft/Infersoft.csproj` (package plus `.snupkg` symbols, SourceLink embedded) and uploads it as the `nupkg` artifact.
   - **publish to NuGet**: runs in the GitHub environment `nuget`, which only accepts refs matching the tag pattern `v*`. It has `id-token: write`; `NuGet/login` exchanges the GitHub OpenID Connect (OIDC) token for a short-lived API key using the nuget.org account named by the `NUGET_USER` repository or organization variable (a variable, not a secret), and `dotnet nuget push --skip-duplicate` uploads to `https://api.nuget.org/v3/index.json`. There is no stored NuGet API key. The nuget.org trusted publishing policy must stay registered for this repo and `release.yml`.

A tag pushed on a commit whose `<Version>` does not match fails the version check and publishes nothing.

Published versions and their dates are on [nuget.org](https://www.nuget.org/packages/Infersoft) and in [CHANGELOG.md](../CHANGELOG.md).

## Code layout

| Path | What it holds |
|---|---|
| [`src/Infersoft/InfersoftClient.cs`](../src/Infersoft/InfersoftClient.cs) | Client construction, `INFERSOFT_*` fallbacks, `WithOptions`, disposal. |
| [`src/Infersoft/InfersoftClient.Extract.cs`](../src/Infersoft/InfersoftClient.Extract.cs) | The `Extract` / `ExtractAsync` pipeline. |
| [`src/Infersoft/InfersoftClientOptions.cs`](../src/Infersoft/InfersoftClientOptions.cs) | Public options and their defaults. |
| [`src/Infersoft/Resources/`](../src/Infersoft/Resources/) | `DocumentsResource` (uploads in `DocumentsResource.Uploads.cs`), `FoldersResource`, `ProjectsResource`, `PromptsResource`, `JobsResource`. Every member has a sync and an `…Async` form. |
| [`src/Infersoft/Models/`](../src/Infersoft/Models/) | Response models. All derive from `InfersoftModel`, whose `[JsonExtensionData] AdditionalData` keeps unknown server fields. Status and data-type enums are extensible (`ExtensibleEnumConverter`). |
| [`src/Infersoft/Internal/Requests/`](../src/Infersoft/Internal/Requests/) | Request body types. JSON uses `JsonNamingPolicy.SnakeCaseLower` ([`Internal/InfersoftJson.cs`](../src/Infersoft/Internal/InfersoftJson.cs)). |
| [`src/Infersoft/Internal/RetryPolicy.cs`](../src/Infersoft/Internal/RetryPolicy.cs) | Retry statuses, backoff, `Retry-After` handling, request-in-progress 409 detection. |
| [`src/Infersoft/Internal/Polling.cs`](../src/Infersoft/Internal/Polling.cs) | Waiters and default budgets (30 minutes for jobs, 10 minutes for documents, 2 s interval). |
| [`src/Infersoft/Internal/ApiErrors.cs`](../src/Infersoft/Internal/ApiErrors.cs), [`Exceptions/`](../src/Infersoft/Exceptions/) | Status code to exception mapping and the exception types. |
| [`src/Infersoft/Selectors.cs`](../src/Infersoft/Selectors.cs) | `Selector` factories (one per selector type in the spec, plus `Raw`) and `Selectors.Build`. |
| [`src/Infersoft/PublicAPI.*.txt`](../src/Infersoft/PublicAPI.Unshipped.txt) | Public API baseline for `Microsoft.CodeAnalysis.PublicApiAnalyzers`. Every public member must be listed or the build fails (warnings are errors). Add new members to `PublicAPI.Unshipped.txt`. |
| [`tests/Infersoft.Tests/`](../tests/Infersoft.Tests/) | xUnit tests on `net8.0`. HTTP goes through a fake `HttpMessageHandler` passed as `Transport`, so no credentials or network are needed. |
| [`examples/Infersoft.Examples/`](../examples/Infersoft.Examples/) | Sample console app in the solution, so CI compiles it; running it needs real credentials. |

Shared build settings are in [`Directory.Build.props`](../Directory.Build.props): nullable enabled, `TreatWarningsAsErrors`, `EnforceCodeStyleInBuild`, `AnalysisMode=Recommended`, and the package `<Version>`. The library targets `net8.0` and `netstandard2.0`; the sync members use real synchronous `HttpClient.Send` under `NET8_0_OR_GREATER` and bridge over the async path on `netstandard2.0`. [`global.json`](../global.json) pins the SDK to `8.0.422` with `rollForward: latestPatch`.

## How the SDK maps to infer-rest

infer-rest registers its routes in `rest-api/internal/server/server.go`. Routes wrapped in `idempotent(...)` record and replay responses per `Idempotency-Key`; routes wrapped in `validated(...)` ignore the header. The SDK follows the same split: it sends a key (auto-generated `Guid` "N" format, or the caller's `idempotencyKey`) only to the recorded routes, and passes `idempotent: true` for read-style `POST`s so they are retried without a key.

| Route | infer-rest wrapper | SDK members | SDK sends `Idempotency-Key` |
|---|---|---|---|
| `POST /api/uploads` | idempotent | `Documents.Upload`, `UploadMany`, `UploadManyFromDirectory` (key `<base>-NNNN` per batch) | yes |
| `POST /api/documents/search` | validated | `Documents.Search`, `Iterate`, `GetValues`, `WaitUntilReady`, `Jobs.Results` | no |
| `GET /api/documents/{id}` | validated | `Documents.Get`, `Download` (name lookup) | no |
| `GET /api/documents/{id}/download` | validated | `Documents.DownloadUrl`, `Download` | no |
| `DELETE /api/documents/{id}` | validated | `Documents.Delete`, `Upload(onDuplicate: Block)` | no |
| `POST /api/documents/move-to-folder` | idempotent | `Documents.MoveToFolder` | yes |
| `POST /api/documents/bulk_delete` | idempotent | `Documents.BulkDelete` | yes |
| `POST /api/folders` | idempotent | `Folders.Create` | yes |
| `GET /api/folders/{id}` | validated | `Folders.Get` | no |
| `PATCH /api/folders/{id}` | idempotent | `Folders.Rename` | yes |
| `POST /api/folders/search` | validated | `Folders.Search`, `Iterate` | no |
| `POST /api/folders/move` | idempotent | `Folders.Move` | yes |
| `POST /api/folders/paths` | validated | `Folders.ResolvePaths`, `EnsurePath` | no |
| `POST /api/folders/bulk_delete` | idempotent | `Folders.BulkDelete` | yes |
| `POST /api/projects` | idempotent | `Projects.Create`, `GetOrCreate` | yes |
| `GET /api/projects/{id}` | validated | `Projects.Get` | no |
| `POST /api/projects/search` | validated | `Projects.Search`, `Iterate`, `GetOrCreate` | no |
| `POST /api/projects/assign-documents` | idempotent | `Projects.AssignDocuments` | yes |
| `POST /api/prompts/search` | validated | `Prompts.Search`, `Iterate` | no |
| `POST /api/jobs/credits/estimate` | idempotent | `Jobs.Estimate`, `Run` | yes |
| `POST /api/jobs/credits/quote` | validated | `Jobs.Quote` | no |
| `POST /api/jobs/start` | idempotent | `Jobs.Start`, `Run` | yes |
| `GET /api/jobs/{id}` | validated | `Jobs.Get`, `Wait`, `Run(wait: true)` | no |
| `POST /api/jobs/search` | validated | `Jobs.Search`, `Iterate` | no |

Routes in the spec that the SDK deliberately does not wrap: `POST /api/documents/{id}/resplit` and `POST /api/documents/{id}/split` (marked deprecated and internal-only in the spec) and `POST /api/documents/extraction_results/search` (deprecated; extraction values come from `Documents.Search` with `prompts`).

### Server contracts copied by hand

These values live in both codebases. Change them together.

| SDK | infer-rest |
|---|---|
| `RetryPolicy.ProblemTypeRequestInProgress` | `ProblemTypeRequestInProgress` in `rest-api/internal/idempotency/middleware.go` (the transient 409 the SDK retries, honoring `Retry-After`) |
| `DocumentsResource.MaxBatchFiles = 100` | `BatchUploadRequest.files` `maxItems: 100` |
| `Selector` factories in [`Selectors.cs`](../src/Infersoft/Selectors.cs) (16 typed ones) | `Selector` union in the spec |
| `JobStatus`, `DocumentStatus`, `DataTypeName` in [`Models/`](../src/Infersoft/Models/) | `JobStatus`, `DocumentStatus`, `DataTypeName` enums in the spec |
| Terminal job statuses (`completed`, `failed`, `partial_success`) in [`JobsResource.cs`](../src/Infersoft/Resources/JobsResource.cs) | `JobStatus` enum (`running` and `creating_workflows` are non-terminal) |
| Default endpoints in [`InfersoftClient.cs`](../src/Infersoft/InfersoftClient.cs) | `ProdInferRESTStack` props in `bin/infer-rest.ts` |

Enums are extensible: an unknown value deserializes, is treated as non-terminal by the waiters, and each wait logs one warning per unknown value. A new terminal status therefore needs an SDK release, or waits run until `maxWait`.

### Keeping in sync with the spec

1. Find what changed in infer-rest's spec since the last SDK release (production runs infer-rest `main`):
   ```bash
   git -C ../infer-rest log -p origin/main -- rest-api/openapi.yaml
   ```
2. Update models, request types, resources and selector factories, add the new public members to `PublicAPI.Unshipped.txt`, and add tests that drive the new wire shape through the fake transport.
3. Update api.md and README.md, and add an entry under `## [Unreleased]` in CHANGELOG.md.
4. Make the same change in infer-python.

## Dependencies

- Runtime: `System.Text.Json` 8.0.6, `Microsoft.Extensions.Logging.Abstractions` 8.0.3, and `Microsoft.Bcl.AsyncInterfaces` 8.0.0 on `netstandard2.0` only. Build-only: `Microsoft.SourceLink.GitHub`, `Microsoft.CodeAnalysis.PublicApiAnalyzers`.
- [Dependabot](../.github/dependabot.yml) opens grouped weekly PRs for GitHub Actions SHA pins and NuGet references. Major bumps of `Microsoft.Extensions.*`, `Microsoft.Bcl.*` and `System.Text.Json` are ignored on purpose: the declared versions are floors every consumer inherits, and the library targets .NET 8 and `netstandard2.0`.

## Security reports

[SECURITY.md](../SECURITY.md): reports go through GitHub private vulnerability reporting; only the latest release is supported.

## Related repos

- [infer-rest](https://github.com/InferSoft-inc/infer-rest): the REST API and its OpenAPI spec.
- [infer-python](https://github.com/InferSoft-inc/infer-python): the Python SDK with the same surface.
