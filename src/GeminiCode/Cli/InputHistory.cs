namespace GeminiCode.Cli;

/// <summary>Shell-style command history with draft preservation.
/// Up recalls older sent entries; Down moves toward newer ones and finally back to the
/// in-progress draft that was being typed when browsing started.</summary>
public class InputHistory
{
    private readonly List<string> _entries = new();
    private int _index = -1;   // -1 = not browsing (editing the draft)
    private string _draft = "";

    public bool IsBrowsing => _index >= 0;
    public IReadOnlyList<string> Entries => _entries;

    /// <summary>Record a submitted entry (skips blanks and consecutive duplicates) and exit browsing.</summary>
    public void Add(string entry)
    {
        entry ??= "";
        if (entry.Trim().Length > 0 && (_entries.Count == 0 || _entries[^1] != entry))
            _entries.Add(entry);
        _index = -1;
        _draft = "";
    }

    /// <summary>Recall an older entry. The first call saves <paramref name="currentText"/> as the draft.
    /// Returns the entry text, or null if there's no history.</summary>
    public string? Up(string currentText)
    {
        if (_entries.Count == 0) return null;
        if (_index == -1) { _draft = currentText; _index = _entries.Count - 1; }
        else if (_index > 0) { _index--; }
        return _entries[_index];
    }

    /// <summary>Move to a newer entry; past the newest restores the draft and exits browsing.
    /// Returns the text to show, or null if not currently browsing.</summary>
    public string? Down()
    {
        if (_index == -1) return null;
        if (_index < _entries.Count - 1) { _index++; return _entries[_index]; }
        _index = -1;
        return _draft;
    }

    public void ExitBrowsing() => _index = -1;
}
