using CliWrap;
using CliWrap.Buffered;
using TrpgVoiceDigest.Core.Config;

namespace TrpgVoiceDigest.Infrastructure.Audio;

public sealed class LinuxAudioInputDiscovery : IAudioInputDiscovery
{
    public IReadOnlyList<string> GetAvailableSources(AudioConfig config)
    {
        if (!config.InputFormat.Equals("pulse", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var output = RunCommand("pactl", ["list", "sources", "short"]);
        return output is null ? [] : LinuxAudioSourceResolver.ParseSourcesOutput(output);
    }

    public AudioInputResolveResult Resolve(AudioConfig config)
    {
        if (!config.InputFormat.Equals("pulse", StringComparison.OrdinalIgnoreCase))
        {
            return new AudioInputResolveResult(config.InputDevice, "non_pulse_passthrough");
        }

        var configuredInputDevice = config.InputDevice;
        if (!NeedsAutoResolve(configuredInputDevice))
        {
            return new AudioInputResolveResult(configuredInputDevice, "user_configured");
        }

        var sources = GetAvailableSources(config);
        var defaultSink = GetDefaultSink();
        if (!string.IsNullOrWhiteSpace(defaultSink))
        {
            var monitor = $"{defaultSink}.monitor";
            if (sources.Any(x => string.Equals(x, monitor, StringComparison.OrdinalIgnoreCase)))
            {
                return new AudioInputResolveResult(monitor, "default_sink_monitor");
            }
        }

        var fallback = sources.FirstOrDefault(x => x.Contains(".monitor", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(fallback))
        {
            return new AudioInputResolveResult(fallback, "first_monitor_fallback");
        }

        return new AudioInputResolveResult(configuredInputDevice, "configured_or_unresolved");
    }

    private static string? GetDefaultSink()
    {
        var output = RunCommand("pactl", ["info"]);
        return output is null ? null : LinuxAudioSourceResolver.ParseDefaultSink(output);
    }

    private static bool NeedsAutoResolve(string configuredInputDevice) =>
        string.IsNullOrWhiteSpace(configuredInputDevice) ||
        configuredInputDevice.Equals("default", StringComparison.OrdinalIgnoreCase);

    private static string? RunCommand(string fileName, IReadOnlyList<string> args)
    {
        try
        {
            var result = Cli.Wrap(fileName)
                .WithArguments(args)
                .ExecuteBufferedAsync()
                .GetAwaiter()
                .GetResult();

            return result.ExitCode == 0 ? result.StandardOutput : null;
        }
        catch
        {
            return null;
        }
    }
}
