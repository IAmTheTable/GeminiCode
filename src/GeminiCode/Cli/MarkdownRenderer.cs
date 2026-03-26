using System.Text;
using System.Text.RegularExpressions;

namespace GeminiCode.Cli;

public static class MarkdownRenderer
{
    private static void AppendLine(StringBuilder sb, string value) =>
        sb.Append(value).Append('\n');

    public static string Render(string markdown)
    {
        var sb = new StringBuilder();
        var lines = markdown.Split('\n');
        var inCodeBlock = false;
        string? codeLanguage = null;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');

            if (line.StartsWith("```"))
            {
                if (!inCodeBlock)
                {
                    inCodeBlock = true;
                    codeLanguage = line.Length > 3 ? line[3..].Trim() : null;
                    if (!string.IsNullOrEmpty(codeLanguage))
                        AppendLine(sb, $"{AnsiHelper.Dim}[{codeLanguage}]{AnsiHelper.Reset}");
                    sb.Append(AnsiHelper.BgDarkGray);
                }
                else
                {
                    inCodeBlock = false;
                    codeLanguage = null;
                    AppendLine(sb, AnsiHelper.Reset);
                }
                continue;
            }

            if (inCodeBlock)
            {
                AppendLine(sb, $"{AnsiHelper.BgDarkGray}{AnsiHelper.Cyan}{line}{AnsiHelper.Reset}");
                continue;
            }

            // Headers
            var headerMatch = Regex.Match(line, @"^(#{1,6})\s+(.+)$");
            if (headerMatch.Success)
            {
                AppendLine(sb, AnsiHelper.Wrap(headerMatch.Groups[2].Value, AnsiHelper.Bold));
                continue;
            }

            // Bullet lists
            if (line.TrimStart().StartsWith("- "))
            {
                AppendLine(sb, $"  {line.TrimStart()}");
                continue;
            }

            // Inline formatting
            var rendered = RenderInline(line);
            AppendLine(sb, rendered);
        }

        return sb.ToString();
    }

    private static string RenderInline(string line)
    {
        // Bold: **text**
        line = Regex.Replace(line, @"\*\*(.+?)\*\*",
            m => $"{AnsiHelper.Bold}{m.Groups[1].Value}{AnsiHelper.Reset}");

        // Italic: *text*
        line = Regex.Replace(line, @"(?<!\*)\*(?!\*)(.+?)(?<!\*)\*(?!\*)",
            m => $"{AnsiHelper.Italic}{m.Groups[1].Value}{AnsiHelper.Reset}");

        // Inline code: `text`
        line = Regex.Replace(line, @"`([^`]+)`",
            m => $"{AnsiHelper.BgDarkGray}{AnsiHelper.White}{m.Groups[1].Value}{AnsiHelper.Reset}");

        return line;
    }
}
