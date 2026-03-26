using GeminiCode.Permissions;

namespace GeminiCode.Tests;

public class SessionAllowlistTests
{
    [Fact]
    public void IsAllowed_NotAdded_ReturnsFalse()
    {
        var al = new SessionAllowlist();
        Assert.False(al.IsAllowed("WriteFile"));
    }

    [Fact]
    public void Add_ThenCheck_ReturnsTrue()
    {
        var al = new SessionAllowlist();
        al.Add("WriteFile");
        Assert.True(al.IsAllowed("WriteFile"));
    }

    [Fact]
    public void Add_RunCommand_Rejected()
    {
        var al = new SessionAllowlist();
        Assert.False(al.Add("RunCommand"));
        Assert.False(al.IsAllowed("RunCommand"));
    }

    [Fact]
    public void Clear_RemovesAllEntries()
    {
        var al = new SessionAllowlist();
        al.Add("WriteFile");
        al.Add("ReadFile");
        al.Clear();
        Assert.False(al.IsAllowed("WriteFile"));
        Assert.False(al.IsAllowed("ReadFile"));
    }

    [Fact]
    public void GetEntries_ReturnsAddedTools()
    {
        var al = new SessionAllowlist();
        al.Add("ReadFile");
        al.Add("EditFile");
        var entries = al.GetEntries();
        Assert.Contains("ReadFile", entries);
        Assert.Contains("EditFile", entries);
    }
}
