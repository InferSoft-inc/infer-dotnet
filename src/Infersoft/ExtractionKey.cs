namespace Infersoft;

/// <summary>How to key extraction values when flattening (see <c>Documents.GetValues</c>).</summary>
public enum ExtractionKey
{
    /// <summary>Key by the prompt's value name, falling back to the prompt id when unnamed.</summary>
    Name,

    /// <summary>Key by the numeric prompt id.</summary>
    PromptId,
}
