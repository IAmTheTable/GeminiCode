// src/GeminiCode.Tests/WorkflowRunnerTests.cs
namespace GeminiCode.Tests;

using GeminiCode.Agent;

public class WorkflowRunnerTests
{
    [Fact]
    public void WorkflowPhase_HasRequiredFields()
    {
        var phase = new WorkflowPhase("Test Phase", "Do something with {diff}", "Testing...", true);
        Assert.Equal("Test Phase", phase.Name);
        Assert.Equal("Do something with {diff}", phase.PromptTemplate);
        Assert.Equal("Testing...", phase.ActiveForm);
        Assert.True(phase.AllowToolCalls);
    }

    [Fact]
    public void WorkflowDefinition_HasPhases()
    {
        var workflow = new WorkflowDefinition("Test Workflow", new[]
        {
            new WorkflowPhase("Phase 1", "prompt 1", "Running phase 1...", false),
            new WorkflowPhase("Phase 2", "prompt 2", "Running phase 2...", true),
        });
        Assert.Equal("Test Workflow", workflow.Name);
        Assert.Equal(2, workflow.Phases.Count);
    }

    [Fact]
    public void WorkflowPhase_ExpandsTemplateVariables()
    {
        var phase = new WorkflowPhase("Review", "Review this diff:\n{diff}\n\nFocus on {focus}", "Reviewing...", true);
        var vars = new Dictionary<string, string>
        {
            ["diff"] = "--- a/foo.cs\n+++ b/foo.cs",
            ["focus"] = "security"
        };
        var expanded = phase.ExpandPrompt(vars);
        Assert.Contains("--- a/foo.cs", expanded);
        Assert.Contains("security", expanded);
        Assert.DoesNotContain("{diff}", expanded);
    }
}
