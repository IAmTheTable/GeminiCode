// src/GeminiCode/Tools/MakeDirTool.cs
using System.Text.Json;
namespace GeminiCode.Tools;

public class MakeDirTool : ITool
{
    private readonly PathSandbox _sandbox;
    public string Name => "MakeDir";
    public RiskLevel Risk => RiskLevel.Medium;
    public MakeDirTool(PathSandbox sandbox) => _sandbox = sandbox;

    public Task<ToolResult> ExecuteAsync(Dictionary<string, JsonElement> parameters, CancellationToken ct)
    {
        try
        {
            var path = _sandbox.Resolve(parameters["path"].GetString()!);
            Directory.CreateDirectory(path);
            return Task.FromResult(new ToolResult(Name, true, $"Created directory {parameters["path"].GetString()}"));
        }
        catch (SandboxViolationException ex) { return Task.FromResult(new ToolResult(Name, false, ex.Message)); }
        catch (Exception ex) { return Task.FromResult(new ToolResult(Name, false, ex.Message)); }
    }

    public string DescribeAction(Dictionary<string, JsonElement> p) => $"Mkdir: {p["path"].GetString()}";
}
