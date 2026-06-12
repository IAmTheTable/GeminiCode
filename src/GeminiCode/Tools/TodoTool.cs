// src/GeminiCode/Tools/TodoTool.cs
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GeminiCode.Tools;

public enum TodoStatus { Pending, InProgress, Done }
public record TodoItem(string Text, TodoStatus Status);

public class TodoStore
{
    private readonly List<TodoItem> _items = new();
    public IReadOnlyList<TodoItem> Items => _items;

    public void Replace(IEnumerable<TodoItem> items) { _items.Clear(); _items.AddRange(items); }

    private static readonly Regex Line = new(@"^\s*-\s*\[\s*([ x~])\s*\]\s*(.+?)\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static List<TodoItem> ParseItems(string text)
    {
        var result = new List<TodoItem>();
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var m = Line.Match(raw);
            if (!m.Success) continue;
            var status = m.Groups[1].Value.ToLowerInvariant() switch
            {
                "x" => TodoStatus.Done,
                "~" => TodoStatus.InProgress,
                _ => TodoStatus.Pending
            };
            result.Add(new TodoItem(m.Groups[2].Value, status));
        }
        return result;
    }

    public string Render()
    {
        if (_items.Count == 0) return "(no todos)";
        var sb = new StringBuilder();
        foreach (var it in _items)
        {
            var (mark, _) = it.Status switch
            {
                TodoStatus.Done => ("[x]", 0),
                TodoStatus.InProgress => ("[~]", 0),
                _ => ("[ ]", 0)
            };
            sb.AppendLine($"  {mark} {it.Text}");
        }
        var done = _items.Count(i => i.Status == TodoStatus.Done);
        sb.Append($"  {done}/{_items.Count} done");
        return sb.ToString();
    }
}

public class TodoTool : ITool
{
    private readonly TodoStore _store;
    public string Name => "Todo";
    public RiskLevel Risk => RiskLevel.Low;
    public TodoTool(TodoStore store) => _store = store;

    public Task<ToolResult> ExecuteAsync(Dictionary<string, JsonElement> parameters, CancellationToken ct)
    {
        var payload = parameters.TryGetValue("items", out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()! : "";
        _store.Replace(TodoStore.ParseItems(payload));
        return Task.FromResult(new ToolResult(Name, true, _store.Render()));
    }

    public string DescribeAction(Dictionary<string, JsonElement> p) => "Update todo list";
}
