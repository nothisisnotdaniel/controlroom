using System.Reflection;

namespace ControlRoom.Server;

/// <summary>The running version: the assembly's informational version without any <c>+build</c> part, or
/// <c>dev</c> for a build without <c>-p:Version</c>.</summary>
internal static class AppVersion
{
    public const string Dev = "dev";

    public static string Current { get; } = FromMetadata(
        typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    /// <summary>Maps the informational version to the shown version: <c>1.2.3+abc</c> to <c>1.2.3</c>, a missing
    /// or empty value to <c>dev</c>.</summary>
    public static string FromMetadata(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return Dev;
        }

        int plus = value.IndexOf('+', StringComparison.Ordinal);
        string version = plus >= 0 ? value[..plus] : value;
        return version.Length == 0 ? Dev : version;
    }
}
