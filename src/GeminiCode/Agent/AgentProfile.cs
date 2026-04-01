using System.Reflection;

namespace GeminiCode.Agent;

public class AgentProfile
{
    private string? _workingDirectory;
    private string _activeProfile = "general";

    private static readonly string[] BuiltInProfiles =
        { "general", "code-reviewer", "architect", "debugger", "refactorer" };

    public string ActiveProfileName => _activeProfile;

    public AgentProfile(string? workingDirectory = null)
    {
        _workingDirectory = workingDirectory;
    }

    public List<string> ListProfiles()
    {
        var profiles = new List<string>(BuiltInProfiles);

        if (_workingDirectory != null)
        {
            var agentDir = Path.Combine(_workingDirectory, ".gemini", "agents");
            if (Directory.Exists(agentDir))
            {
                foreach (var file in Directory.GetFiles(agentDir, "*.md"))
                {
                    var name = Path.GetFileNameWithoutExtension(file);
                    if (!profiles.Contains(name, StringComparer.OrdinalIgnoreCase))
                        profiles.Add(name);
                }
            }
        }

        return profiles;
    }

    public bool SetActive(string name)
    {
        if (_workingDirectory != null)
        {
            var localPath = Path.Combine(_workingDirectory, ".gemini", "agents", $"{name}.md");
            if (File.Exists(localPath))
            {
                _activeProfile = name;
                return true;
            }
        }

        var match = BuiltInProfiles.FirstOrDefault(p => p.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (match != null)
        {
            _activeProfile = match;
            return true;
        }

        return false;
    }

    public string GetActiveProfileContent()
    {
        if (_workingDirectory != null)
        {
            var localPath = Path.Combine(_workingDirectory, ".gemini", "agents", $"{_activeProfile}.md");
            if (File.Exists(localPath))
                return File.ReadAllText(localPath);
        }

        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = $"GeminiCode.Agent.Profiles.{_activeProfile}.md";

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
            return "";

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public string? GetGeminiMdContent()
    {
        if (_workingDirectory == null) return null;
        var path = Path.Combine(_workingDirectory, "GEMINI.md");
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    public void UpdateWorkingDirectory(string newDir)
    {
        _workingDirectory = newDir;
    }
}
