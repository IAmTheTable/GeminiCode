# File Upload, Context Preservation, Agent Profiles & Workflow Commands — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add file upload via WebView2, session context preservation via markdown, switchable agent profiles with GEMINI.md support, and orchestrated workflow commands (/simplify, /brainstorm).

**Architecture:** Four features sharing unified infrastructure. `AgentProfile` composes system prompts from base + profile + GEMINI.md. `SessionContext` auto-logs session state to `.gemini/session-context.md`. `BrowserBridge.UploadFileAsync` enables file attachments. `WorkflowRunner` orchestrates multi-phase prompts with terminal progress display.

**Tech Stack:** C# / .NET 9.0-windows / WebView2 / Windows Forms

---

## File Structure

### New Files
| File | Purpose |
|------|---------|
| `src/GeminiCode/Agent/AgentProfile.cs` | Profile loading, discovery, and system prompt composition |
| `src/GeminiCode/Agent/SessionContext.cs` | Session context auto-logging and markdown generation |
| `src/GeminiCode/Agent/WorkflowRunner.cs` | Multi-phase workflow orchestration with terminal progress |
| `src/GeminiCode/Agent/Workflows/SimplifyWorkflow.cs` | /simplify phase definitions |
| `src/GeminiCode/Agent/Workflows/BrainstormWorkflow.cs` | /brainstorm phase definitions |
| `src/GeminiCode/Agent/Profiles/general.md` | Default agent profile |
| `src/GeminiCode/Agent/Profiles/code-reviewer.md` | Code reviewer profile |
| `src/GeminiCode/Agent/Profiles/architect.md` | Architect profile |
| `src/GeminiCode/Agent/Profiles/debugger.md` | Debugger profile |
| `src/GeminiCode/Agent/Profiles/refactorer.md` | Refactorer profile |
| `src/GeminiCode.Tests/AgentProfileTests.cs` | Tests for profile loading and composition |
| `src/GeminiCode.Tests/SessionContextTests.cs` | Tests for context logging and markdown generation |
| `src/GeminiCode.Tests/WorkflowRunnerTests.cs` | Tests for workflow phase orchestration |
| `src/GeminiCode.Tests/ContextProcessorUploadTests.cs` | Tests for enhanced @file syntax parsing |

### Modified Files
| File | Changes |
|------|---------|
| `src/GeminiCode/Browser/BrowserBridge.cs` | Add `UploadFileAsync()` method |
| `src/GeminiCode/Agent/ContextProcessor.cs` | Enhanced @file L-syntax, @upload tag |
| `src/GeminiCode/Agent/SystemPrompt.cs` | Profile-aware prompt composition |
| `src/GeminiCode/Agent/AgentOrchestrator.cs` | Integrate SessionContext, AgentProfile, WorkflowRunner |
| `src/GeminiCode/Agent/ConversationManager.cs` | Expose session state for context logging |
| `src/GeminiCode/Cli/CommandHandler.cs` | New commands: /agent, /save, /restore, /context, /simplify, /brainstorm |
| `src/GeminiCode/Cli/CliEngine.cs` | Wire workflow commands, pass new dependencies |
| `src/GeminiCode/Program.cs` | Initialize AgentProfile, SessionContext, WorkflowRunner; load GEMINI.md |
| `src/GeminiCode/GeminiCode.csproj` | Add EmbeddedResource for profile .md files |

---

## Task 1: Agent Profile System

**Files:**
- Create: `src/GeminiCode/Agent/AgentProfile.cs`
- Create: `src/GeminiCode/Agent/Profiles/general.md`
- Create: `src/GeminiCode/Agent/Profiles/code-reviewer.md`
- Create: `src/GeminiCode/Agent/Profiles/architect.md`
- Create: `src/GeminiCode/Agent/Profiles/debugger.md`
- Create: `src/GeminiCode/Agent/Profiles/refactorer.md`
- Modify: `src/GeminiCode/GeminiCode.csproj`
- Test: `src/GeminiCode.Tests/AgentProfileTests.cs`

- [ ] **Step 1: Write failing tests for AgentProfile**

```csharp
// src/GeminiCode.Tests/AgentProfileTests.cs
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
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/GeminiCode.Tests/ --filter "FullyQualifiedName~AgentProfileTests" -v minimal`
Expected: Build failure — `AgentProfile` class doesn't exist yet.

- [ ] **Step 3: Create the profile markdown files**

```markdown
<!-- src/GeminiCode/Agent/Profiles/general.md -->
# Role
You are a senior software engineer working in an automated coding environment. You are concise, direct, and action-oriented. You do the work — you don't explain how to do the work.

# Focus
- Implement features quickly and correctly
- Follow existing code patterns and conventions
- Write clean, maintainable code
- Fix bugs efficiently with root cause analysis

# Behavior
- Act immediately on requests — don't ask for confirmation unless genuinely ambiguous
- Read files before editing to understand context
- Prefer surgical edits over full file rewrites
- Run commands to verify your work
```

```markdown
<!-- src/GeminiCode/Agent/Profiles/code-reviewer.md -->
# Role
You are a senior code reviewer in an automated coding environment. Your job is to find bugs, security vulnerabilities, and quality issues in code.

# Focus
- Security vulnerabilities (injection, XSS, auth bypass, path traversal)
- Logic errors and edge cases
- Performance anti-patterns (N+1 queries, unnecessary allocations, blocking calls)
- Code quality (duplication, unclear naming, missing error handling)
- Test coverage gaps

# Behavior
- Always read the full file before commenting
- Cite specific line numbers in your findings
- Suggest concrete fixes, don't just flag problems
- Prioritize: security > correctness > performance > style
- Search for similar patterns across the codebase before suggesting a fix
```

```markdown
<!-- src/GeminiCode/Agent/Profiles/architect.md -->
# Role
You are a software architect in an automated coding environment. Your job is to design systems, evaluate trade-offs, and guide high-level decisions.

# Focus
- System decomposition and component boundaries
- Data flow and interface design
- Scalability and maintainability trade-offs
- Technology selection and migration strategies
- Dependency management and coupling reduction

# Behavior
- Explore the codebase structure before proposing changes
- Propose 2-3 approaches with trade-offs before recommending one
- Consider backwards compatibility and migration paths
- Think about testing strategies for proposed designs
- Document architectural decisions with rationale
```

```markdown
<!-- src/GeminiCode/Agent/Profiles/debugger.md -->
# Role
You are a debugging specialist in an automated coding environment. Your job is to diagnose issues, find root causes, and implement reliable fixes.

# Focus
- Root cause analysis over symptom treatment
- Reproducing issues before fixing them
- Stack trace and error message interpretation
- State inspection and data flow tracing
- Regression prevention via targeted tests

# Behavior
- Read error messages and logs carefully before acting
- Search for related issues in the codebase (grep for similar patterns)
- Check git blame/log to understand when the issue was introduced
- Write a failing test that reproduces the bug before fixing it
- Verify the fix works by running the relevant tests
```

