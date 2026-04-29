using TrpgVoiceDigest.Core.Config;
using TrpgVoiceDigest.Infrastructure.Llm;

namespace TrpgVoiceDigest.Tests;

public class LlmClientTests
{
    [Fact]
    public async Task CompleteAsync_WhenEnvMissing_ShouldThrowBeforeHttpCall()
    {
        var client = new LlmClient(new HttpClient(), new StubEnvironmentKeyResolver(null));
        var config = new LlmConfig
        {
            ApiKeyEnv = "OPENAI_API_KEY"
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.CompleteAsync(config, "system", "user", CancellationToken.None));

        Assert.Contains("OPENAI_API_KEY", ex.Message);
    }

    private sealed class StubEnvironmentKeyResolver(string? value) : IEnvironmentKeyResolver
    {
        public string? Resolve(string envName) => value;
    }
}
