// src/GeminiCode/Tools/MoveFileTool.cs
using System.Text.Json;
namespace GeminiCode.Tools;

public class MoveFileTool : ITool
{
    private readonly PathSandbox _sandbox;
    public string Name => "MoveFile";
    public RiskLevel Risk => RiskLevel.Medium;
    public MoveFileTool(PathSandbox sandbox) => _sandbox = sandbox;

    public Task<ToolResult> ExecuteAsync(Dictionary<string, JsonElement> parameters, CancellationToken ct)
    {
        try
        {
            var src = _sandbox.Resolve(parameters["source"].GetString()!);
            var dst = _sandbox.Resolve(parameters["destination"].GetString()!);
            if (!File.Exists(src) && !Directory.Exists(src))
                return Task.FromResult(new ToolResult(Name, false, $"Source not found: {parameters["source"].GetString()}"));
            var dir = Path.GetDirectoryName(dst)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            if (Directory.Exists(src)) Directory.Move(src, dst);
            else File.Move(src, dst, overwrite: true);
            return Task.FromResult(new ToolResult(Name, true, $"Moved to {parameters["destination"].GetString()}"));
        }
        catch (SandboxViolationException ex) { return Task.FromResult(new ToolResult(Name, false, ex.Message)); }
        catch (Exception ex) { return Task.FromResult(new ToolResult(Name, false, ex.Message)); }
    }

    public string DescribeAction(Dictionary<string, JsonElement> p)
        => $"Move: {p["source"].GetString()} -> {p["destination"].GetString()}";
}
