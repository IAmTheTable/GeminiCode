using System.Text.Json;

namespace GeminiCode.Tools;

public record ToolResult(string Name, bool Success, string Output)
{
    public string ToProtocolString()
    {
        var truncated = Output.Length > 8000 ? Output[..8000] + "\n[truncated]" : Output;
        return Success
            ? $"tool_result({Name}): {truncated}"
            : $"tool_error({Name}): {truncated}";
    }
}

public enum RiskLevel { Low, Medium, High }

public interface ITool
{
    string Name { get; }
    RiskLevel Risk { get; }
    Task<ToolResult> ExecuteAsync(Dictionary<string, JsonElement> parameters, CancellationToken ct);
    string DescribeAction(Dictionary<string, JsonElement> parameters);
}
