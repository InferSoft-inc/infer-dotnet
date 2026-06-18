namespace Infersoft;

/// <summary>How <c>Documents.Upload</c> reacts when the server flags a file as a content duplicate.</summary>
public enum OnDuplicate
{
    /// <summary>Upload it anyway (the matches are still reported on <see cref="FileOutcome.Duplicates"/>).</summary>
    Allow,

    /// <summary>Upload it but log a warning naming the matched document(s).</summary>
    Notify,

    /// <summary>Skip the byte upload and delete the placeholder document the plan created.</summary>
    Block,
}
