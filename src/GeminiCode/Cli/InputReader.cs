// src/GeminiCode/Cli/InputReader.cs
namespace GeminiCode.Cli;

/// <summary>
/// Custom input reader with live autocomplete for / commands and @ contexts.
/// Shows completions immediately when typing / or @, filters as you type.
/// Up/Down to navigate, Tab/Right to accept, Escape to dismiss.
/// </summary>
public class InputReader
{
    private static readonly CompletionItem[] SlashCommands =
    [
        new("/help",           "Show available commands"),
        new("/clear",          "Clear terminal"),
        new("/new",            "Start new conversation"),
        new("/model",          "Show/switch model"),
        new("/model flash",    "Switch to Flash"),
        new("/model pro",      "Switch to Pro"),
        new("/model thinking", "Switch to Thinking"),
        new("/limit",          "Check usage limit status"),
        new("/browser",        "Focus browser window"),
        new("/history",        "Show turn count"),
        new("/allowlist",      "Show auto-approved tools"),
        new("/status",         "Show session state"),
        new("/cd ",            "Change working directory"),
        new("/paste",          "Multi-line paste mode"),
        new("/exit",           "Quit GeminiCode"),
    ];

    private static readonly CompletionItem[] AtContexts =
    [
        new("@file ",       "Attach file contents"),
        new("@tree",        "Attach directory tree"),
        new("@tree depth=", "Tree with custom depth"),
        new("@git status",  "Attach git status"),
        new("@git diff",    "Attach git diff"),
        new("@git log",     "Attach git log"),
        new("@git blame ",  "Attach git blame"),
        new("@git branch",  "Attach branch list"),
        new("@diff",        "Shorthand for git diff"),
        new("@grep ",       "Attach search results"),
        new("@find ",       "Attach file listing"),
        new("@codebase",    "Attach project overview"),
    ];

    private record CompletionItem(string Text, string Description);

