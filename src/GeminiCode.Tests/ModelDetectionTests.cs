using GeminiCode.Agent;
using GeminiCode.Browser;

namespace GeminiCode.Tests;

public class ModelDetectionTests
{
    // --- NormalizeModelName tests ---

    [Theory]
    [InlineData("Flash", "Flash")]
    [InlineData("flash", "Flash")]
    [InlineData("2.5 Flash", "Flash")]
    [InlineData("Gemini 2.5 Flash", "Flash")]
    [InlineData("Pro", "Pro")]
    [InlineData("pro", "Pro")]
    [InlineData("2.5 Pro", "Pro")]
    [InlineData("Gemini Pro", "Pro")]
    [InlineData("Thinking", "Thinking")]
    [InlineData("thinking", "Thinking")]
    [InlineData("2.5 Pro with thinking", "Thinking")]
    [InlineData("Deep Think", "Deep Think")]
    [InlineData("deep think", "Deep Think")]
    [InlineData("Deep Research", "Deep Think")]
    [InlineData("", "unknown")]
    [InlineData("  ", "unknown")]
    [InlineData("unknown", "unknown")]
    public void NormalizeModelName_HandlesVariants(string input, string expected)
    {
        var result = BrowserBridge.NormalizeModelName(input);
        Assert.Equal(expected, result);
    }

    // --- ConversationManager model tracking tests ---

    [Fact]
    public void UpdateModel_FirstDetection_SetsBaseline()
    {
        var cm = new ConversationManager();
        var change = cm.UpdateModel("Pro");

        Assert.Null(change); // First detection is not a change
        Assert.Equal("Pro", cm.CurrentModel);
        Assert.Equal("Pro", cm.SessionStartModel);
        Assert.Equal(0, cm.ModelSwitchCount);
    }

    [Fact]
    public void UpdateModel_SameModel_NoChange()
    {
        var cm = new ConversationManager();
        cm.UpdateModel("Pro");
        var change = cm.UpdateModel("Pro");

        Assert.Null(change);
        Assert.Equal(0, cm.ModelSwitchCount);
    }

    [Fact]
    public void UpdateModel_DifferentModel_ReturnsChange()
    {
        var cm = new ConversationManager();
        cm.UpdateModel("Pro");
        var change = cm.UpdateModel("Flash");

        Assert.NotNull(change);
        Assert.Equal("Pro", change!.PreviousModel);
        Assert.Equal("Flash", change.NewModel);
        Assert.Equal(1, change.TotalSwitches);
        Assert.Equal("Flash", cm.CurrentModel);
        Assert.Equal("Pro", cm.SessionStartModel); // Start model unchanged
    }

    [Fact]
    public void UpdateModel_MultipleSwitches_TracksCount()
    {
        var cm = new ConversationManager();
        cm.UpdateModel("Pro");
        cm.UpdateModel("Flash");
        cm.UpdateModel("Thinking");
        var change = cm.UpdateModel("Pro");

        Assert.NotNull(change);
        Assert.Equal(3, cm.ModelSwitchCount);
        Assert.Equal("Pro", cm.CurrentModel);
        Assert.Equal("Pro", cm.SessionStartModel);
    }

    [Fact]
    public void UpdateModel_IgnoresUnknown()
    {
        var cm = new ConversationManager();
        cm.UpdateModel("Pro");
        var change = cm.UpdateModel("unknown");

        Assert.Null(change);
        Assert.Equal("Pro", cm.CurrentModel);
    }

    [Fact]
    public void UpdateModel_IgnoresEmpty()
    {
        var cm = new ConversationManager();
        cm.UpdateModel("Pro");
        var change = cm.UpdateModel("");

        Assert.Null(change);
        Assert.Equal("Pro", cm.CurrentModel);
    }

    [Fact]
    public void UpdateModel_CaseInsensitive()
    {
        var cm = new ConversationManager();
        cm.UpdateModel("Pro");
        var change = cm.UpdateModel("pro");

        Assert.Null(change); // Same model, different case
        Assert.Equal(0, cm.ModelSwitchCount);
    }

    [Fact]
    public void Reset_ClearsModelTracking()
    {
        var cm = new ConversationManager();
        cm.UpdateModel("Pro");
        cm.UpdateModel("Flash");

        cm.Reset();

        Assert.Null(cm.CurrentModel);
        Assert.Null(cm.SessionStartModel);
        Assert.Equal(0, cm.ModelSwitchCount);
    }
}
