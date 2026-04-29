using System.Diagnostics;
using TrpgVoiceDigest.Core.Config;

namespace TrpgVoiceDigest.Infrastructure.Audio;

public sealed class AudioCaptureService
{
    public async Task CaptureSegmentAsync(AudioConfig config, string outputWavPath, CancellationToken cancellationToken)
    {
        await CaptureForDurationAsync(config, outputWavPath, config.SegmentSeconds, cancellationToken);
    }

    private async Task CaptureForDurationAsync(
        AudioConfig config,
        string outputWavPath,
        double durationSeconds,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputWavPath)!);
        var inputDevice = ResolveInputDevice(config);

        var args =
            $"-y -f {config.InputFormat} -i {inputDevice} -t {durationSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)} -ac {config.Channels} -ar {config.SampleRate} \"{outputWavPath}\"";
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = config.RecorderExecutable,
                Arguments = args,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            }
        };

        process.Start();
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
            throw new InvalidOperationException($"音频录制失败: {stderr}");
        }
    }

    public Process StartMeterStream(AudioConfig config, string inputDevice)
    {
        var args = $"-hide_banner -loglevel error -f {config.InputFormat} -i {inputDevice} -ac 1 -ar {config.SampleRate} -f s16le pipe:1";
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = config.RecorderExecutable,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };
        process.Start();
        return process;
    }

    private static string ResolveInputDevice(AudioConfig config)
    {
        return PlatformAudioInputDiscovery.CreateDefault()
            .Resolve(config)
            .EffectiveInputDevice;
    }
}