    public static string? ReadLine()
    {
        var buffer = new List<char>();
        var cursorPos = 0;
        var selectedIndex = -1; // -1 = no selection in popup
        CompletionItem[]? matches = null;
        var popupVisible = false;
        var popupLineCount = 0;

        while (true)
        {
            var key = Console.ReadKey(intercept: true);

            switch (key.Key)
            {
                case ConsoleKey.Enter:
                    // If a completion is selected, accept it
                    if (popupVisible && selectedIndex >= 0 && matches != null && selectedIndex < matches.Length)
                    {
                        AcceptCompletion(buffer, ref cursorPos, matches[selectedIndex].Text);
                        ClearPopup(ref popupLineCount);
                        popupVisible = false;
                        matches = null;
                        selectedIndex = -1;
                        RedrawLine(buffer, cursorPos);
                        break;
                    }
                    // Otherwise submit
                    ClearPopup(ref popupLineCount);
                    Console.WriteLine();
                    return new string(buffer.ToArray());

                case ConsoleKey.Tab:
                    // Accept current selection or first match
                    if (popupVisible && matches != null && matches.Length > 0)
                    {
                        var idx = selectedIndex >= 0 ? selectedIndex : 0;
                        AcceptCompletion(buffer, ref cursorPos, matches[idx].Text);
                        ClearPopup(ref popupLineCount);
                        popupVisible = false;
                        matches = null;
                        selectedIndex = -1;
                        RedrawLine(buffer, cursorPos);
                    }
                    break;

                case ConsoleKey.DownArrow:
                    if (popupVisible && matches != null && matches.Length > 0)
                    {
                        selectedIndex = Math.Min(selectedIndex + 1, matches.Length - 1);
                        ShowPopup(matches, selectedIndex, ref popupLineCount);
                    }
                    break;

                case ConsoleKey.UpArrow:
                    if (popupVisible && matches != null)
                    {
                        selectedIndex = Math.Max(selectedIndex - 1, 0);
                        ShowPopup(matches, selectedIndex, ref popupLineCount);
                    }
                    break;

                case ConsoleKey.Escape:
                    if (popupVisible)
                    {
                        ClearPopup(ref popupLineCount);
                        popupVisible = false;
                        matches = null;
                        selectedIndex = -1;
                    }
                    else
                    {
                        buffer.Clear();
                        cursorPos = 0;
                        RedrawLine(buffer, cursorPos);
                    }
                    break;

                case ConsoleKey.Backspace:
                    if (cursorPos > 0)
                    {
                        buffer.RemoveAt(cursorPos - 1);
                        cursorPos--;
                        RedrawLine(buffer, cursorPos);
                        UpdatePopup(buffer, cursorPos, ref matches, ref selectedIndex, ref popupVisible, ref popupLineCount);
                    }
                    break;

                case ConsoleKey.Delete:
                    if (cursorPos < buffer.Count)
                    {
                        buffer.RemoveAt(cursorPos);
                        RedrawLine(buffer, cursorPos);
                        UpdatePopup(buffer, cursorPos, ref matches, ref selectedIndex, ref popupVisible, ref popupLineCount);
                    }
                    break;

                case ConsoleKey.LeftArrow:
                    if (cursorPos > 0)
                    {
                        cursorPos--;
                        Console.SetCursorPosition(PromptWidth + cursorPos, Console.CursorTop);
                    }
                    break;

                case ConsoleKey.RightArrow:
                    // Accept completion with right arrow if popup is showing
                    if (popupVisible && matches != null && matches.Length > 0 && selectedIndex >= 0)
                    {
                        AcceptCompletion(buffer, ref cursorPos, matches[selectedIndex].Text);
                        ClearPopup(ref popupLineCount);
                        popupVisible = false;
                        matches = null;
                        selectedIndex = -1;
                        RedrawLine(buffer, cursorPos);
                    }
                    else if (cursorPos < buffer.Count)
                    {
                        cursorPos++;
                        Console.SetCursorPosition(PromptWidth + cursorPos, Console.CursorTop);
                    }
                    break;

                case ConsoleKey.Home:
                    cursorPos = 0;
                    Console.SetCursorPosition(PromptWidth, Console.CursorTop);
                    break;

                case ConsoleKey.End:
                    cursorPos = buffer.Count;
                    Console.SetCursorPosition(PromptWidth + cursorPos, Console.CursorTop);
                    break;

                default:
                    if (key.KeyChar >= 32)
                    {
                        buffer.Insert(cursorPos, key.KeyChar);
                        cursorPos++;
                        RedrawLine(buffer, cursorPos);
                        UpdatePopup(buffer, cursorPos, ref matches, ref selectedIndex, ref popupVisible, ref popupLineCount);
                    }
                    break;
            }
        }
    }

    private const int PromptWidth = 2; // "> "
    private const int MaxPopupItems = 8;

    /// <summary>Check if we should show/hide/update the popup based on current input.</summary>
    private static void UpdatePopup(List<char> buffer, int cursorPos,
        ref CompletionItem[]? matches, ref int selectedIndex,
        ref bool popupVisible, ref int popupLineCount)
    {
        var text = new string(buffer.ToArray());
        var prefix = FindActiveToken(text, cursorPos);

        if (prefix == null)
        {
            if (popupVisible)
            {
                ClearPopup(ref popupLineCount);
                popupVisible = false;
            }
            matches = null;
            selectedIndex = -1;
            return;
        }

        matches = GetMatches(prefix);
        if (matches.Length == 0)
        {
            if (popupVisible) ClearPopup(ref popupLineCount);
            popupVisible = false;
            selectedIndex = -1;
            return;
        }

        selectedIndex = 0;
        popupVisible = true;
        ShowPopup(matches, selectedIndex, ref popupLineCount);
    }

    /// <summary>Find the / or @ token being typed at cursor position.</summary>
    private static string? FindActiveToken(string text, int cursorPos)
    {
        if (cursorPos == 0) return null;
        var before = text[..cursorPos];

        // Find the start of the current token (last @ or / that starts a token)
        for (int i = before.Length - 1; i >= 0; i--)
        {
            var ch = before[i];
            if (ch == '@')
                return before[i..];
            if (ch == '/' && i == 0) // / only at start of line
                return before;
            if (ch == ' ' && i < before.Length - 1)
            {
                // Check if next char after space is @
                if (before[i + 1] == '@')
                    return before[(i + 1)..];
                break;
            }
        }
        return null;
    }

