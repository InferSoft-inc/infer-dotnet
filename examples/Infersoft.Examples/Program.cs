using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Infersoft;

namespace Infersoft.Examples;

// Workflow-curated, compile-gated samples. Each reads credentials from the INFERSOFT_* env vars.
// Run one with: dotnet run --project examples/Infersoft.Examples -- <name>
public static class Program
{
    public static async Task Main(string[] args)
    {
        switch (args.FirstOrDefault())
        {
            case "extract": await QuickstartExtract(); break;
            case "search": await SearchAndExport(); break;
            case "bulk": await BulkImport(); break;
            case "download": await DownloadAll(); break;
            case "concurrent": await ConcurrentReads(); break;
            default: Console.WriteLine("examples: extract | search | bulk | download | concurrent"); break;
        }
    }

    private static InfersoftClient Connect() => new(new InfersoftClientOptions());

    // Upload PDFs and extract two prompts in one call.
    private static async Task QuickstartExtract()
    {
        using var client = Connect();
        var result = await client.ExtractAsync(
            files: new[] { "invoice-1.pdf", "invoice-2.pdf" },
            prompts: new long[] { 101, 102 },
            maxCredits: 500);

        if (result.Upload?.Failed.Count > 0)
        {
            Console.WriteLine($"warning: {result.Upload.Failed.Count} file(s) failed to upload");
        }

        foreach (var pair in result.Values)
        {
            Console.WriteLine($"document {pair.Key}: {string.Join(", ", pair.Value.Keys)}");
        }
    }

    // Export extraction values for every document in a folder.
    private static async Task SearchAndExport()
    {
        using var client = Connect();
        var values = await client.Documents.GetValuesAsync(
            prompts: new long[] { 101, 102 },
            selectors: Selectors.Build(include: new[] { Selector.Folder(7) }));

        foreach (var pair in values)
        {
            var fields = string.Join(", ", pair.Value.Select(kv => $"{kv.Key}={kv.Value}"));
            Console.WriteLine($"{pair.Key}: {fields}");
        }
    }

    // Bulk-import a directory of PDFs into a project, reporting progress per batch.
    private static async Task BulkImport()
    {
        using var client = Connect();
        var result = await client.Documents.UploadManyFromDirectoryAsync(
            "./pdfs",
            searchPattern: "*.pdf",
            projectName: "Imports",
            onBatch: batch => Console.WriteLine($"batch: {batch.Succeeded.Count} uploaded"),
            wait: true);

        Console.WriteLine($"done: {result.Succeeded.Count} uploaded, {result.Failed.Count} failed");
    }

    // Download every document into a local directory.
    private static async Task DownloadAll()
    {
        using var client = Connect();
        var directory = Directory.CreateDirectory("downloads").FullName;
        await foreach (var doc in client.Documents.IterateAsync())
        {
            var path = await client.Documents.DownloadAsync(doc.Id, directory, overwrite: true);
            Console.WriteLine($"saved {path}");
        }
    }

    // Fetch several documents concurrently.
    private static async Task ConcurrentReads()
    {
        using var client = Connect();
        long[] ids = { 1, 2, 3 };
        var documents = await Task.WhenAll(ids.Select(id => client.Documents.GetAsync(id)));
        foreach (var doc in documents)
        {
            Console.WriteLine($"{doc.Id}: {doc.Name} ({doc.Status})");
        }
    }
}
