namespace TrpgVoiceDigest.Core.Config;

public static class ConfigConstants
{
    public const string DefaultConfigPath = "config/app.config.json";
}

public sealed class AppConfig
{
    public AudioConfig Audio { get; set; } = new();
    public WhisperConfig Whisper { get; set; } = new();
    public LlmConfig Llm { get; set; } = new();
    public StorageConfig Storage { get; set; } = new();
    public PromptConfig Prompts { get; set; } = new();
    public ProcessingConfig Processing { get; set; } = new();
    public UiConfig Ui { get; set; } = new();
    public RefinementConfig Refinement { get; set; } = new();
    public AudioSegmentationConfig AudioSegmentation { get; set; } = new();
    public StoryProgressConfig StoryProgress { get; set; } = new();
}

public sealed class StoryProgressConfig
{
    public int PollingSeconds { get; set; } = 90;
    public int MaxStoryEntries { get; set; } = 40;
    public int MaxContextChars { get; set; } = 8000;

    /*
     累积触发上限。仅当精炼累积文本超过该字数时触发裁剪。
     缓存未命中 token 价格约为命中 token 的 100 倍，因此将上限设得尽可能大，
     让裁剪极少（甚至永不）触发，以最大化前缀缓存命中率。
    */
    public int AccumulationMaxChars { get; set; } = 100000;
    public int ColdStartLines { get; set; } = 30;

    /*
     累积保留字数。当累积文本超过 AccumulationMaxChars 上限时，从末尾保留至少此数量的字符，
     且严格按整行裁剪（不保留半行）。
     保留字数需大于两次 LLM 请求间隔内新增的精炼内容量（语速 × 轮询间隔），以免漏信息。
    */
    public int AccumulationRetentionChars { get; set; } = 1000;
}

public sealed class AudioConfig
{
    public const double MinVoiceRmsThreshold = 0.005;

    public string RecorderExecutable { get; set; } = "tools/ffmpeg/ffmpeg";
    public string InputFormat { get; set; } = "pulse";
    public string InputDevice { get; set; } = "default";
    public int SampleRate { get; set; } = 16000;
    public int Channels { get; set; } = 1;

    public double VoiceRmsThreshold { get; set; } = 0.015;

    public static bool IsDefaultDevice(string device)
    {
        return string.IsNullOrWhiteSpace(device) || device.Equals("default", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class WhisperConfig
{
    /// <summary>优先使用项目 venv，避免系统 python 未安装 openai-whisper。</summary>
    public string PythonExecutable { get; set; } = "python/venv/bin/python";

    public string ScriptPath { get; set; } = "python/whisper_transcribe.py";
    public string Model { get; set; } = "turbo";
    public string Language { get; set; } = "zh";
    public string InitialPrompt { get; set; } = "以下是普通话的句子。";

    public bool DiarizationEnabled { get; set; } = true;
    public string HuggingFaceTokenEnv { get; set; } = "HF_TOKEN";
    public string Device { get; set; } = "cuda";
    public string ComputeType { get; set; } = "float16";

    /// <summary>跳过 Wav2Vec2 时间对齐，仅使用 ASR 原始时间戳。处理速度提升 2-3x。</summary>
    public bool SkipAlign { get; set; } = true;
}

public sealed class LlmConfig
{
    public string BaseUrl { get; set; } = "https://api.openai.com/v1/chat/completions";
    public string ApiKeyEnv { get; set; } = "OPENAI_API_KEY";
    public string Model { get; set; } = "gpt-4o-mini";
    public int RetryCount { get; set; } = 3;
    public int TimeoutSeconds { get; set; } = 60;
    public double Temperature { get; set; } = 0.1;
    public int MaxTokens { get; set; } = 2048;

    public bool ThinkingEnabled { get; set; } = false;
    public int ThinkingTokens { get; set; } = 4096;
    public string ThinkingTier { get; set; } = "high";
}

public sealed class StorageConfig
{
    public string CampaignRoot { get; set; } = "Campaigns";
}

public sealed class PromptConfig
{
    public string RefinementSystemPromptPath { get; set; } = "prompts/system_refinement.md";
    public string RefinementProtocolPath { get; set; } = "prompts/refinement_protocol.md";
    public string RefinementRequirementsPath { get; set; } = "prompts/refinement_requirements.md";
    public string ConsistencySystemPromptPath { get; set; } = "prompts/system_consistency.md";
}

public sealed class UiConfig
{
    public string LastCampaignName { get; set; } = string.Empty;
}

public sealed class ProcessingConfig
{
    public int TranscribePollingMs { get; set; } = 1000;
    public int MeterIntervalMs { get; set; } = 150;
    public int MeterWindowMs { get; set; } = 250;
    public bool DeleteAudioAfterTranscribe { get; set; } = true;
}

public sealed class RefinementConfig
{
    public int PollingSeconds { get; set; } = 60;

    public int MaxDialogueLines { get; set; } = 40;
    public int MaxRefinementSentences { get; set; } = 80;
    public int MinContextChars { get; set; } = 1500;
    public int MaxContextChars { get; set; } = 10000;

    public int TotalPromptBudgetChars { get; set; } = 8000;

    /*
     累积触发上限。仅当对话累积文本超过该字数时触发裁剪。
     缓存未命中 token 价格约为命中 token 的 100 倍，因此将上限设得尽可能大，
     让裁剪极少（甚至永不）触发，以最大化前缀缓存命中率。
    */
    public int AccumulationMaxChars { get; set; } = 100000;
    public int ColdStartDialogueLines { get; set; } = 40;

    /*
     累积保留字数。当累积文本超过 AccumulationMaxChars 上限时，从末尾保留至少此数量的字符，
     且严格按整行裁剪（不保留半行）。保留字数需大于两次 LLM 请求间隔内新增的对话量，以免漏信息。

     计算公式：AccumulationRetentionChars > (语速 字/分钟) × (轮询间隔 分钟)
     典型语速参考：中文约 200-300 字/分钟。
     例：PollingSeconds=60 → 1 分钟 → 200-300 字，建议 ≥ 500 字留余量。
    */
    public int AccumulationRetentionChars { get; set; } = 1000;
}

public sealed class AudioSegmentationConfig
{
    public double HardMaxSpeechSec { get; set; } = 120.0;
    public double MinSpeechSec { get; set; } = 0.3;
    public int SilenceCutMs { get; set; } = 400;
    public bool EndOfUtteranceEnabled { get; set; } = false;
    public double EndOfUtteranceSensitivity { get; set; } = 0.5;
}