    private static CompletionItem[] GetMatches(string prefix)
    {
        if (prefix.StartsWith('/'))
            return SlashCommands.Where(c => c.Text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (prefix.StartsWith('@'))
            return AtContexts.Where(c => c.Text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToArray();
        return [];
    }

    private static void AcceptCompletion(List<char> buffer, ref int cursorPos, string completion)
    {
        var text = new string(buffer.ToArray());
        var tokenStart = FindTokenStart(text, cursorPos);

        // Remove old token
        while (buffer.Count > tokenStart) buffer.RemoveAt(buffer.Count - 1);

        // Insert completion
        foreach (var c in completion) buffer.Add(c);
        cursorPos = buffer.Count;
    }

    private static int FindTokenStart(string text, int cursorPos)
    {
        var before = text[..Math.Min(cursorPos, text.Length)];
        for (int i = before.Length - 1; i >= 0; i--)
        {
            if (before[i] == '@') return i;
            if (before[i] == '/' && i == 0) return 0;
            if (before[i] == ' ' && i < before.Length - 1 && before[i + 1] == '@')
                return i + 1;
        }
        return cursorPos;
    }

    private static void ShowPopup(CompletionItem[] items, int selectedIndex, ref int popupLineCount)
    {
        var inputTop = Console.CursorTop;
        var inputLeft = Console.CursorLeft;

        // Clear old popup
        for (int i = 1; i <= popupLineCount; i++)
        {
            var row = inputTop + i;
            if (row < Console.BufferHeight)
            {
                Console.SetCursorPosition(0, row);
                Console.Write(new string(' ', Console.WindowWidth - 1));
            }
        }

        var count = Math.Min(items.Length, MaxPopupItems);
        popupLineCount = count;

        // Ensure we have room below
        while (inputTop + count >= Console.BufferHeight)
        {
            Console.SetCursorPosition(0, Console.BufferHeight - 1);
            Console.WriteLine();
            inputTop--;
        }

        for (int i = 0; i < count; i++)
        {
            Console.SetCursorPosition(PromptWidth, inputTop + 1 + i);
            Console.Write(new string(' ', Console.WindowWidth - PromptWidth - 1));
            Console.SetCursorPosition(PromptWidth, inputTop + 1 + i);

            var item = items[i];
            if (i == selectedIndex)
            {
                Console.Write($"{AnsiHelper.BgDarkGray}{AnsiHelper.Cyan}{AnsiHelper.Bold} {item.Text,-24}{AnsiHelper.Reset}");
                Console.Write($"{AnsiHelper.BgDarkGray}{AnsiHelper.Dim} {item.Description}{AnsiHelper.Reset}");
            }
            else
            {
                Console.Write($"  {AnsiHelper.Dim}{item.Text,-24}{item.Description}{AnsiHelper.Reset}");
            }
        }

        if (items.Length > MaxPopupItems)
        {
            Console.SetCursorPosition(PromptWidth, inputTop + 1 + count);
            Console.Write($"  {AnsiHelper.Dim}+{items.Length - MaxPopupItems} more...{AnsiHelper.Reset}");
            popupLineCount++;
        }

        // Restore cursor
        Console.SetCursorPosition(inputLeft, inputTop);
    }

    private static void ClearPopup(ref int popupLineCount)
    {
        if (popupLineCount == 0) return;

        var inputTop = Console.CursorTop;
        var inputLeft = Console.CursorLeft;

        for (int i = 1; i <= popupLineCount; i++)
        {
            var row = inputTop + i;
            if (row < Console.BufferHeight)
            {
                Console.SetCursorPosition(0, row);
                Console.Write(new string(' ', Console.WindowWidth - 1));
            }
        }

        popupLineCount = 0;
        Console.SetCursorPosition(inputLeft, inputTop);
    }

    private static void RedrawLine(List<char> buffer, int cursorPos)
    {
        Console.SetCursorPosition(PromptWidth, Console.CursorTop);
        var text = new string(buffer.ToArray());
        Console.Write(text);
        var clearLen = Console.WindowWidth - PromptWidth - text.Length - 1;
        if (clearLen > 0) Console.Write(new string(' ', clearLen));
        Console.SetCursorPosition(PromptWidth + cursorPos, Console.CursorTop);
    }
}
