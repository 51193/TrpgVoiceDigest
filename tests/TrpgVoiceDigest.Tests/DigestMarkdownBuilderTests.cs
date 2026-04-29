using TrpgVoiceDigest.Core.Models;

namespace TrpgVoiceDigest.Tests;

public class DigestMarkdownBuilderTests
{
    [Fact]
    public void BuildDigest_ShouldContainGroupedTags()
    {
        var state = new DigestState();
        state.Apply([
            new EditOperation(EditAction.Add, EntryArea.Digest, "线索_红门", new EditValue(new DigestEntry("红门后有密室", ["线索", "地点"]), null)),
            new EditOperation(EditAction.Add, EntryArea.Digest, "NPC_凯恩", new EditValue(new DigestEntry("对祭司保持敌意", ["人物"]), null)),
            new EditOperation(EditAction.Add, EntryArea.Digest, "一致性_凯恩", new EditValue(new DigestEntry("凯恩别名可能是Kain", [DigestState.ConsistencyTag]), null))
        ]);

        var md = state.BuildDigestMarkdown();

        Assert.Contains("# 当前摘录", md);
        Assert.Contains("## 线索", md);
        Assert.Contains("## 人物", md);
        Assert.Contains("**线索_红门**", md);
        Assert.DoesNotContain(DigestState.ConsistencyTag, md);
    }

    [Fact]
    public void BuildConsistency_ShouldContainOnlyConsistencyEntries()
    {
        var state = new DigestState();
        state.Apply([
            new EditOperation(EditAction.Add, EntryArea.Digest, "NPC_凯恩", new EditValue(new DigestEntry("普通摘要", ["人物"]), null)),
            new EditOperation(EditAction.Add, EntryArea.Digest, "一致性_凯恩", new EditValue(new DigestEntry("凯恩别名可能是Kain", [DigestState.ConsistencyTag]), null))
        ]);

        var md = state.BuildConsistencyMarkdown();

        Assert.Contains("# 一致性参考", md);
        Assert.Contains(DigestState.ConsistencyTag, md);
        Assert.Contains("**一致性_凯恩**", md);
        Assert.DoesNotContain("**NPC_凯恩**", md);
    }

    [Fact]
    public void BuildTaskAndStory_ShouldRenderKvpLists()
    {
        var state = new DigestState();
        state.Apply([
            new EditOperation(EditAction.Add, EntryArea.Task, "任务A", new EditValue(null, "调查港口")),
            new EditOperation(EditAction.Add, EntryArea.Story, "推进A", new EditValue(null, "队伍抵达港口"))
        ]);

        var activeTasks = state.BuildActiveTasksMarkdown();
        var story = state.BuildStoryMarkdown();

        Assert.Contains("# 活跃任务", activeTasks);
        Assert.Contains("**任务A**", activeTasks);
        Assert.Contains("# 故事进展", story);
        Assert.Contains("**推进A**", story);
    }
}
