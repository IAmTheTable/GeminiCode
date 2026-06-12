using GeminiCode.Plugins;

namespace GeminiCode.Tests;

public class FrontmatterParserTests
{
    [Fact]
    public void Parse_WithFrontmatter_ExtractsFieldsAndBody()
    {
        var text = "---\nname: brainstorm\ndescription: Turn ideas into designs\n---\n## Phase: One\nbody line\n";
        var result = FrontmatterParser.Parse(text);
        Assert.Equal("brainstorm", result.Fields["name"]);
        Assert.Equal("Turn ideas into designs", result.Fields["description"]);
        Assert.StartsWith("## Phase: One", result.Body.TrimStart());
        Assert.DoesNotContain("name:", result.Body);
    }

    [Fact]
    public void Parse_NoFrontmatter_ReturnsWholeTextAsBody()
    {
        var text = "just instructions, no frontmatter";
        var result = FrontmatterParser.Parse(text);
        Assert.Empty(result.Fields);
        Assert.Equal(text, result.Body);
    }

    [Fact]
    public void Parse_ColonInValue_KeepsRemainder()
    {
        var text = "---\ndescription: ratio is 4:1 here\n---\nbody";
        var result = FrontmatterParser.Parse(text);
        Assert.Equal("ratio is 4:1 here", result.Fields["description"]);
    }

    [Fact]
    public void Parse_BlankAndMalformedLines_AreSkipped()
    {
        var text = "---\nname: x\n\nnotakeyvalueline\ndescription: y\n---\nbody";
        var result = FrontmatterParser.Parse(text);
        Assert.Equal("x", result.Fields["name"]);
        Assert.Equal("y", result.Fields["description"]);
        Assert.Equal(2, result.Fields.Count);
    }
}
