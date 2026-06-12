// src/GeminiCode.Tests/GlobToolTests.cs
using System.Text.Json;
using GeminiCode.Tools;

namespace GeminiCode.Tests;

public class GlobToolTests : IDisposable
{
    private readonly string _workDir;
    private readonly PathSandbox _sandbox;
    public GlobToolTests()
    {
        _workDir = Path.Combine(Path.GetTempPath(), $"glob_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_workDir, "src", "sub"));
        File.WriteAllText(Path.Combine(_workDir, "src", "a.cs"), "x");
        File.WriteAllText(Path.Combine(_workDir, "src", "sub", "b.cs"), "x");
        File.WriteAllText(Path.Combine(_workDir, "readme.md"), "x");
        _sandbox = new PathSandbox(_workDir);
    }
    public void Dispose() { if (Directory.Exists(_workDir)) Directory.Delete(_workDir, true); }

    private static Dictionary<string, JsonElement> P(object o)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(o));
        return doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
    }

    [Fact]
    public async Task Glob_RecursivePattern_FindsAllMatches()
    {
        var r = await new GlobTool(_sandbox).ExecuteAsync(P(new { pattern = "**/*.cs" }), default);
        Assert.True(r.Success);
        Assert.Contains("a.cs", r.Output);
        Assert.Contains("b.cs", r.Output);
        Assert.DoesNotContain("readme.md", r.Output);
    }

    [Fact]
    public async Task Glob_NoMatches_ReportsNone()
    {
        var r = await new GlobTool(_sandbox).ExecuteAsync(P(new { pattern = "**/*.py" }), default);
        Assert.True(r.Success);
        Assert.Contains("No files", r.Output);
    }
}
