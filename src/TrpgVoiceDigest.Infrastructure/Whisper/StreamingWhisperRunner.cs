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

        if (config.SkipAlign)
        {
            args += " --skip-align";
        }

        _logService?.Info($"启动流式转录: 模型={config.Model}, device={config.Device}, 说话者分离={config.DiarizationEnabled}, EOU={segConfig.EndOfUtteranceEnabled}, 跳过对齐={config.SkipAlign}");

        var resolvedFfmpeg = ApplicationPathResolver.ResolveRecorderExecutable(audioConfig.RecorderExecutable);

        var ffmpegArgs =
            $"-hide_banner -nostats -loglevel error -f {audioConfig.InputFormat} -i {inputDevice} -ac 1 -ar {audioConfig.SampleRate} -f s16le pipe:1";

        _logService?.Debug($"ffmpeg: {resolvedFfmpeg} {ffmpegArgs}");

        var resolvedToken = _environmentKeyResolver.Resolve(config.HuggingFaceTokenEnv);
        var logSafeArgs = string.IsNullOrWhiteSpace(resolvedToken) ? args
            : args.Replace($"\"{EscapeArg(resolvedToken)}\"", "\"***\"");
        _logService?.Debug($"python: {resolvedPythonExecutable} {logSafeArgs}");

        var isWindows = OperatingSystem.IsWindows();
        var shellName = isWindows ? "cmd.exe" : "/bin/bash";
        var shellFlag = isWindows ? "/c" : "-c";
        var escapedFfmpegPath = isWindows ? EscapeCmdArg(resolvedFfmpeg) : EscapeBashArg(resolvedFfmpeg);
        var escapedPythonPath = isWindows ? EscapeCmdArg(resolvedPythonExecutable) : EscapeBashArg(resolvedPythonExecutable);
        var shellArgs = $"{shellFlag} \"{escapedFfmpegPath} {ffmpegArgs} | {escapedPythonPath} {args}\"";

        _pythonProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = shellName,
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
        _pythonProcess.StartInfo.Environment["PYTORCH_CUDA_ALLOC_CONF"] = "expandable_segments:True";

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
                var line = await stderr.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null) break;
                if (StderrFilterPatterns.Any(p => line.Contains(p)))
                    continue;

                if (line.Contains("CUDA", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("GPU", StringComparison.OrdinalIgnoreCase))
                {
                    _logService?.Warning($"stream stderr: {line}");
                    OnStatus?.Invoke(line);
                }
                else
                {
                    _logService?.Debug($"stream stderr: {line}");
                }
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

                    var seq = root.TryGetProperty("seq", out var seqProp) ? seqProp.GetInt32() : -1;
                    var statusMsg = $"转录完成: seq={seq}, {segmentsProp.GetArrayLength()} 句";
                    if (root.TryGetProperty("timing", out var timingProp))
                    {
                        var total = timingProp.TryGetProperty("total", out var tp) ? tp.GetDouble() : 0;
                        var audioDur = timingProp.TryGetProperty("audio_duration", out var adp) ? adp.GetDouble() : 0;
                        var segTotal = timingProp.TryGetProperty("segment_total", out var stp) ? stp.GetDouble() : 0;
                        var ratio = audioDur > 0 ? segTotal / audioDur : 0;
                        var diarize = timingProp.TryGetProperty("diarize", out var dp) ? dp.GetDouble() : 0;
                        var match = timingProp.TryGetProperty("match", out var mp) ? mp.GetDouble() : 0;
                        var transcribe = timingProp.TryGetProperty("transcribe", out var trp) ? trp.GetDouble() : 0;
                        var align = timingProp.TryGetProperty("align", out var ap) ? ap.GetDouble() : 0;
                        _logService?.Info(
                            $"⏱ seq={seq}: 总计={segTotal:F2}s 音频={audioDur:F1}s 倍率={ratio:F1}x " +
                            $"(分离={diarize:F2}s 匹配={match:F2}s 转录={transcribe:F2}s 对齐={align:F2}s)");
                        statusMsg = $"转录完成: seq={seq}, {segmentsProp.GetArrayLength()}句 "
                                  + $"(⏱ {segTotal:F1}s/{audioDur:F1}s={ratio:F1}x)";
                    }
                    OnStatus?.Invoke(statusMsg);
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
                try
                {
                    // Send SIGTERM first (graceful), then SIGKILL if still running
                    _pythonProcess.Kill(true);
                }
                catch { }

                var waited = false;
                try
                {
                    waited = _pythonProcess.WaitForExit(8000);
                }
                catch { }

                if (!waited)
                {
                    try
                    {
                        _pythonProcess.Kill();
                        _pythonProcess.WaitForExit(3000);
                    }
                    catch { }
                }
            }
        }
        catch { }

        // Also kill orphan ffmpeg processes that might be capturing from pulse monitor
        try
        {
            var orphanFfmpegs = System.Diagnostics.Process.GetProcessesByName("ffmpeg")
                .Where(p => p.StartTime < DateTime.Now.AddMinutes(-1));
            foreach (var ff in orphanFfmpegs)
            {
                try { ff.Kill(true); } catch { }
            }
        }
        catch { }

        _stdout?.Dispose();
        _pythonProcess?.Dispose();
        _cts?.Dispose();
        _logService?.Info("流式转录已停止");
    }

    private static string EscapeArg(string input) => input.Replace("\"", "\\\"");

    private static string EscapeBashArg(string input)
    {
        return input.Contains(' ') || input.Contains('"') ? $"\"{input.Replace("\"", "\\\"")}\"" : input;
    }

    private static string EscapeCmdArg(string input)
    {
        return input.Contains(' ') ? $"\"{input}\"" : input;
    }
}
