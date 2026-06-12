namespace GeminiCode.Browser;

public class SessionMonitor
{
    private readonly DomSelectors _selectors;

    public SessionMonitor(DomSelectors selectors)
    {
        _selectors = selectors;
    }

    /// <summary>Returns JS that checks if the chat input element exists (logged in).</summary>
    public string GetAuthCheckScript()
    {
        return $"document.querySelector(\"{EscapeJs(_selectors.ChatInput)}\") !== null";
    }

    /// <summary>Returns JS that checks the always-present idle anchors of the Gemini UI.
    /// We deliberately do NOT check the send button or response container here: the send button
    /// only exists when the input has text, and the response container only after a reply — so
    /// they are absent on an empty chat and would produce false "UI changed" warnings at startup.</summary>
    public string GetHealthCheckScript()
    {
        return $$"""
            (function() {
                var results = {};
                results.chatInput = document.querySelector("{{EscapeJs(_selectors.ChatInput)}}") !== null;
                results.modePicker = document.querySelector('[data-test-id="bard-mode-menu-button"]') !== null;
                return JSON.stringify(results);
            })()
            """;
    }

    private static string EscapeJs(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
