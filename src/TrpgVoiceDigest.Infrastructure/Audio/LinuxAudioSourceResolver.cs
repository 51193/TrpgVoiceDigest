using System.Diagnostics;
using System.Text.RegularExpressions;

namespace TrpgVoiceDigest.Infrastructure.Audio;

public static class LinuxAudioSourceResolver
{
    public sealed record ResolveResult(string EffectiveInputDevice, string Strategy, string? DefaultSink);

    private static readonly Regex DefaultSinkRegex = new(@"^Default Sink:\s*(?<sink>\S+)\s*$", RegexOptions.Multiline | RegexOptions.Compiled);

    public static string ResolveInputDevice(string configuredInputDevice)
    {
        return Resolve(configuredInputDevice).EffectiveInputDevice;
    }

    public static ResolveResult Resolve(string configuredInputDevice)
    {
        if (!NeedsAutoResolve(configuredInputDevice))
        {
            return new ResolveResult(configuredInputDevice, "user_configured", null);
        }

        var sources = GetAvailableSources();
        var defaultSink = GetDefaultSink();
        if (!string.IsNullOrWhiteSpace(defaultSink))
        {
            var defaultMonitor = $"{defaultSink}.monitor";
            if (sources.Any(x => string.Equals(x, defaultMonitor, StringComparison.OrdinalIgnoreCase)))
            {
                return new ResolveResult(defaultMonitor, "default_sink_monitor", defaultSink);
            }
        }

        var fallback = sources.FirstOrDefault(x => x.Contains(".monitor", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(fallback))
        {
            return new ResolveResult(fallback, "first_monitor_fallback", defaultSink);
        }

        return new ResolveResult(configuredInputDevice, "configured_or_unresolved", defaultSink);
    }

    public static IReadOnlyList<string> GetAvailableSources()
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "pactl",
                    Arguments = "list sources short",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                }
            };
            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return ParseSourcesOutput(output);
        }
        catch
        {
            return [];
        }
    }

    public static List<string> ParseSourcesOutput(string output)
    {
        var result = new List<string>();
        var lines = output.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var cols = line.Split('\t', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (cols.Length >= 2)
            {
                result.Add(cols[1]);
            }
        }

        return result;
    }

    public static string? GetDefaultSink()
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "pactl",
                    Arguments = "info",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                }
            };
            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return ParseDefaultSink(output);
        }
        catch
        {
            return null;
        }
    }

    public static string? ParseDefaultSink(string pactlInfoOutput)
    {
        if (string.IsNullOrWhiteSpace(pactlInfoOutput))
        {
            return null;
        }

        var match = DefaultSinkRegex.Match(pactlInfoOutput);
        if (!match.Success)
        {
            return null;
        }

        var sink = match.Groups["sink"].Value.Trim();
        return string.IsNullOrWhiteSpace(sink) ? null : sink;
    }

    private static bool NeedsAutoResolve(string configuredInputDevice) =>
        string.IsNullOrWhiteSpace(configuredInputDevice) ||
        configuredInputDevice.Equals("default", StringComparison.OrdinalIgnoreCase);
}
