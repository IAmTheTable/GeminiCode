using GeminiCode.Agent;

namespace GeminiCode.Tests;

public class TagParserTests
{
    [Fact]
    public void Parse_RunTag_ExtractsCommand()
    {
        var input = "[RUN]tree /f /a[/RUN]";
        var result = ToolCallParser.Parse(input);
        Assert.Single(result.ToolCalls);
        Assert.Equal("RunCommand", result.ToolCalls[0].Name);
        Assert.Equal("tree /f /a", result.ToolCalls[0].Parameters["command"].GetString());
    }

    [Fact]
    public void Parse_RunTag_WithSurroundingText()
    {
        var input = "I'll run that for you.\n\n[RUN]dir /b C:\\[/RUN]\n\nWould you like more?";
        var result = ToolCallParser.Parse(input);
        Assert.Single(result.ToolCalls);
        Assert.Equal("RunCommand", result.ToolCalls[0].Name);
        Assert.Contains("dir /b", result.ToolCalls[0].Parameters["command"].GetString());
        Assert.Contains("run that for you", result.TextContent);
    }

    [Fact]
    public void Parse_FileTag_ExtractsPathAndContent()
    {
        var input = "[FILE:hello.py]\nprint('hello')\n[/FILE]";
        var result = ToolCallParser.Parse(input);
        Assert.Single(result.ToolCalls);
        Assert.Equal("WriteFile", result.ToolCalls[0].Name);
        Assert.Equal("hello.py", result.ToolCalls[0].Parameters["path"].GetString());
        Assert.Equal("print('hello')", result.ToolCalls[0].Parameters["content"].GetString());
    }

    [Fact]
    public void Parse_MultiLineFile_PreservesContent()
    {
        var input = "[FILE:script.py]\nimport os\nprint(os.listdir('.'))\n[/FILE]";
        var result = ToolCallParser.Parse(input);
        Assert.Single(result.ToolCalls);
        Assert.Contains("import os", result.ToolCalls[0].Parameters["content"].GetString());
        Assert.Contains("print(os.listdir", result.ToolCalls[0].Parameters["content"].GetString());
    }

    [Fact]
    public void Parse_FileAndRun_ExtractsBoth()
    {
        var input = "Creating script.\n\n[FILE:test.py]\nprint('hi')\n[/FILE]\n\n[RUN]python test.py[/RUN]";
        var result = ToolCallParser.Parse(input);
        Assert.Equal(2, result.ToolCalls.Count);
        Assert.Equal("WriteFile", result.ToolCalls[0].Name);
        Assert.Equal("RunCommand", result.ToolCalls[1].Name);
    }

    [Fact]
    public void Parse_ReadTag_ExtractsPath()
    {
        var input = "Let me check.\n\n[READ]src/main.py[/READ]";
        var result = ToolCallParser.Parse(input);
        Assert.Single(result.ToolCalls);
        Assert.Equal("ReadFile", result.ToolCalls[0].Name);
        Assert.Equal("src/main.py", result.ToolCalls[0].Parameters["path"].GetString());
    }

    [Fact]
    public void Parse_ListTag_ExtractsPattern()
    {
        var input = "[LIST]*.py[/LIST]";
        var result = ToolCallParser.Parse(input);
        Assert.Single(result.ToolCalls);
        Assert.Equal("ListFiles", result.ToolCalls[0].Name);
    }

    [Fact]
    public void Parse_CaseInsensitive_Works()
    {
        var input = "[run]dir /b[/run]";
        var result = ToolCallParser.Parse(input);
        Assert.Single(result.ToolCalls);
        Assert.Equal("RunCommand", result.ToolCalls[0].Name);
    }

    [Fact]
    public void Parse_TagsWithExtraWhitespace_Works()
    {
        var input = "[ RUN ]dir /b[ / RUN ]";
        var result = ToolCallParser.Parse(input);
        Assert.Single(result.ToolCalls);
        Assert.Equal("RunCommand", result.ToolCalls[0].Name);
    }

    [Fact]
    public void Parse_RealGeminiResponse_WithBoilerplate()
    {
        var input = "run tree on C:\\\nTools\n\n[RUN]tree /f /a C:\\[/RUN]\n\nWould you like me to save this?";
        var result = ToolCallParser.Parse(input);
        Assert.Single(result.ToolCalls);
        Assert.Equal("RunCommand", result.ToolCalls[0].Name);
        Assert.Contains("tree /f /a", result.ToolCalls[0].Parameters["command"].GetString());
    }
}
