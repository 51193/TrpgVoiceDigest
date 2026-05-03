using TrpgVoiceDigest.Core.Models;
using TrpgVoiceDigest.Core.Services;

namespace TrpgVoiceDigest.Tests.Core.Models;

public class TestLogService : ILogService
{
    public List<LogEntry> Entries { get; } = [];
    public event Action<LogEntry>? OnEntryLogged;

    public void Log(LogLevel level, string message)
    {
        var entry = new LogEntry(DateTimeOffset.UtcNow, level, message);
        Entries.Add(entry);
        OnEntryLogged?.Invoke(entry);
    }
}

public class IncrementalDigestContainerTests
{
    private static (IncrementalDigestContainer Container, TestLogService Log) CreateContainer(string name = "test", string title = "测试")
    {
        var log = new TestLogService();
        var container = new IncrementalDigestContainer(name, title, log);
        return (container, log);
    }

    [Fact]
    public void AddEntry_Simple_AppendsWithIncrementingKeys()
    {
        var (c, log) = CreateContainer();

        var e1 = c.AddEntry("第一句");
        var e2 = c.AddEntry("第二句");
        var e3 = c.AddEntry("第三句");

        Assert.Equal(3, c.Count);
        Assert.Equal(1, e1.Key);
        Assert.Equal(2, e2.Key);
        Assert.Equal(3, e3.Key);
        Assert.Equal("第一句", e1.Content);
        Assert.Equal("第二句", e2.Content);
        Assert.Equal("第三句", e3.Content);
        Assert.NotEmpty(log.Entries);
    }

    [Fact]
    public void AddEntry_WithAfterKey_FillsGap()
    {
        var (c, _) = CreateContainer();
        c.AddEntry("第一句"); // key 1
        c.AddEntry("第三句"); // key 2
        c.RemoveEntry(2);     // key 2 removed

        var e = c.AddEntry("第二句", afterKey: 1);

        Assert.Equal(2, e.Key);
        Assert.Equal("第二句", e.Content);
        Assert.Equal(2, c.Count);
    }

    [Fact]
    public void AddEntry_AfterKeyOccupied_UsesNextAvailable()
    {
        var (c, _) = CreateContainer();
        c.AddEntry("第一句"); // key 1
        c.AddEntry("第二句"); // key 2

        var e = c.AddEntry("附加句", afterKey: 1);

        // key 2 is occupied, so should use next available
        Assert.True(e.Key > 2);
    }

    [Fact]
    public void AddEntry_EmptyContent_ReturnsNull()
    {
        var (c, log) = CreateContainer();

        var e1 = c.AddEntry("");
        var e2 = c.AddEntry("  ");
        var e3 = c.AddEntry(null!);

        Assert.Equal(0, c.Count);
        Assert.Null(e1);
        Assert.Null(e2);
        Assert.Null(e3);
        Assert.Contains(log.Entries, x => x.Message.Contains("跳过"));
    }

    [Fact]
    public void EditEntry_ExistingKey_UpdatesContent()
    {
        var (c, _) = CreateContainer();
        c.AddEntry("原内容");

        var result = c.EditEntry(1, "新内容");

        Assert.True(result);
        Assert.Equal("新内容", c.GetEntry(1)!.Content);
    }

    [Fact]
    public void EditEntry_NonExistentKey_ReturnsFalse()
    {
        var (c, log) = CreateContainer();
        c.AddEntry("内容");

        var result = c.EditEntry(99, "新内容");

        Assert.False(result);
        Assert.Contains(log.Entries, x => x.Message.Contains("不存在"));
    }

    [Fact]
    public void EditEntry_EmptyContent_ReturnsFalse()
    {
        var (c, _) = CreateContainer();
        c.AddEntry("内容");

        var result = c.EditEntry(1, "");

        Assert.False(result);
        Assert.Equal("内容", c.GetEntry(1)!.Content);
    }

    [Fact]
    public void RemoveEntry_ExistingKey_RemovesAndLeavesGap()
    {
        var (c, _) = CreateContainer();
        c.AddEntry("第一句"); // key 1
        c.AddEntry("第二句"); // key 2
        c.AddEntry("第三句"); // key 3

        var result = c.RemoveEntry(2);

        Assert.True(result);
        Assert.Equal(2, c.Count);
        Assert.NotNull(c.GetEntry(1));
        Assert.Null(c.GetEntry(2));
        Assert.NotNull(c.GetEntry(3));
    }

