namespace GeminiCode.Plugins;

public class PluginLoader
{
    private readonly IReadOnlyList<string> _roots;
    public List<string> Warnings { get; } = new();

    public PluginLoader(IEnumerable<string> roots) => _roots = roots.ToList();

    /// <summary>Scan all roots, parse SKILL.md files. Later roots override earlier by plugin name.</summary>
    public List<PluginManifest> Load()
    {
        Warnings.Clear();
        var byName = new Dictionary<string, PluginManifest>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in _roots)
        {
            if (!Directory.Exists(root)) continue;

            foreach (var dir in Directory.EnumerateDirectories(root))
            {
                var skillFile = Path.Combine(dir, "SKILL.md");
                if (!File.Exists(skillFile)) continue;

                string text;
                try { text = File.ReadAllText(skillFile); }
                catch (Exception ex) { Warnings.Add($"Could not read {skillFile}: {ex.Message}"); continue; }

                var parsed = FrontmatterParser.Parse(text);
                var manifest = PluginManifest.FromParsed(parsed.Fields, parsed.Body, skillFile);

                if (string.IsNullOrWhiteSpace(manifest.Name) || string.IsNullOrWhiteSpace(manifest.Description))
                {
                    Warnings.Add($"Skipped plugin at {skillFile}: missing name or description.");
                    continue;
                }

                byName[manifest.Name] = manifest; // later root wins
            }
        }

        return byName.Values.ToList();
    }
}
