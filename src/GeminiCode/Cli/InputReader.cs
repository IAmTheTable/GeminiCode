// src/GeminiCode/Cli/InputReader.cs
namespace GeminiCode.Cli;

/// <summary>
/// Interactive console input with multi-line editing, command/@ autocomplete, shell-style
/// history (Up/Down with draft preservation), undo/redo (Ctrl+Z / Ctrl+Y), and clipboard
/// paste (Ctrl+V — text, or an image which is attached as @image). Backed by the pure
/// LineEditor + InputHistory models; this layer only handles keys and console rendering.
/// </summary>
public class InputReader
{
    private static readonly List<CompletionItem> SlashCommands =
    [
        new("/help",             "Show available commands"),
        new("/clear",            "Clear terminal"),
        new("/new",              "Start new conversation"),
        new("/model",            "Show/switch model"),
        new("/model flash",      "Switch to 3.5 Flash"),
        new("/model flash-lite", "Switch to 3.1 Flash-Lite"),
        new("/model pro",        "Switch to 3.1 Pro"),
        new("/model standard",   "Thinking level: Standard"),
        new("/model extended",   "Thinking level: Extended"),
        new("/limit",            "Check usage limit status"),
        new("/usage",            "Show estimated token usage"),
        new("/browser",          "Focus browser window"),
        new("/history",          "Show turn count"),
        new("/allowlist",        "Show auto-approved tools"),
        new("/tools",            "List available tools"),
        new("/plugins",          "List loaded plugins"),
        new("/reload",           "Re-scan the plugins folder"),
        new("/status",           "Show session state"),
        new("/cd ",              "Change working directory"),
        new("/agent ",           "List or switch agent profiles"),
        new("/init",             "Create a starter GEMINI.md"),
        new("/compact",          "Summarize context and start fresh"),
        new("/save",             "Save session context"),
        new("/restore",          "Restore previous session context"),
        new("/context",          "Show current session context"),
        new("/paste",            "Multi-line paste mode"),
        new("/exit",             "Quit GeminiCode"),
    ];

