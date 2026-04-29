using System.Text.Json;
using TrpgVoiceDigest.Core.Models;
using TrpgVoiceDigest.Core.Services;
using TrpgVoiceDigest.Infrastructure.Storage;

namespace TrpgVoiceDigest.Tests;

public class SessionStorageTests
{
    [Fact]
    public void ExportCampaignDigest_GroupsByTag()
    {
        var root = Path.Combine(Path.GetTempPath(), $"trpg_test_{Guid.NewGuid():N}");
        var paths = SessionPathBuilder.Build(root, "DND_A", "Session_01");
        var storage = new SessionStorage(paths);
        storage.EnsureDirectories();

        var state = new DigestState();
        state.Apply([
            new EditOperation(EditAction.Add, EntryArea.Digest, "线索A", new EditValue(new DigestEntry("地下室有符号", ["线索", "地点"]), null)),
            new EditOperation(EditAction.Add, EntryArea.Digest, "人物B", new EditValue(new DigestEntry("是神秘教徒", ["人物"]), null)),
            new EditOperation(EditAction.Add, EntryArea.Digest, "一致性_角色别名", new EditValue(new DigestEntry("凯恩=Kain", [DigestState.ConsistencyTag]), null)),
            new EditOperation(EditAction.Add, EntryArea.Task, "任务A", new EditValue(null, "调查地下室")),
            new EditOperation(EditAction.Add, EntryArea.Story, "故事A", new EditValue(null, "队伍进入地下室"))
        ]);

        storage.ExportCampaignDigest(state);
        storage.ExportCampaignConsistency(state);
        storage.ExportCampaignTasks(state);
        storage.ExportCampaignStory(state);
        var text = File.ReadAllText(paths.CampaignDigestMarkdownPath);
        var consistencyText = File.ReadAllText(paths.CampaignConsistencyMarkdownPath);
        var taskText = File.ReadAllText(paths.CampaignTasksMarkdownPath);
        var storyText = File.ReadAllText(paths.CampaignStoryMarkdownPath);

        Assert.Contains("## 线索", text);
        Assert.Contains("## 人物", text);
        Assert.Contains("**线索A**", text);
        Assert.DoesNotContain(DigestState.ConsistencyTag, text);
        Assert.Contains(DigestState.ConsistencyTag, consistencyText);
        Assert.Contains("**一致性_角色别名**", consistencyText);
        Assert.Contains("## Active Tasks", taskText);
        Assert.Contains("**任务A**", taskText);
        Assert.Contains("**故事A**", storyText);

        Directory.Delete(root, true);
    }

    [Fact]
    public void LoadDigestState_ShouldReadLegacyPayload()
    {
        var root = Path.Combine(Path.GetTempPath(), $"trpg_test_{Guid.NewGuid():N}");
        var paths = SessionPathBuilder.Build(root, "DND_A", "Session_01");
        var storage = new SessionStorage(paths);
        storage.EnsureDirectories();
        File.WriteAllText(paths.DigestStatePath, """
                                           {
                                             "线索A": {
                                               "content": "旧格式内容",
                                               "tags": ["线索"]
                                             }
                                           }
                                           """);

        var state = storage.LoadDigestState();

        Assert.Equal("旧格式内容", state.Entries["线索A"].Content);
        Assert.Empty(state.ActiveTasks);
        Directory.Delete(root, true);
    }

    [Fact]
    public void AppendLlmEditLog_ShouldPersistOperations()
    {
        var root = Path.Combine(Path.GetTempPath(), $"trpg_test_{Guid.NewGuid():N}");
        var paths = SessionPathBuilder.Build(root, "DND_A", "Session_01");
        var storage = new SessionStorage(paths);
        storage.EnsureDirectories();
        var operations = new List<EditOperation>
        {
            new(EditAction.Add, EntryArea.Digest, "线索A", new EditValue(new DigestEntry("地下室有符号", ["线索"]), null)),
            new(EditAction.Complete, EntryArea.Task, "任务A", null)
        };

        storage.AppendLlmEditLog(DateTimeOffset.Parse("2026-01-01T12:34:56Z"), "hash-1", "raw-response", operations);

        var lines = File.ReadAllLines(paths.LlmEditLogPath);
        Assert.Single(lines);
        using var doc = JsonDocument.Parse(lines[0]);
        Assert.Equal("hash-1", doc.RootElement.GetProperty("transcriptHash").GetString());
        Assert.Equal(2, doc.RootElement.GetProperty("operationCount").GetInt32());
        Assert.Equal("raw-response", doc.RootElement.GetProperty("llmResponse").GetString());
        Assert.False(doc.RootElement.GetProperty("isEmpty").GetBoolean());
        Assert.Equal(2, doc.RootElement.GetProperty("operations").GetArrayLength());

        Directory.Delete(root, true);
    }

    [Fact]
    public void CampaignConsistencyLexicon_ShouldAppendAndReadAllEntries()
    {
        var root = Path.Combine(Path.GetTempPath(), $"trpg_test_{Guid.NewGuid():N}");
        var paths = SessionPathBuilder.Build(root, "DND_A", "Session_01");
        var storage = new SessionStorage(paths);
        storage.EnsureDirectories();

        storage.AppendCampaignConsistencyLexiconEntry("张三-旅店老板");
        storage.AppendCampaignConsistencyLexiconEntry("张三-护卫队长");
        storage.AppendCampaignConsistencyLexiconEntry("青铜钥匙");

        var text = storage.ReadCampaignConsistencyLexicon();

        Assert.Contains("张三-旅店老板", text);
        Assert.Contains("张三-护卫队长", text);
        Assert.Contains("青铜钥匙", text);
        Assert.Equal(3, File.ReadAllLines(paths.CampaignConsistencyLexiconPath).Length);

        Directory.Delete(root, true);
    }

    [Fact]
    public void ReadCampaignCharacterCards_ShouldMergeMarkdownFilesByName()
    {
        var root = Path.Combine(Path.GetTempPath(), $"trpg_test_{Guid.NewGuid():N}");
        var paths = SessionPathBuilder.Build(root, "DND_A", "Session_01");
        var storage = new SessionStorage(paths);
        storage.EnsureDirectories();

        File.WriteAllText(Path.Combine(paths.CharacterCardsDirectory, "b_rogue.md"), "# 影刃\n- 职业：游荡者");
        File.WriteAllText(Path.Combine(paths.CharacterCardsDirectory, "a_knight.md"), "# 艾琳\n- 职业：圣武士");

        var text = storage.ReadCampaignCharacterCards();

        var firstIndex = text.IndexOf("a_knight.md", StringComparison.Ordinal);
        var secondIndex = text.IndexOf("b_rogue.md", StringComparison.Ordinal);
        Assert.True(firstIndex >= 0);
        Assert.True(secondIndex > firstIndex);
        Assert.Contains("### 人物卡：a_knight.md", text);
        Assert.Contains("### 人物卡：b_rogue.md", text);
        Assert.Contains("职业：圣武士", text);
        Assert.Contains("职业：游荡者", text);

        Directory.Delete(root, true);
    }

}
