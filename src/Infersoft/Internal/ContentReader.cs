using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Infersoft.Internal;

/// <summary>
/// Reads response content, forwarding the cancellation token on net5+ and falling back to the
/// token-less overloads on netstandard2.0 (where they do not exist). Bodies are buffered, so
/// the read completes from memory regardless.
/// </summary>
internal static class ContentReader
{
    public static Task<string> ReadStringAsync(HttpContent content, CancellationToken cancellationToken) =>
#if NET8_0_OR_GREATER
        content.ReadAsStringAsync(cancellationToken);
#else
        content.ReadAsStringAsync();
#endif

    public static Task<byte[]> ReadBytesAsync(HttpContent content, CancellationToken cancellationToken) =>
#if NET8_0_OR_GREATER
        content.ReadAsByteArrayAsync(cancellationToken);
#else
        content.ReadAsByteArrayAsync();
#endif
}
