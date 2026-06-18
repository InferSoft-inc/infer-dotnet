using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Infersoft;
using Infersoft.Internal;
using Infersoft.Tests.Support;
using Xunit;

namespace Infersoft.Tests;

public class AuthTests
{
    private const string TokenUrl = "https://auth.test/oauth/token";

    private static bool IsToken(HttpRequestMessage request) => request.RequestUri!.AbsoluteUri == TokenUrl;

    private static HttpClient BuildClient(TestHttpMessageHandler handler, int maxRetries = 2) =>
        new(new ClientCredentialsHandler("id", "secret", TokenUrl, "aud", scope: null, maxRetries)
        {
            InnerHandler = handler,
        })
        {
            BaseAddress = new Uri("https://api.test"),
        };

    private static int TokenRequests(TestHttpMessageHandler handler) =>
        handler.Requests.Count(r => r.Uri!.AbsoluteUri == TokenUrl);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Fetches_the_token_once_and_attaches_the_bearer(bool useAsync)
    {
        var handler = new TestHttpMessageHandler((req, i, ct) =>
            IsToken(req) ? Responses.Token() : Responses.Json(HttpStatusCode.OK, "{}"));
        using var client = BuildClient(handler);

        for (var call = 0; call < 2; call++)
        {
            using var response = useAsync
                ? await client.GetAsync("/ping")
                : client.Send(new HttpRequestMessage(HttpMethod.Get, "/ping"));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.Equal(1, TokenRequests(handler));
        Assert.All(
            handler.Requests.Where(r => r.Uri!.AbsoluteUri != TokenUrl),
            r => Assert.Equal("Bearer test-token", r.Authorization));
    }

    [Fact]
    public async Task Refreshes_the_token_on_a_401_and_retries()
    {
        var apiCalls = 0;
        var handler = new TestHttpMessageHandler((req, i, ct) =>
        {
            if (IsToken(req))
            {
                return Responses.Token();
            }

            apiCalls++;
            return apiCalls == 1
                ? Responses.Status(HttpStatusCode.Unauthorized)
                : Responses.Json(HttpStatusCode.OK, "{}");
        });
        using var client = BuildClient(handler);

        using var response = await client.GetAsync("/ping");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, apiCalls);
        Assert.Equal(2, TokenRequests(handler)); // initial + forced refresh
    }

    [Fact]
    public async Task Concurrent_callers_trigger_a_single_token_fetch()
    {
        var handler = new TestHttpMessageHandler((req, i, ct) =>
            IsToken(req) ? Responses.Token() : Responses.Json(HttpStatusCode.OK, "{}"));
        using var client = BuildClient(handler);

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 20).Select(_ => client.GetAsync("/ping")));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(1, TokenRequests(handler));
        foreach (var r in responses)
        {
            r.Dispose();
        }
    }

    [Fact]
    public async Task Terminal_token_failure_throws_without_a_retry_storm()
    {
        var handler = new TestHttpMessageHandler((req, i, ct) =>
            IsToken(req) ? Responses.Status(HttpStatusCode.Unauthorized) : Responses.Json(HttpStatusCode.OK, "{}"));
        using var client = BuildClient(handler);

        await Assert.ThrowsAsync<InfersoftAuthenticationException>(() => client.GetAsync("/ping"));
        Assert.Equal(1, TokenRequests(handler));
    }

    [Fact]
    public async Task Transient_token_failure_is_retried()
    {
        var tokenCalls = 0;
        var handler = new TestHttpMessageHandler((req, i, ct) =>
        {
            if (IsToken(req))
            {
                tokenCalls++;
                return tokenCalls == 1 ? Responses.Status(HttpStatusCode.ServiceUnavailable) : Responses.Token();
            }

            return Responses.Json(HttpStatusCode.OK, "{}");
        });
        using var client = BuildClient(handler);

        using var response = await client.GetAsync("/ping");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, tokenCalls);
    }
}
