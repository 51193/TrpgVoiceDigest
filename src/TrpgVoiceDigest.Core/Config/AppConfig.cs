namespace TrpgVoiceDigest.Core.Config;

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
}

public sealed class AudioConfig
{
    public string RecorderExecutable { get; set; } = "ffmpeg";
    public string InputFormat { get; set; } = "pulse";
    public string InputDevice { get; set; } = "default";
    public int SampleRate { get; set; } = 16000;
    public int Channels { get; set; } = 1;
    public int SegmentSeconds { get; set; } = 20;
    public double VoiceRmsThreshold { get; set; } = 0.015;
}

public sealed class WhisperConfig
{
    public string PythonExecutable { get; set; } = "python3";
    public string ScriptPath { get; set; } = "python/whisper_transcribe.py";
    public string Model { get; set; } = "turbo";
    public string Language { get; set; } = "zh";
    public string InitialPrompt { get; set; } = "以下是普通话的句子。";
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
}

public sealed class TriggerConfig
{
    public int EverySentences { get; set; } = 12;
    public int EverySeconds { get; set; } = 180;
}

public sealed class StorageConfig
{
    public string CampaignRoot { get; set; } = "Campaigns";
}

public sealed class PromptConfig
{
    public string SystemPromptPath { get; set; } = "prompts/system_digest.md";
    public string ProtocolPromptPath { get; set; } = "prompts/edit_protocol.md";
}

public sealed class UiConfig
{
    public string LastCampaignName { get; set; } = string.Empty;
    public string LastSessionName { get; set; } = string.Empty;
}

public sealed class ProcessingConfig
{
    public int SegmentQueueCapacity { get; set; } = 8;
    public int MeterIntervalMs { get; set; } = 150;
    public int MeterWindowMs { get; set; } = 250;
    public bool DeleteAudioAfterTranscribe { get; set; } = true;
    public int TranscribeWorkerCount { get; set; } = 1;
}
