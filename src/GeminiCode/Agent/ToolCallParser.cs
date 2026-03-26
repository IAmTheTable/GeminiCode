using System.Text.Json;
using System.Text.RegularExpressions;

namespace GeminiCode.Agent;

public record ParsedToolCall(string Name, Dictionary<string, JsonElement> Parameters);

public record ParseResult(List<ParsedToolCall> ToolCalls, string TextContent);

public static class ToolCallParser
{
    private static readonly Regex FencePattern = new(
        @"```\w*\s*\n?(.*?)\n?\s*```",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex ToolCallPattern = new(
        @"<tool_call>\s*(.*?)\s*</tool_call>",
        RegexOptions.Singleline | RegexOptions.Compiled);

    public static ParseResult Parse(string responseText)
    {
        var toolCalls = new List<ParsedToolCall>();

        var unwrapped = FencePattern.Replace(responseText, m =>
        {
            var inner = m.Groups[1].Value;
            return inner.Contains("<tool_call>") ? inner : m.Value;
        });

        var textContent = ToolCallPattern.Replace(unwrapped, m =>
        {
            var json = m.Groups[1].Value.Trim();
            var parsed = TryParseToolCall(json);
            if (parsed != null)
                toolCalls.Add(parsed);
            return "";
        });

        textContent = Regex.Replace(textContent.Trim(), @"\n{3,}", "\n\n");

        return new ParseResult(toolCalls, textContent);
    }

    private static ParsedToolCall? TryParseToolCall(string json)
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
            {
                parameters[prop.Name] = prop.Value.Clone();
            }

            return new ParsedToolCall(name, parameters);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
