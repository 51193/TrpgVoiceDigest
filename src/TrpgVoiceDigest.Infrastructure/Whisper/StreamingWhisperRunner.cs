using System.Diagnostics;
using System.Text.Json;
using TrpgVoiceDigest.Core.Config;
using TrpgVoiceDigest.Core.Models;
using TrpgVoiceDigest.Core.Services;
using TrpgVoiceDigest.Infrastructure.Llm;
using TrpgVoiceDigest.Infrastructure.Services;

namespace TrpgVoiceDigest.Infrastructure.Whisper;

public sealed class StreamingWhisperRunner : IAsyncDisposable
{
    private static readonly HashSet<string> StderrFilterPatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        "Lightning automatically upgraded",
        "ReproducibilityWarning",
        "TF32",
        "gradient_checkpointing",
        "degrees of freedom",
        "warnings.warn(",
        "UserWarning",
        "FutureWarning",
        "site-packages",
        "non monotonically increasing dts",
        "invalid dts",
        "Application provided invalid",
    };

    private readonly ILogService? _logService;
    private readonly IEnvironmentKeyResolver _environmentKeyResolver;
    private Process? _pythonProcess;
    private StreamReader? _stdout;
    private CancellationTokenSource? _cts;

    public event Action<TranscriptSegment>? OnTranscript;
    public event Action<string>? OnStatus;
    public event Action<Exception>? OnError;

    public StreamingWhisperRunner(ILogService? logService = null, IEnvironmentKeyResolver? environmentKeyResolver = null)
    {
        _logService = logService;
        _environmentKeyResolver = environmentKeyResolver ?? new PlatformEnvironmentKeyResolver();
    }

    public Process Start(WhisperConfig config, AudioConfig audioConfig, AudioSegmentationConfig segConfig, string inputDevice, string speakerEmbeddingsDirectory)
    {
        var resolvedScriptPath = ApplicationPathResolver.ResolvePythonScript("python/whisper_streaming.py");
        var resolvedPythonExecutable = ApplicationPathResolver.ResolvePythonExecutable(config.PythonExecutable);

        var args = $"\"{resolvedScriptPath}\" --model \"{config.Model}\" --language \"{config.Language}\"";
        if (!string.IsNullOrWhiteSpace(config.InitialPrompt))
            args += $" --initial-prompt \"{EscapeArg(config.InitialPrompt)}\"";
        args += $" --device \"{config.Device}\" --compute-type \"{config.ComputeType}\"";
        args += $" --silence-cut-ms {segConfig.SilenceCutMs}";
        args += $" --max-speech-sec {segConfig.HardMaxSpeechSec.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)}";
        args += $" --min-speech-sec {segConfig.MinSpeechSec.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)}";
        if (segConfig.EndOfUtteranceEnabled)
        {
            args += " --eou";
            args += $" --eou-sensitivity {segConfig.EndOfUtteranceSensitivity.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)}";
        }

        args += $" --speaker-embeddings-dir \"{EscapeArg(speakerEmbeddingsDirectory)}\"";

        if (config.DiarizationEnabled)
        {
            args += " --diarize";
            var token = _environmentKeyResolver.Resolve(config.HuggingFaceTokenEnv);
            if (!string.IsNullOrWhiteSpace(token))
                args += $" --hf-token \"{EscapeArg(token)}\"";
        }

        _logService?.Info($"启动流式转录: 模型={config.Model}, device={config.Device}, 说话者分离={config.DiarizationEnabled}, EOU={segConfig.EndOfUtteranceEnabled}");

        var ffmpegArgs =
            $"-hide_banner -nostats -loglevel error -f {audioConfig.InputFormat} -i {inputDevice} -ac 1 -ar {audioConfig.SampleRate} -f s16le pipe:1";

        _logService?.Debug($"ffmpeg: {audioConfig.RecorderExecutable} {ffmpegArgs}");
        _logService?.Debug($"python: {resolvedPythonExecutable} {args}");

        var shellArgs = $"-c \"{EscapeShellArg(audioConfig.RecorderExecutable)} {ffmpegArgs} | {EscapeShellArg(resolvedPythonExecutable)} {args}\"";

        _pythonProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = shellArgs,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            },
            EnableRaisingEvents = true
        };

        var cacheRoot = Path.Combine(ApplicationPathResolver.AppRoot, "python", "cache");
        _pythonProcess.StartInfo.Environment["TORCH_HOME"] = Path.Combine(cacheRoot, "torch");
        _pythonProcess.StartInfo.Environment["HF_HOME"] = Path.Combine(cacheRoot, "huggingface");
        _pythonProcess.StartInfo.Environment["HUGGINGFACE_HUB_CACHE"] = Path.Combine(cacheRoot, "huggingface", "hub");

        _pythonProcess.Exited += (_, _) =>
        {
            _logService?.Warning("流式转录进程退出");
            _cts?.Cancel();
        };

        _pythonProcess.Start();
        _stdout = _pythonProcess.StandardOutput;
        _cts = new CancellationTokenSource();

        _ = ReadStderrLoop(_pythonProcess.StandardError, _cts.Token);
        _ = ReadStdoutLoop(_cts.Token);

        _logService?.Info("流式转录已启动");
        return _pythonProcess;
    }

    private async Task ReadStderrLoop(StreamReader stderr, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await stderr.ReadLineAsync(cancellationToken);
                if (line is null) break;
                if (StderrFilterPatterns.Any(p => line.Contains(p)))
                    continue;
                _logService?.Debug($"stream stderr: {line}");
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }

    private async Task ReadStdoutLoop(CancellationToken cancellationToken)
    {
        try
        {
            while (_stdout is not null && !cancellationToken.IsCancellationRequested)
            {
                var line = await _stdout.ReadLineAsync(cancellationToken);
                if (line is null) break;

                if (line.Length == 0 || line[0] != '{')
                    continue;

                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("ok", out var okProp) && !okProp.GetBoolean())
                    {
                        var error = root.TryGetProperty("error", out var errProp) ? errProp.GetString() : "未知错误";
                        _logService?.Warning($"流式转录错误: {error}");
                        continue;
                    }

                    if (!root.TryGetProperty("segments", out var segmentsProp) || segmentsProp.ValueKind != JsonValueKind.Array)
                        continue;

                    foreach (var seg in segmentsProp.EnumerateArray())
                    {
                        var text = seg.TryGetProperty("text", out var t) ? t.GetString()?.Trim() : null;
                        if (string.IsNullOrWhiteSpace(text)) continue;

                        var start = seg.TryGetProperty("start", out var s) ? s.GetDouble() : 0;
                        var end = seg.TryGetProperty("end", out var e) ? e.GetDouble() : 0;
                        var speaker = seg.TryGetProperty("speaker", out var sp) ? sp.GetString() : null;

                        var segment = new TranscriptSegment(
                            TimeSpan.FromSeconds(start),
                            TimeSpan.FromSeconds(end),
                            text,
                            speaker);

                        OnTranscript?.Invoke(segment);
                    }

                    OnStatus?.Invoke($"转录完成: {segmentsProp.GetArrayLength()} 句");
                }
                catch (JsonException ex)
                {
                    _logService?.Debug($"流式转录 JSON 解析失败: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (Exception ex)
        {
            _logService?.Warning($"流式转录 stdout 读取异常: {ex.Message}");
            OnError?.Invoke(ex);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();

        try
        {
            if (_pythonProcess is { HasExited: false })
            {
                _pythonProcess.Kill(true);
                await Task.Run(() => _pythonProcess.WaitForExit(5000));
            }
        }
        catch { }

        _stdout?.Dispose();
        _pythonProcess?.Dispose();
        _cts?.Dispose();
        _logService?.Info("流式转录已停止");
    }

    private static string EscapeArg(string input) => input.Replace("\"", "\\\"");

    private static string EscapeShellArg(string input)
    {
        return input.Contains(' ') || input.Contains('"') ? $"\"{input.Replace("\"", "\\\"")}\"" : input;
    }
}
