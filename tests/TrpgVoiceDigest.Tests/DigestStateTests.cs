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
            new(EditAction.Add, EntryArea.Digest, "人物_阿尔文", new EditValue(new DigestEntry("游侠，怀疑神官", ["人物", "关系"]), null))
        };

        state.Apply(ops);
        state.Apply(ops);

        Assert.Single(state.Entries);
        Assert.Equal("游侠，怀疑神官", state.Entries["人物_阿尔文"].Content);
    }

    [Fact]
    public void Apply_TaskComplete_MovesEntryToCompletedTasks()
    {
        var state = new DigestState();
        state.Apply([
            new EditOperation(EditAction.Add, EntryArea.Task, "主线任务", new EditValue(null, "找到祭坛入口")),
            new EditOperation(EditAction.Complete, EntryArea.Task, "主线任务", null)
        ]);

        Assert.DoesNotContain("主线任务", state.ActiveTasks.Keys);
        Assert.Equal("找到祭坛入口", state.CompletedTasks["主线任务"]);
    }
}
