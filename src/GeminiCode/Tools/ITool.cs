using System.Text.Json;

namespace GeminiCode.Tools;

public record ToolResult(string Name, bool Success, string Output)
{
    public string ToProtocolString() =>
        $"<tool_result>\n{{\"name\": \"{Name}\", \"success\": {Success.ToString().ToLowerInvariant()}, " +
        (Success ? $"\"output\": {JsonSerializer.Serialize(Output)}" : $"\"error\": {JsonSerializer.Serialize(Output)}") +
        "}}\n</tool_result>";
}

public enum RiskLevel { Low, Medium, High }

public interface ITool
{
    string Name { get; }
    RiskLevel Risk { get; }
    Task<ToolResult> ExecuteAsync(Dictionary<string, JsonElement> parameters, CancellationToken ct);
    string DescribeAction(Dictionary<string, JsonElement> parameters);
}
