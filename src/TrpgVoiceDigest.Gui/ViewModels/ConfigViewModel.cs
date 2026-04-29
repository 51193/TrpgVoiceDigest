using System;
using System.IO;
using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TrpgVoiceDigest.Core.Config;
using TrpgVoiceDigest.Core.Services;
using TrpgVoiceDigest.Infrastructure.Config;
using TrpgVoiceDigest.Infrastructure.Audio;
using TrpgVoiceDigest.Infrastructure.Storage;

namespace TrpgVoiceDigest.Gui.ViewModels;

public partial class ConfigViewModel : ViewModelBase
{
    private const string DefaultConfigPath = ConfigConstants.DefaultConfigPath;
    private readonly IAudioInputDiscovery _audioInputDiscovery;
    private readonly SessionStorage _sessionStorage = new();

    private string _configPath = DefaultConfigPath;
    private AppConfig _baseConfig = new();
    [ObservableProperty] private string _campaignName = string.Empty;
    [ObservableProperty] private string _sessionName = string.Empty;
    [ObservableProperty] private string _campaignRoot = "Campaigns";
    [ObservableProperty] private string _inputDevice = "default";
    [ObservableProperty] private string _recorderExecutable = "ffmpeg";
    [ObservableProperty] private string _inputFormat = "pulse";
    [ObservableProperty] private int _sampleRate = 16000;
    [ObservableProperty] private int _channels = 1;
    [ObservableProperty] private int _segmentSeconds = 20;
    [ObservableProperty] private double _voiceRmsThreshold = 0.015;
    [ObservableProperty] private string _pythonExecutable = "python/venv/bin/python";
    [ObservableProperty] private string _whisperScriptPath = "python/whisper_transcribe.py";
    [ObservableProperty] private string _whisperModel = "turbo";
    [ObservableProperty] private string _whisperLanguage = "zh";
    [ObservableProperty] private string _whisperInitialPrompt = "以下是普通话的句子。";
    [ObservableProperty] private string _llmBaseUrl = "https://api.openai.com/v1/chat/completions";
    [ObservableProperty] private string _llmApiKeyEnv = "OPENAI_API_KEY";
    [ObservableProperty] private string _llmModel = "gpt-4o-mini";
    [ObservableProperty] private int _llmRetryCount = 3;
    [ObservableProperty] private int _llmTimeoutSeconds = 60;
    [ObservableProperty] private double _llmTemperature = 0.1;
    [ObservableProperty] private int _llmMaxTokens = 2048;
    [ObservableProperty] private int _triggerSentenceCount = 12;
    [ObservableProperty] private int _triggerSeconds = 180;
    [ObservableProperty] private int _segmentQueueCapacity = 8;
    [ObservableProperty] private int _meterIntervalMs = 150;
    [ObservableProperty] private int _meterWindowMs = 250;
    [ObservableProperty] private bool _deleteAudioAfterTranscribe = true;
    [ObservableProperty] private string _systemPromptPath = "prompts/system_digest.md";
    [ObservableProperty] private string _protocolPromptPath = "prompts/edit_protocol.md";
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private string _recommendedInputDevice = "default";
    [ObservableProperty] private string _consistencyLexiconEntryInput = string.Empty;
    [ObservableProperty] private string _consistencyLexiconPreview = string.Empty;
    [ObservableProperty] private string _characterCardsDirectoryPath = string.Empty;
    [ObservableProperty] private string _characterCardsPreview = string.Empty;

    public ObservableCollection<string> ExistingCampaigns { get; } = [];
    public ObservableCollection<string> ExistingSessions { get; } = [];
    public ObservableCollection<string> AvailableInputDevices { get; } = [];

    public event Action<AppConfig, string, string>? StartRequested;

    public ConfigViewModel(IAudioInputDiscovery? audioInputDiscovery = null)
    {
        _audioInputDiscovery = audioInputDiscovery ?? PlatformAudioInputDiscovery.CreateDefault();
    }

