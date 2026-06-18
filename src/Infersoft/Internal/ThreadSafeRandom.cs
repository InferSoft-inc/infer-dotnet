using System;

namespace Infersoft.Internal;

/// <summary>
/// Thread-safe non-cryptographic randomness for retry-backoff jitter. Uses a per-thread
/// <see cref="Random"/> so it works identically on net8 and netstandard2.0 (where
/// <c>Random.Shared</c> is unavailable).
/// </summary>
internal static class ThreadSafeRandom
{
    [ThreadStatic]
    private static Random? _local;

    public static double NextDouble()
    {
        _local ??= new Random(Guid.NewGuid().GetHashCode());
        return _local.NextDouble();
    }
}
