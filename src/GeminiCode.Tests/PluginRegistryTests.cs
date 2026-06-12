using GeminiCode.Plugins;

namespace GeminiCode.Tests;

public class PluginRegistryTests : IDisposable
{
    private readonly string _root;
    public PluginRegistryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"reg_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private void Write(string folder, string name)
    {
        var dir = Path.Combine(_root, folder);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "SKILL.md"), $"---\nname: {name}\ndescription: d\n---\nbody");
    }

    [Fact]
    public void ByCommandAndByName_ResolvePlugins()
    {
        Write("brainstorm", "brainstorm");
        var reg = new PluginRegistry(new PluginLoader(new[] { _root }));
        Assert.NotNull(reg.ByCommand("/brainstorm"));
        Assert.NotNull(reg.ByCommand("BRAINSTORM"));   // normalized
        Assert.NotNull(reg.ByName("brainstorm"));
        Assert.Null(reg.ByCommand("/nope"));
    }

    [Fact]
    public void Reload_PicksUpAddedAndRemovedPlugins()
    {
        Write("a", "a");
        var reg = new PluginRegistry(new PluginLoader(new[] { _root }));
        Assert.Single(reg.Plugins);

        Write("b", "b");
        var (added, removed) = reg.Reload();
        Assert.Equal(1, added);
        Assert.Equal(0, removed);
        Assert.Equal(2, reg.Plugins.Count);

        Directory.Delete(Path.Combine(_root, "a"), true);
        var (added2, removed2) = reg.Reload();
        Assert.Equal(0, added2);
        Assert.Equal(1, removed2);
        Assert.Single(reg.Plugins);
    }
}
