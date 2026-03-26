using GeminiCode.Cli;

namespace GeminiCode.Tests;

public class MarkdownRendererTests
{
    [Fact]
    public void RenderCodeBlock_WrapsWithAnsiColors()
    {
        var input = "```csharp\nConsole.WriteLine(\"hi\");\n```";
        var result = MarkdownRenderer.Render(input);
        Assert.Contains("\x1b[", result);
        Assert.Contains("Console.WriteLine", result);
        Assert.Contains("csharp", result);
    }

    [Fact]
    public void RenderBold_AppliesBoldAnsi()
    {
        var input = "This is **bold** text.";
        var result = MarkdownRenderer.Render(input);
        Assert.Contains("\x1b[1m", result);
        Assert.Contains("bold", result);
    }

    [Fact]
    public void RenderHeader_AppliesBoldAndNewline()
    {
        var input = "## My Header";
        var result = MarkdownRenderer.Render(input);
        Assert.Contains("\x1b[1m", result);
        Assert.Contains("My Header", result);
    }

    [Fact]
    public void RenderInlineCode_AppliesHighlight()
    {
        var input = "Use `foo()` here.";
        var result = MarkdownRenderer.Render(input);
        Assert.Contains("\x1b[", result);
        Assert.Contains("foo()", result);
    }

    [Fact]
    public void RenderPlainText_PassesThrough()
    {
        var input = "Just plain text.";
        var result = MarkdownRenderer.Render(input);
        Assert.Equal("Just plain text.\n", result);
    }

    [Fact]
    public void RenderBulletList_IndentsWithMarker()
    {
        var input = "- Item one\n- Item two";
        var result = MarkdownRenderer.Render(input);
        Assert.Contains("  - Item one", result);
        Assert.Contains("  - Item two", result);
    }
}
