using TrpgVoiceDigest.Core.Models;

namespace TrpgVoiceDigest.Core.Services;

public sealed class LlmCallStats
{
    public int CumulativeTokens { get; private set; }
    public int CumulativeCacheHit { get; private set; }
    public int CumulativeCacheMiss { get; private set; }
    public int CallCount { get; private set; }

    public void IncrementCallCount()
    {
        CallCount++;
    }

    public void Record(LlmUsage usage)
    {
        CallCount++;
        CumulativeTokens += usage.TotalTokens;
        if (usage.CacheHitTokens + usage.CacheMissTokens > 0)
        {
            CumulativeCacheHit += usage.CacheHitTokens;
            CumulativeCacheMiss += usage.CacheMissTokens;
        }
    }

    public string FormatEntry(string label, LlmUsage usage)
    {
        var cacheInfo = "";
        if (usage.CacheHitTokens + usage.CacheMissTokens > 0)
        {
            var ratio = CumulativeCacheHit + CumulativeCacheMiss > 0
                ? (double)CumulativeCacheHit / (CumulativeCacheHit + CumulativeCacheMiss) * 100
                : 0;
            cacheInfo = $", 缓存命中: {usage.CacheHitTokens} / 未命中: {usage.CacheMissTokens} "
                + $"(累计 {CumulativeCacheHit}/{CumulativeCacheMiss}={ratio:F1}%)";
        }

        return $"[{label}] Token: 本次 {usage.PromptTokens} in + {usage.CompletionTokens} out = {usage.TotalTokens}, "
            + $"累计 {CumulativeTokens} (共{CallCount}次){cacheInfo}";
    }

    public string StopSummary
    {
        get
        {
            var cacheInfo = CumulativeCacheHit + CumulativeCacheMiss > 0
                ? $", 缓存命中率={CumulativeCacheHit}/{CumulativeCacheMiss + CumulativeCacheHit}="
                    + $"{(double)CumulativeCacheHit / (CumulativeCacheMiss + CumulativeCacheHit) * 100:F1}%"
                : "";
            return $"共 {CallCount} 次 LLM 调用, 累计 {CumulativeTokens} tokens{cacheInfo}";
        }
    }
}
