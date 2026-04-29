using CliWrap;
using CliWrap.Buffered;

namespace TrpgVoiceDigest.Infrastructure.Services;

internal static class ProcessHelper
{
    public static string? RunAndGetOutput(string fileName, params string[] arguments)
    {
        try
        {
            var result = Cli.Wrap(fileName)
                .WithArguments(arguments)
                .WithValidation(CommandResultValidation.None)
                .ExecuteBufferedAsync()
                .GetAwaiter()
                .GetResult();

            if (result.ExitCode != 0) return null;

            return result.StandardOutput;
        }
        catch
        {
            return null;
        }
    }
}