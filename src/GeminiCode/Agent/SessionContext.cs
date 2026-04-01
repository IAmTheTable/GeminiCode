// src/GeminiCode/Agent/SessionContext.cs
using System.Text;

namespace GeminiCode.Agent;

public class SessionContext
{
    private readonly string _workingDirectory;
    private readonly string _agentProfile;
    private readonly DateTime _startTime;
    private readonly List<string> _toolCalls = new();
    private readonly List<string> _modifiedFiles = new();
    private readonly List<string> _decisions = new();
    private readonly List<string> _conversationSummaries = new();

    public SessionContext(string workingDirectory, string agentProfile)
    {
        _workingDirectory = workingDirectory;
        _agentProfile = agentProfile;
        _startTime = DateTime.Now;
    }

    public void LogToolCall(string toolName, string target, bool success, int turn)
    {
        var status = success ? "success" : "failed";
        _toolCalls.Add($"- {toolName}: {target} (turn {turn}, {status})");
    }

    public void LogFileModified(string path, string description)
    {
        _modifiedFiles.Add($"- {path} ({description})");
    }

    public void LogDecision(string decision)
    {
        _decisions.Add($"- {decision}");
    }

    public void AddConversationSummary(string summary)
    {
        _conversationSummaries.Add(summary);
    }

    public string GenerateMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Session Context");
        sb.AppendLine($"- **Started:** {_startTime:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"- **Agent:** {_agentProfile}");
        sb.AppendLine($"- **Working Directory:** {_workingDirectory.Replace("\\", "/")}");

        if (_modifiedFiles.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Modified Files");
            foreach (var entry in _modifiedFiles)
                sb.AppendLine(entry);
        }

        if (_decisions.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Key Decisions");
            foreach (var entry in _decisions)
                sb.AppendLine(entry);
        }

        if (_toolCalls.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Tool Call Summary");
            foreach (var entry in _toolCalls)
                sb.AppendLine(entry);
        }

        if (_conversationSummaries.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Conversation Summary");
            foreach (var entry in _conversationSummaries)
                sb.AppendLine(entry);
        }

        return sb.ToString();
    }

    public void SaveToFile()
    {
        var dir = Path.Combine(_workingDirectory, ".gemini");
        Directory.CreateDirectory(dir);
        var filePath = Path.Combine(dir, "session-context.md");
        File.WriteAllText(filePath, GenerateMarkdown());
    }

    public string GetFilePath()
    {
        return Path.Combine(_workingDirectory, ".gemini", "session-context.md");
    }

    public static bool HasSavedContext(string workingDirectory)
    {
        return File.Exists(Path.Combine(workingDirectory, ".gemini", "session-context.md"));
    }
}
