using TrpgVoiceDigest.Core.Models;

namespace TrpgVoiceDigest.Tests;

public class DigestStateTagGroupsTests
{
    [Fact]
    public void GetTagGroups_Empty_ReturnsEmptyList()
    {
        var state = new DigestState();
        var groups = state.GetTagGroups();
        Assert.Empty(groups);
    }

    [Fact]
    public void GetTagGroups_GroupsByTagAndSorts()
    {
        var state = new DigestState();
        state.Entries["key_a"] = new DigestEntry("content_a", ["人物", "线索"]);
        state.Entries["key_b"] = new DigestEntry("content_b", ["地点"]);
        state.Entries["key_c"] = new DigestEntry("content_c", ["人物"]);

        var groups = state.GetTagGroups();

        Assert.Equal(3, groups.Count);
        Assert.Equal("人物", groups[0].Tag);
        Assert.Equal("地点", groups[1].Tag);
        Assert.Equal("线索", groups[2].Tag);

        Assert.Equal(2, groups[0].Items.Count);
        Assert.Contains(("key_a", "content_a"), groups[0].Items);
        Assert.Contains(("key_c", "content_c"), groups[0].Items);
    }

    [Fact]
    public void GetTagGroups_SameEntryInMultipleTags_AppearsMultipleTimes()
    {
        var state = new DigestState();
        state.Entries["key_x"] = new DigestEntry("content_x", ["a", "b"]);

        var groups = state.GetTagGroups();

        Assert.Equal(2, groups.Count);
        Assert.Single(groups[0].Items);
        Assert.Single(groups[1].Items);
    }

    [Fact]
    public void GetTagGroups_FilteringByConsistencyTag_Works()
    {
        var state = new DigestState();
        state.Entries["digest_a"] = new DigestEntry("main", ["人物"]);
        state.Entries["digest_b"] = new DigestEntry("consistency", [DigestState.ConsistencyTag, "人物"]);

        var consistencyGroups = state.GetTagGroupsByTag(DigestState.ConsistencyTag);
        var mainGroups = state.GetTagGroupsExcludingTag(DigestState.ConsistencyTag);

        Assert.Single(consistencyGroups);
        Assert.Equal(DigestState.ConsistencyTag, consistencyGroups[0].Tag);
        Assert.Single(consistencyGroups[0].Items);
        Assert.Contains(("digest_b", "consistency"), consistencyGroups[0].Items);
        Assert.DoesNotContain(mainGroups, x => x.Tag == DigestState.ConsistencyTag);
    }
}