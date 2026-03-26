using System.Text.Json;

namespace GeminiCode.Config;

public static class ConfigLoader
{
    private static readonly string AppDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GeminiCode");

    public static string AppDataPath => AppDataDir;

    public static AppSettings LoadSettings()
    {
        var path = Path.Combine(AppDataDir, "settings.json");
        return LoadOrCreateDefault<AppSettings>(path);
    }

    public static DomSelectorConfig LoadSelectors()
    {
        var path = Path.Combine(AppDataDir, "selectors.json");
        return LoadOrCreateDefault<DomSelectorConfig>(path);
    }

    private static T LoadOrCreateDefault<T>(string path) where T : new()
    {
        Directory.CreateDirectory(AppDataDir);

        if (File.Exists(path))
        {
            try
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<T>(json) ?? new T();
            }
            catch (JsonException)
            {
                Console.Error.WriteLine($"Warning: Failed to parse {path}, using defaults.");
                return new T();
            }
        }

        // Create default
        var defaults = new T();
        var defaultJson = JsonSerializer.Serialize(defaults, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, defaultJson);
        return defaults;
    }
}