```markdown
<!-- src/GeminiCode/Agent/Profiles/refactorer.md -->
# Role
You are a refactoring specialist in an automated coding environment. Your job is to improve code structure without changing behavior.

# Focus
- Reducing code duplication (DRY)
- Improving naming and readability
- Extracting shared utilities and abstractions
- Simplifying complex conditionals and control flow
- Breaking large files/methods into focused units

# Behavior
- Read the full context before refactoring — understand all callers
- Make one refactoring at a time, verify tests pass after each
- Preserve existing behavior exactly — refactoring is not feature work
- Search for all usages before renaming or moving code
- Prefer small, incremental changes over big-bang rewrites
```

- [ ] **Step 4: Update .csproj to embed profile files**

Add to `src/GeminiCode/GeminiCode.csproj` inside an `<ItemGroup>`:

```xml
  <ItemGroup>
    <EmbeddedResource Include="Agent\Profiles\*.md" />
  </ItemGroup>
```

- [ ] **Step 5: Implement AgentProfile class**

```csharp
// src/GeminiCode/Agent/AgentProfile.cs
using System.Reflection;

namespace GeminiCode.Agent;

public class AgentProfile
{
    private readonly string? _workingDirectory;
    private string _activeProfile = "general";

    // Built-in profile names (embedded resources)
    private static readonly string[] BuiltInProfiles =
        { "general", "code-reviewer", "architect", "debugger", "refactorer" };

    public string ActiveProfileName => _activeProfile;

    public AgentProfile(string? workingDirectory = null)
    {
        _workingDirectory = workingDirectory;
    }

    /// <summary>List all available profiles (built-in + project-local).</summary>
    public List<string> ListProfiles()
    {
        var profiles = new List<string>(BuiltInProfiles);

        // Discover project-local profiles in .gemini/agents/
        if (_workingDirectory != null)
        {
            var agentDir = Path.Combine(_workingDirectory, ".gemini", "agents");
            if (Directory.Exists(agentDir))
            {
                foreach (var file in Directory.GetFiles(agentDir, "*.md"))
                {
                    var name = Path.GetFileNameWithoutExtension(file);
                    if (!profiles.Contains(name, StringComparer.OrdinalIgnoreCase))
                        profiles.Add(name);
                }
            }
        }

        return profiles;
    }

    /// <summary>Set the active profile. Returns false if profile not found.</summary>
    public bool SetActive(string name)
    {
        // Check project-local first (overrides built-in)
        if (_workingDirectory != null)
        {
            var localPath = Path.Combine(_workingDirectory, ".gemini", "agents", $"{name}.md");
            if (File.Exists(localPath))
            {
                _activeProfile = name;
                return true;
            }
        }

        // Check built-in
        if (BuiltInProfiles.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            _activeProfile = name;
            return true;
        }

        return false;
    }

    /// <summary>Get the content of the active profile.</summary>
    public string GetActiveProfileContent()
    {
        // Check project-local first
        if (_workingDirectory != null)
        {
            var localPath = Path.Combine(_workingDirectory, ".gemini", "agents", $"{_activeProfile}.md");
            if (File.Exists(localPath))
                return File.ReadAllText(localPath);
        }

        // Load from embedded resource
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = $"GeminiCode.Agent.Profiles.{_activeProfile.Replace("-", "_")}.md";

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
            return ""; // Fallback: empty profile

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Load GEMINI.md from working directory root. Returns null if not found.</summary>
    public string? GetGeminiMdContent()
    {
        if (_workingDirectory == null) return null;

        var path = Path.Combine(_workingDirectory, "GEMINI.md");
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    /// <summary>Update the working directory (e.g., after /cd). Re-reads GEMINI.md.</summary>
    public void UpdateWorkingDirectory(string newDir)
    {
        // _workingDirectory is readonly via constructor, so we use reflection or make it mutable
        // For simplicity, we'll use a field
    }
}
```

**Note:** Make `_workingDirectory` a non-readonly field so `UpdateWorkingDirectory` can update it:

```csharp
    private string? _workingDirectory;

    public void UpdateWorkingDirectory(string newDir)
    {
        _workingDirectory = newDir;
    }
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test src/GeminiCode.Tests/ --filter "FullyQualifiedName~AgentProfileTests" -v minimal`
Expected: All 7 tests pass.

- [ ] **Step 7: Commit**

```bash
git add src/GeminiCode/Agent/AgentProfile.cs src/GeminiCode/Agent/Profiles/ src/GeminiCode/GeminiCode.csproj src/GeminiCode.Tests/AgentProfileTests.cs
git commit -m "feat: agent profile system with built-in profiles and GEMINI.md support"
```

---

## Task 2: Integrate AgentProfile into SystemPrompt

**Files:**
- Modify: `src/GeminiCode/Agent/SystemPrompt.cs`
- Modify: `src/GeminiCode/Agent/AgentOrchestrator.cs:24-38,41-69`
- Modify: `src/GeminiCode/Program.cs:117-123`

- [ ] **Step 1: Modify SystemPrompt to accept profile and GEMINI.md content**

Replace the `GenerateTemplate` method signature at `src/GeminiCode/Agent/SystemPrompt.cs:6` to accept additional parameters. Add a new overload:

```csharp
    /// <summary>Generate system prompt composed from base + profile + GEMINI.md.</summary>
    public static string Generate(string workingDirectory, string profileContent, string? geminiMdContent)
    {
        var basePrompt = GenerateTemplate(workingDirectory);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine(basePrompt);

        if (!string.IsNullOrWhiteSpace(profileContent))
        {
            sb.AppendLine();
            sb.AppendLine("## Agent Profile");
            sb.AppendLine(profileContent);
        }

        if (!string.IsNullOrWhiteSpace(geminiMdContent))
        {
            sb.AppendLine();
            sb.AppendLine("## Project Instructions (GEMINI.md)");
            sb.AppendLine(geminiMdContent);
        }

        return sb.ToString();
    }
```

- [ ] **Step 2: Add AgentProfile to AgentOrchestrator constructor**

Modify `src/GeminiCode/Agent/AgentOrchestrator.cs`. Add field and constructor parameter:

```csharp
    private readonly AgentProfile _profile;

    public AgentOrchestrator(
        BrowserBridge browser,
        ToolRegistry tools,
        PermissionGate permissionGate,
        ConversationManager conversation,
        AppSettings settings,
        Tools.PathSandbox sandbox,
        AgentProfile profile)
    {
        _browser = browser;
        _tools = tools;
        _permissionGate = permissionGate;
        _conversation = conversation;
        _settings = settings;
        _sandbox = sandbox;
        _profile = profile;
    }
```

- [ ] **Step 3: Update InitializeSessionAsync to use profile-aware prompt**

In `src/GeminiCode/Agent/AgentOrchestrator.cs:57`, change:

```csharp
// Old:
await _browser.SendMessageAsync(SystemPrompt.GenerateTemplate(_sandbox.WorkingDirectory));

// New:
var profileContent = _profile.GetActiveProfileContent();
var geminiMd = _profile.GetGeminiMdContent();
await _browser.SendMessageAsync(SystemPrompt.Generate(_sandbox.WorkingDirectory, profileContent, geminiMd));
```

- [ ] **Step 4: Update Program.cs to create and pass AgentProfile**

