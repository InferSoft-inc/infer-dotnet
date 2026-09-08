using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Infersoft;

/// <summary>
/// A document selector. Build one with the static factories (e.g. <see cref="File"/>,
/// <see cref="Folder"/>) and combine them with <see cref="Selectors.Build"/>. Use
/// <see cref="Raw"/> for a selector type this SDK build does not yet model.
/// </summary>
[JsonConverter(typeof(SelectorJsonConverter))]
public sealed class Selector
{
    private Selector(string type, IReadOnlyDictionary<string, object?> fields)
    {
        Type = type;
        Fields = fields;
    }

    internal string Type { get; }

    internal IReadOnlyDictionary<string, object?> Fields { get; }

    /// <summary>Match documents by id.</summary>
    public static Selector File(IEnumerable<long> ids) =>
        Make("fileSelector", ("files", ids.ToList()));

    /// <summary>Match documents in a folder.</summary>
    public static Selector Folder(long folderId) =>
        Make("folderSelector", ("folder_id", folderId));

    /// <summary>Match documents in a folder or in any folder below it, at any depth.</summary>
    public static Selector FolderSubtree(long folderId) =>
        Make("folderSubtreeSelector", ("folder_id", folderId));

    /// <summary>Match by document name.</summary>
    public static Selector Name(string value) =>
        Make("nameSelector", ("name", value));

    /// <summary>Match by document class.</summary>
    public static Selector DocumentClass(IEnumerable<string> classes) =>
        Make("documentClassSelector", ("classes", classes.ToList()));

    /// <summary>Match documents carrying any of the given tag ids.</summary>
    public static Selector Tag(IEnumerable<long> ids, string? taggedFrom = null, string? taggedTo = null)
    {
        var fields = new Dictionary<string, object?> { ["tags"] = ids.ToList() };
        AddIfPresent(fields, "tagged_from", taggedFrom);
        AddIfPresent(fields, "tagged_to", taggedTo);
        return new Selector("tagSelector", fields);
    }

    /// <summary>Match documents created within a time range (pass at least one ISO-8601 bound).</summary>
    public static Selector CreatedAt(string? createdFrom = null, string? createdTo = null)
    {
        if (createdFrom is null && createdTo is null)
        {
            throw new ArgumentException("CreatedAt requires createdFrom and/or createdTo", nameof(createdFrom));
        }

        var fields = new Dictionary<string, object?>();
        AddIfPresent(fields, "created_from", createdFrom);
        AddIfPresent(fields, "created_to", createdTo);
        return new Selector("createdAtSelector", fields);
    }

    /// <summary>Match documents by byte-size range (pass at least one bound).</summary>
    public static Selector Size(long? sizeFrom = null, long? sizeTo = null)
    {
        if (sizeFrom is null && sizeTo is null)
        {
            throw new ArgumentException("Size requires sizeFrom and/or sizeTo", nameof(sizeFrom));
        }

        var fields = new Dictionary<string, object?>();
        AddIfPresent(fields, "size_from", sizeFrom);
        AddIfPresent(fields, "size_to", sizeTo);
        return new Selector("sizeSelector", fields);
    }

    /// <summary>Match documents by page-count range (pass at least one bound).</summary>
    public static Selector PageCount(int? pageCountFrom = null, int? pageCountTo = null)
    {
        if (pageCountFrom is null && pageCountTo is null)
        {
            throw new ArgumentException("PageCount requires pageCountFrom and/or pageCountTo", nameof(pageCountFrom));
        }

        var fields = new Dictionary<string, object?>();
        AddIfPresent(fields, "page_count_from", pageCountFrom);
        AddIfPresent(fields, "page_count_to", pageCountTo);
        return new Selector("pageCountSelector", fields);
    }

    /// <summary>Match splits derived from the given source document ids.</summary>
    public static Selector SourceDocument(IEnumerable<long> ids) =>
        Make("sourceDocumentSelector", ("source_documents", ids.ToList()));

