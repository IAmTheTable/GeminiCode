using GeminiCode.Plugins;

namespace GeminiCode.Tests;

public class PluginLoaderTests : IDisposable
{
    private readonly string _root;
    public PluginLoaderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"plug_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private void WritePlugin(string root, string folder, string content)
    {
        var dir = Path.Combine(root, folder);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "SKILL.md"), content);
    }

    [Fact]
    public void Load_ReadsValidPlugin()
    {
        WritePlugin(_root, "brainstorm",
            "---\nname: brainstorm\ndescription: d\n---\n## Phase: A\nbody");
        var loader = new PluginLoader(new[] { _root });
        var plugins = loader.Load();
        Assert.Single(plugins);
        Assert.Equal("brainstorm", plugins[0].Name);
    }

    [Fact]
    public void Load_SkipsPluginMissingNameOrDescription_AndWarns()
    {
        WritePlugin(_root, "bad", "---\nname: \n---\nbody");
        var loader = new PluginLoader(new[] { _root });
        var plugins = loader.Load();
        Assert.Empty(plugins);
        Assert.NotEmpty(loader.Warnings);
    }

    [Fact]
    public void Load_LaterRootOverridesSameName()
    {
        var root2 = Path.Combine(Path.GetTempPath(), $"plug2_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root2);
        try
        {
            WritePlugin(_root, "x", "---\nname: x\ndescription: shipped\n---\nb");
            WritePlugin(root2, "x", "---\nname: x\ndescription: local\n---\nb");
            var loader = new PluginLoader(new[] { _root, root2 });
            var plugins = loader.Load();
            Assert.Single(plugins);
            Assert.Equal("local", plugins[0].Description);
        }
        finally { Directory.Delete(root2, true); }
    }

    [Fact]
    public void Load_MissingRoot_IsIgnored()
    {
        var loader = new PluginLoader(new[] { Path.Combine(_root, "does-not-exist") });
        Assert.Empty(loader.Load());
    }
}