In `src/GeminiCode/Program.cs`, after the sandbox initialization (line 37), add:

```csharp
        // Initialize agent profile
        var agentProfile = new AgentProfile(workDir);
```

Update the orchestrator creation at line 118:

```csharp
// Old:
var orchestrator = new AgentOrchestrator(browser, toolRegistry, permissionGate, conversation, settings, sandbox);

// New:
var orchestrator = new AgentOrchestrator(browser, toolRegistry, permissionGate, conversation, settings, sandbox, agentProfile);
```

- [ ] **Step 5: Build and verify**

Run: `dotnet build src/GeminiCode/`
Expected: Build succeeds.

- [ ] **Step 6: Commit**

```bash
git add src/GeminiCode/Agent/SystemPrompt.cs src/GeminiCode/Agent/AgentOrchestrator.cs src/GeminiCode/Program.cs
git commit -m "feat: integrate agent profiles into system prompt composition"
```

---

## Task 3: Add /agent Command to CommandHandler

**Files:**
- Modify: `src/GeminiCode/Cli/CommandHandler.cs`

- [ ] **Step 1: Add AgentProfile dependency to CommandHandler**

In `src/GeminiCode/Cli/CommandHandler.cs`, add field and constructor parameter:

```csharp
    private readonly AgentProfile _profile;

    public CommandHandler(
        BrowserBridge browser,
        ConversationManager conversation,
        SessionAllowlist allowlist,
        PathSandbox sandbox,
        AgentProfile profile)
    {
        _browser = browser;
        _conversation = conversation;
        _allowlist = allowlist;
        _sandbox = sandbox;
        _profile = profile;
    }
```

- [ ] **Step 2: Add /agent case to switch statement**

In the `TryHandleAsync` switch at line 38, add before the `default` case:

```csharp
            case "/agent":
                await HandleAgentAsync(arg);
                return true;
```

- [ ] **Step 3: Implement HandleAgentAsync**

Add to `CommandHandler.cs`:

```csharp
    private async Task HandleAgentAsync(string? arg)
    {
        if (string.IsNullOrWhiteSpace(arg) || arg == "list")
        {
            // List available profiles
            var profiles = _profile.ListProfiles();
            Console.WriteLine($"{AnsiHelper.Bold}Available agent profiles:{AnsiHelper.Reset}");
            foreach (var name in profiles)
            {
                var marker = name == _profile.ActiveProfileName ? $" {AnsiHelper.Green}(active){AnsiHelper.Reset}" : "";
                Console.WriteLine($"  - {name}{marker}");
            }
            Console.WriteLine($"\n  Usage: /agent <name> — switch profile (starts new chat)");
            return;
        }

        if (arg == "info")
        {
            Console.WriteLine($"{AnsiHelper.Bold}Active profile:{AnsiHelper.Reset} {_profile.ActiveProfileName}");
            var content = _profile.GetActiveProfileContent();
            Console.WriteLine(content);
            var geminiMd = _profile.GetGeminiMdContent();
            if (geminiMd != null)
                Console.WriteLine($"\n{AnsiHelper.Bold}GEMINI.md:{AnsiHelper.Reset} loaded ({geminiMd.Length} chars)");
            else
                Console.WriteLine($"\n{AnsiHelper.Dim}No GEMINI.md found in working directory.{AnsiHelper.Reset}");
            return;
        }

        // Switch profile
        if (!_profile.SetActive(arg))
        {
            Console.WriteLine($"{AnsiHelper.Red}Profile '{arg}' not found.{AnsiHelper.Reset}");
            Console.WriteLine("Available: " + string.Join(", ", _profile.ListProfiles()));
            return;
        }

        Console.WriteLine($"{AnsiHelper.Green}Switched to profile: {arg}{AnsiHelper.Reset}");
        // Start new chat with new profile
        await _browser.StartNewChatAsync();
        await _browser.WaitForPageSettleAsync();
        _conversation.Reset();
        Console.WriteLine($"{AnsiHelper.Green}New conversation started with {arg} profile.{AnsiHelper.Reset}");
    }
```

- [ ] **Step 4: Update CommandHandler construction in Program.cs**

In `src/GeminiCode/Program.cs:122`:

```csharp
// Old:
var commands = new CommandHandler(browser, conversation, allowlist, sandbox);

// New:
var commands = new CommandHandler(browser, conversation, allowlist, sandbox, agentProfile);
```

- [ ] **Step 5: Update /help text**

In `CommandHandler.PrintHelp()`, add after the `/paste` line:

```csharp
              /agent [name]    — List or switch agent profiles
```

- [ ] **Step 6: Build and verify**

Run: `dotnet build src/GeminiCode/`
Expected: Build succeeds.

- [ ] **Step 7: Commit**

```bash
git add src/GeminiCode/Cli/CommandHandler.cs src/GeminiCode/Program.cs
git commit -m "feat: /agent command for listing and switching agent profiles"
```

---

## Task 4: Session Context System

**Files:**
- Create: `src/GeminiCode/Agent/SessionContext.cs`
- Test: `src/GeminiCode.Tests/SessionContextTests.cs`

- [ ] **Step 1: Write failing tests for SessionContext**

```csharp
// src/GeminiCode.Tests/SessionContextTests.cs
namespace GeminiCode.Tests;

using GeminiCode.Agent;

public class SessionContextTests
{
    private string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sessionctx_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void LogToolCall_RecordsToolExecution()
    {
        var ctx = new SessionContext(CreateTempDir(), "general");
        ctx.LogToolCall("ReadFile", "src/App.cs", true, 1);
        var md = ctx.GenerateMarkdown();
        Assert.Contains("ReadFile", md);
        Assert.Contains("src/App.cs", md);
    }

    [Fact]
    public void LogFileModified_TracksModifiedFiles()
    {
        var ctx = new SessionContext(CreateTempDir(), "general");
        ctx.LogFileModified("src/App.cs", "edited lines 10-50");
        var md = ctx.GenerateMarkdown();
        Assert.Contains("src/App.cs", md);
        Assert.Contains("edited lines 10-50", md);
    }

    [Fact]
    public void LogDecision_RecordsKeyDecision()
    {
        var ctx = new SessionContext(CreateTempDir(), "general");
        ctx.LogDecision("Using repository pattern for data access");
        var md = ctx.GenerateMarkdown();
        Assert.Contains("repository pattern", md);
    }

    [Fact]
    public void SaveToFile_CreatesGeminiDirectory()
    {
        var tempDir = CreateTempDir();
        var ctx = new SessionContext(tempDir, "general");
        ctx.LogDecision("Test decision");
        ctx.SaveToFile();

        var filePath = Path.Combine(tempDir, ".gemini", "session-context.md");
        Assert.True(File.Exists(filePath));
        var content = File.ReadAllText(filePath);
        Assert.Contains("Test decision", content);
    }

    [Fact]
    public void GenerateMarkdown_IncludesHeader()
    {
        var ctx = new SessionContext(CreateTempDir(), "code-reviewer");
        var md = ctx.GenerateMarkdown();
        Assert.Contains("# Session Context", md);
        Assert.Contains("code-reviewer", md);
    }

    [Fact]
    public void AddConversationSummary_AppendsToSummary()
    {
        var ctx = new SessionContext(CreateTempDir(), "general");
        ctx.AddConversationSummary("User asked to implement auth. Decided on JWT tokens.");
        var md = ctx.GenerateMarkdown();
        Assert.Contains("JWT tokens", md);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/GeminiCode.Tests/ --filter "FullyQualifiedName~SessionContextTests" -v minimal`
