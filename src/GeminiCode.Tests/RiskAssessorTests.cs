using GeminiCode.Permissions;

namespace GeminiCode.Tests;

public class RiskAssessorTests
{
    [Theory]
    [InlineData("rm -rf /", true)]
    [InlineData("del /s /q", true)]
    [InlineData("format C:", true)]
    [InlineData("git clean -fdx", true)]
    [InlineData("find . -delete", true)]
    [InlineData("dotnet build", false)]
    [InlineData("ls -la", false)]
    [InlineData("echo hello", false)]
    public void IsDestructiveCommand_DetectsCorrectly(string command, bool expected)
    {
        Assert.Equal(expected, RiskAssessor.IsDestructiveCommand(command));
    }
}
