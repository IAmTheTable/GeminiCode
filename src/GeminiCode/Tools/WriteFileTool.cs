// src/GeminiCode/Tools/WriteFileTool.cs
using System.Text.Json;

namespace GeminiCode.Tools;

public class WriteFileTool : ITool
{
    private readonly PathSandbox _sandbox;

    public string Name => "WriteFile";
    public RiskLevel Risk => RiskLevel.Medium;

    public WriteFileTool(PathSandbox sandbox) => _sandbox = sandbox;

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, JsonElement> parameters, CancellationToken ct)
    {
        try
        {
            var path = parameters["path"].GetString()!;
            var content = parameters["content"].GetString()!;
            var resolved = _sandbox.Resolve(path);

            var dir = Path.GetDirectoryName(resolved)!;
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            await File.WriteAllTextAsync(resolved, content, ct);
            var lineCount = content.Split('\n').Length;
            return new ToolResult(Name, true, $"Written {lineCount} lines to {path}");
        }
        catch (SandboxViolationException ex)
        {
            return new ToolResult(Name, false, ex.Message);
        }
    }

    public string DescribeAction(Dictionary<string, JsonElement> parameters)
    {
        var path = parameters["path"].GetString();
        var content = parameters["content"].GetString() ?? "";
        var lines = content.Split('\n').Length;
        return $"Write: {path} ({lines} lines)";
    }
}
