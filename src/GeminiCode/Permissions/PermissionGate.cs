using GeminiCode.Cli;
using GeminiCode.Tools;

namespace GeminiCode.Permissions;

public enum PermissionResult { Approved, Denied, AlwaysApproved }

public class PermissionGate
{
    private readonly SessionAllowlist _allowlist;
    private readonly TextReader _input;
    private readonly TextWriter _output;

    public PermissionGate(SessionAllowlist allowlist, TextReader? input = null, TextWriter? output = null)
    {
        _allowlist = allowlist;
        _input = input ?? Console.In;
        _output = output ?? Console.Out;
    }

    public SessionAllowlist Allowlist => _allowlist;

    public PermissionResult RequestPermission(ITool tool, Dictionary<string, System.Text.Json.JsonElement> parameters)
    {
        if (_allowlist.IsAllowed(tool.Name))
            return PermissionResult.Approved;

        var riskLabel = RiskAssessor.GetRiskLabel(tool);
        var description = tool.DescribeAction(parameters);

        _output.WriteLine();
        _output.WriteLine($"{AnsiHelper.Yellow}{'━'.ToString().PadRight(50, '━')}{AnsiHelper.Reset}");
        _output.WriteLine($" {AnsiHelper.Bold}{tool.Name}{AnsiHelper.Reset}  [{riskLabel}]");
        _output.WriteLine($" {description}");

        if (tool.Name == "RunCommand")
        {
            var cmd = parameters["command"].GetString()!;
            if (RiskAssessor.IsDestructiveCommand(cmd))
                _output.WriteLine($" {AnsiHelper.Red}WARNING: Potentially destructive command{AnsiHelper.Reset}");
        }

        _output.WriteLine($"{AnsiHelper.Yellow}{'━'.ToString().PadRight(50, '━')}{AnsiHelper.Reset}");
        _output.Write($" Allow? [{AnsiHelper.Green}y{AnsiHelper.Reset}]es / [{AnsiHelper.Red}n{AnsiHelper.Reset}]o / [{AnsiHelper.Blue}a{AnsiHelper.Reset}]lways > ");

        while (true)
        {
            var key = _input.ReadLine()?.Trim().ToLowerInvariant();
            switch (key)
            {
                case "y" or "yes":
                    return PermissionResult.Approved;
                case "n" or "no":
                    return PermissionResult.Denied;
                case "a" or "always":
                    if (_allowlist.Add(tool.Name))
                    {
                        _output.WriteLine($" {AnsiHelper.Blue}Auto-approving all {tool.Name} calls for this session.{AnsiHelper.Reset}");
                        return PermissionResult.AlwaysApproved;
                    }
                    else
                    {
                        _output.WriteLine($" {AnsiHelper.Yellow}RunCommand cannot be auto-approved. Use [y]es or [n]o.{AnsiHelper.Reset}");
                        _output.Write(" > ");
                        continue;
                    }
                default:
                    _output.Write($" Invalid input. [y/n/a] > ");
                    continue;
            }
        }
    }
}
