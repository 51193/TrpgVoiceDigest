using System.Threading.Channels;
using TrpgVoiceDigest.Core.Models;

namespace TrpgVoiceDigest.Infrastructure.Services;

public sealed class PipelineChannels
{
    public Channel<SegmentJob> SegmentChannel { get; }
    public Channel<int> TranscriptionSignal { get; }

    public PipelineChannels(int segmentQueueCapacity)
    {
        SegmentChannel = Channel.CreateBounded<SegmentJob>(new BoundedChannelOptions(Math.Max(1, segmentQueueCapacity))
        {
            SingleWriter = true,
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait
        });
        TranscriptionSignal = Channel.CreateUnbounded<int>();
    }
}