    public void LoadDefaults(string configPath)
    {
        _configPath = configPath;
        var config = JsonConfigLoader.Load(configPath);
        _baseConfig = config;
        CampaignName = config.Ui.LastCampaignName;
        SessionName = config.Ui.LastSessionName;
        CampaignRoot = config.Storage.CampaignRoot;
        RecorderExecutable = config.Audio.RecorderExecutable;
        InputFormat = config.Audio.InputFormat;
        InputDevice = config.Audio.InputDevice;
        SampleRate = config.Audio.SampleRate;
        Channels = config.Audio.Channels;
        SegmentSeconds = config.Audio.SegmentSeconds;
        VoiceRmsThreshold = config.Audio.VoiceRmsThreshold;
        PythonExecutable = config.Whisper.PythonExecutable;
        WhisperScriptPath = config.Whisper.ScriptPath;
        WhisperModel = config.Whisper.Model;
        WhisperLanguage = config.Whisper.Language;
        WhisperInitialPrompt = config.Whisper.InitialPrompt;
        LlmBaseUrl = config.Llm.BaseUrl;
        LlmApiKeyEnv = config.Llm.ApiKeyEnv;
        LlmModel = config.Llm.Model;
        LlmRetryCount = config.Llm.RetryCount;
        LlmTimeoutSeconds = config.Llm.TimeoutSeconds;
        LlmTemperature = config.Llm.Temperature;
        LlmMaxTokens = config.Llm.MaxTokens;
        TriggerSentenceCount = config.Trigger.EverySentences;
        TriggerSeconds = config.Trigger.EverySeconds;
        SegmentQueueCapacity = config.Processing.SegmentQueueCapacity;
        MeterIntervalMs = config.Processing.MeterIntervalMs;
        MeterWindowMs = config.Processing.MeterWindowMs;
        DeleteAudioAfterTranscribe = config.Processing.DeleteAudioAfterTranscribe;
        SystemPromptPath = config.Prompts.SystemPromptPath;
        ProtocolPromptPath = config.Prompts.ProtocolPromptPath;
        RefreshAudioDevices();
        LoadCampaigns();
        LoadConsistencyLexiconPreview();
        RefreshCharacterCardsContext();
    }

    [RelayCommand]
    private void RefreshCampaigns() => LoadCampaigns();

    [RelayCommand]
    private void RefreshAudioDevices()
    {
        AvailableInputDevices.Clear();
        var audioConfig = BuildAudioConfigSnapshot();
        var sources = _audioInputDiscovery.GetAvailableSources(audioConfig);
        foreach (var source in sources)
        {
            AvailableInputDevices.Add(source);
        }

        var recommended = _audioInputDiscovery.Resolve(audioConfig).EffectiveInputDevice;
        RecommendedInputDevice = recommended;
        if (string.IsNullOrWhiteSpace(InputDevice) || InputDevice.Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            InputDevice = recommended;
        }
    }

    [RelayCommand]
    private void RefreshSessions()
    {
        ExistingSessions.Clear();
        if (string.IsNullOrWhiteSpace(CampaignName))
        {
            return;
        }

        var campaignDir = Path.Combine(CampaignRoot, CampaignName);
        if (!Directory.Exists(campaignDir))
        {
            return;
        }

        foreach (var sessionDir in Directory.GetDirectories(campaignDir))
        {
            ExistingSessions.Add(Path.GetFileName(sessionDir));
        }
    }

    [RelayCommand]
    private void Start()
    {
        if (string.IsNullOrWhiteSpace(CampaignName) || string.IsNullOrWhiteSpace(SessionName))
        {
            StatusMessage = "Campaign 与 Session 必填。";
            return;
        }

        var config = ValidateAndSaveConfig();
        StartRequested?.Invoke(config, CampaignName.Trim(), SessionName.Trim());
    }

    [RelayCommand]
    private void SaveConfig()
    {
        ValidateAndSaveConfig();
    }

    [RelayCommand]
    private void AddConsistencyLexiconEntry()
    {
        if (string.IsNullOrWhiteSpace(CampaignName))
        {
            StatusMessage = "请先填写 Campaign 名称，再添加一致性词汇。";
            return;
        }

        if (string.IsNullOrWhiteSpace(ConsistencyLexiconEntryInput))
        {
            StatusMessage = "一致性词汇不能为空。";
            return;
        }

        var paths = BuildSessionPathsForCampaign();
        _sessionStorage.AppendCampaignConsistencyLexiconEntry(paths, ConsistencyLexiconEntryInput);
        ConsistencyLexiconEntryInput = string.Empty;
        LoadConsistencyLexiconPreview();
        StatusMessage = "一致性词汇已追加到 Campaign 词汇文档。";
    }

    [RelayCommand]
    private void ReloadConsistencyLexicon()
    {
        LoadConsistencyLexiconPreview();
        StatusMessage = "一致性词汇文档已重新加载。";
    }

    [RelayCommand]
    private void ReloadCharacterCards()
    {
        RefreshCharacterCardsContext();
        StatusMessage = "人物卡文档已重新加载。";
    }

