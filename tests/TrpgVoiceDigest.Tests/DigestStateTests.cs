using TrpgVoiceDigest.Core.Models;

namespace TrpgVoiceDigest.Tests;

public class DigestStateTests
{
    [Fact]
    public void Apply_IsIdempotentForSameEdit()
    {
        var state = new DigestState();
        var ops = new List<EditOperation>
        {
            new(EditAction.Add, "人物_阿尔文", new DigestEntry("游侠，怀疑神官", ["人物", "关系"]))
        };

        state.Apply(ops);
        state.Apply(ops);

        Assert.Single(state.Entries);
        Assert.Equal("游侠，怀疑神官", state.Entries["人物_阿尔文"].Content);
    }
}
