using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Infersoft;
using Infersoft.Tests.Support;
using Xunit;

namespace Infersoft.Tests;

public class ResourcesTests
{
    private static string ProjectJson(string name, long id = 1) =>
        $"{{\"id\":{id},\"organization_id\":\"o\",\"name\":\"{name}\",\"created_at\":\"2026-01-01T00:00:00Z\"}}";

    private static string ProjectPageJson(string? itemsJson) =>
        $"{{\"items\":[{itemsJson}],\"page\":1,\"page_size\":50,\"has_more\":false}}";

    private static string FolderJson(long id, string name, long? parentId = null) =>
        $"{{\"id\":{id},\"organization_id\":\"o\",\"name\":\"{name}\"," +
        (parentId is null ? "" : $"\"parent_id\":{parentId},") +
        "\"created_at\":\"2026-01-01T00:00:00Z\",\"updated_at\":\"2026-01-01T00:00:00Z\",\"has_children\":false}";

    private static bool IsProjectSearch(HttpRequestMessage req) => req.RequestUri!.AbsolutePath.EndsWith("/projects/search", StringComparison.Ordinal);

    // ----- Projects --------------------------------------------------------------------------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Project_Create_sends_name_and_idempotency_key(bool useAsync)
    {
        var pipeline = new TestPipeline((req, i, ct) => Responses.Json(HttpStatusCode.OK, ProjectJson("Acme")));
        using (pipeline)
        {
            var projects = new ProjectsResource(pipeline.Pipeline);
            var project = useAsync ? await projects.CreateAsync("Acme") : projects.Create("Acme");

            Assert.Equal("Acme", project.Name);
            var request = pipeline.Handler.Requests.Single();
            Assert.False(string.IsNullOrEmpty(request.IdempotencyKey));
            using var body = JsonDocument.Parse(request.Body!);
            Assert.Equal("Acme", body.RootElement.GetProperty("name").GetString());
        }
    }

