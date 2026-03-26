// src/GeminiCode/Agent/AgentOrchestrator.cs
using GeminiCode.Browser;
using GeminiCode.Cli;
using GeminiCode.Config;
using GeminiCode.Permissions;
using GeminiCode.Tools;

namespace GeminiCode.Agent;

public class AgentOrchestrator
{
    private readonly BrowserBridge _browser;
    private readonly ToolRegistry _tools;
    private readonly PermissionGate _permissionGate;
    private readonly ConversationManager _conversation;
    private readonly AppSettings _settings;
    private const int MaxRetries = 2;

    public AgentOrchestrator(
        BrowserBridge browser,
        ToolRegistry tools,
        PermissionGate permissionGate,
        ConversationManager conversation,
        AppSettings settings)
    {
        _browser = browser;
        _tools = tools;
        _permissionGate = permissionGate;
        _conversation = conversation;
        _settings = settings;
    }

    /// <summary>Sends the system prompt as a separate initialization message and waits for acknowledgment.</summary>
    public async Task<bool> InitializeSessionAsync(CancellationToken ct)
    {
        if (!_conversation.IsFirstMessage)
            return true;

        Console.WriteLine($"{AnsiHelper.Dim}Initializing agent session...{AnsiHelper.Reset}");
        await _browser.SendMessageAsync(SystemPrompt.Template);
        _conversation.MarkSystemPromptSent();

        var response = await _browser.WaitForResponseAsync(_settings.ResponseTimeoutSeconds, ct);
        if (response != null)
        {
            Console.WriteLine($"{AnsiHelper.Green}Agent ready.{AnsiHelper.Reset}");
            return true;
        }

        Console.WriteLine($"{AnsiHelper.Red}Failed to initialize agent session.{AnsiHelper.Reset}");
        return false;
    }

    public async Task<string?> SendAndProcessAsync(string userMessage, CancellationToken ct)
    {
        // Ensure session is initialized
        if (_conversation.IsFirstMessage)
        {
            if (!await InitializeSessionAsync(ct))
                return null;
        }

        var message = _conversation.PrepareMessage(userMessage);

        // Track response count before sending so we can detect the new response
        await _browser.SendMessageAsync(message);

        var response = await _browser.WaitForResponseAsync(_settings.ResponseTimeoutSeconds, ct);
        if (response == null)
        {
            Console.WriteLine($"{AnsiHelper.Yellow}Gemini response timed out. Type your message to retry, or /new to start fresh.{AnsiHelper.Reset}");
            return null;
        }

        return await ProcessResponseAsync(response, ct);
    }

    // Track code blocks we've already shown to avoid re-showing on follow-up messages
    private readonly HashSet<string> _seenCodeBlocks = new();

