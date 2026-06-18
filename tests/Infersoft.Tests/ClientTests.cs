using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Infersoft;
using Infersoft.Tests.Support;
using Xunit;

namespace Infersoft.Tests;

public class ClientTests
{
    private const string TokenUrl = "https://auth.test/oauth/token";

    private static InfersoftClient BuildClient(TestHttpMessageHandler handler) =>
        new(new InfersoftClientOptions
        {
            ClientId = "id",
            ClientSecret = "secret",
            BaseUrl = "https://api.test",
            TokenUrl = TokenUrl,
            Audience = "aud",
            Transport = handler,
        });

    private static TestHttpMessageHandler ApiHandler(string apiJson) =>
        new((req, i, ct) =>
            req.RequestUri!.AbsoluteUri == TokenUrl
                ? Responses.Token()
                : Responses.Json(HttpStatusCode.OK, apiJson));

    [Fact]
    public void Missing_credentials_throws_InfersoftException()
    {
        Environment.SetEnvironmentVariable("INFERSOFT_CLIENT_ID", null);
        Environment.SetEnvironmentVariable("INFERSOFT_CLIENT_SECRET", null);

        Assert.Throws<InfersoftException>(
            () => new InfersoftClient(new InfersoftClientOptions { BaseUrl = "https://api.test" }));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task End_to_end_request_through_the_full_stack(bool useAsync)
    {
        var handler = ApiHandler("{\"name\":\"doc\"}");
        using var client = BuildClient(handler);

        var echo = useAsync
            ? await client.Pipeline.RequestAsync<Echo>(HttpMethod.Get, "/documents/1")
            : client.Pipeline.Request<Echo>(HttpMethod.Get, "/documents/1");

        Assert.Equal("doc", echo!.Name);
        Assert.Contains(handler.Requests, r => r.Authorization == "Bearer test-token");
    }

    [Fact]
    public async Task Requests_carry_the_sdk_user_agent()
    {
        var handler = ApiHandler(
            "{\"id\":1,\"organization_id\":\"o\",\"name\":\"x\",\"status\":\"ready\"," +
            "\"is_valid\":true,\"created_at\":\"2026-01-01T00:00:00Z\",\"has_active_workflow\":false}");
        using var client = BuildClient(handler);

        await client.Documents.GetAsync(1);

        var apiRequest = handler.Requests.First(r => r.Uri!.AbsoluteUri != TokenUrl);
        Assert.StartsWith("infersoft-dotnet/", apiRequest.UserAgent!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithOptions_copy_dispose_does_not_close_the_original()
    {
        var handler = ApiHandler("{\"name\":\"ok\"}");
        using var client = BuildClient(handler);

        var copy = client.WithOptions(maxRetries: 0, timeout: TimeSpan.FromSeconds(5));
        copy.Dispose(); // must be a no-op — the copy shares the original's pool

        var echo = await client.Pipeline.RequestAsync<Echo>(HttpMethod.Get, "/x");
        Assert.Equal("ok", echo!.Name);
    }
}
