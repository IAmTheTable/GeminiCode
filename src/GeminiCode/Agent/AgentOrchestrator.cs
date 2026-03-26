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

    public async Task<string?> SendAndProcessAsync(string userMessage, CancellationToken ct)
    {
        var message = _conversation.PrepareMessage(userMessage);
        await _browser.SendMessageAsync(message);

        var response = await _browser.WaitForResponseAsync(_settings.ResponseTimeoutSeconds, ct);
        if (response == null)
            return null; // Timed out

        return await ProcessResponseAsync(response, 0, ct);
    }

    private async Task<string?> ProcessResponseAsync(string response, int retryCount, CancellationToken ct)
    {
        var parsed = ToolCallParser.Parse(response);

        // Display conversational text
        if (!string.IsNullOrWhiteSpace(parsed.TextContent))
        {
            var rendered = MarkdownRenderer.Render(parsed.TextContent);
            Console.Write(rendered);
        }

        // No tool calls — check if format enforcement retry is needed
        if (parsed.ToolCalls.Count == 0)
        {
            // Detect if response contains code blocks that look like they should be tool calls
            if (retryCount < MaxRetries && LooksLikeUnstructuredToolUse(parsed.TextContent))
            {
                Console.WriteLine($"\n{AnsiHelper.Yellow}Gemini didn't use tool_call format. Requesting correction (attempt {retryCount + 1}/{MaxRetries})...{AnsiHelper.Reset}");
                await _browser.SendMessageAsync(SystemPrompt.CorrectionPrompt);
                var retryResponse = await _browser.WaitForResponseAsync(_settings.ResponseTimeoutSeconds, ct);
                if (retryResponse == null) return null;
                return await ProcessResponseAsync(retryResponse, retryCount + 1, ct);
            }
            return parsed.TextContent;
        }

        // Execute tool calls sequentially with permission checks
        var results = new List<string>();

        foreach (var toolCall in parsed.ToolCalls)
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

            // Execute
            var toolResult = await tool.ExecuteAsync(toolCall.Parameters, ct);

            // Display result summary
            var color = toolResult.Success ? AnsiHelper.Green : AnsiHelper.Red;
            Console.WriteLine($"{color}{tool.Name}: {(toolResult.Success ? "OK" : "FAILED")}{AnsiHelper.Reset}");

            results.Add(toolResult.ToProtocolString());
        }

        // Send results back to Gemini
        var combinedResults = _conversation.PrepareToolResults(results);
        await _browser.SendMessageAsync(combinedResults);

        // Wait for Gemini's follow-up response
        var followUp = await _browser.WaitForResponseAsync(_settings.ResponseTimeoutSeconds, ct);
        if (followUp == null)
            return null;

        return await ProcessResponseAsync(followUp, 0, ct);
    }

    /// <summary>Detect if Gemini responded with code blocks that look like they should have been tool calls.</summary>
    private static bool LooksLikeUnstructuredToolUse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        // Check for code blocks with file-path-like annotations or "save this" language
        return text.Contains("```") && (
            text.Contains("save this", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("create this file", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("write this to", StringComparison.OrdinalIgnoreCase) ||
            Regex.IsMatch(text, @"```\w+:[\w/\\\.]+")
        );
    }
}
