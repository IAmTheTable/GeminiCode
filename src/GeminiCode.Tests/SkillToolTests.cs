using System.Text.Json;
using GeminiCode.Plugins;
using GeminiCode.Tools;

namespace GeminiCode.Tests;

public class SkillToolTests : IDisposable
{
    private readonly string _root;
    public SkillToolTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"skill_{Guid.NewGuid():N}");
        var dir = Path.Combine(_root, "greet");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "SKILL.md"),
            "---\nname: greet\ndescription: d\n---\nSay hello to {input}");
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private static Dictionary<string, JsonElement> Params(object o)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(o));
        return doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
    }

    [Fact]
    public async Task Execute_ReturnsBodyWithInputSubstituted()
    {
        var reg = new PluginRegistry(new PluginLoader(new[] { _root }));
        var tool = new SkillTool(reg);
        var result = await tool.ExecuteAsync(Params(new { name = "greet", input = "World" }), CancellationToken.None);
        Assert.True(result.Success);
        Assert.Contains("Say hello to World", result.Output);
    }

    [Fact]
    public async Task Execute_UnknownSkill_ReturnsError()
    {
        var reg = new PluginRegistry(new PluginLoader(new[] { _root }));
        var tool = new SkillTool(reg);
        var result = await tool.ExecuteAsync(Params(new { name = "missing" }), CancellationToken.None);
        Assert.False(result.Success);
        Assert.Contains("Unknown skill", result.Output);
    }
}
