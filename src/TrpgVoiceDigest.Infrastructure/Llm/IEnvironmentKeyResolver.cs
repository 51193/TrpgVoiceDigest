namespace TrpgVoiceDigest.Infrastructure.Llm;

public interface IEnvironmentKeyResolver
{
    string? Resolve(string envName);
}
