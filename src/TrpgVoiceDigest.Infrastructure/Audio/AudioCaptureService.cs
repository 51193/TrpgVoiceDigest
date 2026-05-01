using System.Diagnostics;
using TrpgVoiceDigest.Core.Config;

namespace TrpgVoiceDigest.Infrastructure.Audio;

public sealed class AudioCaptureService
{
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
}