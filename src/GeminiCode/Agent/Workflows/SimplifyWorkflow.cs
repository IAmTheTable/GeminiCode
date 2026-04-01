// src/GeminiCode/Agent/Workflows/SimplifyWorkflow.cs
namespace GeminiCode.Agent.Workflows;

public static class SimplifyWorkflow
{
    public static WorkflowDefinition Create()
    {
        return new WorkflowDefinition("Simplify", new[]
        {
            new WorkflowPhase(
                "Analyzing changes",
                "I'm going to review recent code changes. First, let me see what changed.\n\n[GIT]diff[/GIT]\n\n[GIT]diff --cached[/GIT]",
                "Analyzing changes...",
                true),

            new WorkflowPhase(
                "Code Reuse Review",
                "Review the following diff for code reuse opportunities:\n\n{phase1_result}\n\nFor each change:\n1. Search for existing utilities and helpers that could replace newly written code. Look for similar patterns elsewhere in the codebase.\n2. Flag any new function that duplicates existing functionality. Suggest the existing function to use instead.\n3. Flag any inline logic that could use an existing utility — hand-rolled string manipulation, manual path handling, custom environment checks, etc.\n\nSearch the codebase with [GREP] to find existing patterns before making claims. Be specific — cite file paths and function names.",
                "Reviewing code reuse...",
                true),

            new WorkflowPhase(
                "Code Quality Review",
                "Review the same changes for code quality issues:\n\n{phase1_result}\n\nLook for:\n1. Redundant state that duplicates existing state or cached values that could be derived\n2. Parameter sprawl — adding new parameters instead of restructuring\n3. Copy-paste with slight variation that should be unified\n4. Leaky abstractions — exposing internal details that should be encapsulated\n5. Stringly-typed code where constants or enums already exist in the codebase\n6. Unnecessary comments explaining WHAT (keep only non-obvious WHY)\n\nBe specific — cite line numbers and suggest fixes.",
                "Reviewing code quality...",
                true),

            new WorkflowPhase(
                "Efficiency Review",
                "Review the same changes for efficiency:\n\n{phase1_result}\n\nLook for:\n1. Unnecessary work: redundant computations, repeated file reads, duplicate API calls, N+1 patterns\n2. Missed concurrency: independent operations that could run in parallel\n3. Hot-path bloat: blocking work on startup or per-request paths\n4. Recurring no-op updates: state updates that fire unconditionally without change detection\n5. Unnecessary existence checks before operating (TOCTOU anti-pattern)\n6. Memory: unbounded data structures, missing cleanup, event listener leaks\n7. Overly broad operations: reading entire files when only a portion is needed\n\nBe specific — cite line numbers and suggest fixes.",
                "Reviewing efficiency...",
                true),

            new WorkflowPhase(
                "Applying fixes",
                "Here are the review findings from the previous phases:\n\n**Code Reuse:**\n{phase2_result}\n\n**Code Quality:**\n{phase3_result}\n\n**Efficiency:**\n{phase4_result}\n\nFix each valid issue directly using [EDIT:] tags. If a finding is a false positive or not worth addressing, skip it. Do not argue with the findings — just fix or skip.",
                "Applying fixes...",
                true)
        });
    }
}
