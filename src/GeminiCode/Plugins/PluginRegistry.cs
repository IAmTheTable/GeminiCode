namespace GeminiCode.Plugins;

public class PluginRegistry
{
    private readonly PluginLoader _loader;
    private List<PluginManifest> _plugins;

    public IReadOnlyList<PluginManifest> Plugins => _plugins;
    public IReadOnlyList<string> Warnings => _loader.Warnings;

    public PluginRegistry(PluginLoader loader)
    {
        _loader = loader;
        _plugins = loader.Load();
    }

    public PluginManifest? ByCommand(string command)
    {
        var c = command.Trim().ToLowerInvariant();
        if (!c.StartsWith('/')) c = "/" + c;
        return _plugins.FirstOrDefault(p => p.Command == c);
    }

    public PluginManifest? ByName(string name)
        => _plugins.FirstOrDefault(p => p.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Re-scan roots. Returns (added, removed) counts vs the previous set.</summary>
    public (int added, int removed) Reload()
    {
        var before = _plugins.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _plugins = _loader.Load();
        var after = _plugins.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = after.Count(n => !before.Contains(n));
        var removed = before.Count(n => !after.Contains(n));
        return (added, removed);
    }
}
