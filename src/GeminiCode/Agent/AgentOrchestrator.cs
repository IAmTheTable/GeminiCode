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

        // Detect starting model
        try
        {
            var startModel = await _browser.GetCurrentModelAsync();
            _conversation.UpdateModel(startModel);
            Console.WriteLine($"{AnsiHelper.Dim}Model: {startModel}{AnsiHelper.Reset}");
        }
        catch { }

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

        // Check for pre-existing usage limit before sending
        var preLimit = await _browser.CheckForLimitAsync();
        if (preLimit != null)
        {
            HandleLimitDetected(preLimit);
            return null;
        }

        // Check model before sending
        await DetectModelChangeAsync("pre-send");

        var message = _conversation.PrepareMessage(userMessage);

        // Capture baseline BEFORE sending so we can detect only NEW content
        var baseline = await _browser.CaptureBaselineAsync();
        await _browser.SendMessageAsync(message);

        var response = await _browser.WaitForResponseAsync(_settings.ResponseTimeoutSeconds, ct, baseline.textLen, baseline.preCount);
        if (response == null)
        {
            // Timeout could be caused by a limit — check again
            var postLimit = await _browser.CheckForLimitAsync();
            if (postLimit != null)
            {
                HandleLimitDetected(postLimit);
                return null;
            }
            Console.WriteLine($"{AnsiHelper.Yellow}Gemini response timed out. Type your message to retry, or /new to start fresh.{AnsiHelper.Reset}");
            return null;
        }

        // Check if the response itself indicates a limit
        if (response.Limit != null)
        {
            HandleLimitDetected(response.Limit);
            return null;
        }

        // Check model after response — Gemini may have switched mid-conversation
        var modelChanged = await DetectModelChangeAsync("post-response");

        var result = await ProcessResponseAsync(response, ct);

        // If model switched, send a re-orientation message so the new model knows the rules
        if (modelChanged)
            await ReorientAfterModelSwitchAsync(ct);

        return result;
    }

    /// <summary>Display limit info and suggest next steps.</summary>
    private void HandleLimitDetected(LimitInfo limit)
    {
        Console.WriteLine();
        Console.WriteLine($"{AnsiHelper.Red}── Usage Limit ─────────────────────────────────────{AnsiHelper.Reset}");
        Console.WriteLine($"  {AnsiHelper.Yellow}{limit.Message}{AnsiHelper.Reset}");

        if (!string.IsNullOrEmpty(limit.RetryAfter))
            Console.WriteLine($"  {AnsiHelper.Dim}Retry after: {limit.RetryAfter}{AnsiHelper.Reset}");

        var currentModel = _conversation.CurrentModel ?? "unknown";
        Console.WriteLine();
        Console.WriteLine($"  {AnsiHelper.Dim}Current model: {currentModel}{AnsiHelper.Reset}");
        Console.WriteLine($"  {AnsiHelper.Cyan}Options:{AnsiHelper.Reset}");
        Console.WriteLine($"    /model flash     — switch to Flash (higher limits)");
        Console.WriteLine($"    /model pro       — switch to Pro");
        Console.WriteLine($"    /model thinking  — switch to Thinking");
        Console.WriteLine($"    /new             — start a new conversation");
        Console.WriteLine($"    {AnsiHelper.Dim}Or just wait and try again{AnsiHelper.Reset}");
        Console.WriteLine($"{AnsiHelper.Red}────────────────────────────────────────────────────{AnsiHelper.Reset}");
    }

    /// <summary>Detect model changes and notify user. Returns true if model changed.</summary>
    private async Task<bool> DetectModelChangeAsync(string context)
    {
        try
        {
            var detectedModel = await _browser.GetCurrentModelAsync();
            var change = _conversation.UpdateModel(detectedModel);

            if (change != null)
            {
                Console.WriteLine($"\n{AnsiHelper.Yellow}Model switched: {change.PreviousModel} → {change.NewModel}{AnsiHelper.Reset}");

                if (change.TotalSwitches >= 3)
                    Console.WriteLine($"{AnsiHelper.Dim}(Model has switched {change.TotalSwitches} times this session — consider pinning a model with /model){AnsiHelper.Reset}");

                return true;
            }
        }
        catch { /* Model detection is best-effort */ }

        return false;
    }

    /// <summary>
    /// After a model switch, send a condensed system context so the new model knows the rules.
    /// This is lighter than the full system prompt — just the essentials.
    /// </summary>
    private async Task ReorientAfterModelSwitchAsync(CancellationToken ct)
    {
        var reorientation = $"""
            (SYSTEM: Model switch detected. You are in GeminiCode — an automated coding environment.
            Working directory: {_sandbox.WorkingDirectory.Replace("\\", "/")}

            Use these action tags in your responses — the build system executes them:
            - [FILE:path]content[/FILE] — create/overwrite files
            - [EDIT:path]old_string>>>...<<<new_string>>>...<<<[/EDIT] — surgical edits
            - [RUN]command[/RUN] — run shell commands (Windows cmd.exe)
            - [READ]path[/READ] or [READ:10-50]path[/READ] — read files
            - [GREP:include=*.cs]pattern[/GREP] — search code
            - [TREE][/TREE] — directory tree
            - [GIT]status[/GIT] — git info

            NEVER use markdown code blocks for code. Always use [FILE:] or [EDIT:] tags.
            Results come back as tool_result(Name): output. Continue the current task.)
            """;

        Console.WriteLine($"{AnsiHelper.Dim}Re-sending context to new model...{AnsiHelper.Reset}");

        var baseline = await _browser.CaptureBaselineAsync();
        await _browser.SendMessageAsync(reorientation);

        // Wait for acknowledgment but don't process it — it's just a system message
        var ack = await _browser.WaitForResponseAsync(30, ct, baseline.textLen, baseline.preCount);
        if (ack != null)
            Console.WriteLine($"{AnsiHelper.Green}New model oriented.{AnsiHelper.Reset}");
        else
            Console.WriteLine($"{AnsiHelper.Yellow}Model re-orientation timed out — responses may not use action tags.{AnsiHelper.Reset}");
    }

    // Track code blocks we've already shown to avoid re-showing on follow-up messages
    private readonly HashSet<string> _seenCodeBlocks = new();

    private async Task<string?> ProcessResponseAsync(GeminiResponse response, CancellationToken ct)
    {
        // Clean boilerplate from response text
        var cleanText = CleanResponseText(response.Text);

        var parsed = ToolCallParser.Parse(cleanText);

        // ── Thinking / Conversational Text ──
        if (!string.IsNullOrWhiteSpace(parsed.TextContent))
        {
            var label = parsed.ToolCalls.Count > 0 ? "Thinking" : "Response";
            PrintSection(label, "magenta");
            var rendered = MarkdownRenderer.Render(parsed.TextContent);
            Console.Write(rendered);
            Console.WriteLine();
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
            PrintSection($"Code Blocks ({newBlocks.Count})", "yellow");

            foreach (var block in newBlocks)
            {
                var ext = GetExtensionForLanguage(block.Language);
                var suggestedName = $"script{ext}";

                var langLabel = string.IsNullOrEmpty(block.Language) ? "detected" : block.Language;
                Console.WriteLine($"  {AnsiHelper.Dim}┌─ {langLabel} ─────────────────────{AnsiHelper.Reset}");
                var codePreview = string.Join("\n", block.Code.Split('\n').Take(5));
                foreach (var line in codePreview.Split('\n'))
                    Console.WriteLine($"  {AnsiHelper.Dim}│{AnsiHelper.Reset} {line}");
                if (block.Code.Split('\n').Length > 5)
                    Console.WriteLine($"  {AnsiHelper.Dim}│ ... ({block.Code.Split('\n').Length} lines total){AnsiHelper.Reset}");
                Console.WriteLine($"  {AnsiHelper.Dim}└─────────────────────────────{AnsiHelper.Reset}");

                Console.Write($"  Save as [{AnsiHelper.Bold}{suggestedName}{AnsiHelper.Reset}] (filename / y / n): ");
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
                        Console.WriteLine($"  {color}{result.Output}{AnsiHelper.Reset}");
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

        // Strip conversation echo artifacts (Gemini UI leaks "You said" / "Gemini said" / prior turns)
        text = Regex.Replace(text, @"(?:^|\n)\s*You said\s*\n[\s\S]*?(?=\n\s*Gemini said|\z)", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"(?:^|\n)\s*Gemini said\s*\n", "\n", RegexOptions.IgnoreCase);

        // Strip leaked system prompt fragments
        text = Regex.Replace(text, @"Reply with exactly ""Ready\."" to confirm.*$", "", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\nConfirm\s*\n", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"^\s*Ready\.\s*$", "", RegexOptions.Multiline);

        // Strip leaked tool_result/tool_error echoes (Gemini sometimes echoes these back)
        text = Regex.Replace(text, @"(?:^|\n)\s*tool_(?:result|error)\(.*?\):.*$", "", RegexOptions.Multiline);

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
        PrintSection($"Tools ({toolCalls.Count})", "cyan");

        var results = new List<string>();

        foreach (var toolCall in toolCalls)
        {
            var tool = _tools.GetTool(toolCall.Name);
            if (tool == null)
            {
                Console.WriteLine($"  {AnsiHelper.Red}✗ {toolCall.Name}: unknown tool{AnsiHelper.Reset}");
                var result = new ToolResult(toolCall.Name, false, $"Unknown tool: {toolCall.Name}");
                results.Add(result.ToProtocolString());
                continue;
            }

            // Show what tool is about to do
            var action = tool.DescribeAction(toolCall.Parameters);
            Console.Write($"  {AnsiHelper.Dim}▸ {action}{AnsiHelper.Reset}");

            var permission = _permissionGate.RequestPermission(tool, toolCall.Parameters);

            if (permission == PermissionResult.Denied)
            {
                Console.WriteLine($" {AnsiHelper.Red}✗ denied{AnsiHelper.Reset}");
                var result = new ToolResult(toolCall.Name, false, "Permission denied by user");
                results.Add(result.ToProtocolString());
                continue;
            }

            var toolResult = await tool.ExecuteAsync(toolCall.Parameters, ct);

            if (toolResult.Success)
            {
                Console.WriteLine($" {AnsiHelper.Green}✓{AnsiHelper.Reset}");
                // Show compact output preview for read-like tools
                if (IsReadTool(tool.Name) && !string.IsNullOrWhiteSpace(toolResult.Output))
                {
                    var preview = GetOutputPreview(toolResult.Output, 6);
                    Console.WriteLine($"{AnsiHelper.Dim}{preview}{AnsiHelper.Reset}");
                }
            }
            else
            {
                Console.WriteLine($" {AnsiHelper.Red}✗ {toolResult.Output.Split('\n')[0]}{AnsiHelper.Reset}");
            }

            results.Add(toolResult.ToProtocolString());
        }

        // Send results back to Gemini
        Console.WriteLine($"\n  {AnsiHelper.Dim}Sending results to Gemini...{AnsiHelper.Reset}");
        var combinedResults = _conversation.PrepareToolResults(results);
        var followBaseline = await _browser.CaptureBaselineAsync();
        await _browser.SendMessageAsync(combinedResults);

        var followUp = await _browser.WaitForResponseAsync(_settings.ResponseTimeoutSeconds, ct, followBaseline.textLen, followBaseline.preCount);
        if (followUp == null)
        {
            var limit = await _browser.CheckForLimitAsync();
            if (limit != null) { HandleLimitDetected(limit); return null; }
            return null;
        }

        if (followUp.Limit != null)
        {
            HandleLimitDetected(followUp.Limit);
            return null;
        }

        return await ProcessResponseAsync(followUp, ct);
    }

    private static bool IsReadTool(string name) =>
        name is "ReadFile" or "Grep" or "SearchFiles" or "ListFiles" or "Tree" or "GitInfo";

    private static string GetOutputPreview(string output, int maxLines)
    {
        var lines = output.Split('\n');
        var preview = string.Join("\n", lines.Take(maxLines).Select(l => $"    {l}"));
        if (lines.Length > maxLines)
            preview += $"\n    ... ({lines.Length} lines total)";
        return preview;
    }

    /// <summary>Print a labeled section divider.</summary>
    private static void PrintSection(string label, string color)
    {
        var colorCode = color switch
        {
            "cyan" => AnsiHelper.Cyan,
            "magenta" => AnsiHelper.Magenta,
            "yellow" => AnsiHelper.Yellow,
            "green" => AnsiHelper.Green,
            "red" => AnsiHelper.Red,
            "blue" => AnsiHelper.Blue,
            _ => AnsiHelper.Dim
        };
        Console.WriteLine($"\n{colorCode}── {label} {"─".PadRight(40, '─')}{AnsiHelper.Reset}");
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
