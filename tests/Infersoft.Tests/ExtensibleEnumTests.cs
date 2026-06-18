using System.Text.Json;
using Infersoft;
using Infersoft.Internal;
using Xunit;

namespace Infersoft.Tests;

public class ExtensibleEnumTests
{
    [Fact]
    public void Known_value_round_trips()
    {
        Assert.Equal("\"ready\"", JsonSerializer.Serialize(DocumentStatus.Ready, InfersoftJson.Options));
        Assert.Equal(DocumentStatus.Ready, JsonSerializer.Deserialize<DocumentStatus>("\"ready\"", InfersoftJson.Options));
    }

    [Fact]
    public void Unknown_value_round_trips_and_is_forward_compatible()
    {
        var status = JsonSerializer.Deserialize<DocumentStatus>("\"archived\"", InfersoftJson.Options);

        Assert.Equal("archived", status.Value);
        Assert.NotEqual(DocumentStatus.Ready, status);
        Assert.True(status == "archived"); // implicit string comparison
        Assert.Equal("\"archived\"", JsonSerializer.Serialize(status, InfersoftJson.Options));

        // A forward-compatible switch falls through to the default branch.
        var label = status == DocumentStatus.Ready ? "ready"
            : status == DocumentStatus.Uploading ? "uploading"
            : "other";
        Assert.Equal("other", label);
    }

    [Fact]
    public void DataType_capitalized_values_and_nullable()
    {
        Assert.Equal("\"Number\"", JsonSerializer.Serialize(DataTypeName.Number, InfersoftJson.Options));

        var value = JsonSerializer.Deserialize<ExtractionResultValue>(
            "{\"data_type\":\"String\"}", InfersoftJson.Options)!;
        Assert.Equal(DataTypeName.String, value.DataType);

        var none = JsonSerializer.Deserialize<ExtractionResultValue>("{}", InfersoftJson.Options)!;
        Assert.Null(none.DataType);
    }

    [Fact]
    public void JobStatus_known_members()
    {
        Assert.Equal("partial_success", JobStatus.PartialSuccess.Value);
        Assert.Equal(JobStatus.Completed, JsonSerializer.Deserialize<JobStatus>("\"completed\"", InfersoftJson.Options));
    }
}
