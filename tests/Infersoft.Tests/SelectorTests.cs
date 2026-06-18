using System;
using System.Collections.Generic;
using System.Text.Json;
using Infersoft;
using Infersoft.Internal;
using Xunit;

namespace Infersoft.Tests;

public class SelectorTests
{
    private static JsonElement Serialize(object value) =>
        JsonDocument.Parse(JsonSerializer.Serialize(value, InfersoftJson.Options)).RootElement;

    [Fact]
    public void File_selector_emits_type_and_files()
    {
        var element = Serialize(Selector.File(new long[] { 1, 2 }));

        Assert.Equal("fileSelector", element.GetProperty("type").GetString());
        Assert.Equal(2, element.GetProperty("files").GetArrayLength());
    }

    [Fact]
    public void Selectors_build_emits_include_and_exclude()
    {
        var selectors = Selectors.Build(
            include: new[] { Selector.Folder(3), Selector.CreatedAt(createdFrom: "2026-01-01") },
            exclude: new[] { Selector.Name("draft") });

        var element = Serialize(selectors);

        Assert.Equal(2, element.GetProperty("include").GetArrayLength());
        Assert.Equal(1, element.GetProperty("exclude").GetArrayLength());

        var folder = element.GetProperty("include")[0];
        Assert.Equal("folderSelector", folder.GetProperty("type").GetString());
        Assert.Equal(3, folder.GetProperty("folder_id").GetInt64());

        var createdAt = element.GetProperty("include")[1];
        Assert.Equal("createdAtSelector", createdAt.GetProperty("type").GetString());
        Assert.Equal("2026-01-01", createdAt.GetProperty("created_from").GetString());
    }

    [Fact]
    public void Raw_selector_emits_arbitrary_type_and_fields()
    {
        var element = Serialize(Selector.Raw("customSelector", new Dictionary<string, object?> { ["foo"] = 1 }));

        Assert.Equal("customSelector", element.GetProperty("type").GetString());
        Assert.Equal(1, element.GetProperty("foo").GetInt32());
    }

    [Fact]
    public void Range_selectors_require_at_least_one_bound()
    {
        Assert.Throws<ArgumentException>(() => Selector.CreatedAt());
        Assert.Throws<ArgumentException>(() => Selector.Size());
        Assert.Throws<ArgumentException>(() => Selector.PageCount());
    }

    [Fact]
    public void Resolve_rejects_both_inputs()
    {
        Assert.Throws<ArgumentException>(
            () => SelectorResolver.Resolve(Selectors.Build(), new long[] { 1 }, required: false));
    }

    [Fact]
    public void Resolve_wraps_document_ids_into_a_file_selector()
    {
        var resolved = SelectorResolver.Resolve(null, new long[] { 1, 2 }, required: true)!;
        var element = Serialize(resolved);

        Assert.Equal("fileSelector", element.GetProperty("include")[0].GetProperty("type").GetString());
        Assert.Equal(2, element.GetProperty("include")[0].GetProperty("files").GetArrayLength());
    }

    [Fact]
    public void Resolve_requires_a_target_when_required()
    {
        Assert.Throws<ArgumentException>(() => SelectorResolver.Resolve(null, null, required: true));
        Assert.Null(SelectorResolver.Resolve(null, null, required: false));
    }
}
