using GeminiCode.Agent;

namespace GeminiCode.Tests;

public class ToolCallParserTests
{
    [Fact]
    public void Parse_SingleToolCall_ExtractsCorrectly()
    {
        var input = """
            Here is some text.
            <tool_call>
            {"name": "ReadFile", "parameters": {"path": "src/foo.cs"}}
            </tool_call>
            More text.
            """;
        var result = ToolCallParser.Parse(input);
        Assert.Single(result.ToolCalls);
        Assert.Equal("ReadFile", result.ToolCalls[0].Name);
        Assert.Equal("src/foo.cs", result.ToolCalls[0].Parameters["path"].GetString());
        Assert.Contains("Here is some text.", result.TextContent);
        Assert.Contains("More text.", result.TextContent);
    }

    [Fact]
    public void Parse_MultipleToolCalls_ExtractsAll()
    {
        var input = """
            <tool_call>
            {"name": "ReadFile", "parameters": {"path": "a.cs"}}
            </tool_call>
            <tool_call>
            {"name": "ReadFile", "parameters": {"path": "b.cs"}}
            </tool_call>
            """;
        var result = ToolCallParser.Parse(input);
        Assert.Equal(2, result.ToolCalls.Count);
    }

    [Fact]
    public void Parse_MarkdownFencedToolCall_UnwrapsAndParses()
    {
        var input = """
            ```xml
            <tool_call>
            {"name": "WriteFile", "parameters": {"path": "x.cs", "content": "hi"}}
            </tool_call>
            ```
            """;
        var result = ToolCallParser.Parse(input);
        Assert.Single(result.ToolCalls);
        Assert.Equal("WriteFile", result.ToolCalls[0].Name);
    }

    [Fact]
    public void Parse_ArgsAlias_TreatedAsParameters()
    {
        var input = """
            <tool_call>
            {"name": "ReadFile", "args": {"path": "foo.cs"}}
            </tool_call>
            """;
        var result = ToolCallParser.Parse(input);
        Assert.Single(result.ToolCalls);
        Assert.Equal("foo.cs", result.ToolCalls[0].Parameters["path"].GetString());
    }

    [Fact]
    public void Parse_NoToolCalls_ReturnsTextOnly()
    {
        var input = "Just a normal response with no tool calls.";
        var result = ToolCallParser.Parse(input);
        Assert.Empty(result.ToolCalls);
        Assert.Contains("Just a normal response", result.TextContent);
    }

    [Fact]
    public void Parse_MalformedJson_SkipsAndReturnsAsText()
    {
        var input = """
            <tool_call>
            {not valid json}
            </tool_call>
            """;
        var result = ToolCallParser.Parse(input);
        Assert.Empty(result.ToolCalls);
    }

    [Fact]
    public void Parse_JsonFencedToolCall_UnwrapsAndParses()
    {
        var input = """
            ```json
            <tool_call>
            {"name": "ListFiles", "parameters": {"pattern": "*.cs"}}
            </tool_call>
            ```
            """;
        var result = ToolCallParser.Parse(input);
        Assert.Single(result.ToolCalls);
        Assert.Equal("ListFiles", result.ToolCalls[0].Name);
    }
}
