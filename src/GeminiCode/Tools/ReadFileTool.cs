// src/GeminiCode/Tools/ReadFileTool.cs
using System.Text;
using System.Text.Json;

namespace GeminiCode.Tools;

public class ReadFileTool : ITool
{
    private readonly PathSandbox _sandbox;
    private const int MaxOutputBytes = 100 * 1024;
    private const int DefaultLimit = 2000;

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

            // Support offset (1-based line number) and limit (number of lines)
            var offset = parameters.TryGetValue("offset", out var offEl) && offEl.ValueKind == JsonValueKind.Number
                ? offEl.GetInt32() : 1;
            var limit = parameters.TryGetValue("limit", out var limEl) && limEl.ValueKind == JsonValueKind.Number
                ? limEl.GetInt32() : 0; // 0 = read all

            var allLines = await File.ReadAllLinesAsync(resolved, ct);
            var totalLines = allLines.Length;

            // Clamp offset to valid range (1-based)
            if (offset < 1) offset = 1;
            if (offset > totalLines)
                return new ToolResult(Name, true, $"[File has {totalLines} lines. Offset {offset} is past end of file.]");

            var startIdx = offset - 1;
            var endIdx = limit > 0 ? Math.Min(startIdx + limit, totalLines) : totalLines;

            // If full file fits easily, return it all with line numbers
            var sb = new StringBuilder();
            for (int i = startIdx; i < endIdx; i++)
            {
                sb.AppendLine($"{i + 1,5}│ {allLines[i]}");
                if (sb.Length > MaxOutputBytes)
                {
                    sb.AppendLine($"[Output truncated at 100KB. Lines {offset}-{i + 1} of {totalLines}. Use offset/limit for specific sections.]");
                    return new ToolResult(Name, true, sb.ToString());
                }
            }

            // Add metadata header
            var header = limit > 0 || offset > 1
                ? $"[{path} — lines {offset}-{endIdx} of {totalLines}]\n"
                : $"[{path} — {totalLines} lines]\n";

            return new ToolResult(Name, true, header + sb.ToString().TrimEnd());
        }
        catch (SandboxViolationException ex)
        {
            return new ToolResult(Name, false, ex.Message);
        }
    }

    public string DescribeAction(Dictionary<string, JsonElement> parameters)
    {
        var path = parameters["path"].GetString();
        var hasRange = parameters.ContainsKey("offset") || parameters.ContainsKey("limit");
        if (hasRange)
        {
            var off = parameters.TryGetValue("offset", out var o) ? o.GetInt32().ToString() : "1";
            var lim = parameters.TryGetValue("limit", out var l) ? l.GetInt32().ToString() : "all";
            return $"Read: {path} (from line {off}, {lim} lines)";
        }
        return $"Read: {path}";
    }
}
