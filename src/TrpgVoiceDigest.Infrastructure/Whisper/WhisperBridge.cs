using System.Diagnostics;
using System.Text.Json;
using TrpgVoiceDigest.Core.Config;
using TrpgVoiceDigest.Core.Models;

namespace TrpgVoiceDigest.Infrastructure.Whisper;

public sealed class WhisperBridge
{
    public async Task<IReadOnlyList<TranscriptSegment>> TranscribeAsync(
        WhisperConfig config,
        string wavPath,
        CancellationToken cancellationToken)
    {
        var args = $"\"{config.ScriptPath}\" --audio \"{wavPath}\" --model \"{config.Model}\" --language \"{config.Language}\"";
        if (!string.IsNullOrWhiteSpace(config.InitialPrompt))
        {
            args += $" --initial-prompt \"{EscapeArg(config.InitialPrompt)}\"";
        }
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = config.PythonExecutable,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };

        process.Start();
        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Whisper 转录失败: {stderr}");
        }

        var payload = JsonSerializer.Deserialize<WhisperResponse>(stdout, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (payload?.Segments is null)
        {
            return [];
        }

        return payload.Segments
            .Where(x => !string.IsNullOrWhiteSpace(x.Text))
            .Select(x => new TranscriptSegment(
                TimeSpan.FromSeconds(x.Start),
                TimeSpan.FromSeconds(x.End),
                x.Text.Trim()))
            .ToList();
    }

    private static string EscapeArg(string input)
    {
        return input.Replace("\"", "\\\"");
    }

    private sealed class WhisperResponse
    {
        public List<WhisperSegment>? Segments { get; set; }
    }

    private sealed class WhisperSegment
    {
        public double Start { get; set; }
        public double End { get; set; }
        public string Text { get; set; } = string.Empty;
    }
}
