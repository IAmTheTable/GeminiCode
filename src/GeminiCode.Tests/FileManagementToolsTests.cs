// src/GeminiCode.Tests/FileManagementToolsTests.cs
using System.Text.Json;
using GeminiCode.Tools;

namespace GeminiCode.Tests;

public class FileManagementToolsTests : IDisposable
{
    private readonly string _workDir;
    private readonly PathSandbox _sandbox;

    public FileManagementToolsTests()
    {
        _workDir = Path.Combine(Path.GetTempPath(), $"fm_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workDir);
        _sandbox = new PathSandbox(_workDir);
    }
    public void Dispose() { if (Directory.Exists(_workDir)) Directory.Delete(_workDir, true); }

    private static Dictionary<string, JsonElement> P(object o)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(o));
        return doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
    }

    [Fact]
    public async Task Copy_DuplicatesFile()
    {
        await File.WriteAllTextAsync(Path.Combine(_workDir, "a.txt"), "hi");
        var r = await new CopyFileTool(_sandbox).ExecuteAsync(P(new { source = "a.txt", destination = "b.txt" }), default);
        Assert.True(r.Success);
        Assert.True(File.Exists(Path.Combine(_workDir, "b.txt")));
        Assert.True(File.Exists(Path.Combine(_workDir, "a.txt")));
    }

    [Fact]
    public async Task Move_RenamesFile()
    {
        await File.WriteAllTextAsync(Path.Combine(_workDir, "a.txt"), "hi");
        var r = await new MoveFileTool(_sandbox).ExecuteAsync(P(new { source = "a.txt", destination = "c.txt" }), default);
        Assert.True(r.Success);
        Assert.False(File.Exists(Path.Combine(_workDir, "a.txt")));
        Assert.True(File.Exists(Path.Combine(_workDir, "c.txt")));
    }

    [Fact]
    public async Task MakeDir_CreatesDirectory()
    {
        var r = await new MakeDirTool(_sandbox).ExecuteAsync(P(new { path = "sub/dir" }), default);
        Assert.True(r.Success);
        Assert.True(Directory.Exists(Path.Combine(_workDir, "sub", "dir")));
    }

    [Fact]
    public async Task Delete_MovesToTrash_NotHardDelete()
    {
        await File.WriteAllTextAsync(Path.Combine(_workDir, "a.txt"), "hi");
        var r = await new DeleteFileTool(_sandbox).ExecuteAsync(P(new { path = "a.txt" }), default);
        Assert.True(r.Success);
        Assert.False(File.Exists(Path.Combine(_workDir, "a.txt")));
        var trash = Path.Combine(_workDir, ".gemini", "trash");
        Assert.True(Directory.Exists(trash));
        Assert.NotEmpty(Directory.GetFiles(trash, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Copy_OutsideSandbox_IsRejected()
    {
        await File.WriteAllTextAsync(Path.Combine(_workDir, "a.txt"), "hi");
        var r = await new CopyFileTool(_sandbox).ExecuteAsync(P(new { source = "a.txt", destination = "../escape.txt" }), default);
        Assert.False(r.Success);
        Assert.Contains("Access denied", r.Output);
    }
}