    [Fact]
    public void Project_GetOrCreate_returns_existing_match_without_creating()
    {
        var pipeline = new TestPipeline((req, i, ct) =>
            Responses.Json(HttpStatusCode.OK, ProjectPageJson(ProjectJson("Acme"))));
        using (pipeline)
        {
            var projects = new ProjectsResource(pipeline.Pipeline);
            var project = projects.GetOrCreate("Acme");

            Assert.Equal("Acme", project.Name);
            // No bare POST /api/projects (create) — only the search.
            Assert.DoesNotContain(pipeline.Handler.Requests, r => r.Uri!.AbsolutePath.EndsWith("/api/projects", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Project_GetOrCreate_recovers_from_a_creation_race()
    {
        var searchCalls = 0;
        var pipeline = new TestPipeline((req, i, ct) =>
        {
            if (IsProjectSearch(req))
            {
                searchCalls++;
                return Responses.Json(HttpStatusCode.OK,
                    searchCalls == 1 ? ProjectPageJson(null) : ProjectPageJson(ProjectJson("Acme")));
            }

            // The create races and loses.
            return Responses.Json(HttpStatusCode.Conflict, "{\"title\":\"Conflict\"}");
        });
        using (pipeline)
        {
            var projects = new ProjectsResource(pipeline.Pipeline);
            var project = projects.GetOrCreate("Acme");

            Assert.Equal("Acme", project.Name);
            Assert.Equal(2, searchCalls);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Project_AssignDocuments_sends_selector_and_project_id(bool useAsync)
    {
        var pipeline = new TestPipeline((req, i, ct) =>
            Responses.Json(HttpStatusCode.OK, $"{{\"project\":{ProjectJson("Acme")},\"matched\":2,\"added\":2,\"skipped\":0}}"));
        using (pipeline)
        {
            var projects = new ProjectsResource(pipeline.Pipeline);
            var result = useAsync
                ? await projects.AssignDocumentsAsync(documentIds: new long[] { 1, 2 }, projectId: 5)
                : projects.AssignDocuments(documentIds: new long[] { 1, 2 }, projectId: 5);

            Assert.Equal(2, result.Added);
            var request = pipeline.Handler.Requests.Single();
            Assert.False(string.IsNullOrEmpty(request.IdempotencyKey));
            using var body = JsonDocument.Parse(request.Body!);
            Assert.Equal("fileSelector", body.RootElement.GetProperty("selectors").GetProperty("include")[0].GetProperty("type").GetString());
            Assert.Equal(5, body.RootElement.GetProperty("project_id").GetInt64());
        }
    }

    // ----- Folders ---------------------------------------------------------------------------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Folder_Create_sends_name_and_parent(bool useAsync)
    {
        var pipeline = new TestPipeline((req, i, ct) => Responses.Json(HttpStatusCode.OK, FolderJson(10, "Invoices", 3)));
        using (pipeline)
        {
            var folders = new FoldersResource(pipeline.Pipeline);
            var folder = useAsync ? await folders.CreateAsync("Invoices", parentId: 3) : folders.Create("Invoices", parentId: 3);

            Assert.Equal(10, folder.Id);
            Assert.Equal(3, folder.ParentId);
            using var body = JsonDocument.Parse(pipeline.Handler.Requests.Single().Body!);
            Assert.Equal("Invoices", body.RootElement.GetProperty("name").GetString());
            Assert.Equal(3, body.RootElement.GetProperty("parent_id").GetInt64());
        }
    }

    [Fact]
    public void Folder_Rename_uses_patch()
    {
        var pipeline = new TestPipeline((req, i, ct) => Responses.Json(HttpStatusCode.OK, FolderJson(10, "Renamed")));
        using (pipeline)
        {
            var folders = new FoldersResource(pipeline.Pipeline);
            var folder = folders.Rename(10, "Renamed");

            Assert.Equal("Renamed", folder.Name);
            Assert.Equal("PATCH", pipeline.Handler.Requests.Single().Method);
        }
    }

    [Fact]
    public void Folder_Move_and_BulkDelete()
    {
        var pipeline = new TestPipeline((req, i, ct) =>
            req.RequestUri!.AbsolutePath.EndsWith("/move", StringComparison.Ordinal)
                ? Responses.Json(HttpStatusCode.OK, "{\"matched\":2,\"moved_ids\":[1,2]}")
                : Responses.Json(HttpStatusCode.OK, "{\"requested\":2,\"deleted\":2,\"deleted_folder_ids\":[1,2],\"not_found_folder_ids\":[]}"));
        using (pipeline)
        {
            var folders = new FoldersResource(pipeline.Pipeline);

            var moved = folders.Move(new long[] { 1, 2 }, targetParentId: 5);
            Assert.Equal(new long[] { 1, 2 }, moved.MovedIds);

            var deleted = folders.BulkDelete(new long[] { 1, 2 });
            Assert.Equal(2, deleted.Deleted);
        }
    }

    [Fact]
    public void Folder_ResolvePaths_is_idempotent_without_a_key()
    {
        var pipeline = new TestPipeline((req, i, ct) =>
            Responses.Json(HttpStatusCode.OK, $"{{\"results\":[{{\"folders\":[{FolderJson(1, "a")}]}}]}}"));
        using (pipeline)
        {
            var folders = new FoldersResource(pipeline.Pipeline);
            var result = folders.ResolvePaths(new[] { "a" });

            Assert.Single(result.Results);
            Assert.Null(pipeline.Handler.Requests.Single().IdempotencyKey);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Folder_EnsurePath_returns_the_leaf(bool useAsync)
    {
        var pipeline = new TestPipeline((req, i, ct) =>
            Responses.Json(HttpStatusCode.OK, $"{{\"results\":[{{\"folders\":[{FolderJson(1, "2026")},{FolderJson(2, "Q1")}]}}]}}"));
        using (pipeline)
        {
            var folders = new FoldersResource(pipeline.Pipeline);
            var leaf = useAsync ? await folders.EnsurePathAsync("2026/Q1") : folders.EnsurePath("2026/Q1");

            Assert.Equal(2, leaf.Id);
            Assert.Equal("Q1", leaf.Name);
        }
    }

    [Fact]
    public void Folder_EnsurePath_rejects_an_empty_path()
    {
        var pipeline = new TestPipeline((req, i, ct) => Responses.Status(HttpStatusCode.OK));
        using (pipeline)
        {
            var folders = new FoldersResource(pipeline.Pipeline);
            Assert.Throws<ArgumentException>(() => folders.EnsurePath("  "));
            Assert.Empty(pipeline.Handler.Requests); // validated before any HTTP
        }
    }

    // ----- Prompts ---------------------------------------------------------------------------

    [Fact]
    public void Prompt_search_parses_display_metadata()
    {
        const string body =
            "{\"items\":[" +
            "{\"id\":1,\"organization_id\":\"o\",\"name\":\"Amount\",\"data_type\":\"Number\"," +
            "\"display_type\":\"currency\",\"group_name\":\"Financials\"," +
            "\"example\":{\"example\":\"$1,234.50\",\"explanation\":\"Total contract value.\"}," +
            "\"document_class\":\"invoice\",\"deleted\":false,\"created_at\":\"2026-01-01T00:00:00Z\",\"updated_at\":\"2026-01-01T00:00:00Z\"}," +
            "{\"id\":2,\"organization_id\":\"o\",\"name\":\"Plain\",\"data_type\":\"String\"," +
            "\"document_class\":\"invoice\",\"deleted\":false,\"created_at\":\"2026-01-01T00:00:00Z\",\"updated_at\":\"2026-01-01T00:00:00Z\"}" +
            "],\"page\":1,\"page_size\":50,\"has_more\":false}";

        var pipeline = new TestPipeline((req, i, ct) => Responses.Json(HttpStatusCode.OK, body));
        using (pipeline)
        {
            var prompts = new PromptsResource(pipeline.Pipeline);
            var page = prompts.Search();
            Assert.Equal("currency", page.Items[0].DisplayType);
            Assert.Equal("Financials", page.Items[0].GroupName);
            Assert.Equal("$1,234.50", page.Items[0].Example!.Value.GetProperty("example").GetString());
            Assert.Null(page.Items[1].DisplayType);
            Assert.Null(page.Items[1].GroupName);
            Assert.Null(page.Items[1].Example);
        }
    }

    [Fact]
    public void Prompt_Iterate_fetches_all_pages()
    {
        string Prompt(long id) =>
            $"{{\"id\":{id},\"organization_id\":\"o\",\"name\":\"p{id}\",\"data_type\":\"String\"," +
            "\"document_class\":\"invoice\",\"deleted\":false,\"created_at\":\"2026-01-01T00:00:00Z\",\"updated_at\":\"2026-01-01T00:00:00Z\"}";

        var pipeline = new TestPipeline((req, i, ct) => Responses.Json(
            HttpStatusCode.OK,
            i == 0
                ? $"{{\"items\":[{Prompt(1)}],\"page\":1,\"page_size\":50,\"has_more\":true}}"
                : $"{{\"items\":[{Prompt(2)}],\"page\":2,\"page_size\":50,\"has_more\":false}}"));
        using (pipeline)
        {
            var prompts = new PromptsResource(pipeline.Pipeline);
            var ids = prompts.Iterate().Select(p => p.Id).ToList();
            Assert.Equal(new long[] { 1, 2 }, ids);
        }
    }
}
