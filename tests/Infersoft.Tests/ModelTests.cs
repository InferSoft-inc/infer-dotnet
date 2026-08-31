using System.Linq;
using System.Text.Json;
using Infersoft;
using Infersoft.Internal;
using Xunit;

namespace Infersoft.Tests;

public class ModelTests
{
    [Fact]
    public void DocumentSummary_maps_snake_case_enum_nested_and_preserves_unknown_fields()
    {
        const string json =
            "{\"id\":7,\"organization_id\":\"org1\",\"name\":\"a.pdf\",\"status\":\"ready\"," +
            "\"is_valid\":true,\"created_at\":\"2026-01-02T03:04:05Z\",\"has_active_workflow\":false," +
            "\"file_size\":1234,\"page_count\":3," +
            "\"extraction_results\":[{\"prompt_id\":10,\"value\":{\"name\":\"total\"," +
            "\"data_type\":\"Number\",\"display_type\":\"currency\",\"group_name\":\"Financials\"," +
            "\"parsed_value\":42,\"raw_value\":\"42\"}}]," +
            "\"some_new_field\":\"keep\"}";

        var doc = JsonSerializer.Deserialize<DocumentSummary>(json, InfersoftJson.Options)!;

        Assert.Equal(7, doc.Id);
        Assert.Equal("org1", doc.OrganizationId);
        Assert.Equal("a.pdf", doc.Name);
        Assert.Equal(DocumentStatus.Ready, doc.Status);
        Assert.True(doc.IsValid);
        Assert.Equal(1234, doc.FileSize);
        Assert.Equal(3, doc.PageCount);

        var value = Assert.Contains(10L, doc.Extractions);
        Assert.Equal("total", value.Name);
        Assert.Equal(DataTypeName.Number, value.DataType);
        Assert.Equal("currency", value.DisplayType);
        Assert.Equal("Financials", value.GroupName);
        Assert.Equal(42, value.ParsedValue!.Value.GetInt32());

        Assert.NotNull(doc.AdditionalData);
        Assert.Equal("keep", doc.AdditionalData!["some_new_field"].GetString());
    }

    [Fact]
    public void Job_uses_the_createdAt_camelCase_alias()
    {
        const string json =
            "{\"id\":5,\"organization_id\":\"o\",\"organization_name\":\"n\",\"total_docs\":2," +
            "\"completed_docs\":2,\"error_count\":0,\"status\":\"completed\"," +
            "\"createdAt\":\"2026-01-02T03:04:05Z\"}";

        var job = JsonSerializer.Deserialize<Job>(json, InfersoftJson.Options)!;

        Assert.Equal(5, job.Id);
        Assert.Equal(JobStatus.Completed, job.Status);
        Assert.Equal(2026, job.CreatedAt.Year);
    }

    [Fact]
    public void Bbox_uses_pascal_case_aliases()
    {
        const string json = "{\"Top\":0.1,\"Left\":0.2,\"Width\":0.3,\"Height\":0.4}";

        var bbox = JsonSerializer.Deserialize<ExtractionTracebackBBox>(json, InfersoftJson.Options)!;

        Assert.Equal(0.1, bbox.Top);
        Assert.Equal(0.2, bbox.Left);
        Assert.Equal(0.3, bbox.Width);
        Assert.Equal(0.4, bbox.Height);
    }

    [Fact]
    public void UploadResult_computed_properties_partition_outcomes()
    {
        var result = new UploadResult
        {
            Outcomes = new[]
            {
                new FileOutcome { FileName = "a", DocumentId = 1, Uploaded = true },
                new FileOutcome { FileName = "b", Uploaded = false },
                new FileOutcome { FileName = "c", SkippedAsDuplicate = true },
            },
        };

        Assert.Equal(new long[] { 1 }, result.DocumentIds);
        Assert.Single(result.Succeeded);
        Assert.Single(result.Failed);
        Assert.Single(result.Skipped);
    }
}
