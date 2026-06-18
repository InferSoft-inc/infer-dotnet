using System.Reflection;

namespace Infersoft.Internal;

/// <summary>SDK version and User-Agent, read once from the assembly informational version.</summary>
internal static class SdkInfo
{
    public static string Version { get; } = ComputeVersion();

    public static string UserAgent { get; } = "infersoft-dotnet/" + Version;

    private static string ComputeVersion()
    {
        var informational = typeof(SdkInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrEmpty(informational))
        {
            return "0.0.0";
        }

        // Strip any "+<build metadata>" (e.g. a SourceLink commit hash).
        var plus = informational!.IndexOf('+');
        return plus >= 0 ? informational.Substring(0, plus) : informational;
    }
}
