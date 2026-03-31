// src/GeminiCode/Tools/TreeTool.cs
using System.Text;
using System.Text.Json;

namespace GeminiCode.Tools;

public class TreeTool : ITool
{
    private readonly PathSandbox _sandbox;
    private const int MaxOutputBytes = 100 * 1024;
    private const int DefaultMaxDepth = 4;
    private const int MaxEntries = 2000;

    private static readonly HashSet<string> SkipDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", ".git", "bin", "obj", ".vs", ".idea",
        "__pycache__", ".mypy_cache", ".pytest_cache", "dist",
        "build", "target", "packages", ".nuget", "coverage",
        ".next", ".cache", "vendor", ".angular", ".svelte-kit"
    };

    public string Name => "Tree";
    public RiskLevel Risk => RiskLevel.Low;

    public TreeTool(PathSandbox sandbox) => _sandbox = sandbox;

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, JsonElement> parameters, CancellationToken ct)
    {
        try
        {
            string basePath;
            if (parameters.TryGetValue("path", out var pathEl) && pathEl.ValueKind == JsonValueKind.String)
                basePath = _sandbox.Resolve(pathEl.GetString()!);
            else
                basePath = _sandbox.WorkingDirectory;

            var maxDepth = parameters.TryGetValue("depth", out var depthEl) && depthEl.ValueKind == JsonValueKind.Number
                ? depthEl.GetInt32() : DefaultMaxDepth;

            if (!Directory.Exists(basePath))
                return new ToolResult(Name, false, $"Directory not found: {basePath}");

            var sb = new StringBuilder();
            var relativeName = Path.GetRelativePath(_sandbox.WorkingDirectory, basePath);
            if (relativeName == ".") relativeName = Path.GetFileName(basePath);
            sb.AppendLine($"{relativeName}/");

            var stats = new TreeStats();
            await Task.Run(() => BuildTree(basePath, "", maxDepth, 0, sb, stats, ct), ct);

            sb.AppendLine();
            sb.AppendLine($"{stats.Directories} directories, {stats.Files} files");
            if (stats.Truncated)
                sb.AppendLine("[Tree truncated — use depth parameter or a more specific path]");

            return new ToolResult(Name, true, sb.ToString().TrimEnd());
        }
        catch (SandboxViolationException ex)
        {
            return new ToolResult(Name, false, ex.Message);
        }
    }

    public string DescribeAction(Dictionary<string, JsonElement> parameters)
    {
        var path = parameters.TryGetValue("path", out var p) ? p.GetString() : ".";
        return $"Tree: {path}";
    }

    private void BuildTree(string dirPath, string prefix, int maxDepth, int currentDepth,
        StringBuilder sb, TreeStats stats, CancellationToken ct)
    {
        if (ct.IsCancellationRequested || stats.TotalEntries > MaxEntries)
        {
            stats.Truncated = true;
            return;
        }

        if (currentDepth >= maxDepth)
        {
            // Show collapsed indicator
            try
            {
                var subCount = Directory.GetDirectories(dirPath).Length;
                var fileCount = Directory.GetFiles(dirPath).Length;
                if (subCount > 0 || fileCount > 0)
                    sb.AppendLine($"{prefix}    ... ({subCount} dirs, {fileCount} files)");
            }
            catch { }
            return;
        }

        // Get entries, sorted: directories first, then files
        List<string> dirs;
        List<string> files;
        try
        {
            dirs = Directory.GetDirectories(dirPath)
                .Where(d => !SkipDirs.Contains(Path.GetFileName(d)) && !Path.GetFileName(d).StartsWith('.'))
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            files = Directory.GetFiles(dirPath)
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch { return; }

        var entries = new List<(string path, bool isDir)>();
        foreach (var d in dirs) entries.Add((d, true));
        foreach (var f in files) entries.Add((f, false));

        for (int i = 0; i < entries.Count; i++)
        {
            if (sb.Length > MaxOutputBytes)
            {
                stats.Truncated = true;
                return;
            }

            var (entryPath, isDir) = entries[i];
            var isLast = i == entries.Count - 1;
            var connector = isLast ? "└── " : "├── ";
            var name = Path.GetFileName(entryPath);

            stats.TotalEntries++;

            if (isDir)
            {
                stats.Directories++;
                sb.AppendLine($"{prefix}{connector}{name}/");
                var newPrefix = prefix + (isLast ? "    " : "│   ");
                BuildTree(entryPath, newPrefix, maxDepth, currentDepth + 1, sb, stats, ct);
            }
            else
            {
                stats.Files++;
                var size = FormatFileSize(entryPath);
                sb.AppendLine($"{prefix}{connector}{name}{size}");
            }
        }
    }

    private static string FormatFileSize(string filePath)
    {
        try
        {
            var info = new FileInfo(filePath);
            var bytes = info.Length;
            return bytes switch
            {
                < 1024 => $"  ({bytes}B)",
                < 1024 * 1024 => $"  ({bytes / 1024}KB)",
                _ => $"  ({bytes / (1024 * 1024)}MB)"
            };
        }
        catch { return ""; }
    }

    private class TreeStats
    {
        public int Directories;
        public int Files;
        public int TotalEntries;
        public bool Truncated;
    }
}
