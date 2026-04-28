using TrpgVoiceDigest.Core.Models;

namespace TrpgVoiceDigest.Tests;

public class EditProtocolParserTests
{
    [Fact]
    public void Parse_AddEditRemove_Works()
    {
        const string input = """
                             add "线索A": {"content":"在酒馆发现血迹","tags":["线索","地点"]}
                             edit "NPC_商人": {"content":"态度转为友善","tags":["NPC"]}
                             remove "旧任务"
                             """;

        var ops = EditProtocolParser.Parse(input);

        Assert.Equal(3, ops.Count);
        Assert.Equal(EditAction.Add, ops[0].Action);
        Assert.Equal("线索A", ops[0].Key);
        Assert.Equal("在酒馆发现血迹", ops[0].Value!.Content);
        Assert.Equal(EditAction.Edit, ops[1].Action);
        Assert.Equal(EditAction.Remove, ops[2].Action);
    }

    [Fact]
    public void Parse_Empty_Works()
    {
        var ops = EditProtocolParser.Parse("EMPTY");
        Assert.Single(ops);
        Assert.Equal(EditAction.Empty, ops[0].Action);
    }
}
