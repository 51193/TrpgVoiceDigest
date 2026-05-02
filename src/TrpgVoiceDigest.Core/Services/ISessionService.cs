namespace TrpgVoiceDigest.Core.Services;

public interface ISessionService
{
    Task RunAsync(SessionOptions options, CancellationToken cancellationToken = default);
}
