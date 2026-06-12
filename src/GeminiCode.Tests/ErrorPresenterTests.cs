using GeminiCode.Cli;

namespace GeminiCode.Tests;

public class ErrorPresenterTests
{
    [Fact]
    public void AuthLost_HasSignInAndRetryAffordances()
    {
        var s = ErrorPresenter.AuthLost();
        Assert.Contains("signed out", s, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sign in", s, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Enter", s);
        Assert.Contains("exit", s, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("re-sent", s, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UiBroken_NamesEachMissingElementAndDiscoverFlag()
    {
        var s = ErrorPresenter.UiBroken(new[] { "sendButton", "responseContainer" });
        Assert.Contains("sendButton", s);
        Assert.Contains("responseContainer", s);
        Assert.Contains("--discover-selectors", s);
    }

    [Fact]
    public void ModelUnavailable_ListsRequestedAndAvailableAndCommand()
    {
        var s = ErrorPresenter.ModelUnavailable("ultra", new[] { "flash", "pro", "thinking" });
        Assert.Contains("ultra", s);
        Assert.Contains("flash", s);
        Assert.Contains("pro", s);
        Assert.Contains("thinking", s);
        Assert.Contains("/model", s);
    }

    [Theory]
    [InlineData("Access denied: path 'x' is outside the working directory '...'.", "working directory")]
    [InlineData("Not found: foo.txt", "exist")]
    [InlineData("Unknown tool: Frobnicate", "not a recognized tool")]
    [InlineData("Permission denied by user", "approve")]
    [InlineData("git exploded unexpectedly", "git exploded unexpectedly")]
    public void ToolFailure_ClassifiesOutputIntoHints(string output, string expectedHintFragment)
    {
        var s = ErrorPresenter.ToolFailure("SomeTool", output);
        Assert.Contains("SomeTool", s);
        Assert.Contains(expectedHintFragment, s, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(GenericErrorCategory.BrowserClosed, "restart")]
    [InlineData(GenericErrorCategory.WebViewScript, "--discover-selectors")]
    [InlineData(GenericErrorCategory.Timeout, "/new")]
    [InlineData(GenericErrorCategory.Other, "boom")]
    public void Generic_MapsCategoryToNextStep(GenericErrorCategory cat, string expectedFragment)
    {
        var s = ErrorPresenter.Generic(cat, "boom");
        Assert.Contains(expectedFragment, s, StringComparison.OrdinalIgnoreCase);
    }
}
