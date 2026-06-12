using GeminiCode.Cli;

namespace GeminiCode.Tests;

public class InputHistoryTests
{
    [Fact]
    public void UpDown_BrowsesEntriesAndRestoresDraft()
    {
        var h = new InputHistory();
        h.Add("first");
        h.Add("second");

        // start typing a draft, then browse up
        Assert.Equal("second", h.Up("my draft")); // newest first
        Assert.Equal("first", h.Up("my draft"));  // older
        Assert.Equal("first", h.Up("my draft"));  // stays at oldest
        Assert.True(h.IsBrowsing);

        Assert.Equal("second", h.Down());          // newer
        Assert.Equal("my draft", h.Down());        // past newest -> draft restored
        Assert.False(h.IsBrowsing);
    }

    [Fact]
    public void Down_WhenNotBrowsing_ReturnsNull()
    {
        var h = new InputHistory();
        h.Add("x");
        Assert.Null(h.Down());
    }

    [Fact]
    public void Up_WithNoHistory_ReturnsNull()
    {
        var h = new InputHistory();
        Assert.Null(h.Up("draft"));
    }

    [Fact]
    public void Add_SkipsBlanksAndConsecutiveDuplicates()
    {
        var h = new InputHistory();
        h.Add("a");
        h.Add("a");      // dup ignored
        h.Add("   ");    // blank ignored
        h.Add("b");
        Assert.Equal(new[] { "a", "b" }, h.Entries);
    }

    [Fact]
    public void Add_ExitsBrowsing()
    {
        var h = new InputHistory();
        h.Add("a");
        h.Up("draft");
        Assert.True(h.IsBrowsing);
        h.Add("b");
        Assert.False(h.IsBrowsing);
    }
}
