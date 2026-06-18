using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Infersoft;

/// <summary>
/// Base for response models. Unknown server fields are preserved in <see cref="AdditionalData"/>
/// (the forward-compatibility posture: additive server changes never break an older build and
/// stay reachable until a release types them).
/// </summary>
public abstract class InfersoftModel
{
    /// <summary>Fields returned by the server that this SDK build does not (yet) model.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }
}
