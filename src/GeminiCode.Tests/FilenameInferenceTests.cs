using GeminiCode.Agent;
using GeminiCode.Browser;

namespace GeminiCode.Tests;

public class FilenameInferenceTests
{
    [Fact]
    public void ExtractFilenames_FindsKnownExtensions_IgnoresNonFiles()
    {
        var got = AgentOrchestrator.ExtractFilenames("update index.html which was script.txt, see Agar.io and 3.5");
        Assert.Contains("index.html", got);
        Assert.Contains("script.txt", got);
        Assert.DoesNotContain("Agar.io", got);   // .io not a known code extension
        Assert.DoesNotContain("3.5", got);        // not a filename
    }

    [Fact]
    public void InferFilename_PrefersUserFilenameMatchingBlockLanguage()
    {
        var block = new CodeBlock("html", "<!doctype html><html></html>");
        var name = AgentOrchestrator.InferFilename(block, responseText: "Here you go.", userMessage: "update index.html which was script.txt");
        Assert.Equal("index.html", name);
    }

    [Fact]
    public void InferFilename_FallsBackToResponseText()
    {
        var block = new CodeBlock("html", "<html></html>");
        var name = AgentOrchestrator.InferFilename(block, responseText: "I'll create index.html for you now.", userMessage: "make me an agar.io clone");
        Assert.Equal("index.html", name);
    }

    [Fact]
    public void InferFilename_NullWhenNoFilenameAnywhere()
    {
        var block = new CodeBlock("python", "print('hi')");
        var name = AgentOrchestrator.InferFilename(block, responseText: "here is a script", userMessage: "write me something");
        Assert.Null(name);
    }
}
