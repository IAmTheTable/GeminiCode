using GeminiCode.Browser;

namespace GeminiCode.Tests;

public class FailureClassifierTests
{
    private static Dictionary<string, bool> Health(bool input = true, bool send = true, bool resp = true)
        => new() { ["chatInput"] = input, ["sendButton"] = send, ["responseContainer"] = resp };

    [Fact]
    public void Classify_LimitPresent_WinsOverEverything()
    {
        var limit = new LimitInfo("limit reached", null, null);
        var d = FailureClassifier.Classify(authenticated: false, Health(input: false), limit);
        Assert.Equal(FailureKind.UsageLimit, d.Kind);
        Assert.Same(limit, d.Limit);
    }

    [Fact]
    public void Classify_NotAuthenticated_WhenNoLimit()
    {
        var d = FailureClassifier.Classify(authenticated: false, Health(), null);
        Assert.Equal(FailureKind.NotAuthenticated, d.Kind);
    }

    [Fact]
    public void Classify_UiBroken_ListsMissingElements()
    {
        var d = FailureClassifier.Classify(authenticated: true, Health(send: false, resp: false), null);
        Assert.Equal(FailureKind.UiBroken, d.Kind);
        Assert.Contains("sendButton", d.MissingElements);
        Assert.Contains("responseContainer", d.MissingElements);
        Assert.DoesNotContain("chatInput", d.MissingElements);
    }

    [Fact]
    public void Classify_Unknown_WhenHealthyAndAuthedAndNoLimit()
    {
        var d = FailureClassifier.Classify(authenticated: true, Health(), null);
        Assert.Equal(FailureKind.Unknown, d.Kind);
        Assert.Empty(d.MissingElements);
    }
}
