// src/GeminiCode.Tests/SessionContextTests.cs
namespace GeminiCode.Tests;

using GeminiCode.Agent;

public class SessionContextTests
{
    private string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sessionctx_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void LogToolCall_RecordsToolExecution()
    {
        var ctx = new SessionContext(CreateTempDir(), "general");
        ctx.LogToolCall("ReadFile", "src/App.cs", true, 1);
        var md = ctx.GenerateMarkdown();
        Assert.Contains("ReadFile", md);
        Assert.Contains("src/App.cs", md);
    }

    [Fact]
    public void LogFileModified_TracksModifiedFiles()
    {
        var ctx = new SessionContext(CreateTempDir(), "general");
        ctx.LogFileModified("src/App.cs", "edited lines 10-50");
        var md = ctx.GenerateMarkdown();
        Assert.Contains("src/App.cs", md);
        Assert.Contains("edited lines 10-50", md);
    }

    [Fact]
    public void LogDecision_RecordsKeyDecision()
    {
        var ctx = new SessionContext(CreateTempDir(), "general");
        ctx.LogDecision("Using repository pattern for data access");
        var md = ctx.GenerateMarkdown();
        Assert.Contains("repository pattern", md);
    }

    [Fact]
    public void SaveToFile_CreatesGeminiDirectory()
    {
        var tempDir = CreateTempDir();
        var ctx = new SessionContext(tempDir, "general");
        ctx.LogDecision("Test decision");
        ctx.SaveToFile();

        var filePath = Path.Combine(tempDir, ".gemini", "session-context.md");
        Assert.True(File.Exists(filePath));
        var content = File.ReadAllText(filePath);
        Assert.Contains("Test decision", content);
    }

    [Fact]
    public void GenerateMarkdown_IncludesHeader()
    {
        var ctx = new SessionContext(CreateTempDir(), "code-reviewer");
        var md = ctx.GenerateMarkdown();
        Assert.Contains("# Session Context", md);
        Assert.Contains("code-reviewer", md);
    }

    [Fact]
    public void AddConversationSummary_AppendsToSummary()
    {
        var ctx = new SessionContext(CreateTempDir(), "general");
        ctx.AddConversationSummary("User asked to implement auth. Decided on JWT tokens.");
        var md = ctx.GenerateMarkdown();
        Assert.Contains("JWT tokens", md);
    }
}