    private static readonly CompletionItem[] AtContexts =
    [
        new("@file ",       "Attach file contents"),
        new("@image ",      "Attach an image (or Ctrl+V an image)"),
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

    /// <summary>Returns clipboard text, or null. Set by the host (marshals to the WinForms STA thread).</summary>
    public static Func<string?>? ClipboardTextProvider;
    /// <summary>Saves a clipboard image to a temp file and returns its path, or null. Set by the host.</summary>
    public static Func<string?>? ClipboardImageProvider;

    private static readonly InputHistory History = new();

    /// <summary>Append dynamic commands (e.g. dropped plugins) to the completion list.</summary>
    public static void AddCommands(IEnumerable<(string Text, string Description)> commands)
    {
        foreach (var (text, desc) in commands)
            if (!SlashCommands.Any(c => c.Text.Equals(text, StringComparison.OrdinalIgnoreCase)))
                SlashCommands.Add(new CompletionItem(text, desc));
    }

    private const int PromptWidth = 2; // "> "
    private const int MaxPopupItems = 10;
    private static int _scrollOffset;

    public static string? ReadLine()
    {
        var editor = new LineEditor();
        CompletionItem[]? matches = null;
        var selectedIndex = -1;
        var popupVisible = false;
        _scrollOffset = 0;

        var anchorTop = Console.CursorTop;   // the row where the caller printed "> "
        var lastRenderRows = 1;

        void RefreshPopup()
        {
            var line = editor.CursorRow < editor.Lines.Count ? editor.Lines[editor.CursorRow] : "";
            var prefix = FindActiveToken(line, editor.CursorCol, editor.CursorRow);
            if (prefix == null) { matches = null; selectedIndex = -1; popupVisible = false; return; }
            var found = GetMatches(prefix);
            if (found.Length == 0) { matches = null; selectedIndex = -1; popupVisible = false; return; }
            matches = found;
            selectedIndex = 0;
            _scrollOffset = 0;
            popupVisible = true;
        }

        void Render()
        {
            var lines = editor.Lines;
            var popupRows = popupVisible && matches != null ? PopupRowCount(matches.Length) : 0;
            var needed = lines.Count + popupRows;

            // Scroll the buffer if the input + popup would run off the bottom.
            var overflow = (anchorTop + needed) - Console.BufferHeight;
            if (overflow > 0)
            {
                Console.SetCursorPosition(0, Console.BufferHeight - 1);
                for (int i = 0; i < overflow; i++) Console.Write('\n');
                anchorTop = Math.Max(0, anchorTop - overflow);
            }

            // Clear the region used by this and the previous render.
            var clearRows = Math.Max(lastRenderRows, needed);
            var width = Console.WindowWidth - 1;
            for (int i = 0; i < clearRows; i++)
            {
                var row = anchorTop + i;
                if (row >= 0 && row < Console.BufferHeight)
                {
                    Console.SetCursorPosition(0, row);
                    Console.Write(new string(' ', width));
                }
            }

            // Draw input lines (prompt on the first, aligned spaces on continuations).
            for (int r = 0; r < lines.Count; r++)
            {
                Console.SetCursorPosition(0, anchorTop + r);
                var pfx = r == 0 ? $"{AnsiHelper.Green}>{AnsiHelper.Reset} " : "  ";
                Console.Write(pfx + lines[r]);
            }

            if (popupRows > 0) DrawPopup(matches!, selectedIndex, anchorTop + lines.Count);
            lastRenderRows = needed;

            var cr = Math.Min(anchorTop + editor.CursorRow, Console.BufferHeight - 1);
            var cc = Math.Min(PromptWidth + editor.CursorCol, Console.WindowWidth - 1);
            Console.SetCursorPosition(cc, cr);
        }

        void AcceptCompletion(string completion)
        {
            var line = editor.Lines[editor.CursorRow];
            var tokenStart = FindTokenStart(line, editor.CursorCol, editor.CursorRow);
            while (editor.CursorCol > tokenStart) editor.Backspace();
            editor.Insert(completion);
            popupVisible = false; matches = null; selectedIndex = -1;
        }

        Render();

        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            var ctrl = key.Modifiers.HasFlag(ConsoleModifiers.Control);
            var shiftOrAlt = key.Modifiers.HasFlag(ConsoleModifiers.Shift) || key.Modifiers.HasFlag(ConsoleModifiers.Alt);

            switch (key.Key)
            {
                case ConsoleKey.Enter when popupVisible && matches != null && selectedIndex >= 0:
                    AcceptCompletion(matches[selectedIndex].Text);
                    Render();
                    break;

                case ConsoleKey.Enter when shiftOrAlt:   // Shift/Alt+Enter inserts a newline
                    editor.Insert("\n");
                    RefreshPopup();
                    Render();
                    break;

                case ConsoleKey.Enter:                    // submit
                    popupVisible = false;
                    Render();
                    Console.SetCursorPosition(0, Math.Min(anchorTop + editor.Lines.Count, Console.BufferHeight - 1));
                    Console.WriteLine();
                    var result = editor.Text;
                    History.Add(result);
                    return result;

                case ConsoleKey.Tab when popupVisible && matches != null && matches.Length > 0:
                    AcceptCompletion(matches[selectedIndex >= 0 ? selectedIndex : 0].Text);
                    Render();
                    break;

                case ConsoleKey.UpArrow when popupVisible && matches != null:
                    selectedIndex = Math.Max(0, selectedIndex - 1);
                    Render();
                    break;
                case ConsoleKey.UpArrow:
                    if (!editor.MoveUpLine())
                    {
                        var recalled = History.Up(editor.Text);
                        if (recalled != null) editor.SetText(recalled);
                    }
                    RefreshPopup();
                    Render();
                    break;

                case ConsoleKey.DownArrow when popupVisible && matches != null:
                    selectedIndex = Math.Min(matches.Length - 1, selectedIndex + 1);
                    Render();
                    break;
                case ConsoleKey.DownArrow:
                    if (!editor.MoveDownLine())
                    {
                        var next = History.Down();
                        if (next != null) editor.SetText(next);
                    }
                    RefreshPopup();
                    Render();
                    break;

                case ConsoleKey.LeftArrow:  editor.MoveLeft(); RefreshPopup(); Render(); break;
                case ConsoleKey.RightArrow: editor.MoveRight(); RefreshPopup(); Render(); break;
                case ConsoleKey.Home:       editor.MoveLineStart(); RefreshPopup(); Render(); break;
                case ConsoleKey.End:        editor.MoveLineEnd(); RefreshPopup(); Render(); break;

                case ConsoleKey.Backspace:  editor.Backspace(); RefreshPopup(); Render(); break;
                case ConsoleKey.Delete:     editor.DeleteForward(); RefreshPopup(); Render(); break;

                case ConsoleKey.Escape:
                    if (popupVisible) { popupVisible = false; matches = null; selectedIndex = -1; }
                    else editor.Clear();
                    Render();
                    break;

                case ConsoleKey.Z when ctrl: editor.Undo(); RefreshPopup(); Render(); break;
                case ConsoleKey.Y when ctrl: editor.Redo(); RefreshPopup(); Render(); break;

                case ConsoleKey.V when ctrl:
                    var imgPath = ClipboardImageProvider?.Invoke();
                    if (!string.IsNullOrEmpty(imgPath))
                        editor.Insert($"@image \"{imgPath}\" ");
                    else
                    {
                        var clip = ClipboardTextProvider?.Invoke();
                        if (!string.IsNullOrEmpty(clip)) editor.Insert(clip);
                    }
                    RefreshPopup();
                    Render();
                    break;

                default:
                    if (!char.IsControl(key.KeyChar) && key.KeyChar != '\0')
                    {
                        editor.InsertChar(key.KeyChar);
                        RefreshPopup();
                        Render();
                    }
                    break;
            }
        }
    }

