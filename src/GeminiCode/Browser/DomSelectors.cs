using GeminiCode.Config;

namespace GeminiCode.Browser;

public class DomSelectors
{
    public string ChatInput { get; }
    public string SendButton { get; }
    public string ResponseContainer { get; }
    public string TypingIndicator { get; }
    public string NewChatButton { get; }

    public DomSelectors(DomSelectorConfig config)
    {
        ChatInput = config.ChatInput;
        SendButton = config.SendButton;
        ResponseContainer = config.ResponseContainer;
        TypingIndicator = config.TypingIndicator;
        NewChatButton = config.NewChatButton;
    }
}
