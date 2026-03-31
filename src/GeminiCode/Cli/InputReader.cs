// src/GeminiCode/Cli/InputReader.cs
namespace GeminiCode.Cli;

/// <summary>
/// Custom input reader with Tab completion for / commands and @ contexts.
/// Replaces Console.ReadLine() with key-by-key handling.
/// </summary>
public class InputReader
{
    private static readonly string[] SlashCommands =
    [
        "/help", "/clear", "/new", "/model", "/model flash", "/model pro", "/model thinking",
        "/browser", "/history", "/allowlist", "/status", "/cd", "/paste", "/exit"
    ];

    private static readonly string[] AtContexts =
    [
        "@file ", "@tree", "@tree depth=", "@git status", "@git diff", "@git log",
        "@git blame ", "@git branch", "@diff", "@grep ", "@find ", "@codebase", "@project"
    ];

    /// <summary>
    /// Read a line of input with Tab completion. Returns null on Ctrl+C / end of stream.
    /// </summary>
    public static string? ReadLine()
    {
        var buffer = new List<char>();
        var cursorPos = 0;
        var tabIndex = -1;
        string? tabPrefix = null;
        string[]? tabMatches = null;

        while (true)
        {
            var key = Console.ReadKey(intercept: true);

            // Reset tab cycling on any non-Tab key
            if (key.Key != ConsoleKey.Tab)
            {
                tabIndex = -1;
                tabPrefix = null;
                tabMatches = null;
            }

            switch (key.Key)
            {
                case ConsoleKey.Enter:
                    Console.WriteLine();
                    return new string(buffer.ToArray());

                case ConsoleKey.Backspace:
                    if (cursorPos > 0)
                    {
                        buffer.RemoveAt(cursorPos - 1);
                        cursorPos--;
                        RedrawLine(buffer, cursorPos);
                    }
                    break;

                case ConsoleKey.Delete:
                    if (cursorPos < buffer.Count)
                    {
                        buffer.RemoveAt(cursorPos);
                        RedrawLine(buffer, cursorPos);
                    }
                    break;

                case ConsoleKey.LeftArrow:
                    if (cursorPos > 0)
                    {
                        cursorPos--;
                        Console.SetCursorPosition(Console.CursorLeft - 1, Console.CursorTop);
                    }
                    break;

                case ConsoleKey.RightArrow:
                    if (cursorPos < buffer.Count)
                    {
                        cursorPos++;
                        Console.SetCursorPosition(Console.CursorLeft + 1, Console.CursorTop);
                    }
                    break;

                case ConsoleKey.Home:
                    cursorPos = 0;
                    RedrawLine(buffer, cursorPos);
                    break;

                case ConsoleKey.End:
                    cursorPos = buffer.Count;
                    RedrawLine(buffer, cursorPos);
                    break;

                case ConsoleKey.Escape:
                    buffer.Clear();
                    cursorPos = 0;
                    RedrawLine(buffer, cursorPos);
                    break;

                case ConsoleKey.Tab:
                    HandleTab(buffer, ref cursorPos, ref tabIndex, ref tabPrefix, ref tabMatches);
                    break;

                default:
                    if (key.KeyChar >= 32) // Printable character
                    {
                        buffer.Insert(cursorPos, key.KeyChar);
                        cursorPos++;

                        // Optimize: if typing at end, just write the char
                        if (cursorPos == buffer.Count)
                            Console.Write(key.KeyChar);
                        else
                            RedrawLine(buffer, cursorPos);
                    }
                    break;
            }
        }
    }

