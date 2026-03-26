# GeminiCode CLI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a C# .NET 9 CLI tool that provides an agentic coding assistant experience powered by Google Gemini through an embedded WebView2 browser.

**Architecture:** Single console process with a WinForms WebView2 window on a separate STA thread. The CLI Engine handles user I/O on the main thread. The Agent Orchestrator sends prompts via the Browser Bridge, parses structured `<tool_call>` responses, routes them through a Permission Gate, executes tools, and sends results back. All file tools are sandboxed to the working directory.

**Tech Stack:** .NET 9, C#, System.Windows.Forms, Microsoft.Web.WebView2, System.Text.Json, xUnit (tests)

**Spec:** `docs/superpowers/specs/2026-03-26-geminicode-cli-design.md`

---

## File Map

### Source Files (`src/GeminiCode/`)

| File | Responsibility |
|---|---|
| `Program.cs` | Entry point: parse args, bootstrap components, start STA thread, run CLI loop |
| `GeminiCode.csproj` | Project file: .NET 9, WinForms, WebView2 NuGet reference |
| `Cli/AnsiHelper.cs` | ANSI escape code constants and utilities (colors, bold, reset) |
| `Cli/MarkdownRenderer.cs` | Convert markdown text to ANSI-formatted console output |
| `Cli/CommandHandler.cs` | Route `/slash` commands to handlers |
| `Cli/CliEngine.cs` | Main prompt loop, user input, output rendering, coordinates orchestrator |
| `Browser/DomSelectors.cs` | C# model class deserializing `selectors.json` |
| `Browser/BrowserWindow.cs` | WinForms Form hosting WebView2 control |
| `Browser/BrowserBridge.cs` | Async facade: send message, read response, health-check, new chat — dispatches to STA |
| `Browser/SessionMonitor.cs` | Polls for auth state, detects session expiry |
| `Agent/ToolCallParser.cs` | Parse `<tool_call>` blocks from Gemini response text |
| `Agent/SystemPrompt.cs` | System prompt template with tool definitions |
| `Agent/ConversationManager.cs` | Track turn count, manage system prompt injection, drift prevention |
| `Agent/AgentOrchestrator.cs` | Core agent loop: send → parse → permission → execute → result → repeat |
| `Tools/ITool.cs` | `ITool` interface and `ToolResult` model |
| `Tools/PathSandbox.cs` | Path validation: resolve, normalize, enforce working directory boundary |
| `Tools/ToolRegistry.cs` | Register tools by name, look up for execution |
| `Tools/ReadFileTool.cs` | Read file contents with truncation |
| `Tools/WriteFileTool.cs` | Write file with path sandbox check |
| `Tools/EditFileTool.cs` | Find/replace edit with matching semantics |
| `Tools/ListFilesTool.cs` | Glob-based file listing |
| `Tools/SearchFilesTool.cs` | Content search (grep-like) |
| `Tools/RunCommandTool.cs` | Shell command execution with timeout |
| `Permissions/RiskAssessor.cs` | Classify tool calls by risk level, detect destructive patterns |
| `Permissions/SessionAllowlist.cs` | Session-scoped tool-type allowlist with workdir binding |
| `Permissions/PermissionGate.cs` | Prompt user for approval, check allowlist, render permission UI |
| `Config/AppSettings.cs` | Model class for `settings.json` (timeouts, etc.) |
| `Config/ConfigLoader.cs` | Load/create `settings.json` and `selectors.json` from `%APPDATA%/GeminiCode/` |

### Test Files (`src/GeminiCode.Tests/`)

| File | Tests |
|---|---|
| `ToolCallParserTests.cs` | Parsing, fence unwrapping, key aliasing, malformed input |
| `PathSandboxTests.cs` | Traversal rejection, normalization, outside-workdir |
| `EditFileToolTests.cs` | Exact match, line endings, not-found, multi-match |
| `RiskAssessorTests.cs` | Destructive pattern detection |
| `PermissionGateTests.cs` | Allowlist add/check, RunCommand exclusion, reset on /cd |
| `MarkdownRendererTests.cs` | Code blocks, headers, bold, lists |
| `SessionAllowlistTests.cs` | Add, check, clear, workdir binding |

### Config Files (shipped as embedded resources, copied to `%APPDATA%/GeminiCode/` on first run)

| File | Content |
|---|---|
| `defaults/selectors.json` | Default DOM selectors for Gemini web UI |
| `defaults/settings.json` | Default settings (responseTimeoutSeconds: 120) |

---

## Task 1: Project Scaffolding & Build Verification

**Files:**
- Create: `src/GeminiCode/GeminiCode.csproj`
- Create: `src/GeminiCode/Program.cs`
- Create: `src/GeminiCode.Tests/GeminiCode.Tests.csproj`
- Create: `GeminiCode.sln`

- [ ] **Step 1: Create solution and project files**

```bash
cd D:/CodeProjects/GeminiCode
dotnet new sln -n GeminiCode
mkdir -p src/GeminiCode src/GeminiCode.Tests
dotnet new console -n GeminiCode -o src/GeminiCode --framework net9.0
dotnet new xunit -n GeminiCode.Tests -o src/GeminiCode.Tests --framework net9.0
dotnet sln add src/GeminiCode/GeminiCode.csproj
dotnet sln add src/GeminiCode.Tests/GeminiCode.Tests.csproj
dotnet add src/GeminiCode.Tests/GeminiCode.Tests.csproj reference src/GeminiCode/GeminiCode.csproj
```

- [ ] **Step 2: Configure GeminiCode.csproj for WinForms + WebView2**

Replace `src/GeminiCode/GeminiCode.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0-windows</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Web.WebView2" Version="1.*" />
  </ItemGroup>
</Project>
```

Update `src/GeminiCode.Tests/GeminiCode.Tests.csproj` to target `net9.0-windows`:

```xml
<TargetFramework>net9.0-windows</TargetFramework>
```

- [ ] **Step 3: Write minimal Program.cs**

```csharp
namespace GeminiCode;

public class Program
{
    public static void Main(string[] args)
    {
        Console.WriteLine("GeminiCode v0.1.0");
    }
}
```

- [ ] **Step 4: Build and run**

```bash
dotnet build GeminiCode.sln
dotnet run --project src/GeminiCode
```

Expected: Prints "GeminiCode v0.1.0" and exits.

- [ ] **Step 5: Run tests**

```bash
dotnet test src/GeminiCode.Tests
```

Expected: Default template test passes.

- [ ] **Step 6: Initialize git and commit**

```bash
cd D:/CodeProjects/GeminiCode
git init
```

Create `.gitignore`:
```
bin/
obj/
.vs/
*.user
```

```bash
git add .
git commit -m "chore: scaffold .NET 9 solution with WinForms and WebView2"
```

---

## Task 2: ANSI Helper & Markdown Renderer

**Files:**
- Create: `src/GeminiCode/Cli/AnsiHelper.cs`
- Create: `src/GeminiCode/Cli/MarkdownRenderer.cs`
- Create: `src/GeminiCode.Tests/MarkdownRendererTests.cs`

- [ ] **Step 1: Write failing tests for MarkdownRenderer**

