using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Infersoft.Internal;

/// <summary>
/// The HTTP engine: authenticated JSON requests plus raw presigned transfers, with retries,
/// jittered backoff, Retry-After honoring, idempotency, and per-request timeouts. Retry
/// *decisions* live in <see cref="RetryPolicy"/> (pure, shared); this type owns the twin
/// sync/async loop shells. The sync shells run real synchronous I/O on net8 and are
/// absent on netstandard2.0, where the client bridges over the async shells.
/// </summary>
internal sealed class RequestPipeline
{
    private readonly HttpClient _apiClient;
    private readonly HttpClient _uploadClient;
    private readonly int _maxRetries;
    private readonly int? _uploadMaxRetries;
    private readonly TimeSpan _apiTimeout;
    private readonly TimeSpan _uploadTimeout;

    public RequestPipeline(
        HttpClient apiClient,
        HttpClient uploadClient,
        int maxRetries,
        int? uploadMaxRetries,
        TimeSpan apiTimeout,
        TimeSpan uploadTimeout)
    {
        _apiClient = apiClient;
        _uploadClient = uploadClient;
        _maxRetries = maxRetries;
        _uploadMaxRetries = uploadMaxRetries;
        _apiTimeout = apiTimeout;
        _uploadTimeout = uploadTimeout;
    }

    private int UploadRetryBudget => _uploadMaxRetries ?? _maxRetries;

    /// <summary>A copy with per-call-site overrides, sharing the pooled clients (the WithOptions seam).</summary>
    public RequestPipeline WithOverrides(TimeSpan? timeout, int? maxRetries, int? uploadMaxRetries) =>
        new(_apiClient, _uploadClient, maxRetries ?? _maxRetries, uploadMaxRetries ?? _uploadMaxRetries,
            timeout ?? _apiTimeout, _uploadTimeout);

    public static string NewIdempotencyKey() => Guid.NewGuid().ToString("N");

    private static bool ResolveRetry(HttpMethod method, bool? idempotent, string? idempotencyKey) =>
        idempotent ?? (RetryPolicy.IsMethodIdempotent(method) || !string.IsNullOrEmpty(idempotencyKey));

    private static HttpRequestMessage BuildRequest(HttpMethod method, string path, byte[]? body, string? idempotencyKey)
    {
        var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = new ByteArrayContent(body);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        }

        if (!string.IsNullOrEmpty(idempotencyKey))
        {
            request.Headers.TryAddWithoutValidation(RetryPolicy.IdempotencyHeader, idempotencyKey);
        }

