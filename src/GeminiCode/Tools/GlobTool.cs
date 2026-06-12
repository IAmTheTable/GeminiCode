// src/GeminiCode/Tools/GlobTool.cs
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GeminiCode.Tools;

public class GlobTool : ITool
{
    private readonly PathSandbox _sandbox;
    private const int MaxResults = 1000;
    private static readonly HashSet<string> SkipDirs = new(StringComparer.OrdinalIgnoreCase)
    { "node_modules", ".git", "bin", "obj", ".vs", "dist", "build", "target", "packages" };

    public string Name => "Glob";
    public RiskLevel Risk => RiskLevel.Low;
    public GlobTool(PathSandbox sandbox) => _sandbox = sandbox;

    public Task<ToolResult> ExecuteAsync(Dictionary<string, JsonElement> parameters, CancellationToken ct)
    {
        try
        {
            var pattern = parameters["pattern"].GetString()!.Replace('\\', '/');
            var regex = GlobToRegex(pattern);
            var matches = new List<string>();

            foreach (var file in EnumerateFiles(_sandbox.WorkingDirectory))
            {
                if (ct.IsCancellationRequested) break;
                var rel = Path.GetRelativePath(_sandbox.WorkingDirectory, file).Replace('\\', '/');
                if (regex.IsMatch(rel))
                {
                    matches.Add(rel);
                    if (matches.Count >= MaxResults) break;
                }
            }

            if (matches.Count == 0)
                return Task.FromResult(new ToolResult(Name, true, $"No files match '{pattern}'."));

            var sb = new StringBuilder($"{matches.Count} match(es):\n");
            foreach (var m in matches.OrderBy(x => x)) sb.AppendLine(m);
            return Task.FromResult(new ToolResult(Name, true, sb.ToString().TrimEnd()));
        }
        catch (SandboxViolationException ex) { return Task.FromResult(new ToolResult(Name, false, ex.Message)); }
    }

    public string DescribeAction(Dictionary<string, JsonElement> p) => $"Glob: {p["pattern"].GetString()}";

    private IEnumerable<string> EnumerateFiles(string root)
    {
        var queue = new Queue<string>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var dir = queue.Dequeue();
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(dir); } catch { continue; }
            foreach (var f in files) yield return f;
            IEnumerable<string> subs;
            try { subs = Directory.EnumerateDirectories(dir); } catch { continue; }
            foreach (var s in subs)
            {
                var n = Path.GetFileName(s);
                if (!SkipDirs.Contains(n) && !n.StartsWith('.')) queue.Enqueue(s);
            }
        }
    }

    private static Regex GlobToRegex(string glob)
    {
        var sb = new StringBuilder("^");
        for (int i = 0; i < glob.Length; i++)
        {
            var c = glob[i];
            if (c == '*')
            {
                if (i + 1 < glob.Length && glob[i + 1] == '*')
                {
                    sb.Append(".*");       // ** → any path including /
                    i++;
                    if (i + 1 < glob.Length && glob[i + 1] == '/') i++; // swallow trailing slash
                }
                else sb.Append("[^/]*");   // * → any non-separator run
            }
            else if (c == '?') sb.Append("[^/]");
            else sb.Append(Regex.Escape(c.ToString()));
        }
        sb.Append('$');
        return new Regex(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }
}