Expected: Build failure — `SessionContext` class doesn't exist yet.

- [ ] **Step 3: Implement SessionContext**

```csharp
// src/GeminiCode/Agent/SessionContext.cs
using System.Text;

namespace GeminiCode.Agent;

public class SessionContext
{
    private readonly string _workingDirectory;
    private readonly string _agentProfile;
    private readonly DateTime _startTime;
    private readonly List<string> _toolCalls = new();
    private readonly List<string> _modifiedFiles = new();
    private readonly List<string> _decisions = new();
    private readonly List<string> _conversationSummaries = new();

    public SessionContext(string workingDirectory, string agentProfile)
    {
        _workingDirectory = workingDirectory;
        _agentProfile = agentProfile;
        _startTime = DateTime.Now;
    }

    public void LogToolCall(string toolName, string target, bool success, int turn)
    {
        var status = success ? "success" : "failed";
        _toolCalls.Add($"- {toolName}: {target} (turn {turn}, {status})");
    }

    public void LogFileModified(string path, string description)
    {
        _modifiedFiles.Add($"- {path} ({description})");
    }

    public void LogDecision(string decision)
    {
        _decisions.Add($"- {decision}");
    }

    public void AddConversationSummary(string summary)
    {
        _conversationSummaries.Add(summary);
    }

    public string GenerateMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Session Context");
        sb.AppendLine($"- **Started:** {_startTime:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"- **Agent:** {_agentProfile}");
        sb.AppendLine($"- **Working Directory:** {_workingDirectory.Replace("\\", "/")}");

        if (_modifiedFiles.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Modified Files");
            foreach (var entry in _modifiedFiles)
                sb.AppendLine(entry);
        }

        if (_decisions.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Key Decisions");
            foreach (var entry in _decisions)
                sb.AppendLine(entry);
        }

        if (_toolCalls.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Tool Call Summary");
            foreach (var entry in _toolCalls)
                sb.AppendLine(entry);
        }

        if (_conversationSummaries.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Conversation Summary");
            foreach (var entry in _conversationSummaries)
                sb.AppendLine(entry);
        }

        return sb.ToString();
    }

    public void SaveToFile()
    {
        var dir = Path.Combine(_workingDirectory, ".gemini");
        Directory.CreateDirectory(dir);
        var filePath = Path.Combine(dir, "session-context.md");
        File.WriteAllText(filePath, GenerateMarkdown());
    }

    public string GetFilePath()
    {
        return Path.Combine(_workingDirectory, ".gemini", "session-context.md");
    }

    public static bool HasSavedContext(string workingDirectory)
    {
        return File.Exists(Path.Combine(workingDirectory, ".gemini", "session-context.md"));
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/GeminiCode.Tests/ --filter "FullyQualifiedName~SessionContextTests" -v minimal`
Expected: All 6 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/GeminiCode/Agent/SessionContext.cs src/GeminiCode.Tests/SessionContextTests.cs
git commit -m "feat: session context system for preserving state between chats"
```

---

## Task 5: Integrate SessionContext into AgentOrchestrator

**Files:**
- Modify: `src/GeminiCode/Agent/AgentOrchestrator.cs`
- Modify: `src/GeminiCode/Program.cs`

- [ ] **Step 1: Add SessionContext field and constructor parameter to AgentOrchestrator**

In `src/GeminiCode/Agent/AgentOrchestrator.cs`, add:

```csharp
    private readonly SessionContext _sessionContext;
```

Update constructor to accept and store it:

```csharp
    public AgentOrchestrator(
        BrowserBridge browser,
        ToolRegistry tools,
        PermissionGate permissionGate,
        ConversationManager conversation,
        AppSettings settings,
        Tools.PathSandbox sandbox,
        AgentProfile profile,
        SessionContext sessionContext)
    {
        _browser = browser;
        _tools = tools;
        _permissionGate = permissionGate;
        _conversation = conversation;
        _settings = settings;
        _sandbox = sandbox;
        _profile = profile;
        _sessionContext = sessionContext;
    }
```

- [ ] **Step 2: Log tool calls in ExecuteToolCallsAsync**

In `src/GeminiCode/Agent/AgentOrchestrator.cs`, inside `ExecuteToolCallsAsync`, after `var toolResult = await tool.ExecuteAsync(...)` (around line 405), add:

```csharp
            // Log to session context
            var target = toolCall.Parameters.TryGetValue("path", out var pathEl) ? pathEl.ToString() : toolCall.Name;
            _sessionContext.LogToolCall(toolCall.Name, target, toolResult.Success, _conversation.TurnCount);

            // Track file modifications
            if (toolResult.Success && toolCall.Name is "WriteFile" or "EditFile")
            {
                var filePath = toolCall.Parameters.TryGetValue("path", out var p) ? p.GetString() ?? "" : "";
                var desc = toolCall.Name == "WriteFile" ? "created/overwritten" : "edited";
                _sessionContext.LogFileModified(filePath, desc);
            }
```

- [ ] **Step 3: Auto-save context periodically**

In `ProcessResponseAsync`, after processing text content (around line 229), add decision detection:

```csharp
            // Auto-detect decisions in response text
            if (parsed.TextContent != null)
            {
                var decisionPatterns = new[] { "decided ", "chose ", "going with ", "approach:", "decision:", "we'll use ", "selected " };
                foreach (var line in parsed.TextContent.Split('\n'))
                {
                    var lower = line.ToLowerInvariant().Trim();
                    if (decisionPatterns.Any(p => lower.Contains(p)) && line.Trim().Length > 20 && line.Trim().Length < 200)
                    {
                        _sessionContext.LogDecision(line.Trim());
                        break; // One decision per response
                    }
                }
            }
```

- [ ] **Step 4: Update Program.cs to create and pass SessionContext**

In `src/GeminiCode/Program.cs`, after `agentProfile` initialization:

```csharp
        var sessionContext = new SessionContext(workDir, agentProfile.ActiveProfileName);
```

Update orchestrator creation:

```csharp
var orchestrator = new AgentOrchestrator(browser, toolRegistry, permissionGate, conversation, settings, sandbox, agentProfile, sessionContext);
```

- [ ] **Step 5: Build and verify**

Run: `dotnet build src/GeminiCode/`
Expected: Build succeeds.

- [ ] **Step 6: Commit**

```bash
git add src/GeminiCode/Agent/AgentOrchestrator.cs src/GeminiCode/Program.cs
git commit -m "feat: integrate session context logging into orchestrator"
```

---

## Task 6: Add /save, /restore, /context Commands

**Files:**
- Modify: `src/GeminiCode/Cli/CommandHandler.cs`

- [ ] **Step 1: Add SessionContext dependency to CommandHandler**

Add field:

```csharp
    private readonly SessionContext _sessionContext;
