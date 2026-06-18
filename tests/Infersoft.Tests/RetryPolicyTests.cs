using System;
using System.Net;
using System.Net.Http;
using Infersoft.Internal;
using Xunit;

namespace Infersoft.Tests;

public class RetryPolicyTests
{
    [Fact]
    public void RetryDelay_clamps_large_RetryAfter_to_60s()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        response.Headers.TryAddWithoutValidation("Retry-After", "86400");
        Assert.Equal(TimeSpan.FromSeconds(60), RetryPolicy.RetryDelay(response, 0));
    }

    [Fact]
    public void RetryDelay_clamps_negative_RetryAfter_to_zero()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.TryAddWithoutValidation("Retry-After", "-5");
        Assert.Equal(TimeSpan.Zero, RetryPolicy.RetryDelay(response, 0));
    }

    [Fact]
    public void RetryDelay_falls_back_to_backoff_on_http_date()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        response.Headers.TryAddWithoutValidation("Retry-After", "Wed, 21 Oct 2099 07:28:00 GMT");
        // attempt 0 ceiling = min(8, 0.5) = 0.5s
        Assert.InRange(RetryPolicy.RetryDelay(response, 0), TimeSpan.Zero, TimeSpan.FromSeconds(0.5));
    }

    [Fact]
    public void Backoff_is_bounded_by_the_jitter_ceiling()
    {
        // attempt 4 ceiling = min(8, 0.5 * 2^4 = 8) = 8s
        for (var i = 0; i < 100; i++)
        {
            Assert.InRange(RetryPolicy.Backoff(4), TimeSpan.Zero, TimeSpan.FromSeconds(8));
        }
    }

    [Theory]
    [InlineData("GET", true)]
    [InlineData("DELETE", true)]
    [InlineData("PUT", true)]
    [InlineData("POST", false)]
    [InlineData("PATCH", false)]
    public void IsMethodIdempotent_matches_the_safe_methods(string method, bool expected) =>
        Assert.Equal(expected, RetryPolicy.IsMethodIdempotent(new HttpMethod(method)));

    [Fact]
    public void IsRequestInProgress_true_only_for_the_problem_type()
    {
        Assert.True(RetryPolicy.IsRequestInProgress(
            "{\"type\":\"https://api.infersoft.com/problems/request-in-progress\"}"));
        Assert.False(RetryPolicy.IsRequestInProgress("{\"type\":\"about:blank\"}"));
        Assert.False(RetryPolicy.IsRequestInProgress("{}"));
        Assert.False(RetryPolicy.IsRequestInProgress("not json"));
        Assert.False(RetryPolicy.IsRequestInProgress(null));
    }
}