        return request;
    }

    private static void ApplyHeaders(HttpRequestMessage request, IReadOnlyDictionary<string, string>? headers)
    {
        if (headers is null)
        {
            return;
        }

        foreach (var pair in headers)
        {
            // Content-Type and friends belong on the content headers; everything else on the request.
            if (!request.Headers.TryAddWithoutValidation(pair.Key, pair.Value) && request.Content is not null)
            {
                request.Content.Headers.TryAddWithoutValidation(pair.Key, pair.Value);
            }
        }
    }

    // ----- Async path (the real implementation on both TFMs) ---------------------------------

    public async Task<T?> RequestAsync<T>(
        HttpMethod method,
        string path,
        object? body = null,
        bool? idempotent = null,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        var payload = body is null ? null : JsonSerializer.SerializeToUtf8Bytes(body, InfersoftJson.Options);
        using var response = await SendAsync(method, path, payload, idempotent, idempotencyKey, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await ContentReader.ReadStringAsync(response.Content, cancellationToken).ConfigureAwait(false);
            throw ApiErrors.CreateApiException(
                (int)response.StatusCode, response.ReasonPhrase, errorBody, ApiErrors.RequestIdOf(response));
        }

        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return default;
        }

        var bytes = await ContentReader.ReadBytesAsync(response.Content, cancellationToken).ConfigureAwait(false);
        if (bytes.Length == 0)
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(bytes, InfersoftJson.Options);
    }

    public async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string path, byte[]? body, bool? idempotent, string? idempotencyKey, CancellationToken cancellationToken)
    {
        var retry = ResolveRetry(method, idempotent, idempotencyKey);
        var attempt = 0;
        while (true)
        {
            using var timeoutCts = new CancellationTokenSource(_apiTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            HttpResponseMessage response;
            try
            {
                using var request = BuildRequest(method, path, body, idempotencyKey);
                response = await _apiClient.SendAsync(request, linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException ex)
            {
                if (retry && attempt < _maxRetries)
                {
                    await Task.Delay(RetryPolicy.Backoff(attempt), cancellationToken).ConfigureAwait(false);
                    attempt++;
                    continue;
                }

                throw new ApiTimeoutException($"{method.Method} {path} timed out", ex);
            }
            catch (HttpRequestException ex)
            {
                if (retry && attempt < _maxRetries)
                {
                    await Task.Delay(RetryPolicy.Backoff(attempt), cancellationToken).ConfigureAwait(false);
                    attempt++;
                    continue;
                }

                throw new ApiConnectionException($"{method.Method} {path} could not reach the API: {ex.Message}", ex);
            }

            if (retry && attempt < _maxRetries && await IsRetryableAsync(response).ConfigureAwait(false))
            {
                var delay = RetryPolicy.RetryDelay(response, attempt);
                response.Dispose();
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                attempt++;
                continue;
            }

            return response;
        }
    }

    private static async Task<bool> IsRetryableAsync(HttpResponseMessage response)
    {
        var status = (int)response.StatusCode;
        if (RetryPolicy.IsRetryableStatus(status))
        {
            return true;
        }

        if (status == (int)HttpStatusCode.Conflict)
        {
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return RetryPolicy.IsRequestInProgress(body);
        }

        return false;
    }

    public async Task PutPresignedAsync(
        string url, byte[] data, IReadOnlyDictionary<string, string>? headers, CancellationToken cancellationToken)
    {
        var budget = UploadRetryBudget;
        var attempt = 0;
        while (true)
        {
            using var timeoutCts = new CancellationTokenSource(_uploadTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            HttpResponseMessage response;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Put, url) { Content = new ByteArrayContent(data) };
                ApplyHeaders(request, headers);
                response = await _uploadClient.SendAsync(request, linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException ex)
            {
                if (attempt < budget)
                {
                    await Task.Delay(RetryPolicy.Backoff(attempt), cancellationToken).ConfigureAwait(false);
                    attempt++;
                    continue;
                }

                throw new UploadTransferException("presigned upload timed out", ex);
            }
            catch (HttpRequestException ex)
            {
                if (attempt < budget)
                {
                    await Task.Delay(RetryPolicy.Backoff(attempt), cancellationToken).ConfigureAwait(false);
                    attempt++;
                    continue;
                }

                throw new UploadTransferException($"presigned upload failed: {ex.Message}", ex);
            }

            using (response)
            {
                var status = (int)response.StatusCode;
                if (RetryPolicy.IsRetryableStatus(status) && attempt < budget)
                {
                    await Task.Delay(RetryPolicy.RetryDelay(response, attempt), cancellationToken).ConfigureAwait(false);
                    attempt++;
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new UploadTransferException(
                        $"presigned upload returned {status}", status, ApiErrors.RequestIdOf(response));
                }

                return;
            }
        }
    }

    public async Task<byte[]> GetPresignedAsync(string url, CancellationToken cancellationToken)
    {
        var budget = UploadRetryBudget;
        var attempt = 0;
        while (true)
        {
            using var timeoutCts = new CancellationTokenSource(_uploadTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            HttpResponseMessage response;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                response = await _uploadClient.SendAsync(request, linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException ex)
            {
                if (attempt < budget)
                {
                    await Task.Delay(RetryPolicy.Backoff(attempt), cancellationToken).ConfigureAwait(false);
                    attempt++;
                    continue;
                }

                throw new DownloadTransferException("signed download timed out", ex);
            }
            catch (HttpRequestException ex)
            {
                if (attempt < budget)
                {
                    await Task.Delay(RetryPolicy.Backoff(attempt), cancellationToken).ConfigureAwait(false);
                    attempt++;
                    continue;
                }

                throw new DownloadTransferException($"signed download failed: {ex.Message}", ex);
            }

            using (response)
            {
                var status = (int)response.StatusCode;
                if (RetryPolicy.IsRetryableStatus(status) && attempt < budget)
                {
                    await Task.Delay(RetryPolicy.RetryDelay(response, attempt), cancellationToken).ConfigureAwait(false);
                    attempt++;
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new DownloadTransferException(
                        $"signed download returned {status}", status, ApiErrors.RequestIdOf(response));
                }

                return await ContentReader.ReadBytesAsync(response.Content, cancellationToken).ConfigureAwait(false);
            }
        }
    }

#if NET8_0_OR_GREATER

    // ----- Sync path (real synchronous I/O on net8; absent on netstandard2.0) ----------------

    public T? Request<T>(
        HttpMethod method,
        string path,
        object? body = null,
        bool? idempotent = null,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        var payload = body is null ? null : JsonSerializer.SerializeToUtf8Bytes(body, InfersoftJson.Options);
        using var response = Send(method, path, payload, idempotent, idempotencyKey, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // String read over the buffered content (does not dispose the stream, so the body
            // stays readable if it was already inspected for the in-flight-409 check).
            var errorBody = response.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
            throw ApiErrors.CreateApiException(
                (int)response.StatusCode, response.ReasonPhrase, errorBody, ApiErrors.RequestIdOf(response));
        }

        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return default;
        }

        using var stream = response.Content.ReadAsStream(cancellationToken);
        if (stream.CanSeek && stream.Length == 0)
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(stream, InfersoftJson.Options);
    }

    public HttpResponseMessage Send(
        HttpMethod method, string path, byte[]? body, bool? idempotent, string? idempotencyKey, CancellationToken cancellationToken)
    {
        var retry = ResolveRetry(method, idempotent, idempotencyKey);
        var attempt = 0;
        while (true)
        {
            using var timeoutCts = new CancellationTokenSource(_apiTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            HttpResponseMessage response;
            try
            {
                using var request = BuildRequest(method, path, body, idempotencyKey);
                response = _apiClient.Send(request, linked.Token);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException ex)
            {
                if (retry && attempt < _maxRetries)
                {
                    SyncDelay(RetryPolicy.Backoff(attempt), cancellationToken);
                    attempt++;
                    continue;
                }

                throw new ApiTimeoutException($"{method.Method} {path} timed out", ex);
            }
            catch (HttpRequestException ex)
            {
                if (retry && attempt < _maxRetries)
                {
                    SyncDelay(RetryPolicy.Backoff(attempt), cancellationToken);
                    attempt++;
                    continue;
                }

                throw new ApiConnectionException($"{method.Method} {path} could not reach the API: {ex.Message}", ex);
            }

            if (retry && attempt < _maxRetries && IsRetryableSync(response))
            {
                var delay = RetryPolicy.RetryDelay(response, attempt);
                response.Dispose();
                SyncDelay(delay, cancellationToken);
                attempt++;
                continue;
            }

            return response;
        }
    }

    private static bool IsRetryableSync(HttpResponseMessage response)
    {
        var status = (int)response.StatusCode;
        if (RetryPolicy.IsRetryableStatus(status))
        {
            return true;
        }

        if (status == (int)HttpStatusCode.Conflict)
        {
            var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            return RetryPolicy.IsRequestInProgress(body);
        }

        return false;
    }

    public void PutPresigned(
        string url, byte[] data, IReadOnlyDictionary<string, string>? headers, CancellationToken cancellationToken)
    {
        var budget = UploadRetryBudget;
        var attempt = 0;
        while (true)
        {
            using var timeoutCts = new CancellationTokenSource(_uploadTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            HttpResponseMessage response;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Put, url) { Content = new ByteArrayContent(data) };
                ApplyHeaders(request, headers);
                response = _uploadClient.Send(request, linked.Token);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException ex)
            {
                if (attempt < budget)
                {
                    SyncDelay(RetryPolicy.Backoff(attempt), cancellationToken);
                    attempt++;
                    continue;
                }

                throw new UploadTransferException("presigned upload timed out", ex);
            }
            catch (HttpRequestException ex)
            {
                if (attempt < budget)
                {
                    SyncDelay(RetryPolicy.Backoff(attempt), cancellationToken);
                    attempt++;
                    continue;
                }

                throw new UploadTransferException($"presigned upload failed: {ex.Message}", ex);
            }

            using (response)
            {
                var status = (int)response.StatusCode;
                if (RetryPolicy.IsRetryableStatus(status) && attempt < budget)
                {
                    SyncDelay(RetryPolicy.RetryDelay(response, attempt), cancellationToken);
                    attempt++;
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new UploadTransferException(
                        $"presigned upload returned {status}", status, ApiErrors.RequestIdOf(response));
                }

                return;
            }
        }
    }

    public byte[] GetPresigned(string url, CancellationToken cancellationToken)
    {
        var budget = UploadRetryBudget;
        var attempt = 0;
        while (true)
        {
            using var timeoutCts = new CancellationTokenSource(_uploadTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            HttpResponseMessage response;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                response = _uploadClient.Send(request, linked.Token);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException ex)
            {
                if (attempt < budget)
                {
                    SyncDelay(RetryPolicy.Backoff(attempt), cancellationToken);
                    attempt++;
                    continue;
                }

                throw new DownloadTransferException("signed download timed out", ex);
            }
            catch (HttpRequestException ex)
            {
                if (attempt < budget)
                {
                    SyncDelay(RetryPolicy.Backoff(attempt), cancellationToken);
                    attempt++;
                    continue;
                }

                throw new DownloadTransferException($"signed download failed: {ex.Message}", ex);
            }

            using (response)
            {
                var status = (int)response.StatusCode;
                if (RetryPolicy.IsRetryableStatus(status) && attempt < budget)
                {
                    SyncDelay(RetryPolicy.RetryDelay(response, attempt), cancellationToken);
                    attempt++;
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new DownloadTransferException(
                        $"signed download returned {status}", status, ApiErrors.RequestIdOf(response));
                }

                using var stream = response.Content.ReadAsStream(cancellationToken);
                using var memory = new MemoryStream();
                stream.CopyTo(memory);
                return memory.ToArray();
            }
        }
    }

    private static void SyncDelay(TimeSpan delay, CancellationToken cancellationToken)
    {
        if (delay > TimeSpan.Zero)
        {
            cancellationToken.WaitHandle.WaitOne(delay);
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

#else

    // netstandard2.0 has no sync HttpClient.Send — the sync entry points bridge over the async
    // shells at this single boundary. ConfigureAwait(false) throughout keeps it deadlock-safe.

    public T? Request<T>(
        HttpMethod method, string path, object? body = null, bool? idempotent = null,
        string? idempotencyKey = null, CancellationToken cancellationToken = default) =>
        RequestAsync<T>(method, path, body, idempotent, idempotencyKey, cancellationToken)
            .ConfigureAwait(false).GetAwaiter().GetResult();

    public void PutPresigned(
        string url, byte[] data, IReadOnlyDictionary<string, string>? headers, CancellationToken cancellationToken) =>
        PutPresignedAsync(url, data, headers, cancellationToken).ConfigureAwait(false).GetAwaiter().GetResult();

    public byte[] GetPresigned(string url, CancellationToken cancellationToken) =>
        GetPresignedAsync(url, cancellationToken).ConfigureAwait(false).GetAwaiter().GetResult();

#endif
}
