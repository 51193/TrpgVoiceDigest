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
    public TriggerConfig Trigger { get; set; } = new();
    public StorageConfig Storage { get; set; } = new();
    public PromptConfig Prompts { get; set; } = new();
    public ProcessingConfig Processing { get; set; } = new();
    public UiConfig Ui { get; set; } = new();
    public RefinementConfig Refinement { get; set; } = new();
    public AudioSegmentationConfig AudioSegmentation { get; set; } = new();
    public CampaignSpeakerConfig CampaignSpeaker { get; set; } = new();
}

public sealed class AudioConfig
{
    public const double MinVoiceRmsThreshold = 0.005;

    public string RecorderExecutable { get; set; } = "ffmpeg";
    public string InputFormat { get; set; } = "pulse";
    public string InputDevice { get; set; } = "default";
    public int SampleRate { get; set; } = 16000;
    public int Channels { get; set; } = 1;

    [Obsolete("使用 AudioSegmentation.HardMaxSpeechSec 代替。保留 SegmentSeconds 用于遗留固定时长录音模式。")]
    public int SegmentSeconds { get; set; } = 20;

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
}

public sealed class TriggerConfig
{
    public int LlmPollingSeconds { get; set; } = 60;
}

public sealed class StorageConfig
{
    public string CampaignRoot { get; set; } = "Campaigns";
}

public sealed class PromptConfig
{
    public string SystemPromptPath { get; set; } = "prompts/system_digest.md";
    public string ConsistencyPromptPath { get; set; } = "prompts/consistency_lexicon.md";
    public string ProtocolPromptPath { get; set; } = "prompts/edit_protocol.md";
    public string ProcessingRequirementsPath { get; set; } = "prompts/processing_requirements.md";
    public string RefinementSystemPromptPath { get; set; } = "prompts/system_refinement.md";
    public string RefinementProtocolPath { get; set; } = "prompts/refinement_protocol.md";
    public string RefinementRequirementsPath { get; set; } = "prompts/refinement_requirements.md";
}

public sealed class UiConfig
{
    public string LastCampaignName { get; set; } = string.Empty;
    public string LastSessionName { get; set; } = string.Empty;
}

public sealed class ProcessingConfig
{
    public int TranscribePollingMs { get; set; } = 1000;
    public int MeterIntervalMs { get; set; } = 150;
    public int MeterWindowMs { get; set; } = 250;
    public bool DeleteAudioAfterTranscribe { get; set; } = true;

    [Obsolete("使用 AudioSegmentation.SilenceCutMs 代替。")]
    public int SilenceCutMs { get; set; } = 800;

    [Obsolete("使用 AudioSegmentation.HardMaxSpeechSec 代替。")]
    public double MaxSpeechSec { get; set; } = 15.0;
}

public sealed class RefinementConfig
{
    public int PollingSeconds { get; set; } = 60;
}

public sealed class AudioSegmentationConfig
{
    public double HardMaxSpeechSec { get; set; } = 120.0;
    public double MinSpeechSec { get; set; } = 0.4;
    public int SilenceCutMs { get; set; } = 800;
    public bool EndOfUtteranceEnabled { get; set; } = false;
    public double EndOfUtteranceSensitivity { get; set; } = 0.5;
}

public sealed class CampaignSpeakerConfig
{
    public string CampaignSpeakersFilePath { get; set; } = string.Empty;
}