```csharp
// src/GeminiCode.Tests/MarkdownRendererTests.cs
using GeminiCode.Cli;

namespace GeminiCode.Tests;

public class MarkdownRendererTests
{
    [Fact]
    public void RenderCodeBlock_WrapsWithAnsiColors()
    {
        var input = "```csharp\nConsole.WriteLine(\"hi\");\n```";
        var result = MarkdownRenderer.Render(input);
        Assert.Contains("\x1b[", result); // Contains ANSI escape
        Assert.Contains("Console.WriteLine", result);
        Assert.Contains("csharp", result); // Language label
    }

    [Fact]
    public void RenderBold_AppliesBoldAnsi()
    {
        var input = "This is **bold** text.";
        var result = MarkdownRenderer.Render(input);
        Assert.Contains("\x1b[1m", result); // Bold on
        Assert.Contains("bold", result);
    }

    [Fact]
    public void RenderHeader_AppliesBoldAndNewline()
    {
        var input = "## My Header";
        var result = MarkdownRenderer.Render(input);
        Assert.Contains("\x1b[1m", result);
        Assert.Contains("My Header", result);
    }

    [Fact]
    public void RenderInlineCode_AppliesHighlight()
    {
        var input = "Use `foo()` here.";
        var result = MarkdownRenderer.Render(input);
        Assert.Contains("\x1b[", result);
        Assert.Contains("foo()", result);
    }

    [Fact]
    public void RenderPlainText_PassesThrough()
    {
        var input = "Just plain text.";
        var result = MarkdownRenderer.Render(input);
        Assert.Equal("Just plain text.\n", result);
    }

    [Fact]
    public void RenderBulletList_IndentsWithMarker()
    {
        var input = "- Item one\n- Item two";
        var result = MarkdownRenderer.Render(input);
        Assert.Contains("  - Item one", result);
        Assert.Contains("  - Item two", result);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test src/GeminiCode.Tests --filter "MarkdownRendererTests"
```

Expected: FAIL — `MarkdownRenderer` does not exist.

- [ ] **Step 3: Implement AnsiHelper**

```csharp
// src/GeminiCode/Cli/AnsiHelper.cs
namespace GeminiCode.Cli;

public static class AnsiHelper
{
    public const string Reset = "\x1b[0m";
    public const string Bold = "\x1b[1m";
    public const string Dim = "\x1b[2m";
    public const string Italic = "\x1b[3m";
    public const string Underline = "\x1b[4m";

    public const string Red = "\x1b[31m";
    public const string Green = "\x1b[32m";
    public const string Yellow = "\x1b[33m";
    public const string Blue = "\x1b[34m";
    public const string Magenta = "\x1b[35m";
    public const string Cyan = "\x1b[36m";
    public const string White = "\x1b[37m";
    public const string Gray = "\x1b[90m";

    public const string BgDarkGray = "\x1b[48;5;236m";

    public static string Wrap(string text, string code) => $"{code}{text}{Reset}";
}
```

- [ ] **Step 4: Implement MarkdownRenderer**

```csharp
// src/GeminiCode/Cli/MarkdownRenderer.cs
using System.Text;
using System.Text.RegularExpressions;

namespace GeminiCode.Cli;

public static class MarkdownRenderer
{
    public static string Render(string markdown)
    {
        var sb = new StringBuilder();
        var lines = markdown.Split('\n');
        var inCodeBlock = false;
        string? codeLanguage = null;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            if (line.StartsWith("```"))
            {
                if (!inCodeBlock)
                {
                    inCodeBlock = true;
                    codeLanguage = line.Length > 3 ? line[3..].Trim() : null;
                    if (!string.IsNullOrEmpty(codeLanguage))
                        sb.AppendLine($"{AnsiHelper.Dim}[{codeLanguage}]{AnsiHelper.Reset}");
                    sb.Append(AnsiHelper.BgDarkGray);
                }
                else
                {
                    inCodeBlock = false;
                    codeLanguage = null;
                    sb.AppendLine(AnsiHelper.Reset);
                }
                continue;
            }

            if (inCodeBlock)
            {
                sb.AppendLine($"{AnsiHelper.BgDarkGray}{AnsiHelper.Cyan}{line}{AnsiHelper.Reset}");
                continue;
            }

            // Headers
            var headerMatch = Regex.Match(line, @"^(#{1,6})\s+(.+)$");
            if (headerMatch.Success)
            {
                sb.AppendLine(AnsiHelper.Wrap(headerMatch.Groups[2].Value, AnsiHelper.Bold));
                continue;
            }

            // Bullet lists
            if (line.TrimStart().StartsWith("- "))
            {
                sb.AppendLine($"  {line.TrimStart()}");
                continue;
            }

            // Inline formatting
            var rendered = RenderInline(line);
            sb.AppendLine(rendered);
        }

        return sb.ToString();
    }

    private static string RenderInline(string line)
    {
        // Bold: **text**
        line = Regex.Replace(line, @"\*\*(.+?)\*\*",
            m => $"{AnsiHelper.Bold}{m.Groups[1].Value}{AnsiHelper.Reset}");

        // Italic: *text*
        line = Regex.Replace(line, @"(?<!\*)\*(?!\*)(.+?)(?<!\*)\*(?!\*)",
            m => $"{AnsiHelper.Italic}{m.Groups[1].Value}{AnsiHelper.Reset}");

        // Inline code: `text`
        line = Regex.Replace(line, @"`([^`]+)`",
            m => $"{AnsiHelper.BgDarkGray}{AnsiHelper.White}{m.Groups[1].Value}{AnsiHelper.Reset}");

        return line;
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

```bash
dotnet test src/GeminiCode.Tests --filter "MarkdownRendererTests"
```

Expected: All 6 tests PASS.

- [ ] **Step 6: Commit**

```bash
git add src/GeminiCode/Cli/AnsiHelper.cs src/GeminiCode/Cli/MarkdownRenderer.cs src/GeminiCode.Tests/MarkdownRendererTests.cs
git commit -m "feat: add ANSI helper and markdown renderer with tests"
```

---

## Task 3: Tool Call Parser

**Files:**
- Create: `src/GeminiCode/Agent/ToolCallParser.cs`
- Create: `src/GeminiCode.Tests/ToolCallParserTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// src/GeminiCode.Tests/ToolCallParserTests.cs
using GeminiCode.Agent;

namespace GeminiCode.Tests;

public class ToolCallParserTests
{
    [Fact]
    public void Parse_SingleToolCall_ExtractsCorrectly()
    {
        var input = """
            Here is some text.
            <tool_call>
            {"name": "ReadFile", "parameters": {"path": "src/foo.cs"}}
            </tool_call>
            More text.
            """;
        var result = ToolCallParser.Parse(input);
        Assert.Single(result.ToolCalls);
        Assert.Equal("ReadFile", result.ToolCalls[0].Name);
        Assert.Equal("src/foo.cs", result.ToolCalls[0].Parameters["path"].GetString());
        Assert.Contains("Here is some text.", result.TextContent);
        Assert.Contains("More text.", result.TextContent);
    }

    [Fact]
    public void Parse_MultipleToolCalls_ExtractsAll()
    {
        var input = """
            <tool_call>
            {"name": "ReadFile", "parameters": {"path": "a.cs"}}
            </tool_call>
            <tool_call>
            {"name": "ReadFile", "parameters": {"path": "b.cs"}}
            </tool_call>
            """;
        var result = ToolCallParser.Parse(input);
        Assert.Equal(2, result.ToolCalls.Count);
    }

    [Fact]
    public void Parse_MarkdownFencedToolCall_UnwrapsAndParses()
    {
        var input = """
            ```xml
            <tool_call>
            {"name": "WriteFile", "parameters": {"path": "x.cs", "content": "hi"}}
            </tool_call>
            ```
            """;
        var result = ToolCallParser.Parse(input);
        Assert.Single(result.ToolCalls);
        Assert.Equal("WriteFile", result.ToolCalls[0].Name);
    }

    [Fact]
    public void Parse_ArgsAlias_TreatedAsParameters()
    {
        var input = """
            <tool_call>
            {"name": "ReadFile", "args": {"path": "foo.cs"}}
            </tool_call>
            """;
        var result = ToolCallParser.Parse(input);
        Assert.Single(result.ToolCalls);
        Assert.Equal("foo.cs", result.ToolCalls[0].Parameters["path"].GetString());
    }

    [Fact]
    public void Parse_NoToolCalls_ReturnsTextOnly()
    {
        var input = "Just a normal response with no tool calls.";
        var result = ToolCallParser.Parse(input);
        Assert.Empty(result.ToolCalls);
        Assert.Contains("Just a normal response", result.TextContent);
    }

    [Fact]
    public void Parse_MalformedJson_SkipsAndReturnsAsText()
    {
        var input = """
            <tool_call>
            {not valid json}
            </tool_call>
            """;
        var result = ToolCallParser.Parse(input);
        Assert.Empty(result.ToolCalls);
    }

    [Fact]
    public void Parse_JsonFencedToolCall_UnwrapsAndParses()
    {
        var input = """
            ```json
            <tool_call>
            {"name": "ListFiles", "parameters": {"pattern": "*.cs"}}
            </tool_call>
            ```
            """;
        var result = ToolCallParser.Parse(input);
        Assert.Single(result.ToolCalls);
        Assert.Equal("ListFiles", result.ToolCalls[0].Name);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test src/GeminiCode.Tests --filter "ToolCallParserTests"
```

Expected: FAIL — `ToolCallParser` does not exist.

- [ ] **Step 3: Implement ToolCallParser**

```csharp
// src/GeminiCode/Agent/ToolCallParser.cs
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GeminiCode.Agent;

public record ParsedToolCall(string Name, Dictionary<string, JsonElement> Parameters);

public record ParseResult(List<ParsedToolCall> ToolCalls, string TextContent);

public static class ToolCallParser
{
    // Matches ```lang\n...\n``` blocks
    private static readonly Regex FencePattern = new(
        @"```\w*\s*\n?(.*?)\n?\s*```",
        RegexOptions.Singleline | RegexOptions.Compiled);

    // Matches <tool_call>...</tool_call> blocks
    private static readonly Regex ToolCallPattern = new(
        @"<tool_call>\s*(.*?)\s*</tool_call>",
        RegexOptions.Singleline | RegexOptions.Compiled);

    public static ParseResult Parse(string responseText)
    {
        var toolCalls = new List<ParsedToolCall>();

        // Step 1: Unwrap markdown fences that contain tool_call tags
        var unwrapped = FencePattern.Replace(responseText, m =>
        {
            var inner = m.Groups[1].Value;
            return inner.Contains("<tool_call>") ? inner : m.Value;
        });

        // Step 2: Extract tool_call blocks
        var textContent = ToolCallPattern.Replace(unwrapped, m =>
        {
            var json = m.Groups[1].Value.Trim();
            var parsed = TryParseToolCall(json);
            if (parsed != null)
                toolCalls.Add(parsed);
            return ""; // Remove from text content
        });

        // Clean up text content
        textContent = Regex.Replace(textContent.Trim(), @"\n{3,}", "\n\n");

        return new ParseResult(toolCalls, textContent);
    }

    private static ParsedToolCall? TryParseToolCall(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var name = root.GetProperty("name").GetString();
            if (string.IsNullOrEmpty(name))
                return null;

            // Accept "parameters" or "args" as the key
            JsonElement paramsElement;
            if (!root.TryGetProperty("parameters", out paramsElement))
            {
                if (!root.TryGetProperty("args", out paramsElement))
                    return null;
            }

            var parameters = new Dictionary<string, JsonElement>();
            foreach (var prop in paramsElement.EnumerateObject())
            {
                parameters[prop.Name] = prop.Value.Clone();
            }

            return new ParsedToolCall(name, parameters);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test src/GeminiCode.Tests --filter "ToolCallParserTests"
```

Expected: All 7 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add src/GeminiCode/Agent/ToolCallParser.cs src/GeminiCode.Tests/ToolCallParserTests.cs
git commit -m "feat: add tool call parser with fence unwrapping and key aliasing"
```

---

## Task 4: Path Sandbox

**Files:**
- Create: `src/GeminiCode/Tools/PathSandbox.cs`
- Create: `src/GeminiCode.Tests/PathSandboxTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// src/GeminiCode.Tests/PathSandboxTests.cs
using GeminiCode.Tools;

namespace GeminiCode.Tests;

public class PathSandboxTests
{
    private readonly string _workDir = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "sandbox_test"));

    [Fact]
    public void Resolve_RelativePath_ReturnsAbsoluteWithinWorkDir()
    {
        var sandbox = new PathSandbox(_workDir);
        var result = sandbox.Resolve("src/foo.cs");
        Assert.StartsWith(_workDir, result);
        Assert.EndsWith("foo.cs", result);
    }

    [Fact]
    public void Resolve_TraversalAttack_ThrowsSandboxException()
    {
        var sandbox = new PathSandbox(_workDir);
        Assert.Throws<SandboxViolationException>(() => sandbox.Resolve("../../etc/passwd"));
    }

    [Fact]
    public void Resolve_AbsolutePathOutsideWorkDir_ThrowsSandboxException()
    {
        var sandbox = new PathSandbox(_workDir);
        Assert.Throws<SandboxViolationException>(() => sandbox.Resolve("C:\\Windows\\System32\\cmd.exe"));
    }

    [Fact]
    public void Resolve_AbsolutePathInsideWorkDir_Succeeds()
    {
        var sandbox = new PathSandbox(_workDir);
        var target = Path.Combine(_workDir, "src", "bar.cs");
        var result = sandbox.Resolve(target);
        Assert.Equal(Path.GetFullPath(target), result);
    }

    [Fact]
    public void Resolve_DotPath_ResolvesToWorkDir()
    {
        var sandbox = new PathSandbox(_workDir);
        var result = sandbox.Resolve(".");
        Assert.Equal(Path.GetFullPath(_workDir), result);
    }

    [Fact]
    public void UpdateWorkDir_ChangesRoot()
    {
        var sandbox = new PathSandbox(_workDir);
        var newDir = Path.Combine(Path.GetTempPath(), "sandbox_test2");
        sandbox.UpdateWorkingDirectory(newDir);
        var result = sandbox.Resolve("foo.cs");
        Assert.StartsWith(Path.GetFullPath(newDir), result);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test src/GeminiCode.Tests --filter "PathSandboxTests"
```

Expected: FAIL — `PathSandbox` does not exist.

- [ ] **Step 3: Implement PathSandbox**

```csharp
// src/GeminiCode/Tools/PathSandbox.cs
namespace GeminiCode.Tools;

public class SandboxViolationException : Exception
{
    public SandboxViolationException(string path, string workDir)
        : base($"Access denied: path '{path}' is outside the working directory '{workDir}'.") { }
}

public class PathSandbox
{
    private string _workingDirectory;

    public string WorkingDirectory => _workingDirectory;

    public PathSandbox(string workingDirectory)
    {
        _workingDirectory = Path.GetFullPath(workingDirectory);
    }

    public string Resolve(string path)
    {
        string absolute;
        if (Path.IsPathRooted(path))
            absolute = Path.GetFullPath(path);
        else
            absolute = Path.GetFullPath(Path.Combine(_workingDirectory, path));

        // Normalize both for comparison (trailing separator, case on Windows)
        var normalizedAbsolute = NormalizePath(absolute);
        var normalizedWorkDir = NormalizePath(_workingDirectory);

        if (!normalizedAbsolute.StartsWith(normalizedWorkDir, StringComparison.OrdinalIgnoreCase))
            throw new SandboxViolationException(path, _workingDirectory);

        return absolute;
    }

    public void UpdateWorkingDirectory(string newPath)
    {
        _workingDirectory = Path.GetFullPath(newPath);
    }

    private static string NormalizePath(string path)
    {
        var full = Path.GetFullPath(path);
        if (!full.EndsWith(Path.DirectorySeparatorChar))
            full += Path.DirectorySeparatorChar;
        return full;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test src/GeminiCode.Tests --filter "PathSandboxTests"
```

Expected: All 6 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add src/GeminiCode/Tools/PathSandbox.cs src/GeminiCode.Tests/PathSandboxTests.cs
git commit -m "feat: add path sandbox with traversal protection"
```

---

## Task 5: Tool Interface, Models & Registry

**Files:**
- Create: `src/GeminiCode/Tools/ITool.cs`
- Create: `src/GeminiCode/Tools/ToolRegistry.cs`

- [ ] **Step 1: Create ITool interface and models**

```csharp
// src/GeminiCode/Tools/ITool.cs
using System.Text.Json;

namespace GeminiCode.Tools;

public record ToolResult(string Name, bool Success, string Output)
{
    public string ToProtocolString() =>
        $"<tool_result>\n{{\"name\": \"{Name}\", \"success\": {Success.ToString().ToLowerInvariant()}, " +
        (Success ? $"\"output\": {JsonSerializer.Serialize(Output)}" : $"\"error\": {JsonSerializer.Serialize(Output)}") +
        "}}\n</tool_result>";
}

public enum RiskLevel { Low, Medium, High }

public interface ITool
{
    string Name { get; }
    RiskLevel Risk { get; }
    Task<ToolResult> ExecuteAsync(Dictionary<string, JsonElement> parameters, CancellationToken ct);
    string DescribeAction(Dictionary<string, JsonElement> parameters);
}
```

- [ ] **Step 2: Create ToolRegistry**

```csharp
// src/GeminiCode/Tools/ToolRegistry.cs
namespace GeminiCode.Tools;

public class ToolRegistry
{
    private readonly Dictionary<string, ITool> _tools = new(StringComparer.OrdinalIgnoreCase);

    public void Register(ITool tool)
    {
        _tools[tool.Name] = tool;
    }

    public ITool? GetTool(string name)
    {
        _tools.TryGetValue(name, out var tool);
        return tool;
    }

    public IReadOnlyCollection<string> ToolNames => _tools.Keys;
}
```

- [ ] **Step 3: Build to verify compilation**

```bash
dotnet build src/GeminiCode
```

Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/GeminiCode/Tools/ITool.cs src/GeminiCode/Tools/ToolRegistry.cs
git commit -m "feat: add tool interface, models, and registry"
```

---

## Task 6: Implement All 6 Tools

**Files:**
- Create: `src/GeminiCode/Tools/ReadFileTool.cs`
- Create: `src/GeminiCode/Tools/WriteFileTool.cs`
- Create: `src/GeminiCode/Tools/EditFileTool.cs`
- Create: `src/GeminiCode/Tools/ListFilesTool.cs`
- Create: `src/GeminiCode/Tools/SearchFilesTool.cs`
- Create: `src/GeminiCode/Tools/RunCommandTool.cs`
- Create: `src/GeminiCode.Tests/EditFileToolTests.cs`

- [ ] **Step 1: Write failing tests for EditFileTool**

```csharp
// src/GeminiCode.Tests/EditFileToolTests.cs
using System.Text.Json;
using GeminiCode.Tools;

namespace GeminiCode.Tests;

public class EditFileToolTests : IDisposable
{
    private readonly string _workDir;
    private readonly PathSandbox _sandbox;

    public EditFileToolTests()
    {
        _workDir = Path.Combine(Path.GetTempPath(), $"edit_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workDir);
        _sandbox = new PathSandbox(_workDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_workDir))
            Directory.Delete(_workDir, true);
    }

    private Dictionary<string, JsonElement> MakeParams(string path, string oldStr, string newStr)
    {
        var json = JsonSerializer.Serialize(new { path, old_string = oldStr, new_string = newStr });
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
    }

    [Fact]
    public async Task Execute_ExactMatch_ReplacesSuccessfully()
    {
        var file = Path.Combine(_workDir, "test.cs");
        await File.WriteAllTextAsync(file, "Hello World");

        var tool = new EditFileTool(_sandbox);
        var result = await tool.ExecuteAsync(MakeParams("test.cs", "Hello", "Goodbye"), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("Goodbye World", await File.ReadAllTextAsync(file));
    }

    [Fact]
    public async Task Execute_NotFound_ReturnsError()
    {
        var file = Path.Combine(_workDir, "test.cs");
        await File.WriteAllTextAsync(file, "Hello World");

        var tool = new EditFileTool(_sandbox);
        var result = await tool.ExecuteAsync(MakeParams("test.cs", "Missing", "Replacement"), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("not found", result.Output);
    }

    [Fact]
    public async Task Execute_MultipleMatches_ReturnsError()
    {
        var file = Path.Combine(_workDir, "test.cs");
        await File.WriteAllTextAsync(file, "aaa bbb aaa");

        var tool = new EditFileTool(_sandbox);
        var result = await tool.ExecuteAsync(MakeParams("test.cs", "aaa", "ccc"), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("matches 2 locations", result.Output);
    }

    [Fact]
    public async Task Execute_LineEndingNormalization_MatchesCRLF()
    {
        var file = Path.Combine(_workDir, "test.cs");
        await File.WriteAllTextAsync(file, "line1\r\nline2\r\nline3");

        var tool = new EditFileTool(_sandbox);
        // old_string uses \n only — should still match after normalization
        var result = await tool.ExecuteAsync(MakeParams("test.cs", "line1\nline2", "replaced"), CancellationToken.None);

        Assert.True(result.Success);
        var content = await File.ReadAllTextAsync(file);
        Assert.StartsWith("replaced", content);
        // Should preserve CRLF for remaining content
        Assert.Contains("\r\n", content);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test src/GeminiCode.Tests --filter "EditFileToolTests"
```

Expected: FAIL — `EditFileTool` does not exist.

- [ ] **Step 3: Implement ReadFileTool**

```csharp
// src/GeminiCode/Tools/ReadFileTool.cs
using System.Text.Json;

namespace GeminiCode.Tools;

public class ReadFileTool : ITool
{
    private readonly PathSandbox _sandbox;
    private const int MaxOutputBytes = 100 * 1024;

    public string Name => "ReadFile";
    public RiskLevel Risk => RiskLevel.Low;

    public ReadFileTool(PathSandbox sandbox) => _sandbox = sandbox;

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, JsonElement> parameters, CancellationToken ct)
    {
        try
        {
            var path = parameters["path"].GetString()!;
            var resolved = _sandbox.Resolve(path);

            if (!File.Exists(resolved))
                return new ToolResult(Name, false, $"File not found: {path}");

            var content = await File.ReadAllTextAsync(resolved, ct);
            if (content.Length > MaxOutputBytes)
                content = content[..MaxOutputBytes] + "\n[Output truncated at 100KB. Request specific sections if needed.]";

            return new ToolResult(Name, true, content);
        }
        catch (SandboxViolationException ex)
        {
            return new ToolResult(Name, false, ex.Message);
        }
    }

    public string DescribeAction(Dictionary<string, JsonElement> parameters)
        => $"Read: {parameters["path"].GetString()}";
}
```

- [ ] **Step 4: Implement WriteFileTool**

```csharp
// src/GeminiCode/Tools/WriteFileTool.cs
using System.Text.Json;

namespace GeminiCode.Tools;

public class WriteFileTool : ITool
{
    private readonly PathSandbox _sandbox;

    public string Name => "WriteFile";
    public RiskLevel Risk => RiskLevel.Medium;

    public WriteFileTool(PathSandbox sandbox) => _sandbox = sandbox;

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, JsonElement> parameters, CancellationToken ct)
    {
        try
        {
            var path = parameters["path"].GetString()!;
            var content = parameters["content"].GetString()!;
            var resolved = _sandbox.Resolve(path);

            var dir = Path.GetDirectoryName(resolved)!;
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            await File.WriteAllTextAsync(resolved, content, ct);
            var lineCount = content.Split('\n').Length;
            return new ToolResult(Name, true, $"Written {lineCount} lines to {path}");
        }
        catch (SandboxViolationException ex)
        {
            return new ToolResult(Name, false, ex.Message);
        }
    }

    public string DescribeAction(Dictionary<string, JsonElement> parameters)
    {
        var path = parameters["path"].GetString();
        var content = parameters["content"].GetString() ?? "";
        var lines = content.Split('\n').Length;
        return $"Write: {path} ({lines} lines)";
    }
}
```

- [ ] **Step 5: Implement EditFileTool**

```csharp
// src/GeminiCode/Tools/EditFileTool.cs
using System.Text.Json;

namespace GeminiCode.Tools;

public class EditFileTool : ITool
{
    private readonly PathSandbox _sandbox;

    public string Name => "EditFile";
    public RiskLevel Risk => RiskLevel.Medium;

    public EditFileTool(PathSandbox sandbox) => _sandbox = sandbox;

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, JsonElement> parameters, CancellationToken ct)
    {
        try
        {
            var path = parameters["path"].GetString()!;
            var oldString = parameters["old_string"].GetString()!;
            var newString = parameters["new_string"].GetString()!;
            var resolved = _sandbox.Resolve(path);

            if (!File.Exists(resolved))
                return new ToolResult(Name, false, $"File not found: {path}");

            var content = await File.ReadAllTextAsync(resolved, ct);

            // Detect original line ending
            var hasCrlf = content.Contains("\r\n");

            // Normalize to \n for matching
            var normalized = content.Replace("\r\n", "\n");
            var normalizedOld = oldString.Replace("\r\n", "\n");

            // Count matches
            var matchCount = CountOccurrences(normalized, normalizedOld);
            if (matchCount == 0)
                return new ToolResult(Name, false, "Edit failed: old_string not found in file. Read the file first to get exact content.");
            if (matchCount > 1)
                return new ToolResult(Name, false, $"Edit failed: old_string matches {matchCount} locations. Provide more context to make the match unique.");

            // Replace
            var normalizedNew = newString.Replace("\r\n", "\n");
            var result = normalized.Replace(normalizedOld, normalizedNew);

            // Restore original line endings
            if (hasCrlf)
                result = result.Replace("\n", "\r\n");

            await File.WriteAllTextAsync(resolved, result, ct);
            return new ToolResult(Name, true, $"Edited {path} successfully.");
        }
        catch (SandboxViolationException ex)
        {
            return new ToolResult(Name, false, ex.Message);
        }
    }

    public string DescribeAction(Dictionary<string, JsonElement> parameters)
        => $"Edit: {parameters["path"].GetString()}";

    private static int CountOccurrences(string source, string target)
    {
        int count = 0, index = 0;
        while ((index = source.IndexOf(target, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += target.Length;
        }
        return count;
    }
}
```

- [ ] **Step 6: Implement ListFilesTool**

```csharp
// src/GeminiCode/Tools/ListFilesTool.cs
using System.Text;
using System.Text.Json;

namespace GeminiCode.Tools;

public class ListFilesTool : ITool
{
    private readonly PathSandbox _sandbox;
    private const int MaxOutputBytes = 100 * 1024;

    public string Name => "ListFiles";
    public RiskLevel Risk => RiskLevel.Low;

    public ListFilesTool(PathSandbox sandbox) => _sandbox = sandbox;

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, JsonElement> parameters, CancellationToken ct)
    {
        try
        {
            var pattern = parameters["pattern"].GetString()!;
            string basePath;
            if (parameters.TryGetValue("path", out var pathEl) && pathEl.ValueKind == JsonValueKind.String)
                basePath = _sandbox.Resolve(pathEl.GetString()!);
            else
                basePath = _sandbox.WorkingDirectory;

            if (!Directory.Exists(basePath))
                return new ToolResult(Name, false, $"Directory not found: {basePath}");

            var files = Directory.EnumerateFiles(basePath, pattern, SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(_sandbox.WorkingDirectory, f))
                .OrderBy(f => f)
                .Take(500);

            var sb = new StringBuilder();
            foreach (var file in files)
            {
                sb.AppendLine(file);
                if (sb.Length > MaxOutputBytes)
                {
                    sb.AppendLine("[Output truncated at 100KB. Use a more specific pattern.]");
                    break;
                }
            }

            return new ToolResult(Name, true, sb.ToString().TrimEnd());
        }
        catch (SandboxViolationException ex)
        {
            return new ToolResult(Name, false, ex.Message);
        }
    }

    public string DescribeAction(Dictionary<string, JsonElement> parameters)
        => $"List: {parameters["pattern"].GetString()}";
}
```

- [ ] **Step 7: Implement SearchFilesTool**

```csharp
// src/GeminiCode/Tools/SearchFilesTool.cs
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GeminiCode.Tools;

public class SearchFilesTool : ITool
{
    private readonly PathSandbox _sandbox;
    private const int MaxOutputBytes = 100 * 1024;

    public string Name => "SearchFiles";
    public RiskLevel Risk => RiskLevel.Low;

    public SearchFilesTool(PathSandbox sandbox) => _sandbox = sandbox;

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, JsonElement> parameters, CancellationToken ct)
    {
        try
        {
            var pattern = parameters["pattern"].GetString()!;
            string basePath;
            if (parameters.TryGetValue("path", out var pathEl) && pathEl.ValueKind == JsonValueKind.String)
                basePath = _sandbox.Resolve(pathEl.GetString()!);
            else
                basePath = _sandbox.WorkingDirectory;

            var includeGlob = parameters.TryGetValue("include", out var incEl) && incEl.ValueKind == JsonValueKind.String
                ? incEl.GetString()! : "*";

            if (!Directory.Exists(basePath))
                return new ToolResult(Name, false, $"Directory not found: {basePath}");

            Regex regex;
            try
            {
                regex = new Regex(pattern, RegexOptions.Compiled, TimeSpan.FromSeconds(5));
            }
            catch (RegexParseException ex)
            {
                return new ToolResult(Name, false, $"Invalid regex pattern: {ex.Message}");
            }
            var sb = new StringBuilder();
            var matchCount = 0;

            foreach (var file in Directory.EnumerateFiles(basePath, includeGlob, SearchOption.AllDirectories))
            {
                if (ct.IsCancellationRequested) break;

                var relativePath = Path.GetRelativePath(_sandbox.WorkingDirectory, file);
                var lines = await File.ReadAllLinesAsync(file, ct);

                for (int i = 0; i < lines.Length; i++)
                {
                    if (regex.IsMatch(lines[i]))
                    {
                        sb.AppendLine($"{relativePath}:{i + 1}: {lines[i].TrimStart()}");
                        matchCount++;
                        if (sb.Length > MaxOutputBytes)
                        {
                            sb.AppendLine($"[Output truncated at 100KB. {matchCount}+ matches found.]");
                            return new ToolResult(Name, true, sb.ToString().TrimEnd());
                        }
                    }
                }
            }

            return new ToolResult(Name, true,
                matchCount > 0 ? sb.ToString().TrimEnd() : "No matches found.");
        }
        catch (SandboxViolationException ex)
        {
            return new ToolResult(Name, false, ex.Message);
        }
    }

    public string DescribeAction(Dictionary<string, JsonElement> parameters)
        => $"Search: {parameters["pattern"].GetString()}";
}
```

- [ ] **Step 8: Implement RunCommandTool**

```csharp
// src/GeminiCode/Tools/RunCommandTool.cs
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace GeminiCode.Tools;

public class RunCommandTool : ITool
{
    private readonly PathSandbox _sandbox;
    private const int MaxOutputBytes = 100 * 1024;
    private const int DefaultTimeoutMs = 120_000;

    public string Name => "RunCommand";
    public RiskLevel Risk => RiskLevel.High;

    public RunCommandTool(PathSandbox sandbox) => _sandbox = sandbox;

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, JsonElement> parameters, CancellationToken ct)
    {
        var command = parameters["command"].GetString()!;
        var timeoutMs = parameters.TryGetValue("timeout_ms", out var t) && t.ValueKind == JsonValueKind.Number
            ? t.GetInt32() : DefaultTimeoutMs;

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c {command}",
            WorkingDirectory = _sandbox.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var process = Process.Start(psi)!;
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeoutMs);

            var stdout = new StringBuilder();
            var stderr = new StringBuilder();

            var readOut = process.StandardOutput.ReadToEndAsync(cts.Token);
            var readErr = process.StandardError.ReadToEndAsync(cts.Token);

            await process.WaitForExitAsync(cts.Token);

            stdout.Append(await readOut);
            stderr.Append(await readErr);

            var output = stdout.ToString();
            if (stderr.Length > 0)
                output += $"\n[stderr]\n{stderr}";

            if (output.Length > MaxOutputBytes)
                output = output[..MaxOutputBytes] + "\n[Output truncated at 100KB.]";

            return new ToolResult(Name, process.ExitCode == 0, output);
        }
        catch (OperationCanceledException)
        {
            return new ToolResult(Name, false, $"Command timed out after {timeoutMs}ms.");
        }
    }

    public string DescribeAction(Dictionary<string, JsonElement> parameters)
        => $"Run: {parameters["command"].GetString()}";
}
```

- [ ] **Step 9: Run EditFileTool tests**

```bash
dotnet test src/GeminiCode.Tests --filter "EditFileToolTests"
```

Expected: All 4 tests PASS.

- [ ] **Step 10: Build to verify all tools compile**

```bash
dotnet build src/GeminiCode
```

Expected: Build succeeded.

- [ ] **Step 11: Commit**

```bash
git add src/GeminiCode/Tools/ src/GeminiCode.Tests/EditFileToolTests.cs
git commit -m "feat: implement all 6 tools with path sandboxing and output truncation"
```

---

## Task 7: Permission System (RiskAssessor, SessionAllowlist, PermissionGate)

**Files:**
- Create: `src/GeminiCode/Permissions/RiskAssessor.cs`
- Create: `src/GeminiCode/Permissions/SessionAllowlist.cs`
- Create: `src/GeminiCode/Permissions/PermissionGate.cs`
- Create: `src/GeminiCode.Tests/RiskAssessorTests.cs`
- Create: `src/GeminiCode.Tests/SessionAllowlistTests.cs`
- Create: `src/GeminiCode.Tests/PermissionGateTests.cs`

- [ ] **Step 1: Write failing tests for RiskAssessor**

```csharp
// src/GeminiCode.Tests/RiskAssessorTests.cs
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
```

- [ ] **Step 2: Write failing tests for SessionAllowlist**

```csharp
// src/GeminiCode.Tests/SessionAllowlistTests.cs
using GeminiCode.Permissions;

namespace GeminiCode.Tests;

public class SessionAllowlistTests
{
    [Fact]
    public void IsAllowed_NotAdded_ReturnsFalse()
    {
        var al = new SessionAllowlist();
        Assert.False(al.IsAllowed("WriteFile"));
    }

    [Fact]
    public void Add_ThenCheck_ReturnsTrue()
    {
        var al = new SessionAllowlist();
        al.Add("WriteFile");
        Assert.True(al.IsAllowed("WriteFile"));
    }

    [Fact]
    public void Add_RunCommand_Rejected()
    {
        var al = new SessionAllowlist();
        Assert.False(al.Add("RunCommand"));
        Assert.False(al.IsAllowed("RunCommand"));
    }

    [Fact]
    public void Clear_RemovesAllEntries()
    {
        var al = new SessionAllowlist();
        al.Add("WriteFile");
        al.Add("ReadFile");
        al.Clear();
        Assert.False(al.IsAllowed("WriteFile"));
        Assert.False(al.IsAllowed("ReadFile"));
    }

    [Fact]
    public void GetEntries_ReturnsAddedTools()
    {
        var al = new SessionAllowlist();
        al.Add("ReadFile");
        al.Add("EditFile");
        var entries = al.GetEntries();
        Assert.Contains("ReadFile", entries);
        Assert.Contains("EditFile", entries);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

```bash
dotnet test src/GeminiCode.Tests --filter "RiskAssessorTests|SessionAllowlistTests"
```

Expected: FAIL — classes do not exist.

- [ ] **Step 4: Implement RiskAssessor**

```csharp
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
```

- [ ] **Step 5: Implement SessionAllowlist**

```csharp
// src/GeminiCode/Permissions/SessionAllowlist.cs
namespace GeminiCode.Permissions;

public class SessionAllowlist
{
    private readonly HashSet<string> _allowed = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns true if added, false if rejected (e.g., RunCommand).</summary>
    public bool Add(string toolName)
    {
        if (toolName.Equals("RunCommand", StringComparison.OrdinalIgnoreCase))
            return false;
        _allowed.Add(toolName);
        return true;
    }

    public bool IsAllowed(string toolName) => _allowed.Contains(toolName);

    public void Clear() => _allowed.Clear();

    public IReadOnlyList<string> GetEntries() => _allowed.ToList().AsReadOnly();
}
```

- [ ] **Step 6: Implement PermissionGate**

```csharp
// src/GeminiCode/Permissions/PermissionGate.cs
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
        // Check allowlist first
        if (_allowlist.IsAllowed(tool.Name))
            return PermissionResult.Approved;

        // Display permission request
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
```

- [ ] **Step 7: Write PermissionGate tests**

```csharp
// src/GeminiCode.Tests/PermissionGateTests.cs
using System.Text.Json;
using GeminiCode.Permissions;
using GeminiCode.Tools;

namespace GeminiCode.Tests;

public class PermissionGateTests
{
    private static Dictionary<string, JsonElement> EmptyParams()
    {
        using var doc = JsonDocument.Parse("{}");
        return doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
    }

    private static Dictionary<string, JsonElement> PathParams(string path)
    {
        var json = JsonSerializer.Serialize(new { path });
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
    }

    private class FakeTool : ITool
    {
        public string Name { get; init; } = "ReadFile";
        public RiskLevel Risk { get; init; } = RiskLevel.Low;
        public Task<ToolResult> ExecuteAsync(Dictionary<string, JsonElement> parameters, CancellationToken ct)
            => Task.FromResult(new ToolResult(Name, true, "ok"));
        public string DescribeAction(Dictionary<string, JsonElement> parameters) => "test action";
    }

    [Fact]
    public void RequestPermission_UserTypesY_ReturnsApproved()
    {
        var allowlist = new SessionAllowlist();
        var input = new StringReader("y\n");
        var output = new StringWriter();
        var gate = new PermissionGate(allowlist, input, output);

        var result = gate.RequestPermission(new FakeTool(), PathParams("foo.cs"));
        Assert.Equal(PermissionResult.Approved, result);
    }

    [Fact]
    public void RequestPermission_UserTypesN_ReturnsDenied()
    {
        var allowlist = new SessionAllowlist();
        var input = new StringReader("n\n");
        var output = new StringWriter();
        var gate = new PermissionGate(allowlist, input, output);

        var result = gate.RequestPermission(new FakeTool(), PathParams("foo.cs"));
        Assert.Equal(PermissionResult.Denied, result);
    }

    [Fact]
    public void RequestPermission_UserTypesA_ReturnsAlwaysAndAddsToAllowlist()
    {
        var allowlist = new SessionAllowlist();
        var input = new StringReader("a\n");
        var output = new StringWriter();
        var gate = new PermissionGate(allowlist, input, output);

        var result = gate.RequestPermission(new FakeTool { Name = "WriteFile", Risk = RiskLevel.Medium }, PathParams("foo.cs"));
        Assert.Equal(PermissionResult.AlwaysApproved, result);
        Assert.True(allowlist.IsAllowed("WriteFile"));
    }

    [Fact]
    public void RequestPermission_AllowlistedTool_SkipsPrompt()
    {
        var allowlist = new SessionAllowlist();
        allowlist.Add("ReadFile");
        var gate = new PermissionGate(allowlist, new StringReader(""), new StringWriter());

        // Should not read from input at all — tool is already allowed
        var result = gate.RequestPermission(new FakeTool(), PathParams("foo.cs"));
        Assert.Equal(PermissionResult.Approved, result);
    }

    [Fact]
    public void RequestPermission_RunCommand_AlwaysRejected_FallsBackToYes()
    {
        var allowlist = new SessionAllowlist();
        // User tries "a" (rejected for RunCommand), then "y"
        var input = new StringReader("a\ny\n");
        var output = new StringWriter();
        var gate = new PermissionGate(allowlist, input, output);

        var tool = new FakeTool { Name = "RunCommand", Risk = RiskLevel.High };
        var cmdParams = JsonSerializer.Serialize(new { command = "ls" });
        using var doc = JsonDocument.Parse(cmdParams);
        var p = doc.RootElement.EnumerateObject().ToDictionary(x => x.Name, x => x.Value.Clone());

        var result = gate.RequestPermission(tool, p);
        Assert.Equal(PermissionResult.Approved, result);
        Assert.False(allowlist.IsAllowed("RunCommand"));
    }
}
```

- [ ] **Step 8: Run all permission tests to verify they pass**

```bash
dotnet test src/GeminiCode.Tests --filter "RiskAssessorTests|SessionAllowlistTests|PermissionGateTests"
```

Expected: All tests PASS.

- [ ] **Step 9: Commit**

```bash
git add src/GeminiCode/Permissions/ src/GeminiCode.Tests/RiskAssessorTests.cs src/GeminiCode.Tests/SessionAllowlistTests.cs src/GeminiCode.Tests/PermissionGateTests.cs
git commit -m "feat: add permission system with risk assessment, session allowlist, and tests"
```

---

## Task 8: Config System (AppSettings, ConfigLoader, selectors.json, settings.json)

**Files:**
- Create: `src/GeminiCode/Config/AppSettings.cs`
- Create: `src/GeminiCode/Config/ConfigLoader.cs`
- Create: `src/GeminiCode/defaults/selectors.json`
- Create: `src/GeminiCode/defaults/settings.json`

- [ ] **Step 1: Create default config files**

```json
// src/GeminiCode/defaults/settings.json
{
  "responseTimeoutSeconds": 120
}
```

```json
// src/GeminiCode/defaults/selectors.json
{
  "chatInput": "[aria-label='Talk to Gemini']",
  "sendButton": "button[aria-label='Send message']",
  "responseContainer": ".response-container",
  "typingIndicator": ".typing-indicator",
  "newChatButton": "[aria-label='New chat']"
}
```

Note: These selectors are best-effort defaults. They will need to be updated after testing against the live Gemini UI. The point is the infrastructure to load them.

- [ ] **Step 2: Implement AppSettings model**

```csharp
// src/GeminiCode/Config/AppSettings.cs
using System.Text.Json.Serialization;

namespace GeminiCode.Config;

public class AppSettings
{
    [JsonPropertyName("responseTimeoutSeconds")]
    public int ResponseTimeoutSeconds { get; set; } = 120;
}

public class DomSelectorConfig
{
    [JsonPropertyName("chatInput")]
    public string ChatInput { get; set; } = "[aria-label='Talk to Gemini']";

    [JsonPropertyName("sendButton")]
    public string SendButton { get; set; } = "button[aria-label='Send message']";

    [JsonPropertyName("responseContainer")]
    public string ResponseContainer { get; set; } = ".response-container";

    [JsonPropertyName("typingIndicator")]
    public string TypingIndicator { get; set; } = ".typing-indicator";

    [JsonPropertyName("newChatButton")]
    public string NewChatButton { get; set; } = "[aria-label='New chat']";
}
```

- [ ] **Step 3: Implement ConfigLoader**

```csharp
// src/GeminiCode/Config/ConfigLoader.cs
using System.Text.Json;

namespace GeminiCode.Config;

public static class ConfigLoader
{
    private static readonly string AppDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GeminiCode");

    public static string AppDataPath => AppDataDir;

    public static AppSettings LoadSettings()
    {
        var path = Path.Combine(AppDataDir, "settings.json");
        return LoadOrCreateDefault<AppSettings>(path);
    }

    public static DomSelectorConfig LoadSelectors()
    {
        var path = Path.Combine(AppDataDir, "selectors.json");
        return LoadOrCreateDefault<DomSelectorConfig>(path);
    }

    private static T LoadOrCreateDefault<T>(string path) where T : new()
    {
        Directory.CreateDirectory(AppDataDir);

        if (File.Exists(path))
        {
            try
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<T>(json) ?? new T();
            }
            catch (JsonException)
            {
                Console.Error.WriteLine($"Warning: Failed to parse {path}, using defaults.");
                return new T();
            }
        }

        // Create default
        var defaults = new T();
        var defaultJson = JsonSerializer.Serialize(defaults, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, defaultJson);
        return defaults;
    }
}
```

- [ ] **Step 4: Mark config files as embedded resources in csproj (optional, not strictly needed since ConfigLoader creates defaults)**

Build to verify:

```bash
dotnet build src/GeminiCode
```

Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add src/GeminiCode/Config/ src/GeminiCode/defaults/
git commit -m "feat: add config system with settings.json and selectors.json"
```

---

## Task 9: Browser Window & Browser Bridge

**Files:**
- Create: `src/GeminiCode/Browser/DomSelectors.cs`
- Create: `src/GeminiCode/Browser/BrowserWindow.cs`
- Create: `src/GeminiCode/Browser/BrowserBridge.cs`
- Create: `src/GeminiCode/Browser/SessionMonitor.cs`

- [ ] **Step 1: Implement DomSelectors (model that wraps config)**

```csharp
// src/GeminiCode/Browser/DomSelectors.cs
using GeminiCode.Config;

namespace GeminiCode.Browser;

public class DomSelectors
{
    public string ChatInput { get; }
    public string SendButton { get; }
    public string ResponseContainer { get; }
    public string TypingIndicator { get; }
    public string NewChatButton { get; }

    public DomSelectors(DomSelectorConfig config)
    {
        ChatInput = config.ChatInput;
        SendButton = config.SendButton;
        ResponseContainer = config.ResponseContainer;
        TypingIndicator = config.TypingIndicator;
        NewChatButton = config.NewChatButton;
    }
}
```

- [ ] **Step 2: Implement BrowserWindow**

```csharp
// src/GeminiCode/Browser/BrowserWindow.cs
using Microsoft.Web.WebView2.WinForms;
using System.Windows.Forms;

namespace GeminiCode.Browser;

public class BrowserWindow : Form
{
    public WebView2 WebView { get; }
    private readonly TaskCompletionSource _initTcs = new();

    public BrowserWindow()
    {
        Text = "GeminiCode - Gemini";
        Width = 1200;
        Height = 800;
        StartPosition = FormStartPosition.CenterScreen;

        WebView = new WebView2
        {
            Dock = DockStyle.Fill
        };
        Controls.Add(WebView);
    }

    public async Task InitializeAsync(string userDataFolder)
    {
        var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(
            userDataFolder: userDataFolder);
        await WebView.EnsureCoreWebView2Async(env);
        _initTcs.TrySetResult();
    }

    public Task WaitForInitialization() => _initTcs.Task;

    public void NavigateTo(string url)
    {
        WebView.CoreWebView2.Navigate(url);
    }
}
```

- [ ] **Step 3: Implement SessionMonitor**

```csharp
// src/GeminiCode/Browser/SessionMonitor.cs
using Microsoft.Web.WebView2.WinForms;

namespace GeminiCode.Browser;

public class SessionMonitor
{
    private readonly DomSelectors _selectors;

    public SessionMonitor(DomSelectors selectors)
    {
        _selectors = selectors;
    }

    /// <summary>Returns JS that checks if the chat input element exists (logged in).</summary>
    public string GetAuthCheckScript()
    {
        return $"document.querySelector(\"{EscapeJs(_selectors.ChatInput)}\") !== null";
    }

    /// <summary>Returns JS that checks if all critical DOM elements are present.</summary>
    public string GetHealthCheckScript()
    {
        return $$"""
            (function() {
                var results = {};
                results.chatInput = document.querySelector("{{EscapeJs(_selectors.ChatInput)}}") !== null;
                results.sendButton = document.querySelector("{{EscapeJs(_selectors.SendButton)}}") !== null;
                results.responseContainer = document.querySelector("{{EscapeJs(_selectors.ResponseContainer)}}") !== null;
                return JSON.stringify(results);
            })()
            """;
    }

    private static string EscapeJs(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
```

- [ ] **Step 4: Implement BrowserBridge**

```csharp
// src/GeminiCode/Browser/BrowserBridge.cs
using System.Text.Json;

namespace GeminiCode.Browser;

public class BrowserBridge : IDisposable
{
    private BrowserWindow? _window;
    private Thread? _staThread;
    private readonly DomSelectors _selectors;
    private readonly SessionMonitor _sessionMonitor;
    private readonly string _userDataFolder;
    private readonly CancellationTokenSource _closedCts = new();
    private readonly TaskCompletionSource _windowReady = new();

    public CancellationToken BrowserClosedToken => _closedCts.Token;

    public BrowserBridge(DomSelectors selectors, string userDataFolder)
    {
        _selectors = selectors;
        _sessionMonitor = new SessionMonitor(selectors);
        _userDataFolder = userDataFolder;
    }

    public Task StartAsync()
    {
        _staThread = new Thread(RunStaThread);
        _staThread.SetApartmentState(ApartmentState.STA);
        _staThread.IsBackground = true;
        _staThread.Start();
        return _windowReady.Task;
    }

    private void RunStaThread()
    {
        try
        {
            _window = new BrowserWindow();
            _window.FormClosed += (_, _) => _closedCts.Cancel();

            // Initialize WebView2 synchronously on the STA thread before starting the message pump.
            // Use Load event to run async init after the message pump is running.
            _window.Load += async (_, _) =>
            {
                try
                {
                    await _window.InitializeAsync(_userDataFolder);
                    _window.NavigateTo("https://gemini.google.com");
                    _windowReady.TrySetResult();
                }
                catch (Exception ex)
                {
                    _windowReady.TrySetException(ex);
                }
            };

            Application.Run(_window);
        }
        catch (Exception ex)
        {
            _windowReady.TrySetException(ex);
        }
    }

    public async Task<bool> CheckAuthenticatedAsync()
    {
        var script = _sessionMonitor.GetAuthCheckScript();
        var result = await InvokeOnStaAsync(() =>
            _window!.WebView.CoreWebView2.ExecuteScriptAsync(script));
        return result == "true";
    }

    public async Task<Dictionary<string, bool>> RunHealthCheckAsync()
    {
        var script = _sessionMonitor.GetHealthCheckScript();
        var result = await InvokeOnStaAsync(() =>
            _window!.WebView.CoreWebView2.ExecuteScriptAsync(script));

        // Result is a JSON string escaped inside a JSON string
        var unescaped = JsonSerializer.Deserialize<string>(result) ?? "{}";
        return JsonSerializer.Deserialize<Dictionary<string, bool>>(unescaped) ?? new();
    }

    public async Task SendMessageAsync(string message)
    {
        var escapedMessage = JsonSerializer.Serialize(message);
        var script = $$"""
            (function() {
                var input = document.querySelector("{{EscapeJs(_selectors.ChatInput)}}");
                if (!input) return 'no_input';

                // Focus and set value
                input.focus();
                input.textContent = {{escapedMessage}};
                input.dispatchEvent(new Event('input', { bubbles: true }));

                // Small delay then click send
                setTimeout(function() {
                    var btn = document.querySelector("{{EscapeJs(_selectors.SendButton)}}");
                    if (btn) btn.click();
                }, 200);

                return 'sent';
            })()
            """;
        await InvokeOnStaAsync(() => _window!.WebView.CoreWebView2.ExecuteScriptAsync(script));
    }

    public async Task<string?> WaitForResponseAsync(int timeoutSeconds, CancellationToken ct)
    {
        var pollScript = $$"""
            (function() {
                var indicator = document.querySelector("{{EscapeJs(_selectors.TypingIndicator)}}");
                if (indicator && indicator.offsetParent !== null) return JSON.stringify({done: false});

                var containers = document.querySelectorAll("{{EscapeJs(_selectors.ResponseContainer)}}");
                if (containers.length === 0) return JSON.stringify({done: false});

                var last = containers[containers.length - 1];
                var text = last.innerText || last.textContent || '';
                return JSON.stringify({done: true, text: text});
            })()
            """;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        // Initial delay to let Gemini start processing
        await Task.Delay(2000, cts.Token);

        while (!cts.Token.IsCancellationRequested)
        {
            var resultJson = await InvokeOnStaAsync(() =>
                _window!.WebView.CoreWebView2.ExecuteScriptAsync(pollScript));

            var unescaped = JsonSerializer.Deserialize<string>(resultJson);
            if (unescaped != null)
            {
                using var doc = JsonDocument.Parse(unescaped);
                if (doc.RootElement.GetProperty("done").GetBoolean())
                    return doc.RootElement.GetProperty("text").GetString();
            }

            await Task.Delay(1000, cts.Token);
        }

        return null; // Timed out
    }

    public async Task StartNewChatAsync()
    {
        var script = $$"""
            (function() {
                var btn = document.querySelector("{{EscapeJs(_selectors.NewChatButton)}}");
                if (btn) { btn.click(); return 'ok'; }
                return 'not_found';
            })()
            """;
        await InvokeOnStaAsync(() => _window!.WebView.CoreWebView2.ExecuteScriptAsync(script));
    }

    public void BringToFront()
    {
        _window?.Invoke(() =>
        {
            _window.WindowState = System.Windows.Forms.FormWindowState.Normal;
            _window.BringToFront();
            _window.Activate();
        });
    }

    private Task<string> InvokeOnStaAsync(Func<Task<string>> action)
    {
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _window!.BeginInvoke(async () =>
        {
            try
            {
                var result = await action();
                tcs.TrySetResult(result);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });
        return tcs.Task;
    }

    private static string EscapeJs(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    public void Dispose()
    {
        if (_window != null && !_window.IsDisposed)
        {
            _window.Invoke(() => _window.Close());
            _staThread?.Join(5000);
        }
        _closedCts.Dispose();
    }
}
```

- [ ] **Step 5: Build to verify compilation**

```bash
dotnet build src/GeminiCode
```

Expected: Build succeeded. (Browser components are tested manually against the live Gemini UI.)

- [ ] **Step 6: Commit**

```bash
git add src/GeminiCode/Browser/
git commit -m "feat: add WebView2 browser bridge with STA threading and DOM interaction"
```

---

## Task 10: System Prompt & Conversation Manager

**Files:**
- Create: `src/GeminiCode/Agent/SystemPrompt.cs`
- Create: `src/GeminiCode/Agent/ConversationManager.cs`

- [ ] **Step 1: Implement SystemPrompt**

```csharp
// src/GeminiCode/Agent/SystemPrompt.cs
namespace GeminiCode.Agent;

public static class SystemPrompt
{
    public const string Template = """
        You are a coding assistant with access to the user's local filesystem and shell. When you need to perform actions, respond with tool calls in this exact format:

        <tool_call>
        {"name": "ToolName", "parameters": {"param1": "value1"}}
        </tool_call>

        You may include multiple tool calls in one response. You may also include explanatory text before/after tool calls.

        Available tools:
        - ReadFile: {"path": "string"} — Read file contents
        - WriteFile: {"path": "string", "content": "string"} — Create or overwrite a file
        - EditFile: {"path": "string", "old_string": "string", "new_string": "string"} — Replace exact text in a file
        - ListFiles: {"pattern": "string", "path": "string (optional)"} — List files matching a glob pattern
        - SearchFiles: {"pattern": "string", "path": "string (optional)", "include": "string (optional)"} — Search file contents with regex
        - RunCommand: {"command": "string", "timeout_ms": "number (optional)"} — Run a shell command

        After each tool execution, you will receive the result in <tool_result> tags. Continue your work based on the results.

        IMPORTANT: Always use tool calls for file operations. Never just show code in a code block and ask the user to save it manually.
        """;

    public const string DriftReminder = "(Remember: use <tool_call> for all file/shell actions.)";

    public const string CorrectionPrompt =
        "Please use the tool_call format to perform file operations instead of showing code blocks. " +
        "Wrap your action in <tool_call>...</tool_call> as specified.";
}
```

- [ ] **Step 2: Implement ConversationManager**

```csharp
// src/GeminiCode/Agent/ConversationManager.cs
namespace GeminiCode.Agent;

public class ConversationManager
{
    private int _turnCount;
    private bool _systemPromptSent;
    private const int DriftPreventionThreshold = 10;

    public int TurnCount => _turnCount;
    public bool IsFirstMessage => !_systemPromptSent;

    public string PrepareMessage(string userMessage)
    {
        _turnCount++;
        string message;

        if (!_systemPromptSent)
        {
            message = SystemPrompt.Template + "\n\n" + userMessage;
            _systemPromptSent = true;
        }
        else if (_turnCount > DriftPreventionThreshold)
        {
            message = userMessage + "\n\n" + SystemPrompt.DriftReminder;
        }
        else
        {
            message = userMessage;
        }

        return message;
    }

    public string PrepareToolResults(IEnumerable<string> results)
    {
        // Don't increment turn count for tool results — only user messages count toward drift prevention
        return string.Join("\n\n", results);
    }

    public void Reset()
    {
        _turnCount = 0;
        _systemPromptSent = false;
    }
}
```

- [ ] **Step 3: Build to verify**

```bash
dotnet build src/GeminiCode
```

Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/GeminiCode/Agent/SystemPrompt.cs src/GeminiCode/Agent/ConversationManager.cs
git commit -m "feat: add system prompt and conversation manager with drift prevention"
```

---

## Task 11: Agent Orchestrator

**Files:**
- Create: `src/GeminiCode/Agent/AgentOrchestrator.cs`

- [ ] **Step 1: Implement AgentOrchestrator**

```csharp
// src/GeminiCode/Agent/AgentOrchestrator.cs
using System.Text.RegularExpressions;
using GeminiCode.Browser;
using GeminiCode.Cli;
using GeminiCode.Config;
using GeminiCode.Permissions;
using GeminiCode.Tools;

namespace GeminiCode.Agent;

public class AgentOrchestrator
{
    private readonly BrowserBridge _browser;
    private readonly ToolRegistry _tools;
    private readonly PermissionGate _permissionGate;
    private readonly ConversationManager _conversation;
    private readonly AppSettings _settings;
    private const int MaxRetries = 2;

    public AgentOrchestrator(
        BrowserBridge browser,
        ToolRegistry tools,
        PermissionGate permissionGate,
        ConversationManager conversation,
        AppSettings settings)
    {
        _browser = browser;
        _tools = tools;
        _permissionGate = permissionGate;
        _conversation = conversation;
        _settings = settings;
    }

    public async Task<string?> SendAndProcessAsync(string userMessage, CancellationToken ct)
    {
        var message = _conversation.PrepareMessage(userMessage);
        await _browser.SendMessageAsync(message);

        var response = await _browser.WaitForResponseAsync(_settings.ResponseTimeoutSeconds, ct);
        if (response == null)
            return null; // Timed out

        return await ProcessResponseAsync(response, 0, ct);
    }

    private async Task<string?> ProcessResponseAsync(string response, int retryCount, CancellationToken ct)
    {
        var parsed = ToolCallParser.Parse(response);

        // Display conversational text
        if (!string.IsNullOrWhiteSpace(parsed.TextContent))
        {
            var rendered = MarkdownRenderer.Render(parsed.TextContent);
            Console.Write(rendered);
        }

        // No tool calls — check if format enforcement retry is needed
        if (parsed.ToolCalls.Count == 0)
        {
            // Detect if response contains code blocks that look like they should be tool calls
            if (retryCount < MaxRetries && LooksLikeUnstructuredToolUse(parsed.TextContent))
            {
                Console.WriteLine($"\n{AnsiHelper.Yellow}Gemini didn't use tool_call format. Requesting correction (attempt {retryCount + 1}/{MaxRetries})...{AnsiHelper.Reset}");
                await _browser.SendMessageAsync(SystemPrompt.CorrectionPrompt);
                var retryResponse = await _browser.WaitForResponseAsync(_settings.ResponseTimeoutSeconds, ct);
                if (retryResponse == null) return null;
                return await ProcessResponseAsync(retryResponse, retryCount + 1, ct);
            }
            return parsed.TextContent;
        }

        // Execute tool calls sequentially with permission checks
        var results = new List<string>();

        foreach (var toolCall in parsed.ToolCalls)
        {
            var tool = _tools.GetTool(toolCall.Name);
            if (tool == null)
            {
                var result = new ToolResult(toolCall.Name, false, $"Unknown tool: {toolCall.Name}");
                results.Add(result.ToProtocolString());
                continue;
            }

            var permission = _permissionGate.RequestPermission(tool, toolCall.Parameters);

            if (permission == PermissionResult.Denied)
            {
                var result = new ToolResult(toolCall.Name, false, "Permission denied by user");
                results.Add(result.ToProtocolString());
                continue;
            }

            // Execute
            var toolResult = await tool.ExecuteAsync(toolCall.Parameters, ct);

            // Display result summary
            var color = toolResult.Success ? AnsiHelper.Green : AnsiHelper.Red;
            Console.WriteLine($"{color}{tool.Name}: {(toolResult.Success ? "OK" : "FAILED")}{AnsiHelper.Reset}");

            results.Add(toolResult.ToProtocolString());
        }

        // Send results back to Gemini
        var combinedResults = _conversation.PrepareToolResults(results);
        await _browser.SendMessageAsync(combinedResults);

        // Wait for Gemini's follow-up response
        var followUp = await _browser.WaitForResponseAsync(_settings.ResponseTimeoutSeconds, ct);
        if (followUp == null)
            return null;

        return await ProcessResponseAsync(followUp, 0, ct);
    }

    /// <summary>Detect if Gemini responded with code blocks that look like they should have been tool calls.</summary>
    private static bool LooksLikeUnstructuredToolUse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        // Check for code blocks with file-path-like annotations or "save this" language
        return text.Contains("```") && (
            text.Contains("save this", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("create this file", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("write this to", StringComparison.OrdinalIgnoreCase) ||
            System.Text.RegularExpressions.Regex.IsMatch(text, @"```\w+:[\w/\\\.]+")
        );
    }
}
```

- [ ] **Step 2: Build to verify**

```bash
dotnet build src/GeminiCode
```

Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/GeminiCode/Agent/AgentOrchestrator.cs
git commit -m "feat: add agent orchestrator with tool execution loop and permission checks"
```

---

## Task 12: Slash Command Handler

**Files:**
- Create: `src/GeminiCode/Cli/CommandHandler.cs`

- [ ] **Step 1: Implement CommandHandler**

```csharp
// src/GeminiCode/Cli/CommandHandler.cs
using GeminiCode.Browser;
using GeminiCode.Agent;
using GeminiCode.Permissions;
using GeminiCode.Tools;

namespace GeminiCode.Cli;

public class CommandHandler
{
    private readonly BrowserBridge _browser;
    private readonly ConversationManager _conversation;
    private readonly SessionAllowlist _allowlist;
    private readonly PathSandbox _sandbox;

    public CommandHandler(
        BrowserBridge browser,
        ConversationManager conversation,
        SessionAllowlist allowlist,
        PathSandbox sandbox)
    {
        _browser = browser;
        _conversation = conversation;
        _allowlist = allowlist;
        _sandbox = sandbox;
    }

    /// <summary>Returns true if the input was a command (handled), false if it's a regular message.</summary>
    public async Task<bool> TryHandleAsync(string input)
    {
        if (!input.StartsWith('/'))
            return false;

        var parts = input.Split(' ', 2);
        var command = parts[0].ToLowerInvariant();
        var arg = parts.Length > 1 ? parts[1] : null;

        switch (command)
        {
            case "/help":
                PrintHelp();
                return true;
            case "/clear":
                Console.Clear();
                return true;
            case "/new":
                await HandleNewChatAsync();
                return true;
            case "/browser":
                _browser.BringToFront();
                Console.WriteLine("Browser window brought to front.");
                return true;
            case "/history":
                Console.WriteLine($"Conversation turns: {_conversation.TurnCount}");
                return true;
            case "/allowlist":
                PrintAllowlist();
                return true;
            case "/status":
                await PrintStatusAsync();
                return true;
            case "/cd":
                HandleCd(arg);
                return true;
            case "/paste":
                Console.WriteLine("Paste mode: enter text, then type END on a new line to finish.");
                return true;
            case "/exit":
                HandleExit();
                return true;
            default:
                Console.WriteLine($"Unknown command: {command}. Type /help for available commands.");
                return true;
        }
    }

    private void PrintHelp()
    {
        Console.WriteLine($"""
            {AnsiHelper.Bold}Available commands:{AnsiHelper.Reset}
              /help       — Show this help
              /clear      — Clear terminal
              /new        — Start a new Gemini conversation
              /browser    — Bring browser window to foreground
              /history    — Show conversation turn count
              /allowlist  — Show current session allowlist
              /status     — Show session state
              /cd <path>  — Change working directory
              /exit       — Quit GeminiCode
            """);
    }

    private async Task HandleNewChatAsync()
    {
        await _browser.StartNewChatAsync();
        _conversation.Reset();
        Console.WriteLine("New conversation started.");
    }

    private void PrintAllowlist()
    {
        var entries = _allowlist.GetEntries();
        if (entries.Count == 0)
        {
            Console.WriteLine("Session allowlist is empty.");
            return;
        }
        Console.WriteLine($"{AnsiHelper.Bold}Auto-approved tools:{AnsiHelper.Reset}");
        foreach (var entry in entries)
            Console.WriteLine($"  - {entry}");
    }

    private async Task PrintStatusAsync()
    {
        var authCheck = await _browser.CheckAuthenticatedAsync();
        Console.WriteLine($"""
            {AnsiHelper.Bold}Status:{AnsiHelper.Reset}
              Auth:       {(authCheck ? $"{AnsiHelper.Green}Authenticated{AnsiHelper.Reset}" : $"{AnsiHelper.Red}Not authenticated{AnsiHelper.Reset}")}
              Work dir:   {_sandbox.WorkingDirectory}
              Turns:      {_conversation.TurnCount}
              Allowlist:  {_allowlist.GetEntries().Count} tools auto-approved
            """);
    }

    private void HandleCd(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            Console.WriteLine("Usage: /cd <path>");
            return;
        }

        var resolved = Path.GetFullPath(path);
        if (!Directory.Exists(resolved))
        {
            Console.WriteLine($"Directory not found: {resolved}");
            return;
        }

        _sandbox.UpdateWorkingDirectory(resolved);
        _allowlist.Clear();
        Console.WriteLine($"Working directory changed to {resolved}. Allowlist cleared.");
    }

    private void HandleExit()
    {
        Console.WriteLine("Goodbye.");
        _browser.Dispose();
        Environment.Exit(0);
    }
}
```

- [ ] **Step 2: Build to verify**

```bash
dotnet build src/GeminiCode
```

Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/GeminiCode/Cli/CommandHandler.cs
git commit -m "feat: add slash command handler with /help, /cd, /status, /new, etc."
```

---

## Task 13: CLI Engine & Main Prompt Loop

**Files:**
- Create: `src/GeminiCode/Cli/CliEngine.cs`

- [ ] **Step 1: Implement CliEngine**

```csharp
// src/GeminiCode/Cli/CliEngine.cs
using GeminiCode.Agent;
using GeminiCode.Browser;

namespace GeminiCode.Cli;

public class CliEngine
{
    private readonly AgentOrchestrator _orchestrator;
    private readonly CommandHandler _commands;
    private readonly BrowserBridge _browser;

    public CliEngine(AgentOrchestrator orchestrator, CommandHandler commands, BrowserBridge browser)
    {
        _orchestrator = orchestrator;
        _commands = commands;
        _browser = browser;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        Console.WriteLine();
        Console.Write($"{AnsiHelper.Green}>{AnsiHelper.Reset} ");

        while (!ct.IsCancellationRequested)
        {
            var input = ReadInput();
            if (input == null) break;

            input = input.Trim();
            if (string.IsNullOrEmpty(input))
            {
                Console.Write($"{AnsiHelper.Green}>{AnsiHelper.Reset} ");
                continue;
            }

            // Slash commands
            if (await _commands.TryHandleAsync(input))
            {
                Console.Write($"\n{AnsiHelper.Green}>{AnsiHelper.Reset} ");
                continue;
            }

            // Send to Gemini
            Console.WriteLine($"{AnsiHelper.Dim}Sending to Gemini...{AnsiHelper.Reset}");

            try
            {
                var result = await _orchestrator.SendAndProcessAsync(input, ct);
                if (result == null)
                    Console.WriteLine($"\n{AnsiHelper.Yellow}Gemini response timed out. Type your message to retry, or /new to start fresh.{AnsiHelper.Reset}");
            }
            catch (OperationCanceledException) when (_browser.BrowserClosedToken.IsCancellationRequested)
            {
                Console.Write($"\n{AnsiHelper.Yellow}Browser closed. Restart browser or exit? [r/e]{AnsiHelper.Reset} > ");
                var choice = Console.ReadLine()?.Trim().ToLowerInvariant();
                if (choice == "r")
                {
                    Console.WriteLine("Restarting browser...");
                    await _browser.StartAsync();
                    Console.WriteLine($"{AnsiHelper.Green}Browser restarted.{AnsiHelper.Reset}");
                }
                else
                {
                    Console.WriteLine("Goodbye.");
                    return;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{AnsiHelper.Red}Error: {ex.Message}{AnsiHelper.Reset}");
            }

            Console.Write($"\n{AnsiHelper.Green}>{AnsiHelper.Reset} ");
        }
    }

    private static string? ReadInput()
    {
        var lines = new List<string>();
        var line = Console.ReadLine();

        if (line == null) return null;

        // /paste mode: read until "END" on its own line
        if (line.Trim().Equals("/paste", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"{AnsiHelper.Dim}Paste mode: enter text, then type END on a new line to finish.{AnsiHelper.Reset}");
            while (true)
            {
                var pasteLine = Console.ReadLine();
                if (pasteLine == null || pasteLine.Trim() == "END") break;
                lines.Add(pasteLine);
            }
            return string.Join("\n", lines);
        }

        // Multi-line: trailing backslash
        while (line.EndsWith('\\'))
        {
            lines.Add(line[..^1]);
            Console.Write($"{AnsiHelper.Gray}  ...{AnsiHelper.Reset} ");
            line = Console.ReadLine();
            if (line == null) break;
        }

        if (line != null) lines.Add(line);
        return string.Join("\n", lines);
    }
}
```

- [ ] **Step 2: Build to verify**

```bash
dotnet build src/GeminiCode
```

Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/GeminiCode/Cli/CliEngine.cs
git commit -m "feat: add CLI engine with prompt loop, multi-line input, and error handling"
```

---

## Task 14: Program.cs — Wire Everything Together

**Files:**
- Modify: `src/GeminiCode/Program.cs`

- [ ] **Step 1: Implement full Program.cs**

```csharp
// src/GeminiCode/Program.cs
using GeminiCode.Agent;
using GeminiCode.Browser;
using GeminiCode.Cli;
using GeminiCode.Config;
using GeminiCode.Permissions;
using GeminiCode.Tools;

namespace GeminiCode;

public class Program
{
    public static async Task Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine($"{AnsiHelper.Bold}GeminiCode v0.1.0{AnsiHelper.Reset}");

        // Determine working directory
        var workDir = args.Length > 0 ? Path.GetFullPath(args[0]) : Directory.GetCurrentDirectory();
        if (!Directory.Exists(workDir))
        {
            Console.Error.WriteLine($"Directory not found: {workDir}");
            return;
        }

        // Load config
        var settings = ConfigLoader.LoadSettings();
        var selectorConfig = ConfigLoader.LoadSelectors();
        var selectors = new DomSelectors(selectorConfig);

        // Initialize path sandbox
        var sandbox = new PathSandbox(workDir);

        // Initialize tools
        var toolRegistry = new ToolRegistry();
        toolRegistry.Register(new ReadFileTool(sandbox));
        toolRegistry.Register(new WriteFileTool(sandbox));
        toolRegistry.Register(new EditFileTool(sandbox));
        toolRegistry.Register(new ListFilesTool(sandbox));
        toolRegistry.Register(new SearchFilesTool(sandbox));
        toolRegistry.Register(new RunCommandTool(sandbox));

        // Initialize permissions
        var allowlist = new SessionAllowlist();
        var permissionGate = new PermissionGate(allowlist);

        // Initialize browser
        var userDataFolder = Path.Combine(ConfigLoader.AppDataPath, "WebView2Data");
        var browser = new BrowserBridge(selectors, userDataFolder);

        Console.WriteLine("Opening Gemini browser...");
        await browser.StartAsync();

        // Wait for authentication
        Console.WriteLine($"Waiting for sign-in... {AnsiHelper.Dim}(sign in via the browser window){AnsiHelper.Reset}");
        if (!await WaitForAuth(browser))
            return;

        // DOM health check
        var health = await browser.RunHealthCheckAsync();
        var missing = health.Where(kv => !kv.Value).Select(kv => kv.Key).ToList();
        if (missing.Count > 0)
            Console.WriteLine($"{AnsiHelper.Yellow}Warning: Some UI elements not found ({string.Join(", ", missing)}) — Gemini may have updated. Check selectors.json.{AnsiHelper.Reset}");

        Console.WriteLine($"{AnsiHelper.Green}Authenticated. Ready.{AnsiHelper.Reset}");
        Console.WriteLine($"Working directory: {AnsiHelper.Bold}{workDir}{AnsiHelper.Reset}");

        // Initialize agent
        var conversation = new ConversationManager();
        var orchestrator = new AgentOrchestrator(browser, toolRegistry, permissionGate, conversation, settings);

        // Initialize CLI
        var commands = new CommandHandler(browser, conversation, allowlist, sandbox);
        var cli = new CliEngine(orchestrator, commands, browser);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(browser.BrowserClosedToken);
        await cli.RunAsync(cts.Token);

        browser.Dispose();
    }

    private static async Task<bool> WaitForAuth(BrowserBridge browser)
    {
        var interval = TimeSpan.FromSeconds(2);

        while (true)
        {
            var timeout = TimeSpan.FromMinutes(5);
            var elapsed = TimeSpan.Zero;

            while (elapsed < timeout)
            {
                if (await browser.CheckAuthenticatedAsync())
                    return true;

                await Task.Delay(interval);
                elapsed += interval;
            }

            Console.Write("Still waiting for sign-in. Press Enter to keep waiting, or type 'exit' to quit: ");
            var response = Console.ReadLine()?.Trim().ToLowerInvariant();
            if (response == "exit")
                return false;
            // Loop continues — wait another 5 minutes
        }
    }
}
```

- [ ] **Step 2: Build the full solution**

```bash
dotnet build GeminiCode.sln
```

Expected: Build succeeded.

- [ ] **Step 3: Run all tests**

```bash
dotnet test GeminiCode.sln
```

Expected: All unit tests PASS.

- [ ] **Step 4: Commit**

```bash
git add src/GeminiCode/Program.cs
git commit -m "feat: wire up all components in Program.cs — app is functional"
```

---

## Task 15: Manual Integration Test

This task requires running the app against the live Gemini web UI.

- [ ] **Step 1: Run the application**

```bash
dotnet run --project src/GeminiCode -- D:/CodeProjects/GeminiCode
```

Expected: Browser window opens showing Gemini. CLI displays "Waiting for sign-in..."

- [ ] **Step 2: Sign in to Google in the browser window**

Expected: After signing in, CLI displays "Authenticated. Ready." and shows the prompt.

- [ ] **Step 3: Test basic conversation**

Type: `Hello, what tools do you have available?`

Expected: Gemini responds acknowledging the tools from the system prompt.

- [ ] **Step 4: Test tool call — ReadFile**

Type: `Read the file src/GeminiCode/Program.cs`

Expected: Permission prompt appears for ReadFile. After approval, Gemini receives and discusses the file contents.

- [ ] **Step 5: Test tool call — WriteFile with permission denial**

Type: `Create a file called test.txt with the content "hello world"`

Expected: Permission prompt appears for WriteFile. Deny with `n`. Gemini receives "Permission denied" result.

- [ ] **Step 6: Test slash commands**

Type: `/status`, `/help`, `/allowlist`

Expected: Each displays correct output.

- [ ] **Step 7: Fix any DOM selector issues**

If selectors don't match the live Gemini UI, update `%APPDATA%/GeminiCode/selectors.json` with corrected selectors discovered by inspecting the Gemini page with browser dev tools.

- [ ] **Step 8: Commit any fixes**

```bash
git add -A
git commit -m "fix: adjust DOM selectors and integration fixes from manual testing"
```

---

## Task 16: Final Cleanup & Documentation

- [ ] **Step 1: Delete the default xUnit test file if it still exists**

```bash
rm src/GeminiCode.Tests/UnitTest1.cs 2>/dev/null
```

- [ ] **Step 2: Run full test suite one final time**

```bash
dotnet test GeminiCode.sln --verbosity normal
```

Expected: All tests PASS, no warnings.

- [ ] **Step 3: Final commit**

```bash
git add -A
git commit -m "chore: cleanup and finalize v0.1.0"
```
