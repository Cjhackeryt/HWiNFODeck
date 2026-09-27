using System.Diagnostics;
using System.Reflection;
using MacroDeck.Sdk.Variables;

namespace HWiNFODeck;

internal static class MacroDeckHostInfo
{
    private const string HostProcessIdVariable = "MACRO_DECK_PLUGIN_HOST_PROCESS_ID";
    private const string HostStartedAtVariable = "MACRO_DECK_PLUGIN_HOST_STARTED_AT";
    private static string? _hostVersion;

    public static string? MacroDeckVersion
    {
        get
        {
            _hostVersion ??= DetectMacroDeckVersion();
            return _hostVersion;
        }
    }

    public static string SdkVersion =>
        typeof(VariableDefinition).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            .Split('+')[0]
        ?? "unknown";

    private static string? DetectMacroDeckVersion()
    {
        if (!int.TryParse(Environment.GetEnvironmentVariable(HostProcessIdVariable), out var hostPid))
            return null;

        try
        {
            using var process = Process.GetProcessById(hostPid);
            if (process.HasExited)
                return null;

            if (DateTimeOffset.TryParse(
                    Environment.GetEnvironmentVariable(HostStartedAtVariable),
                    out var startedAt) &&
                Math.Abs((process.StartTime - startedAt.ToLocalTime()).TotalSeconds) > 10)
                return null;

            var fileName = process.MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(fileName))
                return null;

            var versionInfo = FileVersionInfo.GetVersionInfo(fileName);
            var version = versionInfo.ProductVersion ?? versionInfo.FileVersion;
            return string.IsNullOrWhiteSpace(version) ? null : CleanVersion(version);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string CleanVersion(string version)
    {
        var plus = version.IndexOf('+');
        return plus > 0 ? version[..plus] : version;
    }
}