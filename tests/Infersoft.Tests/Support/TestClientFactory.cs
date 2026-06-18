using System;
using System.Net.Http;
using System.Threading;
using Infersoft;

namespace Infersoft.Tests.Support;

/// <summary>
/// Builds an <see cref="InfersoftClient"/> over a mock transport that auto-answers the token
/// endpoint and routes everything else to the given API responder. The responder receives an
/// API-only attempt index (token requests are not counted).
/// </summary>
internal static class TestClientFactory
{
    public const string TokenUrl = "https://auth.test/oauth/token";

    public static (InfersoftClient Client, TestHttpMessageHandler Handler) Create(
        Func<HttpRequestMessage, int, CancellationToken, HttpResponseMessage> apiResponder,
        int maxRetries = 2)
    {
        var apiIndex = 0;
        var handler = new TestHttpMessageHandler((req, _, ct) =>
            req.RequestUri!.AbsoluteUri == TokenUrl
                ? Responses.Token()
                : apiResponder(req, apiIndex++, ct));

        var client = new InfersoftClient(new InfersoftClientOptions
        {
            ClientId = "id",
            ClientSecret = "secret",
            BaseUrl = "https://api.test",
            TokenUrl = TokenUrl,
            Audience = "aud",
            MaxRetries = maxRetries,
            Transport = handler,
        });

        return (client, handler);
    }

    public static bool IsApi(RecordedRequest request) => request.Uri!.AbsoluteUri != TokenUrl;
}
