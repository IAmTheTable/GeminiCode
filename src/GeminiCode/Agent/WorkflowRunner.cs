// src/GeminiCode/Agent/WorkflowRunner.cs
using GeminiCode.Browser;
using GeminiCode.Cli;

namespace GeminiCode.Agent;

public record WorkflowPhase(string Name, string PromptTemplate, string ActiveForm, bool AllowToolCalls)
{
    public string ExpandPrompt(Dictionary<string, string> variables)
    {
        var result = PromptTemplate;
        foreach (var (key, value) in variables)
            result = result.Replace($"{{{key}}}", value);
        return result;
    }
}

public class WorkflowDefinition
{
    public string Name { get; }
    public IReadOnlyList<WorkflowPhase> Phases { get; }

    public WorkflowDefinition(string name, IEnumerable<WorkflowPhase> phases)
    {
        Name = name;
        Phases = phases.ToList().AsReadOnly();
    }
}

public class WorkflowRunner
{
    private readonly AgentOrchestrator _orchestrator;
    private readonly BrowserBridge _browser;

    public WorkflowRunner(AgentOrchestrator orchestrator, BrowserBridge browser)
    {
        _orchestrator = orchestrator;
        _browser = browser;
    }

    /// <summary>Run a workflow, executing each phase sequentially with progress display.</summary>
    public async Task<List<string>> RunAsync(WorkflowDefinition workflow, Dictionary<string, string> variables, CancellationToken ct)
    {
        var results = new List<string>();

        PrintWorkflowHeader(workflow);

        for (int i = 0; i < workflow.Phases.Count; i++)
        {
            var phase = workflow.Phases[i];
            var phaseNum = i + 1;
            var total = workflow.Phases.Count;

            PrintPhaseStatus(phaseNum, total, phase.Name, "...");

            var prompt = phase.ExpandPrompt(variables);
            var response = await _orchestrator.SendAndProcessAsync(prompt, ct);

            if (response != null)
            {
                results.Add(response);
                variables[$"phase{phaseNum}_result"] = response;
                PrintPhaseComplete(phaseNum, total, phase.Name);
            }
            else
            {
                PrintPhaseFailed(phaseNum, total, phase.Name);
                results.Add($"[Phase {phaseNum} failed: no response]");
            }
        }

        PrintWorkflowFooter(workflow);
        return results;
    }

    private static void PrintWorkflowHeader(WorkflowDefinition workflow)
    {
        Console.WriteLine($"\n{AnsiHelper.Cyan}── Workflow: {workflow.Name} {"─".PadRight(35, '─')}{AnsiHelper.Reset}");
    }

    private static void PrintWorkflowFooter(WorkflowDefinition workflow)
    {
        Console.WriteLine($"{AnsiHelper.Cyan}{"─".PadRight(50, '─')}{AnsiHelper.Reset}\n");
    }

    private static void PrintPhaseStatus(int num, int total, string name, string status)
    {
        Console.Write($"\r  [{num}/{total}] {name,-30} {AnsiHelper.Dim}{status}{AnsiHelper.Reset}");
    }

    private static void PrintPhaseComplete(int num, int total, string name)
    {
        Console.WriteLine($"\r  [{num}/{total}] {name,-30} {AnsiHelper.Green}\u2713{AnsiHelper.Reset}");
    }

    private static void PrintPhaseFailed(int num, int total, string name)
    {
        Console.WriteLine($"\r  [{num}/{total}] {name,-30} {AnsiHelper.Red}\u2717{AnsiHelper.Reset}");
    }
}
