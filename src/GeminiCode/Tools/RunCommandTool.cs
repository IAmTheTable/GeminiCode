// src/GeminiCode/Tools/RunCommandTool.cs
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace GeminiCode.Tools;

public class RunCommandTool : ITool
{
    private readonly PathSandbox _sandbox;
    private const int MaxOutputBytes = 100 * 1024;
    private const int DefaultTimeoutMs = 120_000;

    public string Name => "RunCommand";
    public RiskLevel Risk => RiskLevel.High;

    public RunCommandTool(PathSandbox sandbox) => _sandbox = sandbox;

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, JsonElement> parameters, CancellationToken ct)
    {
        var command = parameters["command"].GetString()!;
        var timeoutMs = parameters.TryGetValue("timeout_ms", out var t) && t.ValueKind == JsonValueKind.Number
            ? t.GetInt32() : DefaultTimeoutMs;

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c {command}",
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
            cts.CancelAfter(timeoutMs);

            var stdout = new StringBuilder();
            var stderr = new StringBuilder();

            var readOut = process.StandardOutput.ReadToEndAsync(cts.Token);
            var readErr = process.StandardError.ReadToEndAsync(cts.Token);

            await process.WaitForExitAsync(cts.Token);

            stdout.Append(await readOut);
            stderr.Append(await readErr);

            var output = stdout.ToString();
            if (stderr.Length > 0)
                output += $"\n[stderr]\n{stderr}";

            if (output.Length > MaxOutputBytes)
                output = output[..MaxOutputBytes] + "\n[Output truncated at 100KB.]";

            return new ToolResult(Name, process.ExitCode == 0, output);
        }
        catch (OperationCanceledException)
        {
            return new ToolResult(Name, false, $"Command timed out after {timeoutMs}ms.");
        }
    }

    public string DescribeAction(Dictionary<string, JsonElement> parameters)
        => $"Run: {parameters["command"].GetString()}";
}
