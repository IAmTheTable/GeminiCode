// src/GeminiCode/Agent/ContextProcessor.cs
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using GeminiCode.Tools;

namespace GeminiCode.Agent;

/// <summary>
/// Expands @context references in user input before sending to Gemini.
/// Supports: @file path[:lines], @tree [path], @git [subcommand], @grep pattern, @find pattern, @diff, @url
/// </summary>
public class ContextProcessor
{
    private readonly PathSandbox _sandbox;
    private const int MaxContextBytes = 80 * 1024; // Leave room in token budget

    /// <summary>Files queued for upload (populated during Process, consumed by caller).</summary>
    public List<string> PendingUploads { get; } = new();

    // Skip directories for tree/find
    private static readonly HashSet<string> SkipDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", ".git", "bin", "obj", ".vs", ".idea",
        "__pycache__", ".mypy_cache", ".pytest_cache", "dist",
        "build", "target", "packages", ".nuget", "coverage",
        ".next", ".cache", "vendor"
    };

    // @file path/to/file.cs
    // @file path/to/file.cs L200-L500
    // @file path/to/file.cs L200
    // @file path/to/file.cs:10-50 (legacy syntax still supported)
    private static readonly Regex FilePattern = new(
        @"@file\s+(\S+?)(?:\s+L(\d+)(?:-L(\d+))?|:(\d+)(?:-(\d+))?)?(?=\s|$)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // @upload path/to/file (always uploads, never text-injects)
    private static readonly Regex UploadPattern = new(
        @"@upload\s+(\S+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // @tree [path] [depth=N]
    private static readonly Regex TreePattern = new(
        @"@tree(?:\s+(\S+))?(?:\s+depth=(\d+))?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // @git status|diff|log|blame [args...]
    private static readonly Regex GitPattern = new(
        @"@git\s+(\w+)(?:\s+(.+))?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // @grep pattern [include=*.cs]
    private static readonly Regex GrepPattern = new(
        @"@grep\s+(?:""([^""]+)""|(\S+))(?:\s+include=(\S+))?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // @find pattern
    private static readonly Regex FindPattern = new(
        @"@find\s+(\S+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // @diff (shorthand for git diff)
    private static readonly Regex DiffPattern = new(
        @"@diff(?:\s+(.+))?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // @codebase / @project (inject project overview)
    private static readonly Regex ProjectPattern = new(
        @"@(?:codebase|project)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public ContextProcessor(PathSandbox sandbox)
    {
        _sandbox = sandbox;
    }

    /// <summary>
    /// Process a user message, expanding any @context references.
    /// Returns the expanded message and a count of contexts injected.
    /// </summary>
    public (string expandedMessage, int contextCount) Process(string input)
    {
        var contexts = new List<(string tag, string content)>();
        var processed = input;

        PendingUploads.Clear();

        // Process @file references
        processed = FilePattern.Replace(processed, m =>
        {
            var filePath = m.Groups[1].Value;
            var hasLRange = m.Groups[2].Success;
            var hasColonRange = m.Groups[4].Success;

            if (hasLRange)
            {
                var result = ExpandFile(filePath, m.Groups[2].Value, m.Groups[3].Value);
                if (result != null)
                    contexts.Add(($"@file {filePath}", result));
            }
            else if (hasColonRange)
            {
                var result = ExpandFile(filePath, m.Groups[4].Value, m.Groups[5].Value);
                if (result != null)
                    contexts.Add(($"@file {filePath}", result));
            }
            else
            {
                try
                {
                    var resolved = _sandbox.Resolve(filePath);
                    if (File.Exists(resolved))
                    {
                        PendingUploads.Add(resolved);
                        contexts.Add(($"@file {filePath}", $"[File queued for upload: {filePath}]"));
                    }
                    else
                    {
                        contexts.Add(($"@file {filePath}", $"[File not found: {filePath}]"));
                    }
                }
                catch (SandboxViolationException)
                {
                    contexts.Add(($"@file {filePath}", $"[Access denied: {filePath} is outside working directory]"));
                }
            }
            return "";
        });

        // Process @upload references
        processed = UploadPattern.Replace(processed, m =>
        {
            var filePath = m.Groups[1].Value;
            try
            {
                var resolved = _sandbox.Resolve(filePath);
                if (File.Exists(resolved))
                {
                    PendingUploads.Add(resolved);
                    contexts.Add(($"@upload {filePath}", $"[File queued for upload: {filePath}]"));
                }
                else
                {
                    contexts.Add(($"@upload {filePath}", $"[File not found: {filePath}]"));
                }
            }
            catch (SandboxViolationException)
            {
                contexts.Add(($"@upload {filePath}", $"[Access denied: {filePath} is outside working directory]"));
            }
            return "";
        });

        // Process @tree
        processed = TreePattern.Replace(processed, m =>
        {
            var path = m.Groups[1].Success ? m.Groups[1].Value : null;
            var depth = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 3;
            var result = ExpandTree(path, depth);
            if (result != null)
                contexts.Add(("@tree", result));
            return "";
        });

        // Process @git
        processed = GitPattern.Replace(processed, m =>
        {
            var subcmd = m.Groups[1].Value;
            var args = m.Groups[2].Success ? m.Groups[2].Value : "";
            var result = ExpandGit(subcmd, args);
            if (result != null)
                contexts.Add(($"@git {subcmd}", result));
            return "";
        });

        // Process @diff (shorthand)
        processed = DiffPattern.Replace(processed, m =>
        {
            var args = m.Groups[1].Success ? m.Groups[1].Value : "";
            var result = ExpandGit("diff", args);
            if (result != null)
                contexts.Add(("@diff", result));
            return "";
        });

        // Process @grep
        processed = GrepPattern.Replace(processed, m =>
        {
            var pattern = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
            var include = m.Groups[3].Success ? m.Groups[3].Value : "*";
            var result = ExpandGrep(pattern, include);
            if (result != null)
                contexts.Add(($"@grep {pattern}", result));
            return "";
        });

        // Process @find
        processed = FindPattern.Replace(processed, m =>
        {
            var pattern = m.Groups[1].Value;
            var result = ExpandFind(pattern);
            if (result != null)
                contexts.Add(($"@find {pattern}", result));
            return "";
        });

        // Process @codebase / @project
        processed = ProjectPattern.Replace(processed, m =>
        {
            var result = ExpandProjectOverview();
            if (result != null)
                contexts.Add(("@project", result));
            return "";
        });

        // Clean up the processed message
        processed = Regex.Replace(processed.Trim(), @"\n{3,}", "\n\n");

        if (contexts.Count == 0)
            return (input, 0);

        // Build the final message: user message + injected contexts
        var sb = new StringBuilder();
        sb.AppendLine(processed);
        sb.AppendLine();
        sb.AppendLine("--- Attached Context ---");

        var totalSize = 0;
        foreach (var (tag, content) in contexts)
        {
            if (totalSize + content.Length > MaxContextBytes)
            {
                sb.AppendLine($"\n[Context {tag} truncated — approaching size limit]");
                var remaining = MaxContextBytes - totalSize;
                if (remaining > 200)
                    sb.AppendLine(content[..remaining] + "\n[truncated]");
                break;
            }

            sb.AppendLine($"\n### {tag}");
            sb.AppendLine("```");
            sb.AppendLine(content);
            sb.AppendLine("```");
            totalSize += content.Length;
        }

        return (sb.ToString(), contexts.Count);
    }

    /// <summary>Returns list of supported @context commands for help text.</summary>
    public static string GetHelpText()
    {
        return """
              @file <path>           — Upload file to Gemini
              @file <path> L200-L500 — Attach specific line range as text
              @file <path> L200      — Attach from line 200 onwards as text
              @file <path>:10-50     — Attach line range (legacy syntax)
              @upload <path>         — Upload file to Gemini (explicit)
              @tree [path] [depth=N] — Attach directory tree
              @git <status|diff|log|blame|branch> [args] — Attach git info
              @diff [args]           — Shorthand for @git diff
              @grep <pattern> [include=*.ext] — Attach search results
              @find <glob-pattern>   — Attach file listing
              @codebase              — Attach project overview
            """;
    }

    private string? ExpandFile(string path, string startLine, string endLine)
    {
        try
        {
            var resolved = _sandbox.Resolve(path);
            if (!File.Exists(resolved)) return $"[File not found: {path}]";

            var lines = File.ReadAllLines(resolved);
            var total = lines.Length;

            int start = 1, end = total;
            if (!string.IsNullOrEmpty(startLine))
            {
                start = int.Parse(startLine);
                if (!string.IsNullOrEmpty(endLine))
                    end = int.Parse(endLine);
                else
                    end = total; // From start to end of file
            }

            start = Math.Max(1, Math.Min(start, total));
            end = Math.Max(start, Math.Min(end, total));

            var sb = new StringBuilder();
            var rangeLabel = start == 1 && end == total
                ? $"{path} ({total} lines)"
                : $"{path} (lines {start}-{end} of {total})";
            sb.AppendLine(rangeLabel);

            for (int i = start - 1; i < end; i++)
            {
                sb.AppendLine($"{i + 1,5}│ {lines[i]}");
                if (sb.Length > MaxContextBytes / 2) // Single file shouldn't take more than half the budget
                {
                    sb.AppendLine($"[truncated at line {i + 1}]");
                    break;
                }
            }

            return sb.ToString().TrimEnd();
        }
        catch (SandboxViolationException)
        {
            return $"[Access denied: {path} is outside working directory]";
        }
        catch (Exception ex)
        {
            return $"[Error reading {path}: {ex.Message}]";
        }
    }

    private string? ExpandTree(string? path, int depth)
    {
        try
        {
            var basePath = path != null ? _sandbox.Resolve(path) : _sandbox.WorkingDirectory;
            if (!Directory.Exists(basePath)) return $"[Directory not found: {path}]";

            var sb = new StringBuilder();
            var rootName = Path.GetRelativePath(_sandbox.WorkingDirectory, basePath);
            if (rootName == ".") rootName = Path.GetFileName(basePath);
            sb.AppendLine($"{rootName}/");
            BuildTreeRecursive(basePath, "", depth, 0, sb);
            return sb.ToString().TrimEnd();
        }
        catch (SandboxViolationException)
        {
            return $"[Access denied: {path} is outside working directory]";
        }
    }

    private void BuildTreeRecursive(string dir, string prefix, int maxDepth, int currentDepth, StringBuilder sb)
    {
        if (currentDepth >= maxDepth || sb.Length > MaxContextBytes / 2) return;

        List<string> dirs, files;
        try
        {
            dirs = Directory.GetDirectories(dir)
                .Where(d => !SkipDirs.Contains(Path.GetFileName(d)) && !Path.GetFileName(d).StartsWith('.'))
                .OrderBy(Path.GetFileName).ToList();
            files = Directory.GetFiles(dir).OrderBy(Path.GetFileName).ToList();
        }
        catch { return; }

        var entries = dirs.Select(d => (d, true)).Concat(files.Select(f => (f, false))).ToList();

        for (int i = 0; i < entries.Count; i++)
        {
            var (entryPath, isDir) = entries[i];
            var isLast = i == entries.Count - 1;
            var connector = isLast ? "└── " : "├── ";
            var name = Path.GetFileName(entryPath);

            sb.AppendLine($"{prefix}{connector}{name}{(isDir ? "/" : "")}");

            if (isDir)
            {
                var newPrefix = prefix + (isLast ? "    " : "│   ");
                BuildTreeRecursive(entryPath, newPrefix, maxDepth, currentDepth + 1, sb);
            }
        }
    }

    private string? ExpandGit(string subcommand, string args)
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "status", "diff", "log", "blame", "branch", "show", "remote", "tag" };

        if (!allowed.Contains(subcommand))
            return $"[Unsupported git subcommand: {subcommand}]";

        var gitArgs = subcommand;
        if (subcommand.Equals("log", StringComparison.OrdinalIgnoreCase) && !args.Contains("-n") && !args.Contains("--max-count"))
            gitArgs += " -20 --oneline";
        if (!string.IsNullOrWhiteSpace(args))
            gitArgs += " " + args;

        return RunGitCommand(gitArgs);
    }

    private string? ExpandGrep(string pattern, string include)
    {
        try
        {
            var regex = new Regex(pattern, RegexOptions.Compiled, TimeSpan.FromSeconds(5));
            var sb = new StringBuilder();
            var matchCount = 0;

            foreach (var file in EnumerateSourceFiles(_sandbox.WorkingDirectory, include))
            {
                string[] lines;
                try { lines = File.ReadAllLines(file); }
                catch { continue; }

                var relPath = Path.GetRelativePath(_sandbox.WorkingDirectory, file);

                for (int i = 0; i < lines.Length; i++)
                {
                    if (regex.IsMatch(lines[i]))
                    {
                        sb.AppendLine($"{relPath}:{i + 1}: {lines[i].TrimStart()}");
                        matchCount++;
                        if (sb.Length > MaxContextBytes / 2)
                        {
                            sb.AppendLine($"[truncated — {matchCount}+ matches]");
                            return sb.ToString().TrimEnd();
                        }
                    }
                }
            }

            return matchCount > 0 ? sb.ToString().TrimEnd() : $"No matches for '{pattern}'";
        }
        catch (RegexParseException ex)
        {
            return $"[Invalid regex: {ex.Message}]";
        }
    }

    private string? ExpandFind(string pattern)
    {
        try
        {
            var files = Directory.EnumerateFiles(_sandbox.WorkingDirectory, pattern, SearchOption.AllDirectories)
                .Where(f =>
                {
                    var parts = Path.GetRelativePath(_sandbox.WorkingDirectory, f).Split(Path.DirectorySeparatorChar);
                    return !parts.Any(p => SkipDirs.Contains(p) || p.StartsWith('.'));
                })
                .Select(f => Path.GetRelativePath(_sandbox.WorkingDirectory, f))
                .OrderBy(f => f)
                .Take(500)
                .ToList();

            if (files.Count == 0) return $"No files matching '{pattern}'";
            return string.Join("\n", files) + (files.Count >= 500 ? "\n[truncated at 500 results]" : "");
        }
        catch (Exception ex)
        {
            return $"[Error: {ex.Message}]";
        }
    }

    private string? ExpandProjectOverview()
    {
        var sb = new StringBuilder();
        var workDir = _sandbox.WorkingDirectory;
        sb.AppendLine($"Project: {Path.GetFileName(workDir)}");
        sb.AppendLine($"Path: {workDir.Replace("\\", "/")}");

        // Check for common project files
        var projectIndicators = new[]
        {
            ("*.sln", "Solution"), ("*.csproj", ".NET Project"), ("package.json", "Node.js"),
            ("Cargo.toml", "Rust"), ("go.mod", "Go"), ("pom.xml", "Java/Maven"),
            ("build.gradle", "Java/Gradle"), ("requirements.txt", "Python"), ("pyproject.toml", "Python"),
            ("Gemfile", "Ruby"), ("composer.json", "PHP")
        };

        foreach (var (pattern, label) in projectIndicators)
        {
            var found = Directory.GetFiles(workDir, pattern, SearchOption.TopDirectoryOnly);
            if (found.Length > 0)
                sb.AppendLine($"Type: {label} ({Path.GetFileName(found[0])})");
        }

        // Git info
        var gitStatus = RunGitCommand("branch --show-current");
        if (gitStatus != null)
            sb.AppendLine($"Git branch: {gitStatus.Trim()}");

        // Directory tree (shallow)
        sb.AppendLine("\nStructure:");
        BuildTreeRecursive(workDir, "  ", 2, 0, sb);

        // Count files by extension
        sb.AppendLine("\nFile types:");
        try
        {
            var extCounts = Directory.EnumerateFiles(workDir, "*", SearchOption.AllDirectories)
                .Where(f =>
                {
                    var parts = Path.GetRelativePath(workDir, f).Split(Path.DirectorySeparatorChar);
                    return !parts.Any(p => SkipDirs.Contains(p) || p.StartsWith('.'));
                })
                .GroupBy(f => Path.GetExtension(f).ToLowerInvariant())
                .Where(g => !string.IsNullOrEmpty(g.Key))
                .OrderByDescending(g => g.Count())
                .Take(15);

            foreach (var group in extCounts)
                sb.AppendLine($"  {group.Key}: {group.Count()}");
        }
        catch { }

        return sb.ToString().TrimEnd();
    }

    private string? RunGitCommand(string args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = args,
                WorkingDirectory = _sandbox.WorkingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc == null) return null;
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(10_000);
            return string.IsNullOrWhiteSpace(output) ? "(no output)" : output.TrimEnd();
        }
        catch
        {
            return null;
        }
    }

    private IEnumerable<string> EnumerateSourceFiles(string basePath, string glob)
    {
        var queue = new Queue<string>();
        queue.Enqueue(basePath);

        while (queue.Count > 0)
        {
            var dir = queue.Dequeue();

            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(dir, glob); }
            catch { continue; }

            foreach (var f in files)
                yield return f;

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
