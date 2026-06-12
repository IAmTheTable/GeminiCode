// src/GeminiCode/Tools/DeleteFileTool.cs
using System.Text.Json;
namespace GeminiCode.Tools;

/// <summary>Moves the target into &lt;workdir&gt;/.gemini/trash/ instead of a hard delete — recoverable.</summary>
public class DeleteFileTool : ITool
{
    private readonly PathSandbox _sandbox;
    public string Name => "DeleteFile";
    public RiskLevel Risk => RiskLevel.High;
    public DeleteFileTool(PathSandbox sandbox) => _sandbox = sandbox;

    public Task<ToolResult> ExecuteAsync(Dictionary<string, JsonElement> parameters, CancellationToken ct)
    {
        try
        {
            var rel = parameters["path"].GetString()!;
            var target = _sandbox.Resolve(rel);
            if (!File.Exists(target) && !Directory.Exists(target))
                return Task.FromResult(new ToolResult(Name, false, $"Not found: {rel}"));

            var trashRoot = Path.Combine(_sandbox.WorkingDirectory, ".gemini", "trash");
            Directory.CreateDirectory(trashRoot);
            // Unique subfolder so repeated deletes of same name don't collide (no Date.Now in tools — use a GUID).
            var bucket = Path.Combine(trashRoot, Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(bucket);
            var dest = Path.Combine(bucket, Path.GetFileName(target.TrimEnd(Path.DirectorySeparatorChar)));

            if (Directory.Exists(target)) Directory.Move(target, dest);
            else File.Move(target, dest);

            return Task.FromResult(new ToolResult(Name, true, $"Moved {rel} to {Path.GetRelativePath(_sandbox.WorkingDirectory, dest)} (recoverable)"));
        }
        catch (SandboxViolationException ex) { return Task.FromResult(new ToolResult(Name, false, ex.Message)); }
        catch (Exception ex) { return Task.FromResult(new ToolResult(Name, false, ex.Message)); }
    }

    public string DescribeAction(Dictionary<string, JsonElement> p) => $"Delete (to trash): {p["path"].GetString()}";
}
