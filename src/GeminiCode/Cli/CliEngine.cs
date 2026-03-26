// src/GeminiCode/Cli/CliEngine.cs
using GeminiCode.Agent;
using GeminiCode.Browser;

namespace GeminiCode.Cli;

public class CliEngine
{
    private readonly AgentOrchestrator _orchestrator;
    private readonly CommandHandler _commands;
    private readonly BrowserBridge _browser;

    public CliEngine(AgentOrchestrator orchestrator, CommandHandler commands, BrowserBridge browser)
    {
        _orchestrator = orchestrator;
        _commands = commands;
        _browser = browser;
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

    private static string? ReadInput()
    {
        var lines = new List<string>();
        var line = Console.ReadLine();

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
            line = Console.ReadLine();
            if (line == null) break;
        }

        if (line != null) lines.Add(line);
        return string.Join("\n", lines);
    }
}
