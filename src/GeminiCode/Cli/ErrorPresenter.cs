namespace GeminiCode.Cli;

public enum GenericErrorCategory { BrowserClosed, WebViewScript, Timeout, Other }

/// <summary>Pure formatters for error/failure messaging. Return strings (no Console writes) so they are testable.</summary>
public static class ErrorPresenter
{
    public static string AuthLost() =>
        $"""
        {AnsiHelper.Yellow}⚠ You appear to be signed out of Gemini.{AnsiHelper.Reset}
          The browser window has been brought forward.
          Sign in there, then press {AnsiHelper.Bold}Enter{AnsiHelper.Reset} to retry — your last message will be re-sent.
          {AnsiHelper.Dim}(or type 'exit' to abort just this message){AnsiHelper.Reset}
        """;

    public static string UiBroken(IReadOnlyList<string> missing) =>
        $"""
        {AnsiHelper.Red}✗ Gemini's page doesn't look the way GeminiCode expects.{AnsiHelper.Reset}
          Missing UI element(s): {AnsiHelper.Bold}{string.Join(", ", missing)}{AnsiHelper.Reset}
          Gemini may have changed its layout. Re-discover selectors by launching with
          {AnsiHelper.Cyan}--discover-selectors{AnsiHelper.Reset} and updating selectors.json.
        """;

    public static string ModelUnavailable(string requested, IEnumerable<string> available) =>
        $"""
        {AnsiHelper.Red}✗ Model '{requested}' isn't available.{AnsiHelper.Reset}
          Available: {string.Join(", ", available.Select(a => $"[{a}]"))}
          Switch with {AnsiHelper.Cyan}/model <name>{AnsiHelper.Reset}.
        """;

    public static string ToolFailure(string toolName, string output)
    {
        var firstLine = (output ?? "").Split('\n')[0];
        bool Has(string s) => (output ?? "").Contains(s, StringComparison.OrdinalIgnoreCase);

        string hint =
            Has("Access denied")    ? "The path is outside the working directory — use a path inside it."
          : Has("Unknown tool")     ? "That is not a recognized tool name."
          : Has("Permission denied") ? "Action was not approved — re-run and approve it at the prompt."
          : Has("not found")        ? "The target file or directory doesn't exist — check the path."
          :                           firstLine;

        return $"  {AnsiHelper.Red}✗ {toolName} failed:{AnsiHelper.Reset} {firstLine}\n  {AnsiHelper.Dim}{hint}{AnsiHelper.Reset}";
    }

    public static string Generic(GenericErrorCategory category, string message) => category switch
    {
        GenericErrorCategory.BrowserClosed =>
            $"{AnsiHelper.Yellow}The browser window closed.{AnsiHelper.Reset} Restart the browser or /exit to quit.",
        GenericErrorCategory.WebViewScript =>
            $"{AnsiHelper.Red}Couldn't talk to the Gemini page.{AnsiHelper.Reset} It may have changed — try /new, or relaunch with --discover-selectors.\n  {AnsiHelper.Dim}{message}{AnsiHelper.Reset}",
        GenericErrorCategory.Timeout =>
            $"{AnsiHelper.Yellow}{message}{AnsiHelper.Reset} Type your message to retry, or /new to start fresh.",
        _ =>
            $"{AnsiHelper.Red}Error:{AnsiHelper.Reset} {message}",
    };
}
