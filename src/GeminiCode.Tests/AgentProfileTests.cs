namespace GeminiCode.Tests;

using GeminiCode.Agent;

public class AgentProfileTests
{
    [Fact]
    public void ListProfiles_ReturnsBuiltInProfiles()
    {
        var profile = new AgentProfile();
        var names = profile.ListProfiles();
        Assert.Contains("general", names);
        Assert.Contains("code-reviewer", names);
        Assert.Contains("architect", names);
        Assert.Contains("debugger", names);
        Assert.Contains("refactorer", names);
    }

    [Fact]
    public void LoadProfile_General_ReturnsContent()
    {
        var profile = new AgentProfile();
        profile.SetActive("general");
        var content = profile.GetActiveProfileContent();
        Assert.Contains("software engineer", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LoadProfile_CodeReviewer_ReturnsContent()
    {
        var profile = new AgentProfile();
        profile.SetActive("code-reviewer");
        var content = profile.GetActiveProfileContent();
        Assert.Contains("code reviewer", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SetActive_NormalizesCase()
    {
        var profile = new AgentProfile();
        profile.SetActive("Code-Reviewer");
        Assert.Equal("code-reviewer", profile.ActiveProfileName);
    }

    [Fact]
    public void LoadProfile_Unknown_ReturnsFalse()
    {
        var profile = new AgentProfile();
        var result = profile.SetActive("nonexistent");
        Assert.False(result);
    }

    [Fact]
    public void SetActive_ChangesActiveProfile()
    {
        var profile = new AgentProfile();
        profile.SetActive("code-reviewer");
        Assert.Equal("code-reviewer", profile.ActiveProfileName);
    }

    [Fact]
    public void GeminiMd_LoadedWhenPresent()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "agentprofile_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "GEMINI.md"), "# Custom Instructions\nAlways use snake_case.");
            var profile = new AgentProfile(tempDir);
            var geminiMd = profile.GetGeminiMdContent();
            Assert.Contains("snake_case", geminiMd);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void GeminiMd_NullWhenMissing()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "agentprofile_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var profile = new AgentProfile(tempDir);
            Assert.Null(profile.GetGeminiMdContent());
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void DiscoverProjectProfiles_FindsLocalProfiles()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "agentprofile_test_" + Guid.NewGuid().ToString("N"));
        var agentDir = Path.Combine(tempDir, ".gemini", "agents");
        Directory.CreateDirectory(agentDir);
        try
        {
            File.WriteAllText(Path.Combine(agentDir, "custom-agent.md"), "# Custom\nDo custom things.");
            var profile = new AgentProfile(tempDir);
            var names = profile.ListProfiles();
            Assert.Contains("custom-agent", names);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
