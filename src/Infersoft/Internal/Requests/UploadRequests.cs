using System;
using System.Collections.Generic;

namespace Infersoft.Internal;

internal sealed class UploadFileRequest
{
    public string FileName { get; set; } = "";

    public string ContentType { get; set; } = "";

    public long Size { get; set; }

    public string Md5 { get; set; } = "";
}

internal sealed class BatchUploadRequest
{
    public IReadOnlyList<UploadFileRequest> Files { get; set; } = Array.Empty<UploadFileRequest>();

    public long? ProjectId { get; set; }

    public string? ProjectName { get; set; }

    public IReadOnlyList<long>? TagIds { get; set; }

    public IReadOnlyList<string>? TagNames { get; set; }
}
