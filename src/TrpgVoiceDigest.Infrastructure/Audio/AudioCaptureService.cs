using System.Diagnostics;
using System.Globalization;
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

        // Write to temp file first, atomically rename to .wav when complete.
        // Prevents the transcribe worker from picking up an incomplete file.
        var tempPath = outputWavPath + ".tmp";

        var args =
            $"-y -f {config.InputFormat} -i {inputDevice} -t {durationSeconds.ToString("0.###", CultureInfo.InvariantCulture)} -ac {config.Channels} -ar {config.SampleRate} -f wav \"{tempPath}\"";
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

        try
        {
            process.Start();
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
            {
                var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
                throw new InvalidOperationException($"音频录制失败: {stderr}");
            }

            File.Move(tempPath, outputWavPath);
        }
        catch
        {
            try
            {
                File.Delete(tempPath);
            }
            catch
            {
                /* best effort */
            }

            throw;
        }
    }

    public Process StartMeterStream(AudioConfig config, string inputDevice)
    {
        var args =
            $"-hide_banner -loglevel error -f {config.InputFormat} -i {inputDevice} -ac 1 -ar {config.SampleRate} -f s16le pipe:1";
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