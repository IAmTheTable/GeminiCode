// src/GeminiCode/Tools/CopyFileTool.cs
using System.Text.Json;
namespace GeminiCode.Tools;

public class CopyFileTool : ITool
{
    private readonly PathSandbox _sandbox;
    public string Name => "CopyFile";
    public RiskLevel Risk => RiskLevel.Medium;
    public CopyFileTool(PathSandbox sandbox) => _sandbox = sandbox;

    public Task<ToolResult> ExecuteAsync(Dictionary<string, JsonElement> parameters, CancellationToken ct)
    {
        try
        {
            var src = _sandbox.Resolve(parameters["source"].GetString()!);
            var dst = _sandbox.Resolve(parameters["destination"].GetString()!);
            if (!File.Exists(src)) return Task.FromResult(new ToolResult(Name, false, $"Source not found: {parameters["source"].GetString()}"));
            var dir = Path.GetDirectoryName(dst)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.Copy(src, dst, overwrite: true);
            return Task.FromResult(new ToolResult(Name, true, $"Copied to {parameters["destination"].GetString()}"));
        }
        catch (SandboxViolationException ex) { return Task.FromResult(new ToolResult(Name, false, ex.Message)); }
        catch (Exception ex) { return Task.FromResult(new ToolResult(Name, false, ex.Message)); }
    }

    public string DescribeAction(Dictionary<string, JsonElement> p)
        => $"Copy: {p["source"].GetString()} -> {p["destination"].GetString()}";
}
