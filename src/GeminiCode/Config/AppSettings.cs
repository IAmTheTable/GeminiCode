using System.Text.Json.Serialization;

namespace GeminiCode.Config;

public class AppSettings
{
    [JsonPropertyName("responseTimeoutSeconds")]
    public int ResponseTimeoutSeconds { get; set; } = 120;
}

public class DomSelectorConfig
{
    [JsonPropertyName("chatInput")]
    public string ChatInput { get; set; } = "[role='textbox'][aria-label='Enter a prompt for Gemini']";

    [JsonPropertyName("sendButton")]
    public string SendButton { get; set; } = "button[aria-label='Send message']";

    [JsonPropertyName("responseContainer")]
    public string ResponseContainer { get; set; } = ".message-container";

    [JsonPropertyName("typingIndicator")]
    public string TypingIndicator { get; set; } = ".loading-indicator";

    [JsonPropertyName("newChatButton")]
    public string NewChatButton { get; set; } = "[aria-label='New chat']";
}
