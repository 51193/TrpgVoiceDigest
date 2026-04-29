using TrpgVoiceDigest.Core.Config;

namespace TrpgVoiceDigest.Infrastructure.Audio;

public sealed record AudioInputResolveResult(string EffectiveInputDevice, string Strategy);

public interface IAudioInputDiscovery
{
    IReadOnlyList<string> GetAvailableSources(AudioConfig config);
    AudioInputResolveResult Resolve(AudioConfig config);
}