using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Infersoft.Internal;

/// <summary>Flattens a document's extraction items to <c>{field: typed value}</c> (see <see cref="ExtractionResultValue.Value"/>).</summary>
internal static class ExtractionFlattener
{
    public static IReadOnlyDictionary<object, object?> Flatten(DocumentSummary document, ExtractionKey keyBy)
    {
        var row = new Dictionary<object, object?>();
        foreach (var item in document.ExtractionResults ?? Array.Empty<DocumentExtractionResultItem>())
        {
            object key = keyBy == ExtractionKey.Name && !string.IsNullOrEmpty(item.Value.Name)
                ? item.Value.Name!
                : item.PromptId;

            if (row.ContainsKey(key))
            {
                throw new ArgumentException(
                    $"two prompts share the extraction key '{key}' on document {document.Id}; " +
                    "pass keyBy: ExtractionKey.PromptId to disambiguate",
                    nameof(keyBy));
            }

            row[key] = item.Value.Value;
        }

        return row;
    }
}
