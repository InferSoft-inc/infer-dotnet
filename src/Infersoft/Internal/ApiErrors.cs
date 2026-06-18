using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Infersoft.Internal;

/// <summary>Parsed RFC 9457 problem-details body (lenient — every field optional).</summary>
internal sealed class ProblemDetails
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("detail")]
    public string? Detail { get; set; }

    [JsonPropertyName("instance")]
    public string? Instance { get; set; }
}

/// <summary>Maps a failed HTTP response onto the most specific <see cref="InfersoftApiException"/>.</summary>
internal static class ApiErrors
{
    // Headers checked, in order, for a server-issued request/trace identifier.
    private static readonly string[] RequestIdHeaders =
    {
        "x-request-id",
        "x-amzn-requestid",
        "x-amz-request-id",
        "x-correlation-id",
    };

    public static string? RequestIdOf(HttpResponseMessage response)
    {
        foreach (var name in RequestIdHeaders)
        {
            if (response.Headers.TryGetValues(name, out var values))
            {
                var value = values.FirstOrDefault();
                if (!string.IsNullOrEmpty(value))
                {
                    return value;
                }
            }
        }

        return null;
    }

    public static InfersoftApiException CreateApiException(
        int statusCode, string? reasonPhrase, string? body, string? requestId)
    {
        string? title = reasonPhrase;
        string? detail = null;
        string? type = null;
        string? instance = null;

        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                var problem = JsonSerializer.Deserialize<ProblemDetails>(body!);
                if (problem is not null)
                {
                    title = problem.Title ?? title;
                    detail = problem.Detail;
                    type = problem.Type;
                    instance = problem.Instance;
                }
            }
            catch (JsonException)
            {
                // Non-JSON body: keep the reason phrase as the title.
            }
        }

        return statusCode switch
        {
            400 => new BadRequestException(statusCode, title, detail, type, instance, requestId, body),
            401 => new UnauthorizedException(statusCode, title, detail, type, instance, requestId, body),
            403 => new ForbiddenException(statusCode, title, detail, type, instance, requestId, body),
            404 => new NotFoundException(statusCode, title, detail, type, instance, requestId, body),
            409 => new ConflictException(statusCode, title, detail, type, instance, requestId, body),
            412 => new PreconditionFailedException(statusCode, title, detail, type, instance, requestId, body),
            413 => new PayloadTooLargeException(statusCode, title, detail, type, instance, requestId, body),
            422 => new UnprocessableEntityException(statusCode, title, detail, type, instance, requestId, body),
            429 => new RateLimitException(statusCode, title, detail, type, instance, requestId, body),
            >= 500 => new ServerException(statusCode, title, detail, type, instance, requestId, body),
            _ => new InfersoftApiException(statusCode, title, detail, type, instance, requestId, body),
        };
    }
}
