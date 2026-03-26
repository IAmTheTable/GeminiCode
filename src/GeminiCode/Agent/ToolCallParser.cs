using System.Text.Json;
using System.Text.RegularExpressions;

namespace GeminiCode.Agent;

public record ParsedToolCall(string Name, Dictionary<string, JsonElement> Parameters);

public record ParseResult(List<ParsedToolCall> ToolCalls, string TextContent);

public static class ToolCallParser
{
    // XML format: <tool_call>{"name":"...", "parameters":{...}}</tool_call>
    private static readonly Regex FencePattern = new(
        @"```\w*\s*\n?(.*?)\n?\s*```",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex ToolCallPattern = new(
        @"<tool_call>\s*(.*?)\s*</tool_call>",
        RegexOptions.Singleline | RegexOptions.Compiled);

    // Function-call format: tool_name(param="value", ...)
    // Matches: write_file(path="...", content="...") etc.
    private static readonly string[] KnownTools = ["write_file", "read_file", "edit_file", "list_files", "search_files", "run_command"];

    private static readonly Regex FuncCallPattern = new(
        @"(?:^|\n)\s*(write_file|read_file|edit_file|list_files|search_files|run_command|execute_shell|execute_command|save_file)\s*\((.*?)\)\s*(?:$|\n)",
        RegexOptions.Singleline | RegexOptions.Compiled | RegexOptions.Multiline);

    // Map function names (including common aliases Gemini uses) to our tool names
    private static readonly Dictionary<string, string> FuncToToolName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["write_file"] = "WriteFile",
        ["save_file"] = "WriteFile",
        ["read_file"] = "ReadFile",
        ["edit_file"] = "EditFile",
        ["list_files"] = "ListFiles",
        ["search_files"] = "SearchFiles",
        ["run_command"] = "RunCommand",
        ["execute_shell"] = "RunCommand",
        ["execute_command"] = "RunCommand",
    };

    // Tag-based patterns: [FILE:name]...[/FILE], [RUN]...[/RUN], [READ]...[/READ], [LIST]...[/LIST]
    private static readonly Regex FileTagPattern = new(
        @"\[FILE:([^\]]+)\]\s*\n([\s\S]*?)\n\s*\[/FILE\]",
        RegexOptions.Compiled);

