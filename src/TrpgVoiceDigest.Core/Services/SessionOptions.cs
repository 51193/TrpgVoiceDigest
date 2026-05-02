using TrpgVoiceDigest.Core.Config;
using TrpgVoiceDigest.Core.Models;

namespace TrpgVoiceDigest.Core.Services;

public sealed record SessionOptions
{
    public required AppConfig Config { get; init; }
    public required string CampaignName { get; init; }
    public required string SessionName { get; init; }
    public required ILogService LogService { get; init; }
    public Action<string>? OnStatus { get; init; }
    public Action<TranscriptSegment>? OnTranscript { get; init; }
    public Action<RefinementState>? OnRefinementChanged { get; init; }
}
