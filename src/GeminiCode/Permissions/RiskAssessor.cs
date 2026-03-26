// src/GeminiCode/Permissions/RiskAssessor.cs
using System.Text.RegularExpressions;
using GeminiCode.Tools;

namespace GeminiCode.Permissions;

public static class RiskAssessor
{
    private static readonly string[] DestructivePatterns =
    [
        @"\brm\b.*-[rRf]",
        @"\bdel\b.*(/[sS]|/[qQ])",
        @"\bformat\b\s+[A-Z]:",
        @"\bgit\s+clean\b.*-[fdx]",
        @"\bfind\b.*-delete",
        @"\brmdir\b",
        @"\brd\b\s+/[sS]",
        @"Remove-Item.*-Recurse",
        @"\bgit\s+reset\s+--hard",
        @"\bgit\s+push\b.*--force",
    ];

    public static bool IsDestructiveCommand(string command)
    {
        return DestructivePatterns.Any(p =>
            Regex.IsMatch(command, p, RegexOptions.IgnoreCase));
    }

    public static string GetRiskLabel(ITool tool) => tool.Risk switch
    {
        RiskLevel.Low => "LOW (read-only)",
        RiskLevel.Medium => "MEDIUM (writes files)",
        RiskLevel.High => "HIGH (shell command)",
        _ => "UNKNOWN"
    };
}
