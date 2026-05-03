using TrpgVoiceDigest.Core.Models;

namespace TrpgVoiceDigest.Core.Services;

public interface IPromptTemplateResolver
{
    string Resolve(
        string template,
        IReadOnlyDictionary<string, string>? data = null,
        IReadOnlyDictionary<string, IncrementalDigestContainer>? containers = null);
}
