using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace Infersoft.Internal;

/// <summary>
/// Pure, transport-agnostic retry decisions shared by the synchronous and asynchronous request
/// paths. No I/O happens here.
/// </summary>
internal static class RetryPolicy
{
    // Transient statuses safe to retry. A curated subset, not a category check:
    // 409 (deterministic conflict) is handled separately; 501/505 are excluded.
    private static readonly HashSet<int> RetryStatuses = new() { 408, 429, 500, 502, 503, 504 };

    // HTTP methods inherently safe to retry. Mutating POSTs retry only with an
    // Idempotency-Key or an explicit idempotent flag.
    private static readonly HashSet<string> IdempotentMethods =
        new(StringComparer.OrdinalIgnoreCase) { "GET", "HEAD", "OPTIONS", "DELETE", "PUT" };

    /// <summary>Request header carrying a client idempotency key (matches the API).</summary>
    public const string IdempotencyHeader = "Idempotency-Key";

    /// <summary>
    /// RFC 9457 problem <c>type</c> marking the server's transient in-flight 409 (another
    /// request with the same Idempotency-Key is still executing). Must match the server
    /// byte-for-byte. Untyped 409s are deterministic conflicts and stay terminal.
    /// </summary>
    public const string ProblemTypeRequestInProgress = "https://api.infersoft.com/problems/request-in-progress";

    private const double MaxBackoffSeconds = 8.0;

    // A misbehaving server/intermediary must not stall the client arbitrarily; negative
    // values clamp to 0 rather than crashing the sync wait.
    private const double MaxRetryAfterSeconds = 60.0;

    public static bool IsMethodIdempotent(HttpMethod method) => IdempotentMethods.Contains(method.Method);

    public static bool IsRetryableStatus(int status) => RetryStatuses.Contains(status);

    /// <summary>True when a 409 body carries the transient request-in-progress problem type.</summary>
    public static bool IsRequestInProgress(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(body!);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("type", out var type)
                && type.ValueKind == JsonValueKind.String
                && string.Equals(type.GetString(), ProblemTypeRequestInProgress, StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Exponential backoff with full jitter, to avoid synchronized retries.</summary>
    public static TimeSpan Backoff(int attempt)
    {
        var ceiling = Math.Min(MaxBackoffSeconds, 0.5 * Math.Pow(2.0, attempt));
        return TimeSpan.FromSeconds(ThreadSafeRandom.NextDouble() * ceiling);
    }

    /// <summary>
    /// Honor a numeric <c>Retry-After</c> clamped to [0, 60] seconds; an HTTP-date or
    /// unparseable value falls back to jittered backoff.
    /// </summary>
    public static TimeSpan RetryDelay(HttpResponseMessage response, int attempt)
    {
        if (response.Headers.TryGetValues("Retry-After", out var values))
        {
            foreach (var value in values)
            {
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
                {
                    return TimeSpan.FromSeconds(Math.Max(0.0, Math.Min(seconds, MaxRetryAfterSeconds)));
                }

                break; // first value only; HTTP-date / garbage falls through to backoff
            }
        }

        return Backoff(attempt);
    }
}
