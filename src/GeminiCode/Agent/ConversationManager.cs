using GeminiCode.Cli;

namespace GeminiCode.Agent;

public class ConversationManager
{
    private int _turnCount;
    private bool _systemPromptSent;
    private string? _currentModel;
    private string? _sessionStartModel;
    private int _modelSwitchCount;
    private const int DriftPreventionThreshold = 10;

    public int TurnCount => _turnCount;
    public bool IsFirstMessage => !_systemPromptSent;
    public string? CurrentModel => _currentModel;
    public string? SessionStartModel => _sessionStartModel;
    public int ModelSwitchCount => _modelSwitchCount;

    public void MarkSystemPromptSent()
    {
        _systemPromptSent = true;
    }

    /// <summary>
    /// Updates the tracked model. Returns a ModelChangeEvent if the model changed, null otherwise.
    /// </summary>
    public ModelChangeEvent? UpdateModel(string detectedModel)
    {
        if (string.IsNullOrWhiteSpace(detectedModel) || detectedModel == "unknown")
            return null;

        if (_currentModel == null)
        {
            // First detection — set baseline
            _currentModel = detectedModel;
            _sessionStartModel = detectedModel;
            return null;
        }

        if (string.Equals(_currentModel, detectedModel, StringComparison.OrdinalIgnoreCase))
            return null;

        // Model changed!
        var previousModel = _currentModel;
        _currentModel = detectedModel;
        _modelSwitchCount++;

        return new ModelChangeEvent(previousModel, detectedModel, _turnCount, _modelSwitchCount);
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
        _currentModel = null;
        _sessionStartModel = null;
        _modelSwitchCount = 0;
    }
}

public record ModelChangeEvent(string PreviousModel, string NewModel, int AtTurn, int TotalSwitches)
{
    public override string ToString() =>
        $"{PreviousModel} -> {NewModel} (turn {AtTurn}, switch #{TotalSwitches})";
}
