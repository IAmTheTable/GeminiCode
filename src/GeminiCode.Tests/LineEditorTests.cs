using GeminiCode.Cli;

namespace GeminiCode.Tests;

public class LineEditorTests
{
    [Fact]
    public void InsertChar_BuildsTextAndAdvancesCursor()
    {
        var e = new LineEditor();
        foreach (var c in "abc") e.InsertChar(c);
        Assert.Equal("abc", e.Text);
        Assert.Equal(0, e.CursorRow);
        Assert.Equal(3, e.CursorCol);
    }

    [Fact]
    public void Insert_WithNewlines_SplitsLines()
    {
        var e = new LineEditor();
        e.Insert("line1\nline2\nline3");
        Assert.Equal(3, e.Lines.Count);
        Assert.Equal("line1", e.Lines[0]);
        Assert.Equal("line3", e.Lines[2]);
        Assert.Equal(2, e.CursorRow);
        Assert.Equal(5, e.CursorCol);
    }

    [Fact]
    public void Insert_MidLine_KeepsTrailingText()
    {
        var e = new LineEditor();
        e.Insert("aXb");
        e.MoveLeft(); // cursor before 'b' (col 2)
        e.Insert("\n");
        Assert.Equal(2, e.Lines.Count);
        Assert.Equal("aX", e.Lines[0]);
        Assert.Equal("b", e.Lines[1]);
    }

    [Fact]
    public void Backspace_JoinsLines()
    {
        var e = new LineEditor();
        e.Insert("ab\ncd");      // cursor at end of "cd"
        e.MoveLineStart();        // col 0 of line 2
        e.Backspace();            // join with line 1
        Assert.Single(e.Lines);
        Assert.Equal("abcd", e.Text);
        Assert.Equal(0, e.CursorRow);
        Assert.Equal(2, e.CursorCol);
    }

    [Fact]
    public void MoveUpDownLine_RespectsBoundaries()
    {
        var e = new LineEditor();
        e.Insert("a\nbb\nccc");   // cursor row 2
        Assert.True(e.OnLastLine);
        Assert.True(e.MoveUpLine());
        Assert.Equal(1, e.CursorRow);
        Assert.True(e.MoveUpLine());
        Assert.True(e.OnFirstLine);
        Assert.False(e.MoveUpLine()); // already at top
    }

    [Fact]
    public void Undo_Redo_RestoresState()
    {
        var e = new LineEditor();
        e.Insert("hello");
        e.Insert(" world");
        Assert.Equal("hello world", e.Text);
        Assert.True(e.Undo());
        Assert.Equal("hello", e.Text);
        Assert.True(e.Undo());
        Assert.Equal("", e.Text);
        Assert.True(e.Redo());
        Assert.Equal("hello", e.Text);
    }

    [Fact]
    public void SetText_LoadsMultilineAndCursorToEnd()
    {
        var e = new LineEditor();
        e.SetText("one\ntwo");
        Assert.Equal(2, e.Lines.Count);
        Assert.Equal(1, e.CursorRow);
        Assert.Equal(3, e.CursorCol);
    }
}
