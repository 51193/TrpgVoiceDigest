using System.Diagnostics;
using System.Text.Json;
using TrpgVoiceDigest.Core.Config;
using TrpgVoiceDigest.Core.Models;
using TrpgVoiceDigest.Core.Services;
using TrpgVoiceDigest.Infrastructure.Llm;
using TrpgVoiceDigest.Infrastructure.Services;

namespace TrpgVoiceDigest.Infrastructure.Whisper;

public sealed class WhisperProcessRunner : IAsyncDisposable
{
    private readonly ILogService? _logService;
    private readonly IEnvironmentKeyResolver _environmentKeyResolver;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private Process? _serverProcess;
    private StreamWriter? _stdin;
    private StreamReader? _stdout;
    private StreamReader? _stderrReader;
    private string? _resolvedPythonExecutable;
    private string? _resolvedScriptPath;
    private WhisperConfig? _currentConfig;

    public WhisperProcessRunner(ILogService? logService = null, IEnvironmentKeyResolver? environmentKeyResolver = null)
    {
        _logService = logService;
        _environmentKeyResolver = environmentKeyResolver ?? new PlatformEnvironmentKeyResolver();
    }

    public async Task<IReadOnlyList<TranscriptSegment>> TranscribeAsync(
        WhisperConfig config,
        string wavPath,
        CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            await EnsureServerStarted(config, cancellationToken);
            return await TranscribeViaServer(wavPath, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task EnsureServerStarted(WhisperConfig config, CancellationToken cancellationToken)
    {
        if (_serverProcess is { HasExited: false })
            return;

        var resolvedScriptPath = ApplicationPathResolver.ResolvePythonScript(config.ScriptPath);
        var resolvedPythonExecutable = ApplicationPathResolver.ResolvePythonExecutable(config.PythonExecutable);
        _resolvedPythonExecutable = resolvedPythonExecutable;
        _resolvedScriptPath = resolvedScriptPath;
        _currentConfig = config;

        var args = $"\"{resolvedScriptPath}\" --model \"{config.Model}\" --language \"{config.Language}\"";
        if (!string.IsNullOrWhiteSpace(config.InitialPrompt))
            args += $" --initial-prompt \"{EscapeArg(config.InitialPrompt)}\"";
        args += $" --device \"{config.Device}\" --compute-type \"{config.ComputeType}\"";

        if (config.DiarizationEnabled)
        {
            args += " --diarize";
            var token = _environmentKeyResolver.Resolve(config.HuggingFaceTokenEnv);
            if (!string.IsNullOrWhiteSpace(token))
            {
                args += $" --hf-token \"{EscapeArg(token)}\"";
                _logService?.Debug($"说话者分离: 已从环境变量 {config.HuggingFaceTokenEnv} 解析到 token");
            }
            else
            {
                _logService?.Warning($"说话者分离已启用但未找到 token: 环境变量 {config.HuggingFaceTokenEnv} 未设置或为空。"
                                     + $" 请设置环境变量或在终端中 export {config.HuggingFaceTokenEnv}=<your_token>");
            }
        }

        args += " --server";

        _logService?.Info($"启动 Whisper 服务器: 模型={config.Model}, 语言={config.Language}, device={config.Device}, 说话者分离={config.DiarizationEnabled}");

        _serverProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = resolvedPythonExecutable,
                Arguments = args,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            },
            EnableRaisingEvents = true
        };

        _serverProcess.Exited += (_, _) =>
        {
            _logService?.Warning("Whisper 服务器进程意外退出");
        };

        _serverProcess.Start();
        _stdin = _serverProcess.StandardInput;
        _stdout = _serverProcess.StandardOutput;
        _stderrReader = _serverProcess.StandardError;

        _ = ReadStderrLoop(cancellationToken);

        _logService?.Info("Whisper 服务器已启动");
    }

    private async Task ReadStderrLoop(CancellationToken cancellationToken)
    {
        try
        {
            while (_stderrReader is not null)
            {
                var line = await _stderrReader.ReadLineAsync(cancellationToken);
                if (line is null) break;
                _logService?.Debug($"Whisper stderr: {line}");
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task<IReadOnlyList<TranscriptSegment>> TranscribeViaServer(
        string wavPath,
        CancellationToken cancellationToken)
    {
        if (_stdin is null || _stdout is null)
            throw new InvalidOperationException("Whisper 服务器未启动");

        _logService?.Info($"Whisper 转录请求: {Path.GetFileName(wavPath)}");

        var request = JsonSerializer.Serialize(new { audio = wavPath });
        await _stdin.WriteLineAsync(request.AsMemory(), cancellationToken);
        await _stdin.FlushAsync(cancellationToken);

        var responseLine = await _stdout.ReadLineAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(responseLine))
            throw new InvalidOperationException("Whisper 服务器返回空响应");

        using var doc = JsonDocument.Parse(responseLine);
        var root = doc.RootElement;

        if (root.TryGetProperty("ok", out var okProp) && !okProp.GetBoolean())
        {
            var error = root.TryGetProperty("error", out var errProp) ? errProp.GetString() : "未知错误";
            throw new InvalidOperationException($"Whisper 转录失败: {error}");
        }

        if (!root.TryGetProperty("segments", out var segmentsProp) || segmentsProp.ValueKind != JsonValueKind.Array)
        {
            _logService?.Debug($"Whisper 转录无有效结果: {Path.GetFileName(wavPath)}");
            return [];
        }

        var result = segmentsProp.EnumerateArray()
            .Select(seg =>
            {
                var text = seg.TryGetProperty("text", out var t) ? t.GetString()?.Trim() : null;
                var start = seg.TryGetProperty("start", out var s) ? s.GetDouble() : 0;
                var end = seg.TryGetProperty("end", out var e) ? e.GetDouble() : 0;
                var speaker = seg.TryGetProperty("speaker", out var sp) ? sp.GetString() : null;
                return new TranscriptSegment(
                    TimeSpan.FromSeconds(start),
                    TimeSpan.FromSeconds(end),
                    text ?? string.Empty,
                    speaker);
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.Text))
            .ToList();

        _logService?.Info($"Whisper 转录完成: {Path.GetFileName(wavPath)} → {result.Count} 句");
        return result;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_stdin is not null && _serverProcess is { HasExited: false })
            {
                await _stdin.WriteLineAsync("{\"action\": \"exit\"}");
                await _stdin.FlushAsync();
            }
        }
        catch
        {
        }

        try
        {
            if (_serverProcess is { HasExited: false })
            {
                if (!_serverProcess.WaitForExit(5000))
                {
                    _logService?.Warning("Whisper 服务器未能在 5 秒内退出，强制终止");
                    _serverProcess.Kill();
                }
            }
        }
        catch
        {
        }

        _stdin?.Dispose();
        _stdout?.Dispose();
        _stderrReader?.Dispose();
        _serverProcess?.Dispose();
        _lock.Dispose();
        _logService?.Info("Whisper 服务器已停止");
    }

    private static string EscapeArg(string input)
    {
        return input.Replace("\"", "\\\"");
    }
}
