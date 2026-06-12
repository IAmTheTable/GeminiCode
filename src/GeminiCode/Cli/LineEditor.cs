namespace GeminiCode.Cli;

/// <summary>Pure multi-line text-editing model: lines + a (row, col) cursor + undo/redo.
/// No console I/O — the renderer (InputReader) reads Lines/CursorRow/CursorCol and draws them.</summary>
public class LineEditor
{
    private readonly List<string> _lines = new() { "" };
    private int _row;
    private int _col;
    private readonly Stack<Snapshot> _undo = new();
    private readonly Stack<Snapshot> _redo = new();
    private const int MaxUndo = 200;

    private readonly record struct Snapshot(string Text, int Row, int Col);

    public IReadOnlyList<string> Lines => _lines;
    public int CursorRow => _row;
    public int CursorCol => _col;
    public string Text => string.Join("\n", _lines);
    public bool IsEmpty => _lines.Count == 1 && _lines[0].Length == 0;
    public bool OnFirstLine => _row == 0;
    public bool OnLastLine => _row == _lines.Count - 1;

    private Snapshot Current() => new(Text, _row, _col);

    private void PushUndo()
    {
        _undo.Push(Current());
        _redo.Clear();
        if (_undo.Count > MaxUndo)
        {
            var kept = _undo.ToArray()[..MaxUndo]; // newest first
            _undo.Clear();
            for (int i = kept.Length - 1; i >= 0; i--) _undo.Push(kept[i]);
        }
    }

    public void Clear()
    {
        _lines.Clear();
        _lines.Add("");
        _row = 0;
        _col = 0;
        _undo.Clear();
        _redo.Clear();
    }

    /// <summary>Replace all content (used for history recall). Does not clear the undo stack.</summary>
    public void SetText(string text, bool cursorToEnd = true)
    {
        PushUndo();
        LoadLines(text ?? "");
        if (cursorToEnd) { _row = _lines.Count - 1; _col = _lines[_row].Length; }
        else { _row = 0; _col = 0; }
    }

    private void LoadLines(string text)
    {
        _lines.Clear();
        foreach (var l in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            _lines.Add(l);
        if (_lines.Count == 0) _lines.Add("");
        _row = Math.Clamp(_row, 0, _lines.Count - 1);
        _col = Math.Clamp(_col, 0, _lines[_row].Length);
    }

    public void InsertChar(char c)
    {
        PushUndo();
        _lines[_row] = _lines[_row].Insert(_col, c.ToString());
        _col++;
    }

    /// <summary>Insert a string at the cursor, splitting on newlines (used for paste).</summary>
    public void Insert(string s)
    {
        if (string.IsNullOrEmpty(s)) return;
        PushUndo();
        var segs = s.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var line = _lines[_row];
        var before = line[.._col];
        var after = line[_col..];
        if (segs.Length == 1)
        {
            _lines[_row] = before + segs[0] + after;
            _col = before.Length + segs[0].Length;
        }
        else
        {
            _lines[_row] = before + segs[0];
            var newLines = new List<string>();
            for (int i = 1; i < segs.Length; i++) newLines.Add(segs[i]);
            newLines[^1] += after;
            _lines.InsertRange(_row + 1, newLines);
            _row += segs.Length - 1;
            _col = segs[^1].Length;
        }
    }

    public void Backspace()
    {
        if (_col > 0) { PushUndo(); _lines[_row] = _lines[_row].Remove(_col - 1, 1); _col--; }
        else if (_row > 0)
        {
            PushUndo();
            var prevLen = _lines[_row - 1].Length;
            _lines[_row - 1] += _lines[_row];
            _lines.RemoveAt(_row);
            _row--;
            _col = prevLen;
        }
    }

    public void DeleteForward()
    {
        if (_col < _lines[_row].Length) { PushUndo(); _lines[_row] = _lines[_row].Remove(_col, 1); }
        else if (_row < _lines.Count - 1) { PushUndo(); _lines[_row] += _lines[_row + 1]; _lines.RemoveAt(_row + 1); }
    }

    public bool MoveLeft()
    {
        if (_col > 0) { _col--; return true; }
        if (_row > 0) { _row--; _col = _lines[_row].Length; return true; }
        return false;
    }

    public bool MoveRight()
    {
        if (_col < _lines[_row].Length) { _col++; return true; }
        if (_row < _lines.Count - 1) { _row++; _col = 0; return true; }
        return false;
    }

    public void MoveLineStart() => _col = 0;
    public void MoveLineEnd() => _col = _lines[_row].Length;

    public bool MoveUpLine()
    {
        if (_row > 0) { _row--; _col = Math.Min(_col, _lines[_row].Length); return true; }
        return false;
    }

    public bool MoveDownLine()
    {
        if (_row < _lines.Count - 1) { _row++; _col = Math.Min(_col, _lines[_row].Length); return true; }
        return false;
    }

    public bool Undo()
    {
        if (_undo.Count == 0) return false;
        _redo.Push(Current());
        Restore(_undo.Pop());
        return true;
    }

    public bool Redo()
    {
        if (_redo.Count == 0) return false;
        _undo.Push(Current());
        Restore(_redo.Pop());
        return true;
    }

    private void Restore(Snapshot s)
    {
        LoadLines(s.Text);
        _row = Math.Clamp(s.Row, 0, _lines.Count - 1);
        _col = Math.Clamp(s.Col, 0, _lines[_row].Length);
    }
}
