// src/GeminiCode/Agent/Workflows/BrainstormWorkflow.cs
namespace GeminiCode.Agent.Workflows;

public static class BrainstormWorkflow
{
    public static WorkflowDefinition Create(string userInput)
    {
        return new WorkflowDefinition("Brainstorm", new[]
        {
            new WorkflowPhase(
                "Understanding context",
                $"The user wants to brainstorm a feature or idea: {userInput}\n\nFirst, explore the current project context:\n1. Check the project structure with [TREE:depth=2][/TREE]\n2. Check recent git history with [GIT]log -10 --oneline[/GIT]\n3. Look at relevant existing code based on what the user described\n\nThen, ask 3-5 clarifying questions to understand:\n- What exactly they want to build\n- Constraints (performance, compatibility, timeline)\n- Success criteria\n\nPresent questions as a numbered list. Be specific to the project.",
                "Understanding context...",
                true),

            new WorkflowPhase(
                "Exploring approaches",
                "Based on what you've learned about the project, propose 2-3 different approaches to implement the feature.\n\nFor each approach:\n- Name it clearly\n- Describe the architecture (which files, what components, how data flows)\n- List pros and cons\n- Estimate relative complexity\n\nLead with your recommended approach and explain why you prefer it. Reference existing code patterns in the project where relevant.",
                "Exploring approaches...",
                true),

            new WorkflowPhase(
                "Designing solution",
                "Based on the recommended approach, create a detailed design:\n\n1. **File structure** — which files to create or modify, what each is responsible for\n2. **Component interfaces** — key classes/functions, their signatures, how they connect\n3. **Data flow** — how data moves through the system\n4. **Error handling** — what can go wrong and how to handle it\n5. **Testing strategy** — what to test and how\n\nBe specific about file paths relative to the project root. Show key interfaces and type definitions. This design should be detailed enough that someone could implement it without further discussion.",
                "Designing solution...",
                true),

            new WorkflowPhase(
                "Summary",
                "Summarize the brainstorming session:\n\n1. **What we're building:** one paragraph\n2. **Chosen approach:** the recommended approach and why\n3. **Key design decisions:** bullet list of the important choices\n4. **Next steps:** what to implement first\n\nKeep it concise — this summary will be saved for reference.",
                "Summarizing...",
                false)
        });
    }
}
