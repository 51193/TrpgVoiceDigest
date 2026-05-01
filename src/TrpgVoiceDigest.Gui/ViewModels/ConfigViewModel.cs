using System;
using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TrpgVoiceDigest.Core.Config;
using TrpgVoiceDigest.Infrastructure.Audio;
using TrpgVoiceDigest.Infrastructure.Config;
using TrpgVoiceDigest.Infrastructure.Services;

namespace TrpgVoiceDigest.Gui.ViewModels;

public partial class ConfigViewModel : ViewModelBase
{
    private const string DefaultConfigPath = ConfigConstants.DefaultConfigPath;
    private readonly IAudioInputDiscovery _audioInputDiscovery;
    private AppConfig _baseConfig = new();
    [ObservableProperty] private string _campaignName = string.Empty;
    [ObservableProperty] private string _campaignRoot = "Campaigns";
    [ObservableProperty] private int _channels = 1;

    private string _configPath = DefaultConfigPath;
    [ObservableProperty] private bool _deleteAudioAfterTranscribe = true;
    [ObservableProperty] private string _inputDevice = "default";
    [ObservableProperty] private string _inputFormat = "pulse";
    [ObservableProperty] private string _llmApiKeyEnv = "OPENAI_API_KEY";
    [ObservableProperty] private string _llmBaseUrl = "https://api.openai.com/v1/chat/completions";
    [ObservableProperty] private int _llmMaxTokens = 2048;
    [ObservableProperty] private string _llmModel = "gpt-4o-mini";
    [ObservableProperty] private int _llmRetryCount = 3;
    [ObservableProperty] private double _llmTemperature = 0.1;
    [ObservableProperty] private int _llmTimeoutSeconds = 60;
    [ObservableProperty] private int _meterIntervalMs = 150;
    [ObservableProperty] private int _meterWindowMs = 250;
    [ObservableProperty] private string _pythonExecutable = "python/venv/bin/python";
    [ObservableProperty] private string _recommendedInputDevice = "default";
    [ObservableProperty] private string _recorderExecutable = "ffmpeg";
    [ObservableProperty] private int _refinementPollingSeconds = 60;
    [ObservableProperty] private int _sampleRate = 16000;
    [ObservableProperty] private string _sessionName = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private int _transcribePollingMs = 1000;
    [ObservableProperty] private double _voiceRmsThreshold = 0.015;
    [ObservableProperty] private string _whisperInitialPrompt = "以下是普通话的句子。";
    [ObservableProperty] private string _whisperLanguage = "zh";
    [ObservableProperty] private string _whisperModel = "turbo";
    [ObservableProperty] private string _whisperScriptPath = "python/whisper_transcribe.py";

    public ConfigViewModel(IAudioInputDiscovery? audioInputDiscovery = null)
    {
        _audioInputDiscovery = audioInputDiscovery ?? PlatformAudioInputDiscovery.CreateDefault();
    }

    public ObservableCollection<string> ExistingCampaigns { get; } = [];
    public ObservableCollection<string> ExistingSessions { get; } = [];
    public ObservableCollection<string> AvailableInputDevices { get; } = [];

    public event Action<AppConfig, string, string>? StartRequested;

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
        RefinementPollingSeconds = config.Refinement.PollingSeconds;
        TranscribePollingMs = config.Processing.TranscribePollingMs;
        MeterIntervalMs = config.Processing.MeterIntervalMs;
        MeterWindowMs = config.Processing.MeterWindowMs;
        DeleteAudioAfterTranscribe = config.Processing.DeleteAudioAfterTranscribe;
        RefreshAudioDevices();
        LoadCampaigns();
    }

    [RelayCommand]
    private void RefreshCampaigns()
    {
        LoadCampaigns();
    }

    [RelayCommand]
    private void RefreshAudioDevices()
    {
        AvailableInputDevices.Clear();
        var audioConfig = BuildAudioConfigSnapshot();
        var sources = _audioInputDiscovery.GetAvailableSources(audioConfig);
        foreach (var source in sources) AvailableInputDevices.Add(source);

        var recommended = _audioInputDiscovery.Resolve(audioConfig).EffectiveInputDevice;
        RecommendedInputDevice = recommended;
        if (AudioConfig.IsDefaultDevice(InputDevice)) InputDevice = recommended;
    }

    [RelayCommand]
    private void RefreshSessions()
    {
        ExistingSessions.Clear();
        if (string.IsNullOrWhiteSpace(CampaignName)) return;

        var resolvedRoot = ApplicationPathResolver.ResolveCampaignRoot(CampaignRoot);
        var campaignDir = Path.Combine(resolvedRoot, CampaignName);
        if (!Directory.Exists(campaignDir)) return;

        foreach (var sessionDir in Directory.GetDirectories(campaignDir))
            ExistingSessions.Add(Path.GetFileName(sessionDir));
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

    private AppConfig ValidateAndSaveConfig()
    {
        if (VoiceRmsThreshold <= 0)
        {
            VoiceRmsThreshold = AudioConfig.MinVoiceRmsThreshold;
            StatusMessage = $"VoiceRmsThreshold 不能为0，已自动调整为{AudioConfig.MinVoiceRmsThreshold}。";
        }

        var config = BuildConfig();
        JsonConfigLoader.Save(_configPath, config);
        if (!StatusMessage.StartsWith("VoiceRmsThreshold", StringComparison.Ordinal))
            StatusMessage = $"配置已保存到 {_configPath}";

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
            Refinement = new RefinementConfig
            {
                PollingSeconds = RefinementPollingSeconds
            },
            Processing = new ProcessingConfig
            {
                TranscribePollingMs = TranscribePollingMs,
                MeterIntervalMs = MeterIntervalMs,
                MeterWindowMs = MeterWindowMs,
                DeleteAudioAfterTranscribe = DeleteAudioAfterTranscribe
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
        var resolvedRoot = ApplicationPathResolver.ResolveCampaignRoot(CampaignRoot);
        if (!Directory.Exists(resolvedRoot)) return;

        foreach (var campaignDir in Directory.GetDirectories(resolvedRoot))
            ExistingCampaigns.Add(Path.GetFileName(campaignDir));
    }

    partial void OnCampaignNameChanged(string value)
    {
        RefreshSessions();
    }

    partial void OnCampaignRootChanged(string value)
    {
        LoadCampaigns();
    }

    private AudioConfig BuildAudioConfigSnapshot()
    {
        return new AudioConfig
        {
            RecorderExecutable = RecorderExecutable.Trim(),
            InputFormat = InputFormat.Trim(),
            InputDevice = InputDevice.Trim(),
            SampleRate = SampleRate,
            Channels = Channels,
            VoiceRmsThreshold = VoiceRmsThreshold
        };
    }
}