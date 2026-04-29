using TrpgVoiceDigest.Infrastructure.Services;

namespace TrpgVoiceDigest.Infrastructure.Llm;

public sealed class PlatformEnvironmentKeyResolver : IEnvironmentKeyResolver
{
    public string? Resolve(string envName)
    {
        var value = Environment.GetEnvironmentVariable(envName);
        if (!string.IsNullOrWhiteSpace(value)) return value;

        if (OperatingSystem.IsLinux())
            return ReadByShell("bash", "-lc", $"printenv {envName}")
                   ?? ReadByShell("bash", "-ic", $"printenv {envName}")
                   ?? ReadByShell("zsh", "-lc", $"printenv {envName}");

        if (OperatingSystem.IsWindows())
            // Windows 默认不做 shell 回退，避免耦合 PowerShell/cmd 的登录会话差异。
            return null;

        return null;
    }

    private static string? ReadByShell(string shell, string mode, string script)
    {
        var output = ProcessHelper.RunAndGetOutput(shell, mode, script);
        return string.IsNullOrWhiteSpace(output) ? null : output.Trim();
    }
}