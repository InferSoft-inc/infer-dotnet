using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
#if NET8_0_OR_GREATER
using System.IO;
#endif

namespace Infersoft.Internal;

/// <summary>
/// OAuth2 client-credentials auth as a <see cref="DelegatingHandler"/>. Fetches a bearer token,
/// caches it until shortly before expiry, refreshes on demand and on a 401, and serializes
/// concurrent refreshes (single-flight). Token logic is shared; the twin <c>Send</c>/<c>SendAsync</c>
/// shells differ only at the I/O points. The token POST goes through <c>base</c> (the inner
/// transport), so it bypasses this handler and carries no bearer.
/// </summary>
internal sealed class ClientCredentialsHandler : DelegatingHandler
{
    private static readonly TimeSpan TokenTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Leeway = TimeSpan.FromSeconds(60);
    private static readonly HashSet<int> TransientTokenStatuses = new() { 408, 429, 500, 502, 503, 504 };

    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly string _tokenUrl;
    private readonly string _audience;
    private readonly string? _scope;
    private readonly int _maxRetries;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private string? _token;
    private TimeSpan _expiresAt;

    public ClientCredentialsHandler(
        string clientId, string clientSecret, string tokenUrl, string audience, string? scope, int maxRetries)
    {
        _clientId = clientId;
        _clientSecret = clientSecret;
        _tokenUrl = tokenUrl;
        _audience = audience;
        _scope = scope;
        _maxRetries = maxRetries;
    }

    private bool IsValid => _token is not null && _clock.Elapsed < _expiresAt - Leeway;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", await GetTokenAsync(force: false, cancellationToken).ConfigureAwait(false));
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != System.Net.HttpStatusCode.Unauthorized)
        {
            return response;
        }

        response.Dispose();
        using var retry = await CloneAsync(request).ConfigureAwait(false);
        retry.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", await GetTokenAsync(force: true, cancellationToken).ConfigureAwait(false));
        return await base.SendAsync(retry, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> GetTokenAsync(bool force, CancellationToken cancellationToken)
    {
        var stale = force ? _token : null;
        if (force || !IsValid)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (ShouldFetch(force, stale))
                {
                    await FetchTokenAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        return _token!;
    }

    // A forced refresh re-fetches only if the cached token is still the stale one the caller saw;
    // otherwise a concurrent caller already refreshed and we reuse their token.
    private bool ShouldFetch(bool force, string? stale) =>
        (force && _token == stale) || !IsValid;

    private async Task FetchTokenAsync(CancellationToken cancellationToken)
    {
        var attempt = 0;
        while (true)
        {
            using var timeoutCts = new CancellationTokenSource(TokenTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            HttpResponseMessage response;
            try
            {
                using var request = BuildTokenRequest();
                response = await base.SendAsync(request, linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException ex)
            {
                if (attempt < _maxRetries)
                {
                    await Task.Delay(RetryPolicy.Backoff(attempt), cancellationToken).ConfigureAwait(false);
                    attempt++;
                    continue;
                }

                throw new InfersoftAuthenticationException($"token request failed: {ex.Message}", ex);
            }
            catch (HttpRequestException ex)
            {
                if (attempt < _maxRetries)
                {
                    await Task.Delay(RetryPolicy.Backoff(attempt), cancellationToken).ConfigureAwait(false);
                    attempt++;
                    continue;
                }

                throw new InfersoftAuthenticationException($"token request failed: {ex.Message}", ex);
            }

            using (response)
            {
                var status = (int)response.StatusCode;
                if (TransientTokenStatuses.Contains(status) && attempt < _maxRetries)
                {
                    await Task.Delay(RetryPolicy.Backoff(attempt), cancellationToken).ConfigureAwait(false);
                    attempt++;
                    continue;
                }

                if (status != 200)
                {
                    var body = await ContentReader.ReadStringAsync(response.Content, cancellationToken).ConfigureAwait(false);
                    throw new InfersoftAuthenticationException($"token endpoint returned {status}: {Truncate(body, 500)}");
                }

                Store(await ContentReader.ReadBytesAsync(response.Content, cancellationToken).ConfigureAwait(false));
                return;
            }
        }
    }

    private HttpRequestMessage BuildTokenRequest()
    {
        var payload = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = _clientId,
            ["client_secret"] = _clientSecret,
            ["audience"] = _audience,
        };
        if (!string.IsNullOrEmpty(_scope))
        {
            payload["scope"] = _scope!;
        }

        var request = new HttpRequestMessage(HttpMethod.Post, _tokenUrl)
        {
            Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(payload)),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        request.Headers.TryAddWithoutValidation("User-Agent", SdkInfo.UserAgent);
        return request;
    }

    private void Store(byte[] json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("access_token", out var tokenElement)
                || tokenElement.ValueKind != JsonValueKind.String
                || string.IsNullOrEmpty(tokenElement.GetString()))
            {
                throw new InfersoftAuthenticationException("token endpoint response did not include access_token");
            }

            _token = tokenElement.GetString();
            var lifetime = 3600.0;
            if (root.TryGetProperty("expires_in", out var expiresElement) && expiresElement.ValueKind == JsonValueKind.Number)
            {
                lifetime = expiresElement.GetDouble();
            }

            _expiresAt = _clock.Elapsed + TimeSpan.FromSeconds(lifetime);
        }
        catch (JsonException ex)
        {
            throw new InfersoftAuthenticationException("token endpoint returned invalid JSON", ex);
        }
    }

    private static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri) { Version = request.Version };
        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (request.Content is not null)
        {
            var bytes = await request.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            var content = new ByteArrayContent(bytes);
            foreach (var header in request.Content.Headers)
            {
                content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            clone.Content = content;
        }

        return clone;
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value.Substring(0, max);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _gate.Dispose();
        }

        base.Dispose(disposing);
    }