    [Fact]
    public void RemoveEntry_NonExistentKey_ReturnsFalse()
    {
        var (c, log) = CreateContainer();
        c.AddEntry("第一句");

        var result = c.RemoveEntry(99);

        Assert.False(result);
        Assert.Contains(log.Entries, x => x.Message.Contains("不存在"));
        Assert.Equal(1, c.Count);
    }

    [Fact]
    public void RemoveEntry_ThenAdd_DoesNotReuseRemovedKey()
    {
        var (c, _) = CreateContainer();
        c.AddEntry("第一句"); // key 1
        c.AddEntry("第二句"); // key 2
        c.RemoveEntry(1);

        var e = c.AddEntry("新内容");

        // New entry gets key 3 (not key 1 unless using afterKey)
        Assert.Equal(3, e.Key);
    }

    [Fact]
    public void BatchOperations_RemoveThenEdit_DifferentKeys_Correct()
    {
        var (c, _) = CreateContainer();
        c.AddEntry("A"); // key 1
        c.AddEntry("B"); // key 2
        c.AddEntry("C"); // key 3
        c.AddEntry("D"); // key 4
        c.AddEntry("E"); // key 5

        // Simulate LLM sending: remove 3, edit 4
        // With stable keys (no renumbering), edit 4 should target the ORIGINAL sentence 4 ("D")
        c.RemoveEntry(3); // removes "C"
        c.EditEntry(4, "D-modified");

        Assert.Equal(4, c.Count);
        Assert.Equal("D-modified", c.GetEntry(4)!.Content);
        Assert.Null(c.GetEntry(3)); // gap where 3 was
        Assert.Equal("E", c.GetEntry(5)!.Content); // key 5 unchanged
    }

    [Fact]
    public void BatchOperations_RemoveThenEdit_SameKey_EditFails()
    {
        var (c, _) = CreateContainer();
        c.AddEntry("A"); // 1
        c.AddEntry("B"); // 2
        c.AddEntry("C"); // 3

        c.RemoveEntry(2);
        var result = c.EditEntry(2, "should fail");

        Assert.False(result);
        Assert.Null(c.GetEntry(2));
    }

    [Fact]
    public void BatchOperations_MultipleAddRemoveEdit_KeyIntegrity()
    {
        var (c, _) = CreateContainer();
        c.AddEntry("A"); // 1
        c.AddEntry("B"); // 2
        c.AddEntry("C"); // 3
        c.AddEntry("D"); // 4
        c.AddEntry("E"); // 5

        c.RemoveEntry(2);       // B gone
        c.EditEntry(4, "D-new"); // D modified
        c.AddEntry("F");        // 6
        c.RemoveEntry(1);       // A gone
        c.AddEntry("G");        // 7

        Assert.Equal(5, c.Count);
        Assert.Null(c.GetEntry(1));
        Assert.Null(c.GetEntry(2));
        Assert.Equal("C", c.GetEntry(3)!.Content);
        Assert.Equal("D-new", c.GetEntry(4)!.Content);
        Assert.Equal("E", c.GetEntry(5)!.Content);
        Assert.Equal("F", c.GetEntry(6)!.Content);
        Assert.Equal("G", c.GetEntry(7)!.Content);
    }

    [Fact]
    public void ExportJson_IncludesAllEntries()
    {
        var (c, _) = CreateContainer();
        c.AddEntry("第一句");
        c.AddEntry("第二句");

        var json = c.ExportJson();

        Assert.Contains("第一句", json);
        Assert.Contains("第二句", json);
        Assert.Contains("\"key\": 1", json);
        Assert.Contains("\"key\": 2", json);
        Assert.Contains("\"content\"", json);
    }

    [Fact]
    public void ExportJson_EmptyContainer_ExportsTitleOnly()
    {
        var (c, _) = CreateContainer();
        var json = c.ExportJson();

        Assert.Contains("\"title\": \"测试\"", json);
        Assert.Contains("\"entries\": []", json);
    }

    [Fact]
    public void ExportMarkdown_IncludesTitleAndContent()
    {
        var (c, _) = CreateContainer();
        c.AddEntry("第一句内容");
        c.AddEntry("第二句内容");

        var md = c.ExportMarkdown();

        Assert.Contains("# 测试", md);
        Assert.Contains("第一句内容", md);
        Assert.Contains("第二句内容", md);
    }

    [Fact]
    public void ExportMarkdown_EmptyContainer_ShowsEmptyMessage()
    {
        var (c, _) = CreateContainer();
        var md = c.ExportMarkdown();

        Assert.Contains("暂无内容", md);
    }