    /// <summary>Find the / or @ token being typed on the current line at the cursor.</summary>
    private static string? FindActiveToken(string line, int col, int row)
    {
        if (col == 0) return null;
        var before = line[..Math.Min(col, line.Length)];
        for (int i = before.Length - 1; i >= 0; i--)
        {
            var ch = before[i];
            if (ch == '@') return before[i..];
            if (ch == '/' && i == 0 && row == 0) return before;
            if (ch == ' ')
            {
                if (i + 1 < before.Length && before[i + 1] == '@') return before[(i + 1)..];
                break;
            }
        }
        return null;
    }

    private static int FindTokenStart(string line, int col, int row)
    {
        var before = line[..Math.Min(col, line.Length)];
        for (int i = before.Length - 1; i >= 0; i--)
        {
            if (before[i] == '@') return i;
            if (before[i] == '/' && i == 0 && row == 0) return 0;
            if (before[i] == ' ' && i + 1 < before.Length && before[i + 1] == '@') return i + 1;
        }
        return col;
    }

    private static CompletionItem[] GetMatches(string prefix)
    {
        if (prefix.StartsWith('/'))
            return SlashCommands.Where(c => c.Text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (prefix.StartsWith('@'))
            return AtContexts.Where(c => c.Text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToArray();
        return [];
    }

    private static int PopupRowCount(int itemCount)
    {
        var visible = Math.Min(MaxPopupItems, itemCount);
        var hasAbove = _scrollOffset > 0;
        var hasBelow = _scrollOffset + visible < itemCount;
        return visible + (hasAbove ? 1 : 0) + (hasBelow ? 1 : 0);
    }

    private static void DrawPopup(CompletionItem[] items, int selectedIndex, int top)
    {
        if (selectedIndex >= 0)
        {
            if (selectedIndex < _scrollOffset) _scrollOffset = selectedIndex;
            else if (selectedIndex >= _scrollOffset + MaxPopupItems) _scrollOffset = selectedIndex - MaxPopupItems + 1;
        }
        var maxOffset = Math.Max(0, items.Length - MaxPopupItems);
        _scrollOffset = Math.Clamp(_scrollOffset, 0, maxOffset);

        var visible = Math.Min(MaxPopupItems, items.Length - _scrollOffset);
        var hasAbove = _scrollOffset > 0;
        var hasBelow = _scrollOffset + visible < items.Length;
        var clearWidth = Console.WindowWidth - PromptWidth - 1;
        var line = 0;

        void Row(string content)
        {
            var r = top + line;
            if (r >= Console.BufferHeight) { line++; return; }
            Console.SetCursorPosition(PromptWidth, r);
            Console.Write(new string(' ', Math.Max(0, clearWidth)));
            Console.SetCursorPosition(PromptWidth, r);
            Console.Write(content);
            line++;
        }

        if (hasAbove) Row($"{AnsiHelper.Dim}↑ {_scrollOffset} more{AnsiHelper.Reset}");
        for (int i = 0; i < visible; i++)
        {
            var idx = _scrollOffset + i;
            var item = items[idx];
            if (idx == selectedIndex)
                Row($"{AnsiHelper.BgDarkGray}{AnsiHelper.Cyan}{AnsiHelper.Bold} {item.Text,-24}{AnsiHelper.Reset}{AnsiHelper.BgDarkGray}{AnsiHelper.Dim} {item.Description}{AnsiHelper.Reset}");
            else
                Row($"  {AnsiHelper.Dim}{item.Text,-24}{item.Description}{AnsiHelper.Reset}");
        }
        if (hasBelow) Row($"{AnsiHelper.Dim}↓ {items.Length - (_scrollOffset + visible)} more (press ↓){AnsiHelper.Reset}");
    }
}
