using System.Text.RegularExpressions;
using GeminiCode.Agent;

namespace GeminiCode.Plugins;

public record PluginManifest(
    string Name,
    string Description,
    string Command,
    string ArgHint,
    IReadOnlyList<WorkflowPhase> Phases,
    string Source,
    bool IsSingleShot,
    string Body)
{
    private static readonly Regex PhaseHeader = new(
        @"^[ \t]*##[ \t]*Phase:[ \t]*(.+?)[ \t]*$",
        RegexOptions.Multiline | RegexOptions.Compiled);

    public static PluginManifest FromParsed(Dictionary<string, string> fields, string body, string source)
    {
        var name = fields.GetValueOrDefault("name", "").Trim();
        var description = fields.GetValueOrDefault("description", "").Trim();
        var argHint = fields.GetValueOrDefault("argHint", "").Trim();

        var rawCommand = fields.GetValueOrDefault("command", "").Trim();
        var command = NormalizeCommand(string.IsNullOrEmpty(rawCommand) ? name : rawCommand);

        var phases = ParsePhases(body, name);
        var isSingleShot = !PhaseHeader.IsMatch(body);

        return new PluginManifest(name, description, command, argHint, phases, source, isSingleShot, body.Trim());
    }

    private static string NormalizeCommand(string raw)
    {
        var c = raw.Trim().ToLowerInvariant();
        if (!c.StartsWith('/')) c = "/" + c;
        return c;
    }

    private static IReadOnlyList<WorkflowPhase> ParsePhases(string body, string name)
    {
        var matches = PhaseHeader.Matches(body);
        if (matches.Count == 0)
        {
            // Single-shot: one phase containing the whole body.
            return new[] { new WorkflowPhase(name, body.Trim(), $"{name}...", true) };
        }

        var phases = new List<WorkflowPhase>();
        for (int i = 0; i < matches.Count; i++)
        {
            var phaseName = matches[i].Groups[1].Value.Trim();
            var start = matches[i].Index + matches[i].Length;
            var end = i + 1 < matches.Count ? matches[i + 1].Index : body.Length;
            var text = body[start..end].Trim();
            var allowToolCalls = !phaseName.Equals("Summary", StringComparison.OrdinalIgnoreCase);
            phases.Add(new WorkflowPhase(phaseName, text, $"{phaseName}...", allowToolCalls));
        }
        return phases;
    }

    public WorkflowDefinition ToWorkflow() => new(Name, Phases);
}
