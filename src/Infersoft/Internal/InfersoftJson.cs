using System.Text.Json;
using System.Text.Json.Serialization;

namespace Infersoft.Internal;

/// <summary>
/// The single, cached <see cref="JsonSerializerOptions"/> used for all request/response
/// (de)serialization. Built once and reused (never per call).
/// </summary>
internal static class InfersoftJson
{
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        return new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNameCaseInsensitive = true,
        };
    }
}
