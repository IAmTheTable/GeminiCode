// src/GeminiCode.Tests/UsageTrackerTests.cs
using GeminiCode.Agent;

namespace GeminiCode.Tests;

public class UsageTrackerTests
{
    [Fact]
    public void Estimate_IsCeilingOfCharsOverFour()
    {
        Assert.Equal(0, UsageTracker.Estimate(""));
        Assert.Equal(1, UsageTracker.Estimate("abcd"));   // 4/4
        Assert.Equal(2, UsageTracker.Estimate("abcde"));  // 5/4 -> 2
    }

    [Fact]
    public void RecordTurn_AccumulatesTotalsAndDelta()
    {
        var u = new UsageTracker();
        u.RecordSent(new string('x', 400));     // 100 tok
        u.RecordReceived(new string('y', 800));  // 200 tok
        Assert.Equal(1, u.TurnCount);
        Assert.Equal(300, u.TotalTokens);
        Assert.Equal(300, u.LastTurnTokens);

        u.RecordSent(new string('x', 40));       // 10 tok
        u.RecordReceived(new string('y', 40));   // 10 tok
        Assert.Equal(2, u.TurnCount);
        Assert.Equal(320, u.TotalTokens);
        Assert.Equal(20, u.LastTurnTokens);
    }

    [Fact]
    public void Footer_ContainsTokensTurnAndDelta()
    {
        var u = new UsageTracker();
        u.RecordSent(new string('x', 4000));
        u.RecordReceived(new string('y', 4000));
        var f = u.Footer();
        Assert.Contains("turn 1", f);
        Assert.Contains("tok", f);
    }
}
