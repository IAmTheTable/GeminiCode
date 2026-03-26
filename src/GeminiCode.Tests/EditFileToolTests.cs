using System.Text.Json;
using GeminiCode.Tools;

namespace GeminiCode.Tests;

public class EditFileToolTests : IDisposable
{
    private readonly string _workDir;
    private readonly PathSandbox _sandbox;

    public EditFileToolTests()
    {
        _workDir = Path.Combine(Path.GetTempPath(), $"edit_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workDir);
        _sandbox = new PathSandbox(_workDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_workDir))
            Directory.Delete(_workDir, true);
    }

    private Dictionary<string, JsonElement> MakeParams(string path, string oldStr, string newStr)
    {
        var json = JsonSerializer.Serialize(new { path, old_string = oldStr, new_string = newStr });
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
    }

    [Fact]
    public async Task Execute_ExactMatch_ReplacesSuccessfully()
    {
        var file = Path.Combine(_workDir, "test.cs");
        await File.WriteAllTextAsync(file, "Hello World");
        var tool = new EditFileTool(_sandbox);
        var result = await tool.ExecuteAsync(MakeParams("test.cs", "Hello", "Goodbye"), CancellationToken.None);
        Assert.True(result.Success);
        Assert.Equal("Goodbye World", await File.ReadAllTextAsync(file));
    }

    [Fact]
    public async Task Execute_NotFound_ReturnsError()
    {
        var file = Path.Combine(_workDir, "test.cs");
        await File.WriteAllTextAsync(file, "Hello World");
        var tool = new EditFileTool(_sandbox);
        var result = await tool.ExecuteAsync(MakeParams("test.cs", "Missing", "Replacement"), CancellationToken.None);
        Assert.False(result.Success);
        Assert.Contains("not found", result.Output);
    }

    [Fact]
    public async Task Execute_MultipleMatches_ReturnsError()
    {
        var file = Path.Combine(_workDir, "test.cs");
        await File.WriteAllTextAsync(file, "aaa bbb aaa");
        var tool = new EditFileTool(_sandbox);
        var result = await tool.ExecuteAsync(MakeParams("test.cs", "aaa", "ccc"), CancellationToken.None);
        Assert.False(result.Success);
        Assert.Contains("matches 2 locations", result.Output);
    }

    [Fact]
    public async Task Execute_LineEndingNormalization_MatchesCRLF()
    {
        var file = Path.Combine(_workDir, "test.cs");
        await File.WriteAllTextAsync(file, "line1\r\nline2\r\nline3");
        var tool = new EditFileTool(_sandbox);
        var result = await tool.ExecuteAsync(MakeParams("test.cs", "line1\nline2", "replaced"), CancellationToken.None);
        Assert.True(result.Success);
        var content = await File.ReadAllTextAsync(file);
        Assert.StartsWith("replaced", content);
        Assert.Contains("\r\n", content);
    }
}