    [Fact]
    public void OrderedEntries_ReturnsInKeyOrder()
    {
        var (c, _) = CreateContainer();
        c.AddEntry("E", afterKey: 4); // key 5
        c.AddEntry("A");              // key 1
        c.AddEntry("C", afterKey: 1); // key 2
        c.AddEntry("D", afterKey: 2); // key 3
        c.AddEntry("B", afterKey: 0); // key ?

        var ordered = c.OrderedEntries;
        Assert.Equal(c.Count, ordered.Count);
        for (var i = 1; i < ordered.Count; i++)
            Assert.True(ordered[i - 1].Key < ordered[i].Key);
    }

    [Fact]
    public void ApplyOperations_RefineOperation_DelegatesCorrectly()
    {
        var (c, _) = CreateContainer();
        c.AddEntry("原有内容");

        var ops = new IOperation[]
        {
            new RefineOperation(RefineAction.Add, null, "新增句"),
            new RefineOperation(RefineAction.Edit, 1, "修改后"),
            new RefineOperation(RefineAction.Remove, 2, null),
        };
        ((IIncrementalDataContainer)c).ApplyOperations(ops);

        Assert.Equal(1, c.Count);
        Assert.Equal("修改后", c.GetEntry(1)!.Content);
        Assert.Null(c.GetEntry(2));
    }

    [Fact]
    public void ApplyOperations_EmptyAction_NoChange()
    {
        var (c, _) = CreateContainer();
        c.AddEntry("内容");

        var ops = new IOperation[] { new RefineOperation(RefineAction.Empty, null, null) };
        ((IIncrementalDataContainer)c).ApplyOperations(ops);

        Assert.Equal(1, c.Count);
    }

    [Fact]
    public void ApplyOperations_NoRefineOperation_NoChange()
    {
        var (c, _) = CreateContainer();
        c.AddEntry("内容");

        var ops = new IOperation[] { };
        ((IIncrementalDataContainer)c).ApplyOperations(ops);

        Assert.Equal(1, c.Count);
    }

    [Fact]
    public void GetEntry_ExistingKey_ReturnsEntry()
    {
        var (c, _) = CreateContainer();
        c.AddEntry("内容");

        var entry = c.GetEntry(1);

        Assert.NotNull(entry);
        Assert.Equal("内容", entry.Content);
    }

    [Fact]
    public void GetEntry_NonExistentKey_ReturnsNull()
    {
        var (c, _) = CreateContainer();
        Assert.Null(c.GetEntry(1));
    }

    [Fact]
    public void AddEntry_AfterRemovedKey_FillsExactGap()
    {
        var (c, _) = CreateContainer();
        c.AddEntry("A"); // 1
        c.AddEntry("B"); // 2
        c.AddEntry("C"); // 3
        c.RemoveEntry(2);

        var e = c.AddEntry("B-new", afterKey: 1);

        Assert.Equal(2, e.Key);
        Assert.Equal("B-new", e.Content);
        Assert.Equal("B-new", c.GetEntry(2)!.Content);
    }

    [Fact]
    public void LargeScale_NoDataLoss()
    {
        var (c, _) = CreateContainer();
        var n = 1000;

        for (var i = 0; i < n; i++)
            c.AddEntry($"Entry {i}");

        Assert.Equal(n, c.Count);

        // Remove every 3rd entry
        for (var i = 3; i <= n; i += 3)
            c.RemoveEntry(i);

        var expectedRemaining = n - n / 3;
        Assert.Equal(expectedRemaining, c.Count);

        // Verify non-removed entries are intact
        for (var i = 1; i <= n; i++)
        {
            if (i % 3 == 0)
                Assert.Null(c.GetEntry(i));
            else
                Assert.NotNull(c.GetEntry(i));
        }
    }

    [Fact]
    public void Logging_CoversAllOperations()
    {
        var (c, log) = CreateContainer();

        c.AddEntry("Test Add");
        Assert.Contains(log.Entries, x => x.Message.Contains("AddEntry"));

        c.EditEntry(1, "Test Edit");
        Assert.Contains(log.Entries, x => x.Message.Contains("EditEntry"));

        c.RemoveEntry(1);
        Assert.Contains(log.Entries, x => x.Message.Contains("RemoveEntry"));

        var ops = new IOperation[] { new RefineOperation(RefineAction.Add, null, "ViaOps") };
        ((IIncrementalDataContainer)c).ApplyOperations(ops);
        Assert.Contains(log.Entries, x => x.Message.Contains("ApplyOperations"));

        // Verify key summary appears in logs
        Assert.Contains(log.Entries, x => x.Message.Contains("keys=["));
    }
}
