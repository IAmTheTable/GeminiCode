// src/GeminiCode/Tools/SkillTool.cs
using System.Text.Json;
using GeminiCode.Plugins;

namespace GeminiCode.Tools;

/// <summary>Lets Gemini self-invoke a plugin via [SKILL:name]input[/SKILL].
/// Returns the skill's instructions as a tool_result so Gemini follows them next turn
/// (single-shot — phased workflows remain user-triggered via /command).</summary>
public class SkillTool : ITool
{
    private readonly PluginRegistry _registry;

    public string Name => "Skill";
    public RiskLevel Risk => RiskLevel.Low;

    public SkillTool(PluginRegistry registry) => _registry = registry;

    public Task<ToolResult> ExecuteAsync(Dictionary<string, JsonElement> parameters, CancellationToken ct)
    {
        var name = parameters.TryGetValue("name", out var n) && n.ValueKind == JsonValueKind.String
            ? n.GetString()! : "";
        var input = parameters.TryGetValue("input", out var i) && i.ValueKind == JsonValueKind.String
            ? i.GetString()! : "";

        var manifest = _registry.ByName(name);
        if (manifest == null)
            return Task.FromResult(new ToolResult(Name, false, $"Unknown skill: '{name}'. Available: {string.Join(", ", _registry.Plugins.Select(p => p.Name))}"));

        var body = manifest.Body.Replace("{input}", input);
        return Task.FromResult(new ToolResult(Name, true, $"Skill '{manifest.Name}' instructions — follow these now:\n\n{body}"));
    }

    public string DescribeAction(Dictionary<string, JsonElement> parameters)
        => $"Skill: {(parameters.TryGetValue("name", out var n) ? n.GetString() : "?")}";
}