    [RelayCommand]
    private void OpenCharacterCardsDirectory()
    {
        if (string.IsNullOrWhiteSpace(CampaignName))
        {
            StatusMessage = "请先填写 Campaign 名称，再打开人物卡目录。";
            return;
        }

        var paths = BuildSessionPathsForCampaign();
        Directory.CreateDirectory(paths.CharacterCardsDirectory);
        CharacterCardsDirectoryPath = paths.CharacterCardsDirectory;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = paths.CharacterCardsDirectory,
                UseShellExecute = true
            });
            StatusMessage = "已打开人物卡目录。";
        }
        catch (Exception ex)
        {
            StatusMessage = $"打开人物卡目录失败: {ex.Message}";
        }
    }

    private AppConfig ValidateAndSaveConfig()
    {
        if (VoiceRmsThreshold <= 0)
        {
            VoiceRmsThreshold = 0.005;
            StatusMessage = "VoiceRmsThreshold 不能为0，已自动调整为0.005。";
        }

        var config = BuildConfig();
        JsonConfigLoader.Save(_configPath, config);
        if (!StatusMessage.StartsWith("VoiceRmsThreshold", StringComparison.Ordinal))
        {
            StatusMessage = $"配置已保存到 {_configPath}";
        }

        return config;
    }

    private AppConfig BuildConfig()
    {
        return new AppConfig
        {
            Storage = new StorageConfig { CampaignRoot = CampaignRoot.Trim() },
            Audio = new AudioConfig
            {
                RecorderExecutable = RecorderExecutable.Trim(),
                InputFormat = InputFormat.Trim(),
                InputDevice = InputDevice.Trim(),
                SampleRate = SampleRate,
                Channels = Channels,
                SegmentSeconds = SegmentSeconds,
                VoiceRmsThreshold = VoiceRmsThreshold
            },
            Whisper = new WhisperConfig
            {
                PythonExecutable = PythonExecutable.Trim(),
                ScriptPath = WhisperScriptPath.Trim(),
                Model = WhisperModel.Trim(),
                Language = WhisperLanguage.Trim(),
                InitialPrompt = WhisperInitialPrompt.Trim()
            },
            Llm = new LlmConfig
            {
                BaseUrl = LlmBaseUrl.Trim(),
                ApiKeyEnv = LlmApiKeyEnv.Trim(),
                Model = LlmModel.Trim(),
                RetryCount = LlmRetryCount,
                TimeoutSeconds = LlmTimeoutSeconds,
                Temperature = LlmTemperature,
                MaxTokens = LlmMaxTokens
            },
            Trigger = new TriggerConfig
            {
                EverySentences = TriggerSentenceCount,
                EverySeconds = TriggerSeconds
            },
            Processing = new ProcessingConfig
            {
                SegmentQueueCapacity = SegmentQueueCapacity,
                MeterIntervalMs = MeterIntervalMs,
                MeterWindowMs = MeterWindowMs,
                DeleteAudioAfterTranscribe = DeleteAudioAfterTranscribe
            },
            Prompts = new PromptConfig
            {
                SystemPromptPath = SystemPromptPath.Trim(),
                ProtocolPromptPath = ProtocolPromptPath.Trim()
            },
            Ui = new UiConfig
            {
                LastCampaignName = CampaignName.Trim(),
                LastSessionName = SessionName.Trim()
            }
        };
    }

    private void LoadCampaigns()
    {
        ExistingCampaigns.Clear();
        if (!Directory.Exists(CampaignRoot))
        {
            return;
        }

        foreach (var campaignDir in Directory.GetDirectories(CampaignRoot))
        {
            ExistingCampaigns.Add(Path.GetFileName(campaignDir));
        }
    }

    partial void OnCampaignNameChanged(string value)
    {
        RefreshSessions();
        LoadConsistencyLexiconPreview();
        RefreshCharacterCardsContext();
    }

    partial void OnCampaignRootChanged(string value)
    {
        LoadCampaigns();
        LoadConsistencyLexiconPreview();
        RefreshCharacterCardsContext();
    }

    private SessionPaths BuildSessionPathsForCampaign()
    {
        var campaign = string.IsNullOrWhiteSpace(CampaignName) ? "_unspecified_campaign" : CampaignName.Trim();
        var session = string.IsNullOrWhiteSpace(SessionName) ? "_lexicon_preview" : SessionName.Trim();
        return SessionPathBuilder.Build(CampaignRoot.Trim(), campaign, session);
    }

    private void LoadConsistencyLexiconPreview()
    {
        if (string.IsNullOrWhiteSpace(CampaignName))
        {
            ConsistencyLexiconPreview = string.Empty;
            return;
        }

        var paths = BuildSessionPathsForCampaign();
        ConsistencyLexiconPreview = _sessionStorage.ReadCampaignConsistencyLexicon(paths);
    }

    private void RefreshCharacterCardsContext()
    {
        if (string.IsNullOrWhiteSpace(CampaignName))
        {
            CharacterCardsDirectoryPath = string.Empty;
            CharacterCardsPreview = string.Empty;
            return;
        }

        var paths = BuildSessionPathsForCampaign();
        CharacterCardsDirectoryPath = paths.CharacterCardsDirectory;
        CharacterCardsPreview = _sessionStorage.ReadCampaignCharacterCards(paths);
    }

    private AudioConfig BuildAudioConfigSnapshot() =>
        new()
        {
            RecorderExecutable = RecorderExecutable.Trim(),
            InputFormat = InputFormat.Trim(),
            InputDevice = InputDevice.Trim(),
            SampleRate = SampleRate,
            Channels = Channels,
            SegmentSeconds = SegmentSeconds,
            VoiceRmsThreshold = VoiceRmsThreshold
        };
}
