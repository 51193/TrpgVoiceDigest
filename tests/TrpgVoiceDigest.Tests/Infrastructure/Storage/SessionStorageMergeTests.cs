using TrpgVoiceDigest.Infrastructure.Storage;

namespace TrpgVoiceDigest.Tests.Infrastructure.Storage;

public class SessionStorageMergeTests
{
    [Fact]
    public void Merge_ConsecutiveSameSpeaker()
    {
        const string input = """
                             [14:32:01] [speaker_0]: 我们到了酒馆门口
                             [14:32:05] [speaker_0]: 看来这里没什么人
                             [14:32:12] [speaker_1]: 进去看看吧
                             [14:32:15] [speaker_1]: 等等，柜台上好像有血迹
                             """;

        var result = SessionStorage.MergeConsecutiveSpeakerLines(input);

        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.Contains("[1] [speaker_0]", lines[0]);
        Assert.Contains("我们到了酒馆门口。看来这里没什么人", lines[0]);
        Assert.Contains("[2] [speaker_1]", lines[1]);
        Assert.Contains("进去看看吧。等等，柜台上好像有血迹", lines[1]);
    }

    [Fact]
    public void Merge_NonConsecutiveSameSpeaker_NotMerged()
    {
        const string input = """
                             [14:32:01] [speaker_0]: 第一句
                             [14:32:05] [speaker_1]: 第二句
                             [14:32:12] [speaker_0]: 第三句
                             """;

        var result = SessionStorage.MergeConsecutiveSpeakerLines(input);

        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
    }

    [Fact]
    public void Merge_NoSpeaker_NotMerged()
    {
        const string input = """
                             [14:32:01]: 第一句
                             [14:32:05]: 第二句
                             """;

        var result = SessionStorage.MergeConsecutiveSpeakerLines(input);

        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
    }

    [Fact]
    public void Merge_EmptyInput()
    {
        var result = SessionStorage.MergeConsecutiveSpeakerLines("");
        Assert.Empty(result);
    }

    [Fact]
    public void Merge_SingleLine()
    {
        const string input = "[14:32:01] [speaker_0]: 我们到了";

        var result = SessionStorage.MergeConsecutiveSpeakerLines(input);

        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Single(lines);
        Assert.Contains("[1] [speaker_0]", lines[0]);
    }
}
