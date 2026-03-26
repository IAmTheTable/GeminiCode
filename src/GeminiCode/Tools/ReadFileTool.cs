// src/GeminiCode/Tools/ReadFileTool.cs
using System.Text.Json;

namespace GeminiCode.Tools;

public class ReadFileTool : ITool
{
    private readonly PathSandbox _sandbox;
    private const int MaxOutputBytes = 100 * 1024;

    public string Name => "ReadFile";
    public RiskLevel Risk => RiskLevel.Low;

    public ReadFileTool(PathSandbox sandbox) => _sandbox = sandbox;

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, JsonElement> parameters, CancellationToken ct)
    {
        try
        {
            var path = parameters["path"].GetString()!;
            var resolved = _sandbox.Resolve(path);

            if (!File.Exists(resolved))
                return new ToolResult(Name, false, $"File not found: {path}");

            var content = await File.ReadAllTextAsync(resolved, ct);
            if (content.Length > MaxOutputBytes)
                content = content[..MaxOutputBytes] + "\n[Output truncated at 100KB. Request specific sections if needed.]";

            return new ToolResult(Name, true, content);
        }
        catch (SandboxViolationException ex)
        {
            return new ToolResult(Name, false, ex.Message);
        }
    }

    public string DescribeAction(Dictionary<string, JsonElement> parameters)
        => $"Read: {parameters["path"].GetString()}";
}
