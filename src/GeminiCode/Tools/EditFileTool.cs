// src/GeminiCode/Tools/EditFileTool.cs
using System.Text.Json;

namespace GeminiCode.Tools;

public class EditFileTool : ITool
{
    private readonly PathSandbox _sandbox;

    public string Name => "EditFile";
    public RiskLevel Risk => RiskLevel.Medium;

    public EditFileTool(PathSandbox sandbox) => _sandbox = sandbox;

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, JsonElement> parameters, CancellationToken ct)
    {
        try
        {
            var path = parameters["path"].GetString()!;
            var oldString = parameters["old_string"].GetString()!;
            var newString = parameters["new_string"].GetString()!;
            var resolved = _sandbox.Resolve(path);

            if (!File.Exists(resolved))
                return new ToolResult(Name, false, $"File not found: {path}");

            var content = await File.ReadAllTextAsync(resolved, ct);

            // Detect original line ending
            var hasCrlf = content.Contains("\r\n");

            // Normalize to \n for matching
            var normalized = content.Replace("\r\n", "\n");
            var normalizedOld = oldString.Replace("\r\n", "\n");

            // Count matches
            var matchCount = CountOccurrences(normalized, normalizedOld);
            if (matchCount == 0)
                return new ToolResult(Name, false, "Edit failed: old_string not found in file. Read the file first to get exact content.");
            if (matchCount > 1)
                return new ToolResult(Name, false, $"Edit failed: old_string matches {matchCount} locations. Provide more context to make the match unique.");

            // Replace
            var normalizedNew = newString.Replace("\r\n", "\n");
            var result = normalized.Replace(normalizedOld, normalizedNew);

            // Restore original line endings
            if (hasCrlf)
                result = result.Replace("\n", "\r\n");

            await File.WriteAllTextAsync(resolved, result, ct);
            return new ToolResult(Name, true, $"Edited {path} successfully.");
        }
        catch (SandboxViolationException ex)
        {
            return new ToolResult(Name, false, ex.Message);
        }
    }

    public string DescribeAction(Dictionary<string, JsonElement> parameters)
        => $"Edit: {parameters["path"].GetString()}";

    private static int CountOccurrences(string source, string target)
    {
        int count = 0, index = 0;
        while ((index = source.IndexOf(target, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += target.Length;
        }
        return count;
    }
}
