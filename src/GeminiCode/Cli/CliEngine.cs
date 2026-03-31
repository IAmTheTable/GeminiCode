// src/GeminiCode/Cli/CliEngine.cs
using System.Diagnostics;
using System.Text.RegularExpressions;
using GeminiCode.Agent;
using GeminiCode.Browser;
using GeminiCode.Permissions;
using GeminiCode.Tools;

namespace GeminiCode.Cli;

public class CliEngine
{
    private readonly AgentOrchestrator _orchestrator;
    private readonly CommandHandler _commands;
    private readonly BrowserBridge _browser;
    private readonly ToolRegistry _tools;
    private readonly PermissionGate _permissionGate;
    private readonly ContextProcessor _contextProcessor;
    private string? _lastSavedFile;

    public CliEngine(AgentOrchestrator orchestrator, CommandHandler commands, BrowserBridge browser,
        ToolRegistry tools, PermissionGate permissionGate, ContextProcessor contextProcessor)
    {
        _orchestrator = orchestrator;
        _commands = commands;
        _browser = browser;
        _tools = tools;
        _permissionGate = permissionGate;
        _contextProcessor = contextProcessor;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        Console.WriteLine();
        Console.Write($"{AnsiHelper.Green}>{AnsiHelper.Reset} ");

        while (!ct.IsCancellationRequested)
        {
            var input = ReadInput();
            if (input == null) break;

            input = input.Trim();
            if (string.IsNullOrEmpty(input))
            {
                Console.Write($"{AnsiHelper.Green}>{AnsiHelper.Reset} ");
                continue;
            }

            // Slash commands
            if (await _commands.TryHandleAsync(input))
            {
                Console.Write($"\n{AnsiHelper.Green}>{AnsiHelper.Reset} ");
                continue;
            }

            // Smart local commands — detect "run <file>" patterns and execute directly
            if (await TryHandleLocalRunAsync(input, ct))
            {
                Console.Write($"\n{AnsiHelper.Green}>{AnsiHelper.Reset} ");
                continue;
            }

            // Process @context references
            var (expandedInput, contextCount) = _contextProcessor.Process(input);
            if (contextCount > 0)
            {
                Console.WriteLine($"{AnsiHelper.Cyan}Attached {contextCount} context(s){AnsiHelper.Reset}");
                input = expandedInput;
            }

            // Send to Gemini
            Console.WriteLine($"{AnsiHelper.Dim}Sending to Gemini...{AnsiHelper.Reset}");

            try
            {
                var result = await _orchestrator.SendAndProcessAsync(input, ct);
                if (result == null)
                    Console.WriteLine($"\n{AnsiHelper.Yellow}Gemini response timed out. Type your message to retry, or /new to start fresh.{AnsiHelper.Reset}");
            }
            catch (OperationCanceledException) when (_browser.BrowserClosedToken.IsCancellationRequested)
            {
                Console.Write($"\n{AnsiHelper.Yellow}Browser closed. Restart browser or exit? [r/e]{AnsiHelper.Reset} > ");
                var choice = Console.ReadLine()?.Trim().ToLowerInvariant();
                if (choice == "r")
                {
                    Console.WriteLine("Restarting browser...");
                    await _browser.StartAsync();
                    Console.WriteLine($"{AnsiHelper.Green}Browser restarted.{AnsiHelper.Reset}");
                }
                else
                {
                    Console.WriteLine("Goodbye.");
                    return;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{AnsiHelper.Red}Error: {ex.Message}{AnsiHelper.Reset}");
            }

            Console.Write($"\n{AnsiHelper.Green}>{AnsiHelper.Reset} ");
        }
    }

    /// <summary>Track the last file we saved so "run it" / "run the script" works.</summary>
    public void NotifyFileSaved(string path) => _lastSavedFile = path;

    /// <summary>Detect "run script.py", "run it", "run the script", "execute it" and handle locally.</summary>
    private async Task<bool> TryHandleLocalRunAsync(string input, CancellationToken ct)
    {
        // Match: "run it", "run the script", "run script.py", "run", "execute it", "run command", etc.
        var runMatch = Regex.Match(input, @"^(?:run|execute|launch)\s*(?:the\s+)?(?:script|it|file|that|command|this)?\s*$", RegexOptions.IgnoreCase);
        var runFileMatch = Regex.Match(input, @"^(?:run|execute|launch)\s+(\S+\.(?:py|js|ts|sh|ps1|bat|cmd|rb|go|rs))\s*$", RegexOptions.IgnoreCase);

        string? fileToRun = null;

        if (runFileMatch.Success)
        {
            fileToRun = runFileMatch.Groups[1].Value;
        }
        else if (runMatch.Success && _lastSavedFile != null)
        {
            fileToRun = _lastSavedFile;
        }

        if (fileToRun == null) return false;

        // Determine the command based on file extension
        var ext = Path.GetExtension(fileToRun).ToLowerInvariant();
        var command = ext switch
        {
            ".py" => $"python {fileToRun}",
            ".js" => $"node {fileToRun}",
            ".ts" => $"npx ts-node {fileToRun}",
            ".sh" => $"bash {fileToRun}",
            ".ps1" => $"powershell -File {fileToRun}",
            ".bat" or ".cmd" => fileToRun,
            ".rb" => $"ruby {fileToRun}",
            ".go" => $"go run {fileToRun}",
            _ => null
        };

        if (command == null) return false;

        Console.WriteLine($"{AnsiHelper.Cyan}Running: {command}{AnsiHelper.Reset}");

        // Use RunCommand tool with permission check
        var runTool = _tools.GetTool("RunCommand");
        if (runTool == null) return false;

        var runParams = new Dictionary<string, System.Text.Json.JsonElement>();
        using var doc = System.Text.Json.JsonDocument.Parse(
            System.Text.Json.JsonSerializer.Serialize(new { command }));
        foreach (var prop in doc.RootElement.EnumerateObject())
            runParams[prop.Name] = prop.Value.Clone();

        var permission = _permissionGate.RequestPermission(runTool, runParams);
        if (permission == PermissionResult.Denied)
        {
            Console.WriteLine("Permission denied.");
            return true;
        }

        var result = await runTool.ExecuteAsync(runParams, ct);
        Console.WriteLine(result.Output);
        return true;
    }

    private static string? ReadInput()
    {
        var lines = new List<string>();
        string? line;

        // Use tab-completing reader when terminal supports it, fallback otherwise
        try
        {
            line = InputReader.ReadLine();
        }
        catch
        {
            // Fallback for piped input / non-interactive terminals
            line = Console.ReadLine();
        }

        if (line == null) return null;

        // /paste mode: read until "END" on its own line
        if (line.Trim().Equals("/paste", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"{AnsiHelper.Dim}Paste mode: enter text, then type END on a new line to finish.{AnsiHelper.Reset}");
            while (true)
            {
                var pasteLine = Console.ReadLine();
                if (pasteLine == null || pasteLine.Trim() == "END") break;
                lines.Add(pasteLine);
            }
            return string.Join("\n", lines);
        }

        // Multi-line: trailing backslash
        while (line.EndsWith('\\'))
        {
            lines.Add(line[..^1]);
            Console.Write($"{AnsiHelper.Gray}  ...{AnsiHelper.Reset} ");
            try { line = InputReader.ReadLine(); }
            catch { line = Console.ReadLine(); }
            if (line == null) break;
        }

        if (line != null) lines.Add(line);
        return string.Join("\n", lines);
    }
}