#if NET8_0_OR_GREATER

    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", GetToken(force: false, cancellationToken));
        var response = base.Send(request, cancellationToken);
        if (response.StatusCode != System.Net.HttpStatusCode.Unauthorized)
        {
            return response;
        }

        response.Dispose();
        using var retry = CloneAsync(request).GetAwaiter().GetResult();
        retry.Headers.Authorization = new AuthenticationHeaderValue("Bearer", GetToken(force: true, cancellationToken));
        return base.Send(retry, cancellationToken);
    }

    private string GetToken(bool force, CancellationToken cancellationToken)
    {
        var stale = force ? _token : null;
        if (force || !IsValid)
        {
            _gate.Wait(cancellationToken);
            try
            {
                if (ShouldFetch(force, stale))
                {
                    FetchToken(cancellationToken);
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        return _token!;
    }

    private void FetchToken(CancellationToken cancellationToken)
    {
        var attempt = 0;
        while (true)
        {
            using var timeoutCts = new CancellationTokenSource(TokenTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            HttpResponseMessage response;
            try
            {
                using var request = BuildTokenRequest();
                response = base.Send(request, linked.Token);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException ex)
            {
                if (attempt < _maxRetries)
                {
                    SyncDelay(RetryPolicy.Backoff(attempt), cancellationToken);
                    attempt++;
                    continue;
                }

                throw new InfersoftAuthenticationException($"token request failed: {ex.Message}", ex);
            }
            catch (HttpRequestException ex)
            {
                if (attempt < _maxRetries)
                {
                    SyncDelay(RetryPolicy.Backoff(attempt), cancellationToken);
                    attempt++;
                    continue;
                }

                throw new InfersoftAuthenticationException($"token request failed: {ex.Message}", ex);
            }

            using (response)
            {
                var status = (int)response.StatusCode;
                if (TransientTokenStatuses.Contains(status) && attempt < _maxRetries)
                {
                    SyncDelay(RetryPolicy.Backoff(attempt), cancellationToken);
                    attempt++;
                    continue;
                }

                if (status != 200)
                {
                    using var reader = new StreamReader(response.Content.ReadAsStream(cancellationToken));
                    throw new InfersoftAuthenticationException($"token endpoint returned {status}: {Truncate(reader.ReadToEnd(), 500)}");
                }

                using var stream = response.Content.ReadAsStream(cancellationToken);
                using var memory = new MemoryStream();
                stream.CopyTo(memory);
                Store(memory.ToArray());
                return;
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

#endif
}
