// src/GeminiCode/Tools/GitInfoTool.cs
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace GeminiCode.Tools;

public class GitInfoTool : ITool
{
    private readonly PathSandbox _sandbox;
    private const int MaxOutputBytes = 100 * 1024;
    private const int DefaultTimeoutMs = 30_000;

    private static readonly HashSet<string> AllowedSubcommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "status", "diff", "log", "blame", "branch", "show", "stash list", "remote -v", "tag"
    };

    public string Name => "GitInfo";
    public RiskLevel Risk => RiskLevel.Low;

    public GitInfoTool(PathSandbox sandbox) => _sandbox = sandbox;

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, JsonElement> parameters, CancellationToken ct)
    {
        var subcommand = parameters.TryGetValue("subcommand", out var subEl) && subEl.ValueKind == JsonValueKind.String
            ? subEl.GetString()! : "status";

        var args = parameters.TryGetValue("args", out var argsEl) && argsEl.ValueKind == JsonValueKind.String
            ? argsEl.GetString()! : "";

        // Security: only allow read-only git subcommands
        if (!IsAllowedSubcommand(subcommand))
            return new ToolResult(Name, false,
                $"Subcommand '{subcommand}' not allowed. Allowed: {string.Join(", ", AllowedSubcommands)}");

        // Block any args that could be destructive
        if (ContainsDangerousArgs(args))
            return new ToolResult(Name, false, "Dangerous arguments detected. GitInfo is read-only.");

        var gitCommand = string.IsNullOrWhiteSpace(args)
            ? $"git {subcommand}"
            : $"git {subcommand} {args}";

        // Add sensible defaults for common subcommands
        if (subcommand.Equals("log", StringComparison.OrdinalIgnoreCase) && !args.Contains("-n") && !args.Contains("--max-count"))
            gitCommand = $"git log -20 --oneline {args}".Trim();

        if (subcommand.Equals("diff", StringComparison.OrdinalIgnoreCase) && !args.Contains("--stat"))
            gitCommand = $"git diff --stat {args}".Trim();

        var psi = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = gitCommand.Substring(4), // strip "git "
            WorkingDirectory = _sandbox.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var process = Process.Start(psi)!;
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(DefaultTimeoutMs);

            var stdout = await process.StandardOutput.ReadToEndAsync(cts.Token);
            var stderr = await process.StandardError.ReadToEndAsync(cts.Token);
            await process.WaitForExitAsync(cts.Token);

            var output = stdout;
            if (!string.IsNullOrWhiteSpace(stderr) && process.ExitCode != 0)
                output += $"\n[stderr]\n{stderr}";

            if (output.Length > MaxOutputBytes)
                output = output[..MaxOutputBytes] + "\n[Output truncated at 100KB.]";

            if (string.IsNullOrWhiteSpace(output))
                output = "(no output)";

            return new ToolResult(Name, process.ExitCode == 0, output.TrimEnd());
        }
        catch (OperationCanceledException)
        {
            return new ToolResult(Name, false, $"Git command timed out after {DefaultTimeoutMs}ms.");
        }
        catch (Exception ex)
        {
            return new ToolResult(Name, false, $"Git error: {ex.Message}");
        }
    }

    public string DescribeAction(Dictionary<string, JsonElement> parameters)
    {
        var sub = parameters.TryGetValue("subcommand", out var s) ? s.GetString() : "status";
        return $"Git: {sub}";
    }

    private static bool IsAllowedSubcommand(string subcommand)
    {
        var normalized = subcommand.Trim().ToLowerInvariant();
        return AllowedSubcommands.Contains(normalized) ||
               normalized.StartsWith("log") ||
               normalized.StartsWith("diff") ||
               normalized.StartsWith("blame") ||
               normalized.StartsWith("show") ||
               normalized.StartsWith("branch") ||
               normalized.StartsWith("stash") ||
               normalized.StartsWith("remote") ||
               normalized.StartsWith("tag");
    }

    private static bool ContainsDangerousArgs(string args)
    {
        var lower = args.ToLowerInvariant();
        // Block any write operations smuggled via args
        string[] dangerous = ["--force", "-f", "--delete", "-d", "--hard", "push", "reset", "checkout",
            "merge", "rebase", "cherry-pick", "revert", "commit", "stash drop", "stash pop",
            "clean", "gc", "prune"];
        return dangerous.Any(d => lower.Contains(d));
    }
}
