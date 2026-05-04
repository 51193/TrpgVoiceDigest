using TrpgVoiceDigest.Core.Config;
using TrpgVoiceDigest.Core.Models;

namespace TrpgVoiceDigest.Core.Services;

public interface ILlmClient
{
    Task<(string Content, LlmUsage? Usage)> CompleteAsync(
        LlmConfig config,
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken);

    Task<(string Content, LlmUsage? Usage)> CompleteAsync(
        LlmConfig config,
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken);
}