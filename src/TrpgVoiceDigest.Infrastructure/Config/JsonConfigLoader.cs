using System.Text.Json;
using TrpgVoiceDigest.Core.Config;

namespace TrpgVoiceDigest.Infrastructure.Config;

public static class JsonConfigLoader
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true
    };

    public static AppConfig Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("未找到配置文件", path);
        }

        var json = File.ReadAllText(path);
        var config = JsonSerializer.Deserialize<AppConfig>(json, ReadOptions) ?? new AppConfig();
        ApplyLegacyAsrToWhisper(json, config);
        return config;
    }

    /// <summary>
    /// 旧版 JSON 使用根节点 <c>Asr</c>（FunASR 等）；当前模型为 <c>Whisper</c>。将仍存在的 Asr 字段合并进 Whisper，避免配置被静默忽略。
    /// </summary>
    private static void ApplyLegacyAsrToWhisper(string json, AppConfig config)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("Asr", out var asr) || asr.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        static string? ReadString(JsonElement parent, string name)
        {
            if (!parent.TryGetProperty(name, out var el))
            {
                return null;
            }

            return el.ValueKind switch
            {
                JsonValueKind.String => el.GetString(),
                JsonValueKind.Number => el.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => null
            };
        }

        var py = ReadString(asr, "PythonExecutable");
        if (!string.IsNullOrWhiteSpace(py))
        {
            config.Whisper.PythonExecutable = py;
        }

        var script = ReadString(asr, "ScriptPath");
        if (!string.IsNullOrWhiteSpace(script))
        {
            config.Whisper.ScriptPath = script;
        }

        var model = ReadString(asr, "Model");
        if (!string.IsNullOrWhiteSpace(model))
        {
            config.Whisper.Model = model;
        }

        var lang = ReadString(asr, "Language");
        if (!string.IsNullOrWhiteSpace(lang))
        {
            config.Whisper.Language = lang;
        }

        var prompt = ReadString(asr, "InitialPrompt");
        if (prompt is not null)
        {
            config.Whisper.InitialPrompt = prompt;
        }

        // 迁移期：若脚本仍指向已移除的 FunASR 入口，改回 Whisper 脚本
        if (config.Whisper.ScriptPath.Contains("funasr", StringComparison.OrdinalIgnoreCase))
        {
            config.Whisper.ScriptPath = "python/whisper_transcribe.py";
        }

        // 流式模型名对 openai-whisper 无意义，回退到项目默认
        if (config.Whisper.Model.Contains("paraformer", StringComparison.OrdinalIgnoreCase) ||
            config.Whisper.Model.Contains("streaming", StringComparison.OrdinalIgnoreCase))
        {
            config.Whisper.Model = "turbo";
        }
    }

    public static void Save(string path, AppConfig config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        var json = JsonSerializer.Serialize(config, WriteOptions);
        File.WriteAllText(path, json);
    }
}
