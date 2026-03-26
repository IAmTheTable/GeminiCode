namespace GeminiCode.Permissions;

public class SessionAllowlist
{
    private readonly HashSet<string> _allowed = new(StringComparer.OrdinalIgnoreCase);

    public bool Add(string toolName)
    {
        if (toolName.Equals("RunCommand", StringComparison.OrdinalIgnoreCase))
            return false;
        _allowed.Add(toolName);
        return true;
    }

    public bool IsAllowed(string toolName) => _allowed.Contains(toolName);
    public void Clear() => _allowed.Clear();
    public IReadOnlyList<string> GetEntries() => _allowed.ToList().AsReadOnly();
}
