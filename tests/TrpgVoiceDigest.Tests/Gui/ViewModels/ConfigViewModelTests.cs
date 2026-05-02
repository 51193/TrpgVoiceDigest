using TrpgVoiceDigest.Core.Config;
using TrpgVoiceDigest.Gui.ViewModels;
using TrpgVoiceDigest.Infrastructure.Config;
using TrpgVoiceDigest.Infrastructure.Services;

namespace TrpgVoiceDigest.Tests.Gui.ViewModels;

public class ConfigViewModelTests
{
    public ConfigViewModelTests()
    {
        ApplicationPathResolver.Initialize(Directory.GetCurrentDirectory());
    }
    [Fact]
    public void StartCommand_WithoutCampaignOrSession_ShouldSetStatus()
    {
        var vm = new ConfigViewModel();
        vm.StartCommand.Execute(null);
        Assert.Contains("必填", vm.StatusMessage);
    }

    [Fact]
    public void SaveConfig_ShouldPersistSplashSettingsToConfig()
    {
        var root = Path.Combine(Path.GetTempPath(), $"trpg_cfg_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var configPath = Path.Combine(root, "app.config.json");
        JsonConfigLoader.Save(configPath, new AppConfig());

        var vm = new ConfigViewModel();
        vm.LoadDefaults(configPath);
        vm.CampaignName = "DND_A";
        vm.SessionName = "S03";
        vm.CampaignRoot = "CampaignsNew";
        vm.RecorderExecutable = "ffmpeg-custom";
        vm.InputFormat = "pulse";
        vm.InputDevice = "alsa_output.test.monitor";
        vm.SampleRate = 22050;
        vm.Channels = 2;
        vm.VoiceRmsThreshold = 0.02;
        vm.PythonExecutable = "python/venv/bin/python";
        vm.WhisperScriptPath = "python/custom_whisper.py";
        vm.WhisperModel = "small";
        vm.WhisperLanguage = "en";
        vm.WhisperInitialPrompt = "This is a Mandarin sentence.";
        vm.LlmBaseUrl = "https://example.com/v1/chat/completions";
        vm.LlmApiKeyEnv = "TEST_API_KEY";
        vm.LlmModel = "gpt-test";
        vm.LlmRetryCount = 5;
        vm.LlmTimeoutSeconds = 75;
        vm.LlmTemperature = 0.3;
        vm.LlmMaxTokens = 1024;
        vm.RefinementPollingSeconds = 99;
        vm.TranscribePollingMs = 2000;
        vm.MeterIntervalMs = 120;
        vm.MeterWindowMs = 240;
        vm.DeleteAudioAfterTranscribe = true;

        vm.SaveConfigCommand.Execute(null);

        var saved = JsonConfigLoader.Load(configPath);
        Assert.Equal("DND_A", saved.Ui.LastCampaignName);
        Assert.Equal("S03", saved.Ui.LastSessionName);
        Assert.Equal("CampaignsNew", saved.Storage.CampaignRoot);
        Assert.Equal("ffmpeg-custom", saved.Audio.RecorderExecutable);
        Assert.Equal("pulse", saved.Audio.InputFormat);
        Assert.Equal("alsa_output.test.monitor", saved.Audio.InputDevice);
        Assert.Equal(22050, saved.Audio.SampleRate);
        Assert.Equal(2, saved.Audio.Channels);
        Assert.Equal(0.02, saved.Audio.VoiceRmsThreshold);
        Assert.Equal("python/venv/bin/python", saved.Whisper.PythonExecutable);
        Assert.Equal("python/custom_whisper.py", saved.Whisper.ScriptPath);
        Assert.Equal("small", saved.Whisper.Model);
        Assert.Equal("en", saved.Whisper.Language);
        Assert.Equal("This is a Mandarin sentence.", saved.Whisper.InitialPrompt);
        Assert.Equal("https://example.com/v1/chat/completions", saved.Llm.BaseUrl);
        Assert.Equal("TEST_API_KEY", saved.Llm.ApiKeyEnv);
        Assert.Equal("gpt-test", saved.Llm.Model);
        Assert.Equal(5, saved.Llm.RetryCount);
        Assert.Equal(75, saved.Llm.TimeoutSeconds);
        Assert.Equal(0.3, saved.Llm.Temperature);
        Assert.Equal(1024, saved.Llm.MaxTokens);
        Assert.Equal(99, saved.Refinement.PollingSeconds);
        Assert.Equal(2000, saved.Processing.TranscribePollingMs);
        Assert.Equal(120, saved.Processing.MeterIntervalMs);
        Assert.Equal(240, saved.Processing.MeterWindowMs);
        Assert.True(saved.Processing.DeleteAudioAfterTranscribe);

        Directory.Delete(root, true);
    }
}