```

Update constructor:

```csharp
    public CommandHandler(
        BrowserBridge browser,
        ConversationManager conversation,
        SessionAllowlist allowlist,
        PathSandbox sandbox,
        AgentProfile profile,
        SessionContext sessionContext)
    {
        _browser = browser;
        _conversation = conversation;
        _allowlist = allowlist;
        _sandbox = sandbox;
        _profile = profile;
        _sessionContext = sessionContext;
    }
```

- [ ] **Step 2: Add command cases to TryHandleAsync switch**

```csharp
            case "/save":
                HandleSave();
                return true;
            case "/restore":
                await HandleRestoreAsync();
                return true;
            case "/context":
                HandleShowContext();
                return true;
```

- [ ] **Step 3: Implement the command handlers**

```csharp
    private void HandleSave()
    {
        _sessionContext.SaveToFile();
        Console.WriteLine($"{AnsiHelper.Green}Session context saved to {_sessionContext.GetFilePath()}{AnsiHelper.Reset}");
    }

    private async Task HandleRestoreAsync()
    {
        var filePath = _sessionContext.GetFilePath();
        if (!File.Exists(filePath))
        {
            Console.WriteLine($"{AnsiHelper.Yellow}No saved session context found at {filePath}{AnsiHelper.Reset}");
            return;
        }

        // Start new chat
        await _browser.StartNewChatAsync();
        await _browser.WaitForPageSettleAsync();
        _conversation.Reset();

        // Upload the context file
        Console.WriteLine($"{AnsiHelper.Dim}Uploading session context...{AnsiHelper.Reset}");
        await _browser.UploadFileAsync(filePath);
        await Task.Delay(1000); // Wait for upload to register

        Console.WriteLine($"{AnsiHelper.Green}Session context restored. New chat initialized with previous context.{AnsiHelper.Reset}");
    }

    private void HandleShowContext()
    {
        var md = _sessionContext.GenerateMarkdown();
        Console.WriteLine(md);
    }
```

- [ ] **Step 4: Update /help text**

Add these lines to PrintHelp:

```csharp
              /save            — Save session context to .gemini/session-context.md
              /restore         — Restore previous session context in new chat
              /context         — Show current session context
```

- [ ] **Step 5: Update Program.cs constructor call**

```csharp
var commands = new CommandHandler(browser, conversation, allowlist, sandbox, agentProfile, sessionContext);
```

- [ ] **Step 6: Build and verify**

Run: `dotnet build src/GeminiCode/`
Expected: Build succeeds.

- [ ] **Step 7: Commit**

```bash
git add src/GeminiCode/Cli/CommandHandler.cs src/GeminiCode/Program.cs
git commit -m "feat: /save, /restore, /context commands for session context management"
```

---

## Task 7: File Upload via WebView2

**Files:**
- Modify: `src/GeminiCode/Browser/BrowserBridge.cs`

- [ ] **Step 1: Add UploadFileAsync to BrowserBridge**

Add this method to `src/GeminiCode/Browser/BrowserBridge.cs` after `SendMessageAsync` (after line 261):

```csharp
    /// <summary>Upload a file to Gemini via the file input element or drag-drop simulation.</summary>
    public async Task<bool> UploadFileAsync(string filePath)
    {
        if (!File.Exists(filePath))
            return false;

        var absolutePath = Path.GetFullPath(filePath).Replace("\\", "/");
        var fileName = Path.GetFileName(filePath);
        var mimeType = GetMimeType(fileName);

        // Read file as base64 for injection
        var bytes = await File.ReadAllBytesAsync(filePath);
        var base64 = Convert.ToBase64String(bytes);

        // Strategy: Find file input element, create a File object from base64, set it
        var uploadScript = $$"""
            (async function() {
                // Convert base64 to File object
                var base64 = '{{base64}}';
                var byteChars = atob(base64);
                var byteArray = new Uint8Array(byteChars.length);
                for (var i = 0; i < byteChars.length; i++) {
                    byteArray[i] = byteChars.charCodeAt(i);
                }
                var blob = new Blob([byteArray], { type: '{{mimeType}}' });
                var file = new File([blob], '{{EscapeJs(fileName)}}', { type: '{{mimeType}}' });

                // Strategy 1: Find the file input element and set files
                var fileInputs = document.querySelectorAll('input[type="file"]');
                if (fileInputs.length > 0) {
                    var dt = new DataTransfer();
                    dt.items.add(file);
                    fileInputs[0].files = dt.files;
                    fileInputs[0].dispatchEvent(new Event('change', { bubbles: true }));
                    return 'input_set';
                }

                // Strategy 2: Find upload/attach button and click it, then set via created input
                var attachBtns = document.querySelectorAll('[aria-label*="upload" i], [aria-label*="attach" i], [aria-label*="file" i], [data-tooltip*="upload" i], [data-tooltip*="attach" i]');
                for (var btn of attachBtns) {
                    btn.click();
                    await new Promise(r => setTimeout(r, 500));
                    var newInputs = document.querySelectorAll('input[type="file"]');
                    if (newInputs.length > 0) {
                        var dt2 = new DataTransfer();
                        dt2.items.add(file);
                        newInputs[newInputs.length - 1].files = dt2.files;
                        newInputs[newInputs.length - 1].dispatchEvent(new Event('change', { bubbles: true }));
                        return 'button_then_input';
                    }
                }

                // Strategy 3: Drag-drop simulation on the chat input area
                var editor = document.querySelector("{{EscapeJs(_selectors.ChatInput)}}");
                if (editor) {
                    var dt3 = new DataTransfer();
                    dt3.items.add(file);
                    var dropEvent = new DragEvent('drop', {
                        bubbles: true,
                        cancelable: true,
                        dataTransfer: dt3
                    });
                    editor.dispatchEvent(new DragEvent('dragenter', { bubbles: true, dataTransfer: dt3 }));
                    editor.dispatchEvent(new DragEvent('dragover', { bubbles: true, dataTransfer: dt3 }));
                    editor.dispatchEvent(dropEvent);
                    return 'drop_simulated';
                }

                return 'no_upload_found';
            })()
            """;

        var result = await InvokeOnStaAsync(() =>
            _window!.WebView.CoreWebView2.ExecuteScriptAsync(uploadScript));

        var status = result.Trim('"');
        return status != "no_upload_found";
    }

    private static string GetMimeType(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".txt" or ".md" or ".csv" => "text/plain",
            ".json" => "application/json",
            ".xml" => "application/xml",
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".svg" => "image/svg+xml",
            ".cs" or ".py" or ".js" or ".ts" or ".java" or ".go" or ".rs" or ".rb" or ".cpp" or ".c" or ".h" => "text/plain",
            ".html" => "text/html",
            ".css" => "text/css",
            ".yaml" or ".yml" => "text/yaml",
            _ => "application/octet-stream"
        };
    }
```

- [ ] **Step 2: Build and verify**

Run: `dotnet build src/GeminiCode/`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/GeminiCode/Browser/BrowserBridge.cs
git commit -m "feat: file upload via WebView2 with multi-strategy approach"
```

