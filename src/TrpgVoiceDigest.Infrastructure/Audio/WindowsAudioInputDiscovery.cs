using System.Text.RegularExpressions;
using CliWrap;
using CliWrap.Buffered;
using TrpgVoiceDigest.Core.Config;

namespace TrpgVoiceDigest.Infrastructure.Audio;

public sealed class WindowsAudioInputDiscovery : IAudioInputDiscovery
{
    private static readonly Regex DeviceLineRegex = new("\"(?<name>[^\"]+)\"", RegexOptions.Compiled);

    public IReadOnlyList<string> GetAvailableSources(AudioConfig config)
    {
        if (!config.InputFormat.Equals("dshow", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var output = RunDeviceListing(config.RecorderExecutable);
        return output is null ? [] : ParseDshowDevices(output);
    }

    public AudioInputResolveResult Resolve(AudioConfig config)
    {
        if (!config.InputFormat.Equals("dshow", StringComparison.OrdinalIgnoreCase))
        {
            return new AudioInputResolveResult(config.InputDevice, "non_dshow_passthrough");
        }

        if (!NeedsAutoResolve(config.InputDevice))
        {
            return new AudioInputResolveResult(config.InputDevice, "user_configured");
        }

        var devices = GetAvailableSources(config);
        var first = devices.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(first))
        {
            return new AudioInputResolveResult($"audio={first}", "first_dshow_device");
        }

        return new AudioInputResolveResult(config.InputDevice, "configured_or_unresolved");
    }

    internal static List<string> ParseDshowDevices(string stderrOutput)
    {
        var devices = new List<string>();
        var lines = stderrOutput.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            if (!line.Contains("DirectShow audio devices", StringComparison.OrdinalIgnoreCase)
                && !line.Contains("dshow", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var match = DeviceLineRegex.Match(line);
            if (!match.Success)
            {
                continue;
            }

            var name = match.Groups["name"].Value.Trim();
            if (!string.IsNullOrWhiteSpace(name) && !name.StartsWith('@'))
            {
                devices.Add(name);
            }
        }

        return devices;
    }

    private static bool NeedsAutoResolve(string configuredInputDevice) =>
        AudioConfig.IsDefaultDevice(configuredInputDevice);

    private static string? RunDeviceListing(string ffmpegExecutable)
    {
        try
        {
            var result = Cli.Wrap(ffmpegExecutable)
                .WithArguments(["-hide_banner", "-list_devices", "true", "-f", "dshow", "-i", "dummy"])
                .WithValidation(CommandResultValidation.None)
                .ExecuteBufferedAsync()
                .GetAwaiter()
                .GetResult();

            return string.Concat(result.StandardOutput, "\n", result.StandardError);
        }
        catch
        {
            return null;
        }
    }
}