    private static void HandleTab(List<char> buffer, ref int cursorPos,
        ref int tabIndex, ref string? tabPrefix, ref string[]? tabMatches)
    {
        var text = new string(buffer.ToArray());

        // First Tab press: find matches
        if (tabMatches == null)
        {
            tabPrefix = FindCompletionPrefix(text, cursorPos);
            if (tabPrefix == null) return;

            tabMatches = GetMatches(tabPrefix);
            if (tabMatches.Length == 0) { tabMatches = null; return; }

            // If only one match, complete immediately
            if (tabMatches.Length == 1)
            {
                ApplyCompletion(buffer, ref cursorPos, tabPrefix, tabMatches[0]);
                tabMatches = null;
                return;
            }

            // Show all matches below the input
            tabIndex = 0;
            ShowCompletions(tabMatches, tabIndex);
            ApplyCompletion(buffer, ref cursorPos, tabPrefix, tabMatches[0]);
            return;
        }

        // Subsequent Tab: cycle through matches
        tabIndex = (tabIndex + 1) % tabMatches.Length;
        // Undo previous completion by restoring prefix
        var current = new string(buffer.ToArray());
        var prefixStart = current.LastIndexOf(tabMatches[(tabIndex - 1 + tabMatches.Length) % tabMatches.Length]);
        if (prefixStart < 0) prefixStart = FindPrefixStart(current, cursorPos);

        // Revert to prefix, then apply new completion
        while (buffer.Count > prefixStart) buffer.RemoveAt(buffer.Count - 1);
        cursorPos = buffer.Count;
        foreach (var c in tabPrefix!) buffer.Add(c);
        cursorPos = buffer.Count;

        ApplyCompletion(buffer, ref cursorPos, tabPrefix, tabMatches[tabIndex]);
        ShowCompletions(tabMatches, tabIndex);
    }

    private static string? FindCompletionPrefix(string text, int cursorPos)
    {
        var textToCursor = text[..cursorPos];

        // Find the last @ or / token
        var lastAt = textToCursor.LastIndexOf('@');
        var lastSlash = -1;
        if (textToCursor.StartsWith('/'))
            lastSlash = 0;

        if (lastAt >= 0)
            return textToCursor[lastAt..];
        if (lastSlash >= 0)
            return textToCursor[lastSlash..];

        return null;
    }

    private static int FindPrefixStart(string text, int cursorPos)
    {
        var textToCursor = text[..Math.Min(cursorPos, text.Length)];
        var lastAt = textToCursor.LastIndexOf('@');
        if (lastAt >= 0) return lastAt;
        if (textToCursor.StartsWith('/')) return 0;
        return cursorPos;
    }

    private static string[] GetMatches(string prefix)
    {
        if (prefix.StartsWith('/'))
        {
            return SlashCommands
                .Where(c => c.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        if (prefix.StartsWith('@'))
        {
            return AtContexts
                .Where(c => c.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        return [];
    }

    private static void ApplyCompletion(List<char> buffer, ref int cursorPos, string prefix, string completion)
    {
        // Find where the prefix starts in the buffer
        var bufStr = new string(buffer.ToArray());
        var prefixStart = bufStr.LastIndexOf(prefix);
        if (prefixStart < 0) prefixStart = FindPrefixStart(bufStr, cursorPos);

        // Remove old prefix
        while (buffer.Count > prefixStart) buffer.RemoveAt(buffer.Count - 1);

        // Insert completion
        foreach (var c in completion) buffer.Add(c);
        cursorPos = buffer.Count;

        RedrawLine(buffer, cursorPos);
    }

    private static void ShowCompletions(string[] matches, int activeIndex)
    {
        // Save cursor position
        var savedLeft = Console.CursorLeft;
        var savedTop = Console.CursorTop;

        // Write completions on the next line
        Console.SetCursorPosition(0, savedTop + 1);
        Console.Write(new string(' ', Console.BufferWidth)); // Clear line
        Console.SetCursorPosition(0, savedTop + 1);

        for (int i = 0; i < matches.Length && i < 10; i++)
        {
            if (i == activeIndex)
                Console.Write($"{AnsiHelper.Cyan}{AnsiHelper.Bold}{matches[i]}{AnsiHelper.Reset} ");
            else
                Console.Write($"{AnsiHelper.Dim}{matches[i]}{AnsiHelper.Reset} ");
        }
        if (matches.Length > 10)
            Console.Write($"{AnsiHelper.Dim}+{matches.Length - 10} more{AnsiHelper.Reset}");

        // Restore cursor
        Console.SetCursorPosition(savedLeft, savedTop);
    }

    private static void RedrawLine(List<char> buffer, int cursorPos)
    {
        // Calculate prompt width ("> " = 2 chars, but with ANSI codes it's trickier)
        var promptWidth = 2; // "> "
        var lineStart = promptWidth;

        Console.SetCursorPosition(lineStart, Console.CursorTop);
        var text = new string(buffer.ToArray());
        Console.Write(text);
        // Clear any leftover characters from previous longer input
        var clearLen = Console.BufferWidth - lineStart - text.Length - 1;
        if (clearLen > 0) Console.Write(new string(' ', clearLen));
        // Set cursor to correct position
        Console.SetCursorPosition(lineStart + cursorPos, Console.CursorTop);
    }
}