---

## Task 8: Enhanced @file Syntax and @upload Tag

**Files:**
- Modify: `src/GeminiCode/Agent/ContextProcessor.cs`
- Test: `src/GeminiCode.Tests/ContextProcessorUploadTests.cs`

- [ ] **Step 1: Write failing tests for new syntax**

```csharp
// src/GeminiCode.Tests/ContextProcessorUploadTests.cs
namespace GeminiCode.Tests;

using GeminiCode.Agent;
using GeminiCode.Tools;

public class ContextProcessorUploadTests
{
    private ContextProcessor CreateProcessor(string workDir)
    {
        return new ContextProcessor(new PathSandbox(workDir));
    }

    [Fact]
    public void FileWithLSyntax_ParsesLineRange()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ctx_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var testFile = Path.Combine(tempDir, "test.cs");
            File.WriteAllLines(testFile, Enumerable.Range(1, 500).Select(i => $"Line {i} content"));

            var processor = CreateProcessor(tempDir);
            var (expanded, count) = processor.Process("check @file test.cs L200-L300");
            Assert.Equal(1, count);
            Assert.Contains("Line 200 content", expanded);
            Assert.Contains("Line 300 content", expanded);
            Assert.DoesNotContain("Line 100 content", expanded);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void FileWithLSyntax_SingleLine()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ctx_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var testFile = Path.Combine(tempDir, "test.cs");
            File.WriteAllLines(testFile, Enumerable.Range(1, 500).Select(i => $"Line {i} content"));

            var processor = CreateProcessor(tempDir);
            var (expanded, count) = processor.Process("check @file test.cs L200");
            Assert.Equal(1, count);
            Assert.Contains("Line 200 content", expanded);
            Assert.Contains("Line 500 content", expanded);
            Assert.DoesNotContain("Line 100 content", expanded);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void UploadTag_DetectedAsUploadRequest()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ctx_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var testFile = Path.Combine(tempDir, "test.txt");
            File.WriteAllText(testFile, "hello world");

            var processor = CreateProcessor(tempDir);
            var (expanded, count) = processor.Process("analyze @upload test.txt");
            Assert.Equal(1, count);
            // Upload requests are tracked in pending uploads, not text-injected
            Assert.Contains("test.txt", expanded);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void FileWithoutLineRange_MarkedForUpload()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ctx_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var testFile = Path.Combine(tempDir, "test.txt");
            File.WriteAllText(testFile, "hello world");

            var processor = CreateProcessor(tempDir);
            var (expanded, count) = processor.Process("analyze @file test.txt");
            Assert.Equal(1, count);
            // Without line range, file should be queued for upload
            Assert.True(processor.PendingUploads.Count > 0);
            Assert.Contains(processor.PendingUploads, u => u.Contains("test.txt"));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/GeminiCode.Tests/ --filter "FullyQualifiedName~ContextProcessorUploadTests" -v minimal`
Expected: Build failure — `PendingUploads` property doesn't exist, L-syntax not parsed.

- [ ] **Step 3: Update ContextProcessor — add PendingUploads and new regex**

In `src/GeminiCode/Agent/ContextProcessor.cs`, add a field after the class declaration:

```csharp
    /// <summary>Files queued for upload (populated during Process, consumed by caller).</summary>
    public List<string> PendingUploads { get; } = new();
```

Replace the `FilePattern` regex at line 30:

```csharp
    // @file path/to/file.cs
    // @file path/to/file.cs L200-L500
    // @file path/to/file.cs L200
    // @file path/to/file.cs:10-50 (legacy syntax still supported)
    private static readonly Regex FilePattern = new(
        @"@file\s+(\S+?)(?:\s+L(\d+)(?:-L(\d+))?|:(\d+)(?:-(\d+))?)?(?=\s|$)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // @upload path/to/file (always uploads, never text-injects)
    private static readonly Regex UploadPattern = new(
        @"@upload\s+(\S+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
```

- [ ] **Step 4: Update Process method to handle new syntax**

In `ContextProcessor.Process()`, clear pending uploads at the start:

```csharp
        PendingUploads.Clear();
```

Replace the @file processing block (lines 79-85) with:

```csharp
        // Process @file references
        processed = FilePattern.Replace(processed, m =>
        {
            var filePath = m.Groups[1].Value;
            // Check for L-syntax line range
            var hasLRange = m.Groups[2].Success;
            // Check for legacy colon syntax
            var hasColonRange = m.Groups[4].Success;

            if (hasLRange)
            {
                // L200-L500 or L200 — text injection
                var result = ExpandFile(filePath, m.Groups[2].Value, m.Groups[3].Value);
                if (result != null)
                    contexts.Add(($"@file {filePath}", result));
            }
            else if (hasColonRange)
            {
                // Legacy :10-50 syntax — text injection
                var result = ExpandFile(filePath, m.Groups[4].Value, m.Groups[5].Value);
                if (result != null)
                    contexts.Add(($"@file {filePath}", result));
            }
            else
            {
                // No line range — queue for upload
                try
                {
                    var resolved = _sandbox.Resolve(filePath);
                    if (File.Exists(resolved))
                    {
                        PendingUploads.Add(resolved);
                        contexts.Add(($"@file {filePath}", $"[File queued for upload: {filePath}]"));
                    }
                    else
                    {
                        contexts.Add(($"@file {filePath}", $"[File not found: {filePath}]"));
                    }
                }
                catch (SandboxViolationException)
                {
                    contexts.Add(($"@file {filePath}", $"[Access denied: {filePath} is outside working directory]"));
                }
            }
            return "";
        });

        // Process @upload references
        processed = UploadPattern.Replace(processed, m =>
        {
            var filePath = m.Groups[1].Value;
            try
            {
                var resolved = _sandbox.Resolve(filePath);
                if (File.Exists(resolved))
                {
                    PendingUploads.Add(resolved);
                    contexts.Add(($"@upload {filePath}", $"[File queued for upload: {filePath}]"));
                }
                else
                {
                    contexts.Add(($"@upload {filePath}", $"[File not found: {filePath}]"));
                }
            }
            catch (SandboxViolationException)
            {
                contexts.Add(($"@upload {filePath}", $"[Access denied: {filePath} is outside working directory]"));
            }
            return "";
        });
```

- [ ] **Step 5: Update /help text in ContextProcessor.GetHelpText()**

Replace the @file help lines:

```csharp
    public static string GetHelpText()
    {
        return """
              @file <path>           — Upload file to Gemini
              @file <path> L200-L500 — Attach specific line range as text
              @file <path> L200      — Attach from line 200 onwards as text
              @file <path>:10-50     — Attach line range (legacy syntax)
              @upload <path>         — Upload file to Gemini (explicit)
              @tree [path] [depth=N] — Attach directory tree
              @git <status|diff|log|blame|branch> [args] — Attach git info
              @diff [args]           — Shorthand for @git diff
              @grep <pattern> [include=*.ext] — Attach search results
              @find <glob-pattern>   — Attach file listing
              @codebase              — Attach project overview
            """;
    }
```

- [ ] **Step 6: Update CliEngine to handle pending uploads**

