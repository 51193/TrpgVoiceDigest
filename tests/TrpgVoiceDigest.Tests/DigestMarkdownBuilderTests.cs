using TrpgVoiceDigest.Core.Models;
using TrpgVoiceDigest.Gui.Services;

namespace TrpgVoiceDigest.Tests;

public class DigestMarkdownBuilderTests
{
    [Fact]
    public void Build_ShouldContainGroupedTags()
    {
        var state = new DigestState();
        state.Apply([
            new EditOperation(EditAction.Add, "线索_红门", new DigestEntry("红门后有密室", ["线索", "地点"])),
            new EditOperation(EditAction.Add, "NPC_凯恩", new DigestEntry("对祭司保持敌意", ["人物"]))
        ]);

        var md = DigestMarkdownBuilder.Build(state);

        Assert.Contains("# 当前摘录", md);
        Assert.Contains("## 线索", md);
        Assert.Contains("## 人物", md);
        Assert.Contains("**线索_红门**", md);
    }
}
