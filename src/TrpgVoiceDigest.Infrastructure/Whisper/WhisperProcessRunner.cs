using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;
using TrpgVoiceDigest.Core.Config;
using TrpgVoiceDigest.Core.Models;
using TrpgVoiceDigest.Core.Services;
using TrpgVoiceDigest.Infrastructure.Services;

namespace TrpgVoiceDigest.Infrastructure.Whisper;

public sealed class WhisperProcessRunner
{
    private readonly ILogService? _logService;

    public WhisperProcessRunner(ILogService? logService = null)
    {
        _logService = logService;
    }

    public async Task<IReadOnlyList<TranscriptSegment>> TranscribeAsync(
        WhisperConfig config,
        string wavPath,
        CancellationToken cancellationToken)
    {
        _logService?.Info($"启动 Whisper 转录: {Path.GetFileName(wavPath)} (模型={config.Model}, 语言={config.Language}, 说话者分离={config.DiarizationEnabled})");

        var resolvedScriptPath = ApplicationPathResolver.ResolvePythonScript(config.ScriptPath);
        var resolvedPythonExecutable = ApplicationPathResolver.ResolvePythonExecutable(config.PythonExecutable);

        var args =
            $"\"{resolvedScriptPath}\" --audio \"{wavPath}\" --model \"{config.Model}\" --language \"{config.Language}\"";
        if (!string.IsNullOrWhiteSpace(config.InitialPrompt))
            args += $" --initial-prompt \"{EscapeArg(config.InitialPrompt)}\"";
        args += $" --device \"{config.Device}\" --compute-type \"{config.ComputeType}\"";
        if (config.DiarizationEnabled)
            args += " --diarize";
        if (!string.IsNullOrWhiteSpace(config.HuggingFaceTokenEnv))
        {
            var token = Environment.GetEnvironmentVariable(config.HuggingFaceTokenEnv);
            if (!string.IsNullOrWhiteSpace(token))
                args += $" --hf-token \"{EscapeArg(token)}\"";
        }
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = resolvedPythonExecutable,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };

        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await Task.WhenAll(stdoutTask, stderrTask);
        var stdout = stdoutTask.Result;
        var stderr = stderrTask.Result;
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0) throw new InvalidOperationException($"Whisper 转录失败: {stderr}");

        var payload = JsonSerializer.Deserialize<WhisperResponse>(stdout, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (payload?.Segments is null)
        {
            _logService?.Debug($"Whisper 转录无有效结果: {Path.GetFileName(wavPath)}");
            return [];
        }

        var result = payload.Segments
            .Where(x => !string.IsNullOrWhiteSpace(x.Text))
            .Select(x => new TranscriptSegment(
                TimeSpan.FromSeconds(x.Start),
                TimeSpan.FromSeconds(x.End),
                x.Text.Trim(),
                x.Speaker))
            .ToList();
        _logService?.Info($"Whisper 转录完成: {Path.GetFileName(wavPath)} → {result.Count} 句");
        return result;
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
        public string? Speaker { get; set; }
    }
}