In `src/GeminiCode/Cli/CliEngine.cs`, after context expansion (around line 68), add upload handling:

```csharp
            // Upload any pending files
            if (_contextProcessor.PendingUploads.Count > 0)
            {
                foreach (var uploadPath in _contextProcessor.PendingUploads)
                {
                    Console.WriteLine($"{AnsiHelper.Cyan}Uploading: {Path.GetFileName(uploadPath)}{AnsiHelper.Reset}");
                    var uploaded = await _browser.UploadFileAsync(uploadPath);
                    if (!uploaded)
                        Console.WriteLine($"{AnsiHelper.Yellow}Upload may not have succeeded for {Path.GetFileName(uploadPath)}{AnsiHelper.Reset}");
                }
                await Task.Delay(500); // Let uploads register before sending message
            }
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test src/GeminiCode.Tests/ --filter "FullyQualifiedName~ContextProcessorUploadTests" -v minimal`
Expected: All 4 tests pass.

- [ ] **Step 8: Build full project**

Run: `dotnet build src/GeminiCode/`
Expected: Build succeeds.

- [ ] **Step 9: Commit**

```bash
git add src/GeminiCode/Agent/ContextProcessor.cs src/GeminiCode/Cli/CliEngine.cs src/GeminiCode.Tests/ContextProcessorUploadTests.cs
git commit -m "feat: enhanced @file L-syntax and @upload tag with WebView2 file upload"
```

---

## Task 9: Workflow Runner Infrastructure

**Files:**
- Create: `src/GeminiCode/Agent/WorkflowRunner.cs`
- Test: `src/GeminiCode.Tests/WorkflowRunnerTests.cs`

- [ ] **Step 1: Write failing tests for WorkflowRunner**

```csharp
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
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/GeminiCode.Tests/ --filter "FullyQualifiedName~WorkflowRunnerTests" -v minimal`
Expected: Build failure.

- [ ] **Step 3: Implement WorkflowRunner and supporting types**

```csharp
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
                // Make previous phase results available to next phases
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
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/GeminiCode.Tests/ --filter "FullyQualifiedName~WorkflowRunnerTests" -v minimal`
Expected: All 3 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/GeminiCode/Agent/WorkflowRunner.cs src/GeminiCode.Tests/WorkflowRunnerTests.cs
git commit -m "feat: workflow runner infrastructure for multi-phase orchestration"
```

---

## Task 10: /simplify Workflow

**Files:**
- Create: `src/GeminiCode/Agent/Workflows/SimplifyWorkflow.cs`
- Modify: `src/GeminiCode/Cli/CommandHandler.cs`

- [ ] **Step 1: Create SimplifyWorkflow**

```csharp
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
                """
                Review the following diff for code reuse opportunities:

                {phase1_result}

                For each change:
                1. Search for existing utilities and helpers that could replace newly written code. Look for similar patterns elsewhere in the codebase.
                2. Flag any new function that duplicates existing functionality. Suggest the existing function to use instead.
                3. Flag any inline logic that could use an existing utility — hand-rolled string manipulation, manual path handling, custom environment checks, etc.

                Search the codebase with [GREP] to find existing patterns before making claims. Be specific — cite file paths and function names.
                """,
                "Reviewing code reuse...",
                true),

            new WorkflowPhase(
                "Code Quality Review",
                """
                Review the same changes for code quality issues:

                {phase1_result}

                Look for:
                1. Redundant state that duplicates existing state or cached values that could be derived
                2. Parameter sprawl — adding new parameters instead of restructuring
                3. Copy-paste with slight variation that should be unified
                4. Leaky abstractions — exposing internal details that should be encapsulated
                5. Stringly-typed code where constants or enums already exist in the codebase
                6. Unnecessary comments explaining WHAT (keep only non-obvious WHY)

                Be specific — cite line numbers and suggest fixes.
                """,
                "Reviewing code quality...",
                true),

            new WorkflowPhase(
                "Efficiency Review",
                """
                Review the same changes for efficiency:

                {phase1_result}

                Look for:
                1. Unnecessary work: redundant computations, repeated file reads, duplicate API calls, N+1 patterns
                2. Missed concurrency: independent operations that could run in parallel
                3. Hot-path bloat: blocking work on startup or per-request paths
                4. Recurring no-op updates: state updates that fire unconditionally without change detection
                5. Unnecessary existence checks before operating (TOCTOU anti-pattern)
                6. Memory: unbounded data structures, missing cleanup, event listener leaks
                7. Overly broad operations: reading entire files when only a portion is needed

                Be specific — cite line numbers and suggest fixes.
                """,
                "Reviewing efficiency...",
                true),

            new WorkflowPhase(
                "Applying fixes",
                """
                Here are the review findings from the previous phases:

                **Code Reuse:**
                {phase2_result}

                **Code Quality:**
                {phase3_result}

                **Efficiency:**
                {phase4_result}

                Fix each valid issue directly using [EDIT:] tags. If a finding is a false positive or not worth addressing, skip it. Do not argue with the findings — just fix or skip.
                """,
                "Applying fixes...",
                true)
        });
    }
}
```

- [ ] **Step 2: Add /simplify to CommandHandler**

Add WorkflowRunner dependency to CommandHandler:

```csharp
    private readonly WorkflowRunner _workflowRunner;
```

Update constructor to accept it:

```csharp
    public CommandHandler(
        BrowserBridge browser,
        ConversationManager conversation,
        SessionAllowlist allowlist,
        PathSandbox sandbox,
        AgentProfile profile,
        SessionContext sessionContext,
        WorkflowRunner workflowRunner)
    {
        // ... existing assignments ...
        _workflowRunner = workflowRunner;
    }
```

Add case to switch:

```csharp
            case "/simplify":
                await HandleSimplifyAsync(ct);
                return true;
```

Note: `TryHandleAsync` needs a `CancellationToken` parameter. Update signature:

```csharp
    public async Task<bool> TryHandleAsync(string input, CancellationToken ct = default)
```

Implement handler:

```csharp
    private async Task HandleSimplifyAsync(CancellationToken ct)
    {
        var workflow = Workflows.SimplifyWorkflow.Create();
        var variables = new Dictionary<string, string>();
        await _workflowRunner.RunAsync(workflow, variables, ct);
    }
```

- [ ] **Step 3: Update /help text**

```csharp
              /simplify        — Review and fix changed code (reuse, quality, efficiency)
```

- [ ] **Step 4: Update CliEngine to pass CancellationToken to commands**

In `src/GeminiCode/Cli/CliEngine.cs:50`, change:

```csharp
// Old:
if (await _commands.TryHandleAsync(input))

// New:
if (await _commands.TryHandleAsync(input, ct))
```

- [ ] **Step 5: Update Program.cs to create WorkflowRunner**

After orchestrator creation:

```csharp
        var workflowRunner = new WorkflowRunner(orchestrator, browser);
        var commands = new CommandHandler(browser, conversation, allowlist, sandbox, agentProfile, sessionContext, workflowRunner);
