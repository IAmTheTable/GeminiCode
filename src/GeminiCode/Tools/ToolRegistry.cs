namespace GeminiCode.Tools;

public class ToolRegistry
{
    private readonly Dictionary<string, ITool> _tools = new(StringComparer.OrdinalIgnoreCase);

    public void Register(ITool tool) { _tools[tool.Name] = tool; }

    public ITool? GetTool(string name) { _tools.TryGetValue(name, out var tool); return tool; }

    public IReadOnlyCollection<string> ToolNames => _tools.Keys;
}
