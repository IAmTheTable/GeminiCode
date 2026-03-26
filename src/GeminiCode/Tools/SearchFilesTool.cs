// src/GeminiCode/Tools/SearchFilesTool.cs
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GeminiCode.Tools;

public class SearchFilesTool : ITool
{
    private readonly PathSandbox _sandbox;
    private const int MaxOutputBytes = 100 * 1024;

    public string Name => "SearchFiles";
    public RiskLevel Risk => RiskLevel.Low;

    public SearchFilesTool(PathSandbox sandbox) => _sandbox = sandbox;

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

            var includeGlob = parameters.TryGetValue("include", out var incEl) && incEl.ValueKind == JsonValueKind.String
                ? incEl.GetString()! : "*";

            if (!Directory.Exists(basePath))
                return new ToolResult(Name, false, $"Directory not found: {basePath}");

            Regex regex;
            try
            {
                regex = new Regex(pattern, RegexOptions.Compiled, TimeSpan.FromSeconds(5));
            }
            catch (RegexParseException ex)
            {
                return new ToolResult(Name, false, $"Invalid regex pattern: {ex.Message}");
            }
            var sb = new StringBuilder();
            var matchCount = 0;

            foreach (var file in Directory.EnumerateFiles(basePath, includeGlob, SearchOption.AllDirectories))
            {
                if (ct.IsCancellationRequested) break;

                var relativePath = Path.GetRelativePath(_sandbox.WorkingDirectory, file);
                var lines = await File.ReadAllLinesAsync(file, ct);

                for (int i = 0; i < lines.Length; i++)
                {
                    if (regex.IsMatch(lines[i]))
                    {
                        sb.AppendLine($"{relativePath}:{i + 1}: {lines[i].TrimStart()}");
                        matchCount++;
                        if (sb.Length > MaxOutputBytes)
                        {
                            sb.AppendLine($"[Output truncated at 100KB. {matchCount}+ matches found.]");
                            return new ToolResult(Name, true, sb.ToString().TrimEnd());
                        }
                    }
                }
            }

            return new ToolResult(Name, true,
                matchCount > 0 ? sb.ToString().TrimEnd() : "No matches found.");
        }
        catch (SandboxViolationException ex)
        {
            return new ToolResult(Name, false, ex.Message);
        }
    }

    public string DescribeAction(Dictionary<string, JsonElement> parameters)
        => $"Search: {parameters["pattern"].GetString()}";
}
