namespace GeminiCode.Plugins;

public record FrontmatterResult(Dictionary<string, string> Fields, string Body);

public static class FrontmatterParser
{
    /// <summary>Parse a leading YAML-ish '---' frontmatter block of simple key: value lines.
    /// No code evaluation — plugins are data, never executed.</summary>
    public static FrontmatterResult Parse(string text)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var normalized = text.Replace("\r\n", "\n");

        if (!normalized.StartsWith("---\n"))
            return new FrontmatterResult(fields, text);

        var afterOpen = normalized[4..];
        var closeIdx = afterOpen.IndexOf("\n---", StringComparison.Ordinal);
        if (closeIdx < 0)
            return new FrontmatterResult(fields, text); // unterminated → treat as no frontmatter

        var block = afterOpen[..closeIdx];
        foreach (var rawLine in block.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;
            var colon = line.IndexOf(':');
            if (colon <= 0) continue;
            var key = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            if (key.Length > 0)
                fields[key] = value;
        }

        // Body = everything after the closing '---' line
        var rest = afterOpen[(closeIdx + 4)..]; // skip "\n---"
        var nl = rest.IndexOf('\n');
        var body = nl >= 0 ? rest[(nl + 1)..] : "";
        return new FrontmatterResult(fields, body);
    }
}
