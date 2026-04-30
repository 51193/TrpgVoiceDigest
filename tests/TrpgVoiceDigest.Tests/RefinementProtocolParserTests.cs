using TrpgVoiceDigest.Core.Models;

namespace TrpgVoiceDigest.Tests;

public class RefinementProtocolParserTests
{
    [Fact]
    public void Parse_AddWithoutNumber()
    {
        const string input = "refine add \"这是一条新增的句子\"";

        var ops = RefinementProtocolParser.Parse(input);

        Assert.Single(ops);
        Assert.Equal(RefineAction.Add, ops[0].Action);
        Assert.Null(ops[0].Number);
        Assert.Equal("这是一条新增的句子", ops[0].Text);
    }

    [Fact]
    public void Parse_AddWithNumber()
    {
        const string input = "refine add 3 \"插入在第3句之后\"";

        var ops = RefinementProtocolParser.Parse(input);

        Assert.Single(ops);
        Assert.Equal(RefineAction.Add, ops[0].Action);
        Assert.Equal(3, ops[0].Number);
        Assert.Equal("插入在第3句之后", ops[0].Text);
    }

    [Fact]
    public void Parse_Edit()
    {
        const string input = "refine edit 5 \"修改后的句子文本\"";

        var ops = RefinementProtocolParser.Parse(input);

        Assert.Single(ops);
        Assert.Equal(RefineAction.Edit, ops[0].Action);
        Assert.Equal(5, ops[0].Number);
        Assert.Equal("修改后的句子文本", ops[0].Text);
    }

    [Fact]
    public void Parse_Remove()
    {
        const string input = "refine remove 7";

        var ops = RefinementProtocolParser.Parse(input);

        Assert.Single(ops);
        Assert.Equal(RefineAction.Remove, ops[0].Action);
        Assert.Equal(7, ops[0].Number);
        Assert.Null(ops[0].Text);
    }

    [Fact]
    public void Parse_Empty()
    {
        var ops = RefinementProtocolParser.Parse("EMPTY");

        Assert.Single(ops);
        Assert.Equal(RefineAction.Empty, ops[0].Action);
    }

    [Fact]
    public void Parse_WhitespaceOnly()
    {
        var ops = RefinementProtocolParser.Parse("  ");

        Assert.Single(ops);
        Assert.Equal(RefineAction.Empty, ops[0].Action);
    }

    [Fact]
    public void Parse_MultipleOperations()
    {
        const string input = """
                             refine add "第一句新内容"
                             refine edit 2 "修改第二句"
                             refine remove 3
                             refine add 1 "插入到第一句后"
                             """;

        var ops = RefinementProtocolParser.Parse(input);

        Assert.Equal(4, ops.Count);
        Assert.Equal(RefineAction.Add, ops[0].Action);
        Assert.Null(ops[0].Number);
        Assert.Equal(RefineAction.Edit, ops[1].Action);
        Assert.Equal(2, ops[1].Number);
        Assert.Equal(RefineAction.Remove, ops[2].Action);
        Assert.Equal(3, ops[2].Number);
        Assert.Equal(RefineAction.Add, ops[3].Action);
        Assert.Equal(1, ops[3].Number);
    }

    [Fact]
    public void Parse_CrlfInput()
    {
        const string input = "refine add \"句子A\"\r\nrefine remove 1\r\n";

        var ops = RefinementProtocolParser.Parse(input);

        Assert.Equal(2, ops.Count);
        Assert.Equal(RefineAction.Add, ops[0].Action);
        Assert.Equal(RefineAction.Remove, ops[1].Action);
    }

    [Fact]
    public void Parse_InvalidLines_IgnoredAndReturnsEmpty()
    {
        const string input = "some garbage text\nnot a valid protocol line";

        var ops = RefinementProtocolParser.Parse(input);

        Assert.Single(ops);
        Assert.Equal(RefineAction.Empty, ops[0].Action);
    }

    [Fact]
    public void Parse_AddWithQuotesInText()
    {
        const string input = "refine add \"他说了\\\"你好\\\"然后离开\"";

        var ops = RefinementProtocolParser.Parse(input);

        Assert.Single(ops);
        Assert.Equal(RefineAction.Add, ops[0].Action);
        Assert.Contains("你好", ops[0].Text);
    }
}
