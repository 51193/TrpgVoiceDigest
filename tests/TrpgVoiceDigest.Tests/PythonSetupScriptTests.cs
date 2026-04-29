namespace TrpgVoiceDigest.Tests;

public class PythonSetupScriptTests
{
    [Fact]
    public void InitScript_ShouldExist_AndContainExpectedCommands()
    {
        var repoRoot = FindRepoRoot();
        var scriptPath = Path.Combine(repoRoot, "scripts", "init_python_venv.sh");
        Assert.True(File.Exists(scriptPath), "init_python_venv.sh 不存在。");

        var content = File.ReadAllText(scriptPath);
        Assert.Contains("python3 -m venv", content);
        Assert.Contains("pip install -r", content);
        Assert.Contains("ffmpeg", content);
    }

    private static string FindRepoRoot()
    {
        var current = AppContext.BaseDirectory;
        var dir = new DirectoryInfo(current);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "TrpgVoiceDigest.slnx"))) return dir.FullName;

            dir = dir.Parent;
        }

        throw new InvalidOperationException("无法定位仓库根目录。");
    }
}