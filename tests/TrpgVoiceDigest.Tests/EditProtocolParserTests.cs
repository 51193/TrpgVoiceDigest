using TrpgVoiceDigest.Core.Models;

namespace TrpgVoiceDigest.Tests;

public class EditProtocolParserTests
{
    [Fact]
    public void Parse_AddEditRemoveComplete_Works()
    {
        const string input = """
                             digest add "线索A": {"content":"在酒馆发现血迹","tags":["线索","地点"]}
                             task edit "任务A": {"content":"去酒馆地下室"}
                             story add "章节_01": {"content":"队伍进入酒馆并发现异常"}
                             task complete "任务A"
                             digest remove "过期线索"
                             """;

        var ops = EditProtocolParser.Parse(input);

        Assert.Equal(5, ops.Count);
        Assert.Equal(EditAction.Add, ops[0].Action);
        Assert.Equal(EntryArea.Digest, ops[0].Area);
        Assert.Equal("线索A", ops[0].Key);
        Assert.Equal("在酒馆发现血迹", ops[0].Value!.Digest!.Content);
        Assert.Equal(EditAction.Edit, ops[1].Action);
        Assert.Equal(EntryArea.Task, ops[1].Area);
        Assert.Equal("去酒馆地下室", ops[1].Value!.Text);
        Assert.Equal(EditAction.Add, ops[2].Action);
        Assert.Equal(EntryArea.Story, ops[2].Area);
        Assert.Equal(EditAction.Complete, ops[3].Action);
        Assert.Equal(EditAction.Remove, ops[4].Action);
    }

    [Fact]
    public void Parse_Empty_Works()
    {
        var ops = EditProtocolParser.Parse("EMPTY");
        Assert.Single(ops);
        Assert.Equal(EditAction.Empty, ops[0].Action);
    }

    [Fact]
    public void Parse_CrlfInput_Works()
    {
        const string input = "task add \"任务B\": {\"content\":\"检查北门\"}\r\ndigest remove \"旧线索\"\r\n";

        var ops = EditProtocolParser.Parse(input);

        Assert.Equal(2, ops.Count);
        Assert.Equal(EditAction.Add, ops[0].Action);
        Assert.Equal(EntryArea.Task, ops[0].Area);
        Assert.Equal(EditAction.Remove, ops[1].Action);
        Assert.Equal(EntryArea.Digest, ops[1].Area);
    }
}