    private async Task<string?> ProcessResponseAsync(GeminiResponse response, CancellationToken ct)
    {
        // Clean boilerplate from response text
        var cleanText = CleanResponseText(response.Text);
        var parsed = ToolCallParser.Parse(cleanText);

        // Display conversational text
        if (!string.IsNullOrWhiteSpace(parsed.TextContent))
        {
            var rendered = MarkdownRenderer.Render(parsed.TextContent);
            Console.Write(rendered);
        }

        // If Gemini used <tool_call> format — execute them
        if (parsed.ToolCalls.Count > 0)
        {
            return await ExecuteToolCallsAsync(parsed.ToolCalls, ct);
        }

        // Filter to only NEW code blocks (not seen before)
        var newBlocks = response.CodeBlocks
            .Where(b => !_seenCodeBlocks.Contains(b.Code[..Math.Min(b.Code.Length, 80)]))
            .Where(b => b.Code.Split('\n').Length >= 3) // Skip tiny snippets like "python script.py"
            .ToList();

        // Mark them as seen
        foreach (var b in newBlocks)
            _seenCodeBlocks.Add(b.Code[..Math.Min(b.Code.Length, 80)]);

        // Offer to save new code blocks as files
        if (newBlocks.Count > 0)
        {
            Console.WriteLine($"\n{AnsiHelper.Yellow}Gemini showed {newBlocks.Count} code block(s). Save as files?{AnsiHelper.Reset}");

            foreach (var block in newBlocks)
            {
                var ext = GetExtensionForLanguage(block.Language);
                var suggestedName = $"script{ext}";

                Console.WriteLine($"\n{AnsiHelper.Cyan}--- Code Block ({(string.IsNullOrEmpty(block.Language) ? "detected" : block.Language)}) ---{AnsiHelper.Reset}");
                var preview = string.Join("\n", block.Code.Split('\n').Take(5));
                Console.WriteLine(preview);
                if (block.Code.Split('\n').Length > 5)
                    Console.WriteLine($"{AnsiHelper.Dim}... ({block.Code.Split('\n').Length} lines total){AnsiHelper.Reset}");

                Console.Write($"\nSave as [{AnsiHelper.Bold}{suggestedName}{AnsiHelper.Reset}] (enter filename, 'y' for default, or 'n' to skip): ");
                var input = Console.ReadLine()?.Trim();

                // Handle y/yes as accepting default name
                if (string.IsNullOrEmpty(input) || input.Equals("y", StringComparison.OrdinalIgnoreCase) || input.Equals("yes", StringComparison.OrdinalIgnoreCase))
                    input = suggestedName;

                if (input.Equals("n", StringComparison.OrdinalIgnoreCase) || input.Equals("no", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Use WriteFileTool to save
                var writeTool = _tools.GetTool("WriteFile");
                if (writeTool != null)
                {
                    var writeParams = new Dictionary<string, System.Text.Json.JsonElement>();
                    using var doc = System.Text.Json.JsonDocument.Parse(
                        System.Text.Json.JsonSerializer.Serialize(new { path = input, content = block.Code }));
                    foreach (var prop in doc.RootElement.EnumerateObject())
                        writeParams[prop.Name] = prop.Value.Clone();

                    var permission = _permissionGate.RequestPermission(writeTool, writeParams);
                    if (permission != PermissionResult.Denied)
                    {
                        var result = await writeTool.ExecuteAsync(writeParams, ct);
                        var color = result.Success ? AnsiHelper.Green : AnsiHelper.Red;
                        Console.WriteLine($"{color}{result.Output}{AnsiHelper.Reset}");
                    }
                }
            }
        }

        return parsed.TextContent;
    }

    /// <summary>Remove known Gemini page boilerplate from response text.</summary>
    private static string CleanResponseText(string text)
    {
        // Remove common boilerplate that leaks from the page footer/header
        string[] boilerplate = [
            "Welcome to Gemini, your personal AI assistant",
            "Google Terms",
            "Opens in a new window",
            "Gemini Apps Privacy Notice",
            "Chats are reviewed and used to improve Google AI",
            "Learn about your choices",
            "Gemini can make mistakes, so double-check it",
            "Info about your location",
            "is also stored with your Gemini Apps activity",
            "Gemini is AI and can make mistakes.",
            "Tools\nFast",
            "Tools\nPro",
            "Tools\nThinking",
        ];

        foreach (var bp in boilerplate)
            text = text.Replace(bp, "", StringComparison.OrdinalIgnoreCase);

        // Clean up excessive whitespace left behind
        text = System.Text.RegularExpressions.Regex.Replace(text.Trim(), @"\n{3,}", "\n\n");
        return text;
    }

    private async Task<string?> ExecuteToolCallsAsync(List<ParsedToolCall> toolCalls, CancellationToken ct)
    {
        var results = new List<string>();

        foreach (var toolCall in toolCalls)
        {
            var tool = _tools.GetTool(toolCall.Name);
            if (tool == null)
            {
                var result = new ToolResult(toolCall.Name, false, $"Unknown tool: {toolCall.Name}");
                results.Add(result.ToProtocolString());
                continue;
            }

            var permission = _permissionGate.RequestPermission(tool, toolCall.Parameters);

            if (permission == PermissionResult.Denied)
            {
                var result = new ToolResult(toolCall.Name, false, "Permission denied by user");
                results.Add(result.ToProtocolString());
                continue;
            }

            var toolResult = await tool.ExecuteAsync(toolCall.Parameters, ct);
            var color = toolResult.Success ? AnsiHelper.Green : AnsiHelper.Red;
            Console.WriteLine($"{color}{tool.Name}: {(toolResult.Success ? "OK" : "FAILED")}{AnsiHelper.Reset}");
            results.Add(toolResult.ToProtocolString());
        }

        // Send results back to Gemini
        var combinedResults = _conversation.PrepareToolResults(results);
        await _browser.SendMessageAsync(combinedResults);

        var followUp = await _browser.WaitForResponseAsync(_settings.ResponseTimeoutSeconds, ct);
        if (followUp == null) return null;

        return await ProcessResponseAsync(followUp, ct);
    }

    private static string GetExtensionForLanguage(string lang) => lang.ToLowerInvariant() switch
    {
        "python" or "py" => ".py",
        "javascript" or "js" => ".js",
        "typescript" or "ts" => ".ts",
        "csharp" or "cs" or "c#" => ".cs",
        "java" => ".java",
        "cpp" or "c++" => ".cpp",
        "c" => ".c",
        "go" => ".go",
        "rust" or "rs" => ".rs",
        "ruby" or "rb" => ".rb",
        "php" => ".php",
        "bash" or "sh" or "shell" => ".sh",
        "powershell" or "ps1" => ".ps1",
        "sql" => ".sql",
        "html" => ".html",
        "css" => ".css",
        "json" => ".json",
        "yaml" or "yml" => ".yaml",
        "xml" => ".xml",
        _ => ".txt"
    };
}
