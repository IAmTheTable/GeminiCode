// src/GeminiCode/Agent/AgentOrchestrator.cs
using System.Text.RegularExpressions;
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
    private readonly Tools.PathSandbox _sandbox;
    private const int MaxRetries = 2;

    /// <summary>Raised when a file is saved (so CLI can track it for "run it" commands).</summary>
    public event Action<string>? FileSaved;

    public AgentOrchestrator(
        BrowserBridge browser,
        ToolRegistry tools,
        PermissionGate permissionGate,
        ConversationManager conversation,
        AppSettings settings,
        Tools.PathSandbox sandbox)
    {
        _browser = browser;
        _tools = tools;
        _permissionGate = permissionGate;
        _conversation = conversation;
        _settings = settings;
        _sandbox = sandbox;
    }

    /// <summary>Sends the system prompt as a separate initialization message and waits for acknowledgment.</summary>
    public async Task<bool> InitializeSessionAsync(CancellationToken ct)
    {
        if (!_conversation.IsFirstMessage)
            return true;

        Console.WriteLine($"{AnsiHelper.Dim}Initializing agent session...{AnsiHelper.Reset}");
        var initBaseline = await _browser.CaptureBaselineAsync();
        await _browser.SendMessageAsync(SystemPrompt.GenerateTemplate(_sandbox.WorkingDirectory));
        _conversation.MarkSystemPromptSent();

        var response = await _browser.WaitForResponseAsync(_settings.ResponseTimeoutSeconds, ct, initBaseline.textLen, initBaseline.preCount);
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

        // Capture baseline BEFORE sending so we can detect only NEW content
        var baseline = await _browser.CaptureBaselineAsync();
        await _browser.SendMessageAsync(message);

        var response = await _browser.WaitForResponseAsync(_settings.ResponseTimeoutSeconds, ct, baseline.textLen, baseline.preCount);
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

        // If text contained tool calls — execute them
        if (parsed.ToolCalls.Count > 0)
        {
            return await ExecuteToolCallsAsync(parsed.ToolCalls, ct);
        }

        // Also scan code blocks for tool call patterns (Gemini often puts write_file() in code blocks)
        var toolCallsFromBlocks = new List<ParsedToolCall>();
        var regularBlocks = new List<CodeBlock>();

        foreach (var block in response.CodeBlocks)
        {
            var blockParsed = ToolCallParser.Parse(block.Code);
            if (blockParsed.ToolCalls.Count > 0)
            {
                toolCallsFromBlocks.AddRange(blockParsed.ToolCalls);
            }
            else
            {
                regularBlocks.Add(block);
            }
        }

        // Execute any tool calls found in code blocks
        if (toolCallsFromBlocks.Count > 0)
        {
            Console.WriteLine($"\n{AnsiHelper.Cyan}Detected {toolCallsFromBlocks.Count} tool call(s) from Gemini.{AnsiHelper.Reset}");
            return await ExecuteToolCallsAsync(toolCallsFromBlocks, ct);
        }

        // Filter to only NEW code blocks (not seen before)
        var newBlocks = regularBlocks
            .Where(b => !_seenCodeBlocks.Contains(b.Code[..Math.Min(b.Code.Length, 80)]))
            .Where(b => b.Code.Split('\n').Length >= 3)
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
                var codePreview = string.Join("\n", block.Code.Split('\n').Take(5));
                Console.WriteLine(codePreview);
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
                        if (result.Success)
                            FileSaved?.Invoke(input);
                    }
                }
            }
        }

        return parsed.TextContent;
    }

    /// <summary>Remove known Gemini page boilerplate from response text.</summary>
    private static string CleanResponseText(string text)
    {
        // Use regex to strip the entire ToS/disclaimer block in one go
        text = Regex.Replace(text,
            @"Welcome to Gemini.*?Gemini Apps activity\.?",
            "", RegexOptions.Singleline | RegexOptions.IgnoreCase);

        // Strip footer elements
        text = Regex.Replace(text,
            @"Google Terms.*?apply\.?",
            "", RegexOptions.Singleline | RegexOptions.IgnoreCase);

        text = Regex.Replace(text,
            @"Opens in a new window",
            "", RegexOptions.IgnoreCase);

        text = Regex.Replace(text,
            @"Gemini (is AI and )?can make mistakes.*?$",
            "", RegexOptions.Multiline | RegexOptions.IgnoreCase);

        // Strip model picker labels that leak in
        text = Regex.Replace(text, @"\n(Tools\n)?(Fast|Pro|Thinking)\s*$", "", RegexOptions.Multiline);

        // Strip "Code snippet" labels Gemini adds
        text = Regex.Replace(text, @"\nCode snippet\s*\n", "\n", RegexOptions.IgnoreCase);

        // Clean up leftover punctuation fragments from removed boilerplate
        text = Regex.Replace(text, @"\n\s*and the\s*\n", "\n");
        text = Regex.Replace(text, @"\n\s*apply\.\s*\n", "\n");
        text = Regex.Replace(text, @"\n\s*\.\s*\n", "\n");

        // Clean up excessive whitespace
        text = Regex.Replace(text.Trim(), @"\n{3,}", "\n\n");
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
        var followBaseline = await _browser.CaptureBaselineAsync();
        await _browser.SendMessageAsync(combinedResults);

        var followUp = await _browser.WaitForResponseAsync(_settings.ResponseTimeoutSeconds, ct, followBaseline.textLen, followBaseline.preCount);
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
