// src/GeminiCode/Tools/ListFilesTool.cs
using System.Text;
using System.Text.Json;

namespace GeminiCode.Tools;

public class ListFilesTool : ITool
{
    private readonly PathSandbox _sandbox;
    private const int MaxOutputBytes = 100 * 1024;

    public string Name => "ListFiles";
    public RiskLevel Risk => RiskLevel.Low;

    public ListFilesTool(PathSandbox sandbox) => _sandbox = sandbox;

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, JsonElement> parameters, CancellationToken ct)
    {
        try
        {
            var pattern = parameters["pattern"].GetString()!;
            string basePath;
            if (parameters.TryGetValue("path", out var pathEl) && pathEl.ValueKind == JsonValueKind.String)
                basePath = _sandbox.Resolve(pathEl.GetString()!);
            else
                basePath = _sandbox.WorkingDirectory;

            if (!Directory.Exists(basePath))
                return new ToolResult(Name, false, $"Directory not found: {basePath}");

            var files = Directory.EnumerateFiles(basePath, pattern, SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(_sandbox.WorkingDirectory, f))
                .OrderBy(f => f)
                .Take(500);

            var sb = new StringBuilder();
            foreach (var file in files)
            {
                sb.AppendLine(file);
                if (sb.Length > MaxOutputBytes)
                {
                    sb.AppendLine("[Output truncated at 100KB. Use a more specific pattern.]");
                    break;
                }
            }

            return new ToolResult(Name, true, sb.ToString().TrimEnd());
        }
        catch (SandboxViolationException ex)
        {
            return new ToolResult(Name, false, ex.Message);
        }
    }

    public string DescribeAction(Dictionary<string, JsonElement> parameters)
        => $"List: {parameters["pattern"].GetString()}";
}
