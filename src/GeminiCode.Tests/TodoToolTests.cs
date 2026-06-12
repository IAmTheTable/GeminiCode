// src/GeminiCode.Tests/TodoToolTests.cs
using System.Text.Json;
using GeminiCode.Tools;

namespace GeminiCode.Tests;

public class TodoToolTests
{
    private static Dictionary<string, JsonElement> P(object o)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(o));
        return doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
    }

    [Fact]
    public void ParseItems_ReadsStatusMarkers()
    {
        var items = TodoStore.ParseItems("- [ ] pending one\n- [~] in progress\n- [x] done one");
        Assert.Equal(3, items.Count);
        Assert.Equal(TodoStatus.Pending, items[0].Status);
        Assert.Equal(TodoStatus.InProgress, items[1].Status);
        Assert.Equal(TodoStatus.Done, items[2].Status);
        Assert.Equal("pending one", items[0].Text);
    }

    [Fact]
    public async Task Execute_ReplacesListAndRendersCounts()
    {
        var store = new TodoStore();
        var tool = new TodoTool(store);
        var r = await tool.ExecuteAsync(P(new { items = "- [x] a\n- [ ] b" }), default);
        Assert.True(r.Success);
        Assert.Equal(2, store.Items.Count);
        Assert.Contains("1/2", r.Output); // 1 of 2 done
    }
}
