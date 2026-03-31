// src/GeminiCode/Tools/GrepTool.cs
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GeminiCode.Tools;

public class GrepTool : ITool
{
    private readonly PathSandbox _sandbox;
    private const int MaxOutputBytes = 100 * 1024;
    private const int MaxFiles = 5000;

    // Common binary/generated directories to skip
    private static readonly HashSet<string> SkipDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", ".git", "bin", "obj", ".vs", ".idea",
        "__pycache__", ".mypy_cache", ".pytest_cache", "dist",
        "build", "target", "packages", ".nuget", "coverage",
        ".next", ".cache", "vendor"
    };

    private static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".pdb", ".so", ".dylib", ".o", ".a",
        ".zip", ".tar", ".gz", ".7z", ".rar",
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".svg",
        ".pdf", ".doc", ".docx", ".xls", ".xlsx",
        ".woff", ".woff2", ".ttf", ".eot",
        ".mp3", ".mp4", ".avi", ".mov", ".wav",
        ".db", ".sqlite", ".mdb"
    };

    public string Name => "Grep";
    public RiskLevel Risk => RiskLevel.Low;

    public GrepTool(PathSandbox sandbox) => _sandbox = sandbox;

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

            var contextLines = parameters.TryGetValue("context", out var ctxEl) && ctxEl.ValueKind == JsonValueKind.Number
                ? ctxEl.GetInt32() : 0;

            var caseInsensitive = parameters.TryGetValue("case_insensitive", out var ciEl) &&
                                  ciEl.ValueKind == JsonValueKind.True;

            if (!Directory.Exists(basePath))
                return new ToolResult(Name, false, $"Directory not found: {basePath}");

            Regex regex;
            try
            {
                var opts = RegexOptions.Compiled;
                if (caseInsensitive) opts |= RegexOptions.IgnoreCase;
                regex = new Regex(pattern, opts, TimeSpan.FromSeconds(5));
            }
            catch (RegexParseException ex)
            {
                return new ToolResult(Name, false, $"Invalid regex pattern: {ex.Message}");
            }

            var sb = new StringBuilder();
            var matchCount = 0;
            var filesSearched = 0;
            var filesMatched = 0;

            foreach (var file in EnumerateSourceFiles(basePath, includeGlob))
            {
                if (ct.IsCancellationRequested) break;
                if (++filesSearched > MaxFiles) break;

                var ext = Path.GetExtension(file);
                if (BinaryExtensions.Contains(ext)) continue;

                string[] lines;
                try { lines = await File.ReadAllLinesAsync(file, ct); }
                catch { continue; }

                var relativePath = Path.GetRelativePath(_sandbox.WorkingDirectory, file);
                var matchedLineIndices = new HashSet<int>();

                // First pass: find all matching lines
                for (int i = 0; i < lines.Length; i++)
                {
                    if (regex.IsMatch(lines[i]))
                        matchedLineIndices.Add(i);
                }

                if (matchedLineIndices.Count == 0) continue;

                filesMatched++;

                // Build output with context
                var outputLines = new SortedSet<int>();
                foreach (var idx in matchedLineIndices)
                {
                    for (int c = Math.Max(0, idx - contextLines); c <= Math.Min(lines.Length - 1, idx + contextLines); c++)
                        outputLines.Add(c);
                }

                sb.AppendLine($"── {relativePath} ──");
                int? lastLine = null;
                foreach (var lineIdx in outputLines)
                {
                    if (lastLine.HasValue && lineIdx > lastLine.Value + 1)
                        sb.AppendLine("  ...");

                    var prefix = matchedLineIndices.Contains(lineIdx) ? ">" : " ";
                    sb.AppendLine($"{prefix} {lineIdx + 1,4}│ {lines[lineIdx]}");
                    matchCount++;
                    lastLine = lineIdx;
                }
                sb.AppendLine();

                if (sb.Length > MaxOutputBytes)
                {
                    sb.AppendLine($"[Output truncated at 100KB. {matchCount}+ matches across {filesMatched}+ files.]");
                    return new ToolResult(Name, true, sb.ToString().TrimEnd());
                }
            }

            if (matchCount == 0)
                return new ToolResult(Name, true, $"No matches found. Searched {filesSearched} files.");

            sb.Insert(0, $"Found matches in {filesMatched} file(s) ({filesSearched} searched):\n\n");
            return new ToolResult(Name, true, sb.ToString().TrimEnd());
        }
        catch (SandboxViolationException ex)
        {
            return new ToolResult(Name, false, ex.Message);
        }
    }

    public string DescribeAction(Dictionary<string, JsonElement> parameters)
        => $"Grep: {parameters["pattern"].GetString()}";

    private IEnumerable<string> EnumerateSourceFiles(string basePath, string glob)
    {
        var queue = new Queue<string>();
        queue.Enqueue(basePath);

        while (queue.Count > 0)
        {
            var dir = queue.Dequeue();

            // Enumerate files in current directory
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(dir, glob); }
            catch { continue; }

            foreach (var f in files)
                yield return f;

            // Recurse into subdirectories, skipping known junk
            IEnumerable<string> subdirs;
            try { subdirs = Directory.EnumerateDirectories(dir); }
            catch { continue; }

            foreach (var sub in subdirs)
            {
                var dirName = Path.GetFileName(sub);
                if (!SkipDirs.Contains(dirName) && !dirName.StartsWith('.'))
                    queue.Enqueue(sub);
            }
        }
    }
}
