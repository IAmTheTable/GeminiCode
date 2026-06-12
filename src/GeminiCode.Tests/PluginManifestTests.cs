using GeminiCode.Plugins;

namespace GeminiCode.Tests;

public class PluginManifestTests
{
    [Fact]
    public void FromParsed_MultiPhase_SplitsOnPhaseHeaders()
    {
        var body = "## Phase: Understanding\nexplore {input}\n\n## Phase: Summary\nwrap up\n";
        var m = PluginManifest.FromParsed(
            fields: new() { ["name"] = "brainstorm", ["description"] = "d", ["argHint"] = "<topic>" },
            body: body, source: "plugins/brainstorm/SKILL.md");

        Assert.Equal("brainstorm", m.Name);
        Assert.Equal("/brainstorm", m.Command);
        Assert.False(m.IsSingleShot);
        Assert.Equal(2, m.Phases.Count);
        Assert.Equal("Understanding", m.Phases[0].Name);
        Assert.Contains("explore {input}", m.Phases[0].PromptTemplate);
        Assert.True(m.Phases[0].AllowToolCalls);
        Assert.False(m.Phases[1].AllowToolCalls); // "Summary" → no tool calls
    }

    [Fact]
    public void FromParsed_NoPhaseHeaders_IsSingleShotOnePhase()
    {
        var m = PluginManifest.FromParsed(
            fields: new() { ["name"] = "greet", ["description"] = "d" },
            body: "Say hello to {input}", source: "x");

        Assert.True(m.IsSingleShot);
        Assert.Single(m.Phases);
        Assert.Equal("Say hello to {input}", m.Phases[0].PromptTemplate.Trim());
    }

    [Fact]
    public void FromParsed_ExplicitCommand_IsNormalizedLowercaseWithSlash()
    {
        var m = PluginManifest.FromParsed(
            fields: new() { ["name"] = "x", ["description"] = "d", ["command"] = "Brainstorm" },
            body: "b", source: "x");
        Assert.Equal("/brainstorm", m.Command);
    }

    [Fact]
    public void ToWorkflow_ProducesDefinitionWithSamePhases()
    {
        var m = PluginManifest.FromParsed(
            fields: new() { ["name"] = "brainstorm", ["description"] = "d" },
            body: "## Phase: A\nx\n## Phase: B\ny", source: "x");
        var wf = m.ToWorkflow();
        Assert.Equal("brainstorm", wf.Name);
        Assert.Equal(2, wf.Phases.Count);
    }
}
