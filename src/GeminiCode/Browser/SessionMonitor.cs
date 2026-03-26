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

    /// <summary>Returns JS that checks if all critical DOM elements are present.</summary>
    public string GetHealthCheckScript()
    {
        return $$"""
            (function() {
                var results = {};
                results.chatInput = document.querySelector("{{EscapeJs(_selectors.ChatInput)}}") !== null;
                results.sendButton = document.querySelector("{{EscapeJs(_selectors.SendButton)}}") !== null;
                results.responseContainer = document.querySelector("{{EscapeJs(_selectors.ResponseContainer)}}") !== null;
                return JSON.stringify(results);
            })()
            """;
    }

    private static string EscapeJs(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
