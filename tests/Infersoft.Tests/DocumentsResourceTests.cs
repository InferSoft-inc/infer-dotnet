using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Infersoft;
using Infersoft.Tests.Support;
using Xunit;

namespace Infersoft.Tests;

public class DocumentsResourceTests
{
    private const string DocJson =
        "{\"id\":1,\"organization_id\":\"o\",\"name\":\"a.pdf\",\"status\":\"ready\"," +
        "\"is_valid\":true,\"created_at\":\"2026-01-01T00:00:00Z\",\"has_active_workflow\":false}";

    private static string Page(string itemsJson, bool hasMore) =>
        $"{{\"items\":[{itemsJson}],\"page\":1,\"page_size\":50,\"has_more\":{(hasMore ? "true" : "false")}}}";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Get_fetches_and_passes_prompt_ids(bool useAsync)
    {
        var (client, handler) = TestClientFactory.Create((req, i, ct) => Responses.Json(HttpStatusCode.OK, DocJson));
        using (client)
        {
            var doc = useAsync
                ? await client.Documents.GetAsync(1, new long[] { 10, 20 })
                : client.Documents.Get(1, new long[] { 10, 20 });

            Assert.Equal(1, doc.Id);
            Assert.Equal(DocumentStatus.Ready, doc.Status);

            var apiRequest = handler.Requests.Single(TestClientFactory.IsApi);
            Assert.Equal("GET", apiRequest.Method);
            var query = Uri.UnescapeDataString(apiRequest.Uri!.Query);
            Assert.Contains("prompt_ids=10,20", query);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Iterate_fetches_all_pages(bool useAsync)
    {
        var doc2 = DocJson.Replace("\"id\":1", "\"id\":2", StringComparison.Ordinal);
        var (client, handler) = TestClientFactory.Create((req, i, ct) =>
            Responses.Json(HttpStatusCode.OK, i == 0 ? Page(DocJson, hasMore: true) : Page(doc2, hasMore: false)));
        using (client)
        {
            var ids = new List<long>();
            if (useAsync)
            {
                await foreach (var d in client.Documents.IterateAsync())
                {
                    ids.Add(d.Id);
                }
            }
            else
            {
                foreach (var d in client.Documents.Iterate())
                {
                    ids.Add(d.Id);
                }
            }

            Assert.Equal(new long[] { 1, 2 }, ids);
            Assert.Equal(2, handler.Requests.Count(TestClientFactory.IsApi));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Search_documentIds_shortcut_sends_a_file_selector(bool useAsync)
    {
        var (client, handler) = TestClientFactory.Create((req, i, ct) =>
            Responses.Json(HttpStatusCode.OK, Page(DocJson, hasMore: false)));
        using (client)
        {
            _ = useAsync
                ? await client.Documents.SearchAsync(documentIds: new long[] { 5, 6 })
                : client.Documents.Search(documentIds: new long[] { 5, 6 });

            var apiRequest = handler.Requests.Single(TestClientFactory.IsApi);
            using var body = JsonDocument.Parse(apiRequest.Body!);
            var type = body.RootElement.GetProperty("selectors").GetProperty("include")[0].GetProperty("type").GetString();
            Assert.Equal("fileSelector", type);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MoveToFolder_sends_an_idempotency_key(bool useAsync)
    {
        var (client, handler) = TestClientFactory.Create((req, i, ct) =>
            Responses.Json(HttpStatusCode.OK, "{\"matched\":1,\"moved\":1,\"skipped\":0}"));
        using (client)
        {
            var result = useAsync
                ? await client.Documents.MoveToFolderAsync(documentIds: new long[] { 1 }, targetFolderId: 7)
                : client.Documents.MoveToFolder(documentIds: new long[] { 1 }, targetFolderId: 7);

            Assert.Equal(1, result.Moved);
            var apiRequest = handler.Requests.Single(TestClientFactory.IsApi);
            Assert.False(string.IsNullOrEmpty(apiRequest.IdempotencyKey));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BulkDelete_sends_dry_run_and_key(bool useAsync)
    {
        var (client, handler) = TestClientFactory.Create((req, i, ct) =>
            Responses.Json(HttpStatusCode.OK, "{\"matched\":3,\"dry_run\":true}"));
        using (client)
        {
            var result = useAsync
                ? await client.Documents.BulkDeleteAsync(documentIds: new long[] { 1, 2, 3 }, dryRun: true)
                : client.Documents.BulkDelete(documentIds: new long[] { 1, 2, 3 }, dryRun: true);

            Assert.Equal(3, result.Matched);
            Assert.True(result.DryRun);

            var apiRequest = handler.Requests.Single(TestClientFactory.IsApi);
            Assert.False(string.IsNullOrEmpty(apiRequest.IdempotencyKey));
            using var body = JsonDocument.Parse(apiRequest.Body!);
            Assert.True(body.RootElement.GetProperty("dry_run").GetBoolean());
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Delete_issues_a_delete(bool useAsync)
    {
        var (client, handler) = TestClientFactory.Create((req, i, ct) => Responses.Status(HttpStatusCode.NoContent));
        using (client)
        {
            if (useAsync)
            {
                await client.Documents.DeleteAsync(9);
            }
            else
            {
                client.Documents.Delete(9);
            }

            var apiRequest = handler.Requests.Single(TestClientFactory.IsApi);
            Assert.Equal("DELETE", apiRequest.Method);
            Assert.Contains("/api/documents/9", apiRequest.Uri!.AbsolutePath);
        }
    }

    [Fact]
    public void GetValues_flattens_by_name()
    {
        var docJson = DocJson.Replace(
            "\"has_active_workflow\":false}",
            "\"has_active_workflow\":false,\"extraction_results\":[{\"prompt_id\":10,\"value\":{\"name\":\"total\",\"parsed_value\":42}}]}",
            StringComparison.Ordinal);
        var (client, _) = TestClientFactory.Create((req, i, ct) =>
            Responses.Json(HttpStatusCode.OK, Page(docJson, hasMore: false)));
        using (client)
        {
            var values = client.Documents.GetValues(prompts: new long[] { 10 }, documentIds: new long[] { 1 });
            Assert.Equal(42, ((JsonElement)values[1]["total"]!).GetInt32());
        }
    }

    [Fact]
    public void GetValues_throws_on_key_collision_and_PromptId_disambiguates()
    {
        var docJson = DocJson.Replace(
            "\"has_active_workflow\":false}",
            "\"has_active_workflow\":false,\"extraction_results\":[" +
            "{\"prompt_id\":10,\"value\":{\"name\":\"total\",\"parsed_value\":1}}," +
            "{\"prompt_id\":11,\"value\":{\"name\":\"total\",\"parsed_value\":2}}]}",
            StringComparison.Ordinal);
        var (client, _) = TestClientFactory.Create((req, i, ct) =>
            Responses.Json(HttpStatusCode.OK, Page(docJson, hasMore: false)));
        using (client)
        {
            Assert.Throws<ArgumentException>(
                () => client.Documents.GetValues(prompts: new long[] { 10, 11 }, documentIds: new long[] { 1 }));

            var values = client.Documents.GetValues(
                prompts: new long[] { 10, 11 }, documentIds: new long[] { 1 }, keyBy: ExtractionKey.PromptId);
            Assert.Equal(2, values[1].Count);
        }
    }
}
