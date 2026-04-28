using TrpgVoiceDigest.Gui.ViewModels;
using TrpgVoiceDigest.Infrastructure.Config;

namespace TrpgVoiceDigest.Tests;

public class ConfigViewModelTests
{
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
        JsonConfigLoader.Save(configPath, new TrpgVoiceDigest.Core.Config.AppConfig());

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
        vm.SegmentSeconds = 5;
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
        vm.TriggerSentenceCount = 9;
        vm.TriggerSeconds = 99;
        vm.SegmentQueueCapacity = 6;
        vm.MeterIntervalMs = 120;
        vm.MeterWindowMs = 240;
        vm.DeleteAudioAfterTranscribe = true;
        vm.SystemPromptPath = "prompts/sys.md";
        vm.ProtocolPromptPath = "prompts/proto.md";

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
        Assert.Equal(5, saved.Audio.SegmentSeconds);
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
        Assert.Equal(9, saved.Trigger.EverySentences);
        Assert.Equal(99, saved.Trigger.EverySeconds);
        Assert.Equal(6, saved.Processing.SegmentQueueCapacity);
        Assert.Equal(120, saved.Processing.MeterIntervalMs);
        Assert.Equal(240, saved.Processing.MeterWindowMs);
        Assert.True(saved.Processing.DeleteAudioAfterTranscribe);
        Assert.Equal("prompts/sys.md", saved.Prompts.SystemPromptPath);
        Assert.Equal("prompts/proto.md", saved.Prompts.ProtocolPromptPath);

        Directory.Delete(root, true);
    }
}