    /// <summary>Match documents in a project, optionally filtered by when they were added (ISO-8601).</summary>
    public static Selector Project(long projectId, string? addedFrom = null, string? addedTo = null)
    {
        var fields = new Dictionary<string, object?> { ["project_id"] = projectId };
        AddIfPresent(fields, "added_from", addedFrom);
        AddIfPresent(fields, "added_to", addedTo);
        return new Selector("projectSelector", fields);
    }

    /// <summary>Match documents touched by the given job ids.</summary>
    public static Selector Job(IEnumerable<long> ids) =>
        Make("jobSelector", ("jobs", ids.ToList()));

    /// <summary>Flag selector: valid documents (use in <c>exclude</c> for invalid).</summary>
    public static Selector IsValid() => Make("isValidSelector");

    /// <summary>Flag selector: documents that have child splits.</summary>
    public static Selector HasChildren() => Make("hasChildrenSelector");

    /// <summary>Flag selector: documents with a running workflow.</summary>
    public static Selector HasRunningWorkflow() => Make("hasRunningWorkflowSelector");

    /// <summary>
    /// Flag selector: documents with a classification result (use in <c>exclude</c> for
    /// the unclassified ones).
    /// </summary>
    public static Selector HasClassification() => Make("hasClassificationSelector");

    /// <summary>
    /// Flag selector: documents that have run classification (use in <c>exclude</c> for
    /// those that never ran a classification job).
    /// </summary>
    public static Selector HasClassificationWorkflow() => Make("hasClassificationWorkflowSelector");

    /// <summary>
    /// Forward-compat escape hatch: build a selector of any <paramref name="type"/> with raw
    /// <paramref name="fields"/>, for a selector this SDK build does not yet model.
    /// </summary>
    public static Selector Raw(string type, IDictionary<string, object?> fields) =>
        new(type, new Dictionary<string, object?>(fields));

    private static Selector Make(string type, params (string Key, object? Value)[] fields)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var (key, value) in fields)
        {
            dict[key] = value;
        }

        return new Selector(type, dict);
    }

    private static void AddIfPresent(Dictionary<string, object?> fields, string key, object? value)
    {
        if (value is not null)
        {
            fields[key] = value;
        }
    }
}

/// <summary>An include/exclude set of <see cref="Selector"/>s (include = AND, exclude = NOT).</summary>
[JsonConverter(typeof(SelectorsJsonConverter))]
public sealed class Selectors
{
    private Selectors(IReadOnlyList<Selector> include, IReadOnlyList<Selector> exclude)
    {
        Include = include;
        Exclude = exclude;
    }

    /// <summary>Selectors that must all match (AND).</summary>
    public IReadOnlyList<Selector> Include { get; }

    /// <summary>Selectors that exclude matches (NOT).</summary>
    public IReadOnlyList<Selector> Exclude { get; }

    /// <summary>Combine selectors into a Selectors object.</summary>
    public static Selectors Build(IEnumerable<Selector>? include = null, IEnumerable<Selector>? exclude = null) =>
        new(include?.ToList() ?? new List<Selector>(), exclude?.ToList() ?? new List<Selector>());
}

internal sealed class SelectorJsonConverter : JsonConverter<Selector>
{
    public override Selector Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new NotSupportedException("Selectors are write-only.");

    public override void Write(Utf8JsonWriter writer, Selector value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("type", value.Type);
        foreach (var pair in value.Fields)
        {
            writer.WritePropertyName(pair.Key);
            JsonSerializer.Serialize(writer, pair.Value, options);
        }

        writer.WriteEndObject();
    }
}

internal sealed class SelectorsJsonConverter : JsonConverter<Selectors>
{
    public override Selectors Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new NotSupportedException("Selectors are write-only.");

    public override void Write(Utf8JsonWriter writer, Selectors value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("include");
        JsonSerializer.Serialize(writer, value.Include, options);
        writer.WritePropertyName("exclude");
        JsonSerializer.Serialize(writer, value.Exclude, options);
        writer.WriteEndObject();
    }
}
