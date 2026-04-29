using TrpgVoiceDigest.Infrastructure.Config;

namespace TrpgVoiceDigest.Tests;

public class JsonConfigLoaderCompatibilityTests
{
    [Fact]
    public void Load_WithLegacyAsrSection_ShouldMergeIntoWhisper()
    {
        var root = Path.Combine(Path.GetTempPath(), $"trpg_cfg_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var configPath = Path.Combine(root, "app.config.json");
        File.WriteAllText(configPath, """
                                      {
                                        "Asr": {
                                          "PythonExecutable": "/opt/venv/bin/python",
                                          "ScriptPath": "python/funasr_stream_transcribe.py",
                                          "Model": "paraformer-zh-streaming",
                                          "Language": "en",
                                          "InitialPrompt": "legacy"
                                        }
                                      }
                                      """);

        var config = JsonConfigLoader.Load(configPath);

        Assert.Equal("/opt/venv/bin/python", config.Whisper.PythonExecutable);
        Assert.Equal("python/whisper_transcribe.py", config.Whisper.ScriptPath);
        Assert.Equal("turbo", config.Whisper.Model);
        Assert.Equal("en", config.Whisper.Language);
        Assert.Equal("legacy", config.Whisper.InitialPrompt);

        Directory.Delete(root, true);
    }

    [Fact]
    public void Load_WithWhisperSection_ShouldUseWhisperValues()
    {
        var root = Path.Combine(Path.GetTempPath(), $"trpg_cfg_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var configPath = Path.Combine(root, "app.config.json");
        File.WriteAllText(configPath, """
                                      {
                                        "Whisper": {
                                          "PythonExecutable": "python/venv/bin/python",
                                          "ScriptPath": "python/custom_whisper.py",
                                          "Model": "small",
                                          "Language": "zh",
                                          "InitialPrompt": "x"
                                        }
                                      }
                                      """);

        var config = JsonConfigLoader.Load(configPath);

        Assert.Equal("python/venv/bin/python", config.Whisper.PythonExecutable);
        Assert.Equal("python/custom_whisper.py", config.Whisper.ScriptPath);
        Assert.Equal("small", config.Whisper.Model);

        Directory.Delete(root, true);
    }
}