    private static readonly Regex RunTagPattern = new(
        @"\[RUN\](.*?)\[/RUN\]",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex ReadTagPattern = new(
        @"\[READ\](.*?)\[/READ\]",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex ListTagPattern = new(
        @"\[LIST\](.*?)\[/LIST\]",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex SearchTagPattern = new(
        @"\[SEARCH\](.*?)\[/SEARCH\]",
        RegexOptions.Singleline | RegexOptions.Compiled);

    public static ParseResult Parse(string responseText)
    {
        var toolCalls = new List<ParsedToolCall>();

        // Strategy 1 (PRIMARY): Tag-based format [FILE:name]...[/FILE], [RUN]...[/RUN]
        var textContent = FileTagPattern.Replace(responseText, m =>
        {
            var path = m.Groups[1].Value.Trim();
            var content = m.Groups[2].Value;
            toolCalls.Add(MakeToolCall("WriteFile", new() { ["path"] = path, ["content"] = content }));
            return "";
        });

        textContent = RunTagPattern.Replace(textContent, m =>
        {
            var command = m.Groups[1].Value.Trim();
            toolCalls.Add(MakeToolCall("RunCommand", new() { ["command"] = command }));
            return "";
        });

        textContent = ReadTagPattern.Replace(textContent, m =>
        {
            var path = m.Groups[1].Value.Trim();
            toolCalls.Add(MakeToolCall("ReadFile", new() { ["path"] = path }));
            return "";
        });

        textContent = ListTagPattern.Replace(textContent, m =>
        {
            var pattern = m.Groups[1].Value.Trim();
            toolCalls.Add(MakeToolCall("ListFiles", new() { ["pattern"] = pattern }));
            return "";
        });

        textContent = SearchTagPattern.Replace(textContent, m =>
        {
            var pattern = m.Groups[1].Value.Trim();
            toolCalls.Add(MakeToolCall("SearchFiles", new() { ["pattern"] = pattern }));
            return "";
        });

        // Strategy 2: XML <tool_call> format
        if (toolCalls.Count == 0)
        {
            var unwrapped = FencePattern.Replace(textContent, m =>
            {
                var inner = m.Groups[1].Value;
                return inner.Contains("<tool_call>") ? inner : m.Value;
            });

            textContent = ToolCallPattern.Replace(unwrapped, m =>
            {
                var json = m.Groups[1].Value.Trim();
                var parsed = TryParseXmlToolCall(json);
                if (parsed != null)
                    toolCalls.Add(parsed);
                return "";
            });
        }

        // Strategy 3: Function-call format write_file(path="...", content="...")
        if (toolCalls.Count == 0)
        {
            textContent = FuncCallPattern.Replace(textContent, m =>
            {
                var funcName = m.Groups[1].Value.Trim();
                var argsStr = m.Groups[2].Value.Trim();
                var parsed = TryParseFuncCall(funcName, argsStr);
                if (parsed != null)
                {
                    toolCalls.Add(parsed);
                    return "\n";
                }
                return m.Value;
            });
        }

        // Strategy 4: JSON action format {"action": "execute_shell", "command": "..."}
        if (toolCalls.Count == 0)
        {
            var jsonParsed = TryParseJsonAction(textContent);
            if (jsonParsed != null)
            {
                toolCalls.Add(jsonParsed);
                textContent = Regex.Replace(textContent, @"\{[\s\S]*?\}", "").Trim();
            }
        }

        textContent = Regex.Replace(textContent.Trim(), @"\n{3,}", "\n\n");

        return new ParseResult(toolCalls, textContent);
    }

    /// <summary>Helper to create a ParsedToolCall from string parameters.</summary>
    private static ParsedToolCall MakeToolCall(string name, Dictionary<string, string> stringParams)
    {
        var jsonParams = new Dictionary<string, JsonElement>();
        foreach (var kv in stringParams)
        {
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(kv.Value));
            jsonParams[kv.Key] = doc.RootElement.Clone();
        }
        return new ParsedToolCall(name, jsonParams);
    }

    private static ParsedToolCall? TryParseXmlToolCall(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var name = root.GetProperty("name").GetString();
            if (string.IsNullOrEmpty(name))
                return null;

            JsonElement paramsElement;
            if (!root.TryGetProperty("parameters", out paramsElement))
            {
                if (!root.TryGetProperty("args", out paramsElement))
                    return null;
            }

            var parameters = new Dictionary<string, JsonElement>();
            foreach (var prop in paramsElement.EnumerateObject())
                parameters[prop.Name] = prop.Value.Clone();

            return new ParsedToolCall(name, parameters);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Parse JSON action format: {"action": "execute_shell", "command": "ls"}</summary>
    private static ParsedToolCall? TryParseJsonAction(string text)
    {
        // Find JSON objects in the text
        var jsonMatch = Regex.Match(text, @"\{[^{}]*""action""\s*:\s*""[^""]+""[^{}]*\}", RegexOptions.Singleline);
        if (!jsonMatch.Success) return null;

        try
        {
            using var doc = JsonDocument.Parse(jsonMatch.Value);
            var root = doc.RootElement;

            var action = root.GetProperty("action").GetString()?.ToLowerInvariant();
            if (string.IsNullOrEmpty(action)) return null;

            // Map action names to tool names
            var toolName = action switch
            {
                "execute_shell" or "shell_command" or "run_command" or "execute" => "RunCommand",
                "write_file" or "save_file" or "create_file" => "WriteFile",
                "read_file" => "ReadFile",
                "list_files" or "list_directory" => "ListFiles",
                _ => null
            };
            if (toolName == null) return null;

            // Extract parameters (everything except "action")
            var parameters = new Dictionary<string, JsonElement>();
            foreach (var prop in root.EnumerateObject())
            {
                if (prop.Name != "action")
                    parameters[prop.Name] = prop.Value.Clone();
            }

            return new ParsedToolCall(toolName, parameters);
        }
        catch { return null; }
    }

    /// <summary>Parse function-call style: write_file(path="hello.py", content="print('hi')")</summary>
    private static ParsedToolCall? TryParseFuncCall(string funcName, string argsStr)
    {
        if (!FuncToToolName.TryGetValue(funcName, out var toolName))
            return null;

        try
        {
            // Parse keyword arguments: key="value", key="value"
            // Handle multi-line string values with triple quotes or regular quotes
            var parameters = new Dictionary<string, JsonElement>();
            var paramPattern = new Regex(
                @"(\w+)\s*=\s*(?:""""""([\s\S]*?)""""""|""((?:[^""\\]|\\.)*)"")",
                RegexOptions.Compiled);

            foreach (Match match in paramPattern.Matches(argsStr))
            {
                var key = match.Groups[1].Value;
                // Triple-quote value or single-quote value
                var value = match.Groups[2].Success && match.Groups[2].Length > 0
                    ? match.Groups[2].Value
                    : match.Groups[3].Value;

                // Unescape Python string escapes
                value = value.Replace("\\n", "\n").Replace("\\t", "\t").Replace("\\\"", "\"").Replace("\\\\", "\\");

                using var doc = JsonDocument.Parse(JsonSerializer.Serialize(value));
                parameters[key] = doc.RootElement.Clone();
            }

            if (parameters.Count == 0)
                return null;

            return new ParsedToolCall(toolName, parameters);
        }
        catch
        {
            return null;
        }
    }
}
