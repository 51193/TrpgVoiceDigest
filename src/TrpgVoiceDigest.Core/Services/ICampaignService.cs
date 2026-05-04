namespace TrpgVoiceDigest.Core.Services;

public interface ICampaignService
{
    Task RunAsync(CampaignOptions options, CancellationToken cancellationToken = default);
}