```

- [ ] **Step 6: Build and verify**

Run: `dotnet build src/GeminiCode/`
Expected: Build succeeds.

- [ ] **Step 7: Commit**

```bash
git add src/GeminiCode/Agent/Workflows/SimplifyWorkflow.cs src/GeminiCode/Cli/CommandHandler.cs src/GeminiCode/Cli/CliEngine.cs src/GeminiCode/Program.cs
git commit -m "feat: /simplify workflow command for automated code review and fixes"
```

---

## Task 11: /brainstorm Workflow

**Files:**
- Create: `src/GeminiCode/Agent/Workflows/BrainstormWorkflow.cs`
- Modify: `src/GeminiCode/Cli/CommandHandler.cs`

- [ ] **Step 1: Create BrainstormWorkflow**

```csharp
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
                $"""
                The user wants to brainstorm a feature or idea: {userInput}

                First, explore the current project context:
                1. Check the project structure with [TREE:depth=2][/TREE]
                2. Check recent git history with [GIT]log -10 --oneline[/GIT]
                3. Look at relevant existing code based on what the user described

                Then, ask 3-5 clarifying questions to understand:
                - What exactly they want to build
                - Constraints (performance, compatibility, timeline)
                - Success criteria

                Present questions as a numbered list. Be specific to the project.
                """,
                "Understanding context...",
                true),

            new WorkflowPhase(
                "Exploring approaches",
                """
                Based on what you've learned about the project, propose 2-3 different approaches to implement the feature.

                For each approach:
                - Name it clearly
                - Describe the architecture (which files, what components, how data flows)
                - List pros and cons
                - Estimate relative complexity

                Lead with your recommended approach and explain why you prefer it. Reference existing code patterns in the project where relevant.
                """,
                "Exploring approaches...",
                true),

            new WorkflowPhase(
                "Designing solution",
                """
                Based on the recommended approach, create a detailed design:

                1. **File structure** — which files to create or modify, what each is responsible for
                2. **Component interfaces** — key classes/functions, their signatures, how they connect
                3. **Data flow** — how data moves through the system
                4. **Error handling** — what can go wrong and how to handle it
                5. **Testing strategy** — what to test and how

                Be specific about file paths relative to the project root. Show key interfaces and type definitions. This design should be detailed enough that someone could implement it without further discussion.
                """,
                "Designing solution...",
                true),

            new WorkflowPhase(
                "Summary",
                """
                Summarize the brainstorming session:

                1. **What we're building:** one paragraph
                2. **Chosen approach:** the recommended approach and why
                3. **Key design decisions:** bullet list of the important choices
                4. **Next steps:** what to implement first

                Keep it concise — this summary will be saved for reference.
                """,
                "Summarizing...",
                false)
        });
    }
}
```

- [ ] **Step 2: Add /brainstorm to CommandHandler**

Add case to switch:

```csharp
            case "/brainstorm":
                await HandleBrainstormAsync(arg, ct);
                return true;
```

Implement handler:

```csharp
    private async Task HandleBrainstormAsync(string? topic, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(topic))
        {
            Console.WriteLine("Usage: /brainstorm <topic or feature description>");
            Console.WriteLine("  Example: /brainstorm add user authentication with JWT");
            return;
        }

        var workflow = Workflows.BrainstormWorkflow.Create(topic);
        var variables = new Dictionary<string, string>();
        await _workflowRunner.RunAsync(workflow, variables, ct);
    }
```

- [ ] **Step 3: Update /help text**

```csharp
              /brainstorm <topic> — Guided brainstorming for features and designs
```

- [ ] **Step 4: Build and verify**

Run: `dotnet build src/GeminiCode/`
Expected: Build succeeds.

- [ ] **Step 5: Commit**

```bash
git add src/GeminiCode/Agent/Workflows/BrainstormWorkflow.cs src/GeminiCode/Cli/CommandHandler.cs
git commit -m "feat: /brainstorm workflow command for guided feature design"
```

---

## Task 12: Final Integration and Full Build

**Files:**
- Modify: `src/GeminiCode/Program.cs` (final wiring)

- [ ] **Step 1: Review and finalize Program.cs**

The final `Program.cs` initialization should flow as:

```csharp
        // Initialize path sandbox
        var sandbox = new PathSandbox(workDir);

        // Initialize agent profile
        var agentProfile = new AgentProfile(workDir);

        // Initialize tools
        var toolRegistry = new ToolRegistry();
        // ... tool registrations ...

        // Initialize permissions
        var allowlist = new SessionAllowlist();
        var permissionGate = new PermissionGate(allowlist);

        // Initialize browser
        var userDataFolder = Path.Combine(ConfigLoader.AppDataPath, "WebView2Data");
        var browser = new BrowserBridge(selectors, userDataFolder);
        // ... browser start, auth, health check ...

        // Initialize agent
        var conversation = new ConversationManager();
        var sessionContext = new SessionContext(workDir, agentProfile.ActiveProfileName);
        var orchestrator = new AgentOrchestrator(browser, toolRegistry, permissionGate, conversation, settings, sandbox, agentProfile, sessionContext);

        // Initialize workflows and CLI
        var workflowRunner = new WorkflowRunner(orchestrator, browser);
        var contextProcessor = new ContextProcessor(sandbox);
        var commands = new CommandHandler(browser, conversation, allowlist, sandbox, agentProfile, sessionContext, workflowRunner);
        var cli = new CliEngine(orchestrator, commands, browser, toolRegistry, permissionGate, contextProcessor);
```

- [ ] **Step 2: Run all tests**

Run: `dotnet test src/GeminiCode.Tests/ -v minimal`
Expected: All tests pass (existing + new).

- [ ] **Step 3: Full build**

Run: `dotnet build src/GeminiCode/`
Expected: Build succeeds with no warnings.

- [ ] **Step 4: Auto-save context on exit**

In `CommandHandler.HandleExit()`, add context auto-save:

```csharp
    private void HandleExit()
    {
        _sessionContext.SaveToFile();
        Console.WriteLine($"{AnsiHelper.Dim}Session context saved.{AnsiHelper.Reset}");
        Console.WriteLine("Goodbye.");
        _browser.Dispose();
        Environment.Exit(0);
    }
```

- [ ] **Step 5: Final commit**

```bash
git add -A
git commit -m "feat: complete integration of file upload, context preservation, agents, and workflows"
```

---

## Summary of All Commands Added

| Command | Description |
|---------|-------------|
| `/agent` | List available agent profiles |
| `/agent <name>` | Switch to agent profile (starts new chat) |
| `/agent info` | Show active profile details |
| `/save` | Save session context to `.gemini/session-context.md` |
| `/restore` | Restore previous session context in new chat |
| `/context` | Show current session context |
| `/simplify` | Run code review workflow (reuse, quality, efficiency, fix) |
| `/brainstorm <topic>` | Run guided brainstorming workflow |

## New @context Syntax

| Syntax | Behavior |
|--------|----------|
| `@file path` | Upload file to Gemini |
| `@file path L200-L500` | Inject lines 200-500 as text |
| `@file path L200` | Inject from line 200 onwards |
| `@file path:10-50` | Legacy line range syntax (still works) |
| `@upload path` | Explicit file upload |
