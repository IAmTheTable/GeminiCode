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
    public string ChatInput { get; set; } = "[aria-label='Talk to Gemini']";

    [JsonPropertyName("sendButton")]
    public string SendButton { get; set; } = "button[aria-label='Send message']";

    [JsonPropertyName("responseContainer")]
    public string ResponseContainer { get; set; } = ".response-container";

    [JsonPropertyName("typingIndicator")]
    public string TypingIndicator { get; set; } = ".typing-indicator";

    [JsonPropertyName("newChatButton")]
    public string NewChatButton { get; set; } = "[aria-label='New chat']";
}
