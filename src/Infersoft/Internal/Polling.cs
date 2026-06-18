using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Infersoft.Internal;

/// <summary>
/// Polling helpers backing the <c>Wait</c> flags. Deadlines are finite by default; pass
/// <see cref="Timeout.InfiniteTimeSpan"/> (or a non-positive budget) to wait without one.
/// </summary>
internal static class Polling
{
    public static readonly TimeSpan DefaultJobWait = TimeSpan.FromMinutes(30);

    public static readonly TimeSpan DefaultDocumentWait = TimeSpan.FromMinutes(10);

    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(2);

    public static T PollUntil<T>(
        Func<T> fetch,
        Func<T, bool> done,
        TimeSpan maxWait,
        TimeSpan pollInterval,
        string timeoutMessage,
        CancellationToken cancellationToken)
    {
        var clock = maxWait > TimeSpan.Zero ? Stopwatch.StartNew() : null;
        while (true)
        {
            var value = fetch();
            if (done(value))
            {
                return value;
            }

            if (clock is not null && clock.Elapsed >= maxWait)
            {
                throw new WaitTimeoutException(timeoutMessage);
            }

            if (cancellationToken.WaitHandle.WaitOne(pollInterval))
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
    }

    public static async Task<T> PollUntilAsync<T>(
        Func<CancellationToken, Task<T>> fetch,
        Func<T, bool> done,
        TimeSpan maxWait,
        TimeSpan pollInterval,
        string timeoutMessage,
        CancellationToken cancellationToken)
    {
        var clock = maxWait > TimeSpan.Zero ? Stopwatch.StartNew() : null;
        while (true)
        {
            var value = await fetch(cancellationToken).ConfigureAwait(false);
            if (done(value))
            {
                return value;
            }

            if (clock is not null && clock.Elapsed >= maxWait)
            {
                throw new WaitTimeoutException(timeoutMessage);
            }

            await Task.Delay(pollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Log one warning per distinct unknown status value seen by a wait loop. An extensible enum
    /// accepts statuses this build does not know; a wait loop cannot tell a new non-terminal
    /// status from a new terminal one, so it keeps polling but makes the case diagnosable.
    /// </summary>
    public static void WarnOnceUnknownStatus(
        ILogger logger, ISet<string> seen, string value, ISet<string> known, string what)
    {
        if (known.Contains(value) || !seen.Add(value))
        {
            return;
        }

        logger.LogWarning(
            "unknown {What} status '{Status}' observed while waiting; treating it as non-terminal " +
            "(this SDK build may predate it) — polling continues until done or timeout",
            what,
            value);
    }
}
