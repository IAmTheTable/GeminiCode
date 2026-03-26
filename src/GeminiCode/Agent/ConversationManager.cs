namespace GeminiCode.Agent;

public class ConversationManager
{
    private int _turnCount;
    private bool _systemPromptSent;
    private const int DriftPreventionThreshold = 10;

    public int TurnCount => _turnCount;
    public bool IsFirstMessage => !_systemPromptSent;

    public void MarkSystemPromptSent()
    {
        _systemPromptSent = true;
    }

    public string PrepareMessage(string userMessage)
    {
        _turnCount++;
        string message;

        if (_turnCount > DriftPreventionThreshold)
        {
            message = userMessage + "\n\n" + SystemPrompt.DriftReminder;
        }
        else
        {
            message = userMessage;
        }

        return message;
    }

    public string PrepareToolResults(IEnumerable<string> results)
    {
        // Don't increment turn count for tool results — only user messages count toward drift prevention
        return string.Join("\n\n", results);
    }

    public void Reset()
    {
        _turnCount = 0;
        _systemPromptSent = false;
    }
}
