# GeminiCode "Claude-like" Upgrade Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a droppable markdown plugin/skill system, more slash commands, a secure extended tool set, and response UX (thinking spinner + estimated token usage) to GeminiCode.

**Architecture:** Plugins are `SKILL.md` files (frontmatter + `## Phase:` sections) discovered from `plugins/` and `<workdir>/.gemini/plugins/`, loaded into a registry, exposed both as `/commands` (run through the existing `WorkflowRunner`) and as a `[SKILL:name]` action tag Gemini can self-invoke (handled by a new `SkillTool`). New tools follow the existing `ITool` + action-tag + `ToolCallParser` + `SystemPrompt` pattern, all sandboxed and permission-gated. UX adds a console `Spinner` around browser waits and a `UsageTracker` that estimates tokens (chars/4).

**Tech Stack:** C# / .NET 9 (net9.0-windows), xUnit tests, WinForms+WebView2 (unchanged).

---

## Conventions (read once)

- **Build:** `dotnet build` (run from repo root `D:\CodeProjects\GeminiCode`).
- **All tests:** `dotnet test src/GeminiCode.Tests/GeminiCode.Tests.csproj`
- **One test class:** `dotnet test src/GeminiCode.Tests/GeminiCode.Tests.csproj --filter "FullyQualifiedName~ClassName"`
- Tests are xUnit (`[Fact]`, `Assert.*`), use a temp working dir with `IDisposable` cleanup (mirror `EditFileToolTests`).
- Tools return `ToolResult(Name, Success, Output)` and catch `SandboxViolationException` → `ToolResult(Name, false, ex.Message)`.
- Parameters arrive as `Dictionary<string, JsonElement>`; read with `parameters["x"].GetString()!` and guard optional ones with `TryGetValue` + `ValueKind`.

---

## Task 0: Establish green baseline ("up to date")

**Files:** none (verification only).

- [ ] **Step 1: Build**

Run: `dotnet build`
Expected: `Build succeeded`. If it fails, fix the compile error before continuing (do not start features on a red baseline).

- [ ] **Step 2: Run the full test suite**

Run: `dotnet test src/GeminiCode.Tests/GeminiCode.Tests.csproj`
Expected: all tests pass. Record the passing count; later tasks must keep it green.

- [ ] **Step 3: Commit a checkpoint tag (no code change — skip if nothing to commit)**

Only if `git status` is dirty from build artifacts you want ignored; otherwise proceed. No commit required here.

---

## Task 1: FrontmatterParser

**Files:**
- Create: `src/GeminiCode/Plugins/FrontmatterParser.cs`
- Test: `src/GeminiCode.Tests/FrontmatterParserTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
// src/GeminiCode.Tests/FrontmatterParserTests.cs
using GeminiCode.Plugins;

namespace GeminiCode.Tests;

public class FrontmatterParserTests
{
    [Fact]
    public void Parse_WithFrontmatter_ExtractsFieldsAndBody()
    {
        var text = "---\nname: brainstorm\ndescription: Turn ideas into designs\n---\n## Phase: One\nbody line\n";
        var result = FrontmatterParser.Parse(text);
        Assert.Equal("brainstorm", result.Fields["name"]);
        Assert.Equal("Turn ideas into designs", result.Fields["description"]);
        Assert.StartsWith("## Phase: One", result.Body.TrimStart());
        Assert.DoesNotContain("name:", result.Body);
    }

    [Fact]
    public void Parse_NoFrontmatter_ReturnsWholeTextAsBody()
    {
        var text = "just instructions, no frontmatter";
        var result = FrontmatterParser.Parse(text);
        Assert.Empty(result.Fields);
        Assert.Equal(text, result.Body);
    }

    [Fact]
    public void Parse_ColonInValue_KeepsRemainder()
    {
        var text = "---\ndescription: ratio is 4:1 here\n---\nbody";
        var result = FrontmatterParser.Parse(text);
        Assert.Equal("ratio is 4:1 here", result.Fields["description"]);
    }

    [Fact]
    public void Parse_BlankAndMalformedLines_AreSkipped()
    {
        var text = "---\nname: x\n\nnotakeyvalueline\ndescription: y\n---\nbody";
        var result = FrontmatterParser.Parse(text);
        Assert.Equal("x", result.Fields["name"]);
        Assert.Equal("y", result.Fields["description"]);
        Assert.Equal(2, result.Fields.Count);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/GeminiCode.Tests/GeminiCode.Tests.csproj --filter "FullyQualifiedName~FrontmatterParserTests"`
Expected: FAIL — `FrontmatterParser` does not exist.

- [ ] **Step 3: Write the implementation**

```csharp
// src/GeminiCode/Plugins/FrontmatterParser.cs
namespace GeminiCode.Plugins;

public record FrontmatterResult(Dictionary<string, string> Fields, string Body);

public static class FrontmatterParser
{
    /// <summary>Parse a leading YAML-ish '---' frontmatter block of simple key: value lines.
    /// No code evaluation — plugins are data, never executed.</summary>
    public static FrontmatterResult Parse(string text)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var normalized = text.Replace("\r\n", "\n");

        if (!normalized.StartsWith("---\n"))
            return new FrontmatterResult(fields, text);

        var afterOpen = normalized[4..];
        var closeIdx = afterOpen.IndexOf("\n---", StringComparison.Ordinal);
        if (closeIdx < 0)
            return new FrontmatterResult(fields, text); // unterminated → treat as no frontmatter

        var block = afterOpen[..closeIdx];
        foreach (var rawLine in block.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;
            var colon = line.IndexOf(':');
            if (colon <= 0) continue;
            var key = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            if (key.Length > 0)
                fields[key] = value;
        }

        // Body = everything after the closing '---' line
        var rest = afterOpen[(closeIdx + 4)..]; // skip "\n---"
        var nl = rest.IndexOf('\n');
        var body = nl >= 0 ? rest[(nl + 1)..] : "";
        return new FrontmatterResult(fields, body);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/GeminiCode.Tests/GeminiCode.Tests.csproj --filter "FullyQualifiedName~FrontmatterParserTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add src/GeminiCode/Plugins/FrontmatterParser.cs src/GeminiCode.Tests/FrontmatterParserTests.cs
git commit -m "feat: frontmatter parser for plugin SKILL.md files"
```

---

## Task 2: PluginManifest + phase parsing + ToWorkflow

**Files:**
- Create: `src/GeminiCode/Plugins/PluginManifest.cs`
- Test: `src/GeminiCode.Tests/PluginManifestTests.cs`

Note: references `GeminiCode.Agent.WorkflowPhase` / `WorkflowDefinition` (already exist in `WorkflowRunner.cs`).

- [ ] **Step 1: Write the failing test**

```csharp
// src/GeminiCode.Tests/PluginManifestTests.cs
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
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/GeminiCode.Tests/GeminiCode.Tests.csproj --filter "FullyQualifiedName~PluginManifestTests"`
Expected: FAIL — `PluginManifest` does not exist.

- [ ] **Step 3: Write the implementation**

```csharp
// src/GeminiCode/Plugins/PluginManifest.cs
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
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/GeminiCode.Tests/GeminiCode.Tests.csproj --filter "FullyQualifiedName~PluginManifestTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add src/GeminiCode/Plugins/PluginManifest.cs src/GeminiCode.Tests/PluginManifestTests.cs
git commit -m "feat: plugin manifest with phase parsing and workflow conversion"
```

---

## Task 3: PluginLoader

**Files:**
- Create: `src/GeminiCode/Plugins/PluginLoader.cs`
- Test: `src/GeminiCode.Tests/PluginLoaderTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
// src/GeminiCode.Tests/PluginLoaderTests.cs
using GeminiCode.Plugins;

namespace GeminiCode.Tests;

public class PluginLoaderTests : IDisposable
{
    private readonly string _root;
    public PluginLoaderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"plug_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private void WritePlugin(string root, string folder, string content)
    {
        var dir = Path.Combine(root, folder);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "SKILL.md"), content);
    }

    [Fact]
    public void Load_ReadsValidPlugin()
    {
        WritePlugin(_root, "brainstorm",
            "---\nname: brainstorm\ndescription: d\n---\n## Phase: A\nbody");
        var loader = new PluginLoader(new[] { _root });
        var plugins = loader.Load();
        Assert.Single(plugins);
        Assert.Equal("brainstorm", plugins[0].Name);
    }

    [Fact]
    public void Load_SkipsPluginMissingNameOrDescription_AndWarns()
    {
        WritePlugin(_root, "bad", "---\nname: \n---\nbody");
        var loader = new PluginLoader(new[] { _root });
        var plugins = loader.Load();
        Assert.Empty(plugins);
        Assert.NotEmpty(loader.Warnings);
    }

    [Fact]
    public void Load_LaterRootOverridesSameName()
    {
        var root2 = Path.Combine(Path.GetTempPath(), $"plug2_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root2);
        try
        {
            WritePlugin(_root, "x", "---\nname: x\ndescription: shipped\n---\nb");
            WritePlugin(root2, "x", "---\nname: x\ndescription: local\n---\nb");
            var loader = new PluginLoader(new[] { _root, root2 });
            var plugins = loader.Load();
            Assert.Single(plugins);
            Assert.Equal("local", plugins[0].Description);
        }
        finally { Directory.Delete(root2, true); }
    }

    [Fact]
    public void Load_MissingRoot_IsIgnored()
    {
        var loader = new PluginLoader(new[] { Path.Combine(_root, "does-not-exist") });
        Assert.Empty(loader.Load());
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/GeminiCode.Tests/GeminiCode.Tests.csproj --filter "FullyQualifiedName~PluginLoaderTests"`
Expected: FAIL — `PluginLoader` does not exist.

- [ ] **Step 3: Write the implementation**

```csharp
// src/GeminiCode/Plugins/PluginLoader.cs
namespace GeminiCode.Plugins;

public class PluginLoader
{
    private readonly IReadOnlyList<string> _roots;
    public List<string> Warnings { get; } = new();

    public PluginLoader(IEnumerable<string> roots) => _roots = roots.ToList();

    /// <summary>Scan all roots, parse SKILL.md files. Later roots override earlier by plugin name.</summary>
    public List<PluginManifest> Load()
    {
        Warnings.Clear();
        var byName = new Dictionary<string, PluginManifest>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in _roots)
        {
            if (!Directory.Exists(root)) continue;

            foreach (var dir in Directory.EnumerateDirectories(root))
            {
                var skillFile = Path.Combine(dir, "SKILL.md");
                if (!File.Exists(skillFile)) continue;

                string text;
                try { text = File.ReadAllText(skillFile); }
                catch (Exception ex) { Warnings.Add($"Could not read {skillFile}: {ex.Message}"); continue; }

                var parsed = FrontmatterParser.Parse(text);
                var manifest = PluginManifest.FromParsed(parsed.Fields, parsed.Body, skillFile);

                if (string.IsNullOrWhiteSpace(manifest.Name) || string.IsNullOrWhiteSpace(manifest.Description))
                {
                    Warnings.Add($"Skipped plugin at {skillFile}: missing name or description.");
                    continue;
                }

                byName[manifest.Name] = manifest; // later root wins
            }
        }

        return byName.Values.ToList();
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/GeminiCode.Tests/GeminiCode.Tests.csproj --filter "FullyQualifiedName~PluginLoaderTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add src/GeminiCode/Plugins/PluginLoader.cs src/GeminiCode.Tests/PluginLoaderTests.cs
git commit -m "feat: plugin loader scanning SKILL.md across roots"
```

---

## Task 4: PluginRegistry

**Files:**
- Create: `src/GeminiCode/Plugins/PluginRegistry.cs`
- Test: `src/GeminiCode.Tests/PluginRegistryTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
// src/GeminiCode.Tests/PluginRegistryTests.cs
using GeminiCode.Plugins;

namespace GeminiCode.Tests;

public class PluginRegistryTests : IDisposable
{
    private readonly string _root;
    public PluginRegistryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"reg_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private void Write(string folder, string name)
    {
        var dir = Path.Combine(_root, folder);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "SKILL.md"), $"---\nname: {name}\ndescription: d\n---\nbody");
    }

    [Fact]
    public void ByCommandAndByName_ResolvePlugins()
    {
        Write("brainstorm", "brainstorm");
        var reg = new PluginRegistry(new PluginLoader(new[] { _root }));
        Assert.NotNull(reg.ByCommand("/brainstorm"));
        Assert.NotNull(reg.ByCommand("BRAINSTORM"));   // normalized
        Assert.NotNull(reg.ByName("brainstorm"));
        Assert.Null(reg.ByCommand("/nope"));
    }

    [Fact]
    public void Reload_PicksUpAddedAndRemovedPlugins()
    {
        Write("a", "a");
        var reg = new PluginRegistry(new PluginLoader(new[] { _root }));
        Assert.Single(reg.Plugins);

        Write("b", "b");
        var (added, removed) = reg.Reload();
        Assert.Equal(1, added);
        Assert.Equal(0, removed);
        Assert.Equal(2, reg.Plugins.Count);

        Directory.Delete(Path.Combine(_root, "a"), true);
        var (added2, removed2) = reg.Reload();
        Assert.Equal(0, added2);
        Assert.Equal(1, removed2);
        Assert.Single(reg.Plugins);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/GeminiCode.Tests/GeminiCode.Tests.csproj --filter "FullyQualifiedName~PluginRegistryTests"`
Expected: FAIL — `PluginRegistry` does not exist.

- [ ] **Step 3: Write the implementation**

```csharp
// src/GeminiCode/Plugins/PluginRegistry.cs
namespace GeminiCode.Plugins;

public class PluginRegistry
{
    private readonly PluginLoader _loader;
    private List<PluginManifest> _plugins;

    public IReadOnlyList<PluginManifest> Plugins => _plugins;
    public IReadOnlyList<string> Warnings => _loader.Warnings;

    public PluginRegistry(PluginLoader loader)
    {
        _loader = loader;
        _plugins = loader.Load();
    }

    public PluginManifest? ByCommand(string command)
    {
        var c = command.Trim().ToLowerInvariant();
        if (!c.StartsWith('/')) c = "/" + c;
        return _plugins.FirstOrDefault(p => p.Command == c);
    }

    public PluginManifest? ByName(string name)
        => _plugins.FirstOrDefault(p => p.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Re-scan roots. Returns (added, removed) counts vs the previous set.</summary>
    public (int added, int removed) Reload()
    {
        var before = _plugins.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _plugins = _loader.Load();
        var after = _plugins.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = after.Count(n => !before.Contains(n));
        var removed = before.Count(n => !after.Contains(n));
        return (added, removed);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/GeminiCode.Tests/GeminiCode.Tests.csproj --filter "FullyQualifiedName~PluginRegistryTests"`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add src/GeminiCode/Plugins/PluginRegistry.cs src/GeminiCode.Tests/PluginRegistryTests.cs
git commit -m "feat: plugin registry with command/name lookup and reload"
```

---

## Task 5: Ship brainstorm + simplify as plugins; delete old workflow classes

**Files:**
- Create: `plugins/brainstorm/SKILL.md`
- Create: `plugins/simplify/SKILL.md`
- Delete: `src/GeminiCode/Agent/Workflows/BrainstormWorkflow.cs`
- Delete: `src/GeminiCode/Agent/Workflows/SimplifyWorkflow.cs`

Wiring that references these (CommandHandler) is updated in Task 7. Do this task and Task 6/7 together if compiling in between — see Step 4 note.

- [ ] **Step 1: Create the brainstorm plugin**

Use `{input}` where the old factory used the topic. Content of `plugins/brainstorm/SKILL.md`:

```markdown
---
name: brainstorm
description: Turn ideas into fully formed designs through guided dialogue
command: /brainstorm
argHint: <topic or feature description>
---
## Phase: Understanding context
The user wants to brainstorm a feature or idea: {input}

First, explore the current project context:
1. Check the project structure with [TREE:depth=2][/TREE]
2. Check recent git history with [GIT]log -10 --oneline[/GIT]
3. Look at relevant existing code based on what the user described

Then ask 3-5 clarifying questions to understand what they want to build, constraints (performance, compatibility, timeline), and success criteria. Present questions as a numbered list. Be specific to the project.

## Phase: Exploring approaches
Based on what you've learned about the project, propose 2-3 different approaches to implement the feature.

For each approach: name it clearly, describe the architecture (which files, what components, how data flows), list pros and cons, and estimate relative complexity. Lead with your recommended approach and explain why. Reference existing code patterns where relevant.

## Phase: Designing solution
Based on the recommended approach, create a detailed design:
1. File structure — which files to create or modify, what each is responsible for
2. Component interfaces — key classes/functions, their signatures, how they connect
3. Data flow — how data moves through the system
4. Error handling — what can go wrong and how to handle it
5. Testing strategy — what to test and how

Be specific about file paths relative to the project root. Show key interfaces and type definitions.

## Phase: Summary
Summarize the brainstorming session:
1. What we're building: one paragraph
2. Chosen approach: the recommended approach and why
3. Key design decisions: bullet list
4. Next steps: what to implement first

Keep it concise — this summary will be saved for reference.
```

- [ ] **Step 2: Create the simplify plugin**

First read the existing `src/GeminiCode/Agent/Workflows/SimplifyWorkflow.cs` to copy its phase prompts verbatim into `plugins/simplify/SKILL.md` using the same `## Phase:` format. Frontmatter:

```markdown
---
name: simplify
description: Review changed code for reuse, simplification, and efficiency, then apply fixes
command: /simplify
argHint:
---
## Phase: <copy each phase name + prompt body from SimplifyWorkflow.Create()>
...
```

(The exact phase text comes from the existing file — preserve it. If the final phase was non-tool-calling, name it `Summary` so the manifest sets `AllowToolCalls=false`, matching prior behavior; otherwise keep tool calls on.)

- [ ] **Step 3: Delete the old workflow classes**

```bash
git rm src/GeminiCode/Agent/Workflows/BrainstormWorkflow.cs src/GeminiCode/Agent/Workflows/SimplifyWorkflow.cs
```

- [ ] **Step 4: Note — do not build yet**

These deletions break `CommandHandler.cs` (it references `BrainstormWorkflow`/`SimplifyWorkflow`). Proceed directly to Task 6 + 7, then build. (If executing strictly task-by-task, combine Tasks 5–7 into one commit boundary.)

- [ ] **Step 5: Stage (commit happens at end of Task 7)**

```bash
git add plugins/brainstorm/SKILL.md plugins/simplify/SKILL.md
```

---

## Task 6: Add SkillTool (Gemini self-invocation)

**Files:**
- Create: `src/GeminiCode/Tools/SkillTool.cs`
- Test: `src/GeminiCode.Tests/SkillToolTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
// src/GeminiCode.Tests/SkillToolTests.cs
using System.Text.Json;
using GeminiCode.Plugins;
using GeminiCode.Tools;

namespace GeminiCode.Tests;

public class SkillToolTests : IDisposable
{
    private readonly string _root;
    public SkillToolTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"skill_{Guid.NewGuid():N}");
        var dir = Path.Combine(_root, "greet");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "SKILL.md"),
            "---\nname: greet\ndescription: d\n---\nSay hello to {input}");
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private static Dictionary<string, JsonElement> Params(object o)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(o));
        return doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
    }

    [Fact]
    public async Task Execute_ReturnsBodyWithInputSubstituted()
    {
        var reg = new PluginRegistry(new PluginLoader(new[] { _root }));
        var tool = new SkillTool(reg);
        var result = await tool.ExecuteAsync(Params(new { name = "greet", input = "World" }), CancellationToken.None);
        Assert.True(result.Success);
        Assert.Contains("Say hello to World", result.Output);
    }

    [Fact]
    public async Task Execute_UnknownSkill_ReturnsError()
    {
        var reg = new PluginRegistry(new PluginLoader(new[] { _root }));
        var tool = new SkillTool(reg);
        var result = await tool.ExecuteAsync(Params(new { name = "missing" }), CancellationToken.None);
        Assert.False(result.Success);
        Assert.Contains("Unknown skill", result.Output);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/GeminiCode.Tests/GeminiCode.Tests.csproj --filter "FullyQualifiedName~SkillToolTests"`
Expected: FAIL — `SkillTool` does not exist.

- [ ] **Step 3: Write the implementation**

```csharp
// src/GeminiCode/Tools/SkillTool.cs
using System.Text.Json;
using GeminiCode.Plugins;

namespace GeminiCode.Tools;

/// <summary>Lets Gemini self-invoke a plugin via [SKILL:name]input[/SKILL].
/// Returns the skill's instructions as a tool_result so Gemini follows them next turn
/// (single-shot — phased workflows remain user-triggered via /command).</summary>
public class SkillTool : ITool
{
    private readonly PluginRegistry _registry;

    public string Name => "Skill";
    public RiskLevel Risk => RiskLevel.Low;

    public SkillTool(PluginRegistry registry) => _registry = registry;

    public Task<ToolResult> ExecuteAsync(Dictionary<string, JsonElement> parameters, CancellationToken ct)
    {
        var name = parameters.TryGetValue("name", out var n) && n.ValueKind == JsonValueKind.String
            ? n.GetString()! : "";
        var input = parameters.TryGetValue("input", out var i) && i.ValueKind == JsonValueKind.String
            ? i.GetString()! : "";

        var manifest = _registry.ByName(name);
        if (manifest == null)
            return Task.FromResult(new ToolResult(Name, false, $"Unknown skill: '{name}'. Available: {string.Join(", ", _registry.Plugins.Select(p => p.Name))}"));

        var body = manifest.Body.Replace("{input}", input);
        return Task.FromResult(new ToolResult(Name, true, $"Skill '{manifest.Name}' instructions — follow these now:\n\n{body}"));
    }

    public string DescribeAction(Dictionary<string, JsonElement> parameters)
        => $"Skill: {(parameters.TryGetValue("name", out var n) ? n.GetString() : "?")}";
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/GeminiCode.Tests/GeminiCode.Tests.csproj --filter "FullyQualifiedName~SkillToolTests"`
Expected: PASS (2 tests).

- [ ] **Step 5: Stage (commit at end of Task 7)**

```bash
git add src/GeminiCode/Tools/SkillTool.cs src/GeminiCode.Tests/SkillToolTests.cs
```

---

## Task 7: Parse `[SKILL:]` tag + wire plugins into CommandHandler, SystemPrompt, Program

**Files:**
- Modify: `src/GeminiCode/Agent/ToolCallParser.cs` (add `[SKILL:]` pattern)
- Modify: `src/GeminiCode/Agent/SystemPrompt.cs` (Available Skills section + `[SKILL:]` doc)
- Modify: `src/GeminiCode/Agent/AgentOrchestrator.cs` (accept registry, pass to prompt)
- Modify: `src/GeminiCode/Cli/CommandHandler.cs` (plugin fallthrough; remove HandleBrainstorm/Simplify)
- Modify: `src/GeminiCode/Program.cs` (build loader/registry, register SkillTool, wire)
- Test: `src/GeminiCode.Tests/ToolCallParserTests.cs` (add a `[SKILL:]` case)

- [ ] **Step 1: Add the failing parser test**

Append to `src/GeminiCode.Tests/ToolCallParserTests.cs`:

```csharp
[Fact]
public void Parse_SkillTag_EmitsSkillToolCall()
{
    var result = ToolCallParser.Parse("Let me brainstorm. [SKILL:brainstorm]add auth[/SKILL]");
    var call = Assert.Single(result.ToolCalls);
    Assert.Equal("Skill", call.Name);
    Assert.Equal("brainstorm", call.Parameters["name"].GetString());
    Assert.Equal("add auth", call.Parameters["input"].GetString());
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test src/GeminiCode.Tests/GeminiCode.Tests.csproj --filter "FullyQualifiedName~ToolCallParserTests.Parse_SkillTag_EmitsSkillToolCall"`
Expected: FAIL.

- [ ] **Step 3: Add the `[SKILL:]` regex + handling in `ToolCallParser.cs`**

Add the pattern near the other tag patterns (after `GitTagPattern`):

```csharp
    // [SKILL:name]optional input[/SKILL]
    private static readonly Regex SkillTagPattern = new(
        @"\[\s*SKILL\s*:\s*([^\]]+?)\s*\](.*?)\[\s*/\s*SKILL\s*\]",
        RegexOptions.Singleline | RegexOptions.Compiled | RegexOptions.IgnoreCase);
```

In `Parse`, add this replacement in Strategy 1 (after the `[GIT]` block, before `// Strategy 2`):

```csharp
        // [SKILL:name]input[/SKILL]
        textContent = SkillTagPattern.Replace(textContent, m =>
        {
            var skillName = m.Groups[1].Value.Trim();
            var input = m.Groups[2].Value.Trim();
            toolCalls.Add(MakeToolCall("Skill", new() { ["name"] = skillName, ["input"] = input }));
            return "";
        });
```

- [ ] **Step 4: Run to verify the parser test passes**

Run: `dotnet test src/GeminiCode.Tests/GeminiCode.Tests.csproj --filter "FullyQualifiedName~ToolCallParserTests.Parse_SkillTag_EmitsSkillToolCall"`
Expected: PASS.

- [ ] **Step 5: Add the Available Skills section to `SystemPrompt.cs`**

Change the `Generate` signature and append a skills section. Replace the current `Generate(...)`:

```csharp
    /// <summary>Generate system prompt composed from base + profile + GEMINI.md + plugins.</summary>
    public static string Generate(string workingDirectory, string profileContent, string? geminiMdContent,
        IEnumerable<(string Name, string Description, string Command, string ArgHint)>? plugins = null)
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

        var pluginList = plugins?.ToList();
        if (pluginList is { Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("## Available Skills");
            sb.AppendLine("You can invoke a skill yourself with the tag `[SKILL:name]input[/SKILL]`. The build system returns the skill's instructions as a tool_result for you to follow. Use a skill when its description matches the user's request.");
            foreach (var p in pluginList)
                sb.AppendLine($"- **{p.Name}** — {p.Description} (also `/{p.Name}` for the user)");
        }

        return sb.ToString();
    }
```

- [ ] **Step 6: Pass the registry through `AgentOrchestrator`**

In `AgentOrchestrator.cs`: add `using GeminiCode.Plugins;`, a `private readonly PluginRegistry _plugins;` field, add it as the last constructor parameter, assign it. In `InitializeSessionAsync`, change the `SendMessageAsync(SystemPrompt.Generate(...))` call to:

```csharp
        var pluginInfo = _plugins.Plugins.Select(p => (p.Name, p.Description, p.Command, p.ArgHint));
        await _browser.SendMessageAsync(SystemPrompt.Generate(_sandbox.WorkingDirectory, profileContent, geminiMd, pluginInfo));
```

- [ ] **Step 7: Update `CommandHandler.cs` — plugin fallthrough, remove hardcoded brainstorm/simplify**

- Add `using GeminiCode.Plugins;`. Remove `using GeminiCode.Agent.Workflows;`.
- Add constructor params `PluginRegistry plugins`, `ToolRegistry toolRegistry`, `UsageTracker usage` (UsageTracker created in Task 13 — for now add `PluginRegistry plugins` and `ToolRegistry toolRegistry`; revisit `usage` in Task 13/8). Store them.
- Delete `HandleSimplifyAsync`, `HandleBrainstormAsync`, and their `case "/simplify"` / `case "/brainstorm"` entries.
- In the `default:` branch of the switch, before printing "Unknown command", add plugin resolution:

```csharp
            default:
                if (await TryRunPluginAsync(command, arg, ct))
                    return true;
                Console.WriteLine($"Unknown command: {command}. Type /help for available commands.");
                return true;
```

- Add the helper:

```csharp
    private async Task<bool> TryRunPluginAsync(string command, string? arg, CancellationToken ct)
    {
        var manifest = _plugins.ByCommand(command);
        if (manifest == null) return false;

        var variables = new Dictionary<string, string> { ["input"] = arg ?? "" };
        await _workflowRunner.RunAsync(manifest.ToWorkflow(), variables, ct);
        return true;
    }
```

- [ ] **Step 8: Wire everything in `Program.cs`**

After `var agentProfile = new AgentProfile(workDir);` add plugin loading:

```csharp
        // Initialize plugins (shipped + user-dropped)
        var shippedPluginsDir = Path.Combine(AppContext.BaseDirectory, "plugins");
        var userPluginsDir = Path.Combine(workDir, ".gemini", "plugins");
        var pluginLoader = new PluginLoader(new[] { shippedPluginsDir, userPluginsDir });
        var pluginRegistry = new PluginRegistry(pluginLoader);
        if (pluginRegistry.Plugins.Count > 0)
            Console.WriteLine($"{AnsiHelper.Dim}Loaded {pluginRegistry.Plugins.Count} plugin(s): {string.Join(", ", pluginRegistry.Plugins.Select(p => p.Name))}{AnsiHelper.Reset}");
        foreach (var w in pluginRegistry.Warnings)
            Console.WriteLine($"{AnsiHelper.Yellow}Plugin warning: {w}{AnsiHelper.Reset}");
```

Add `using GeminiCode.Plugins;` at the top. Register the SkillTool in the tools block:

```csharp
        toolRegistry.Register(new SkillTool(pluginRegistry));
```

Update the orchestrator construction to pass `pluginRegistry` as the final argument:

```csharp
        var orchestrator = new AgentOrchestrator(browser, toolRegistry, permissionGate, conversation, settings, sandbox, agentProfile, sessionContext, pluginRegistry);
```

Update the `CommandHandler` construction to pass `pluginRegistry` and `toolRegistry`:

```csharp
        var commands = new CommandHandler(browser, conversation, allowlist, sandbox, agentProfile, sessionContext, workflowRunner, pluginRegistry, toolRegistry);
```

**Ensure the `plugins/` folder is copied to output.** Add to `src/GeminiCode/GeminiCode.csproj` inside a `<ItemGroup>`:

```xml
  <ItemGroup>
    <None Include="..\..\plugins\**\*" CopyToOutputDirectory="PreserveNewest" LinkBase="plugins\" />
  </ItemGroup>
```

(Read the csproj first to place this correctly; the source `plugins/` folder is at repo root.)

- [ ] **Step 9: Build and run all tests**

Run: `dotnet build`
Expected: `Build succeeded`. Fix any reference errors (e.g. remaining `BrainstormWorkflow` usages).

Run: `dotnet test src/GeminiCode.Tests/GeminiCode.Tests.csproj`
Expected: all green (baseline count + new tests).

- [ ] **Step 10: Commit (Tasks 5–7 together)**

```bash
git add -A
git commit -m "feat: droppable markdown plugin system; brainstorm/simplify migrated to plugins; [SKILL:] self-invocation"
```

---

## Task 8: New slash commands (/plugins, /reload, /tools, /init, /compact) + dynamic /help

**Files:**
- Modify: `src/GeminiCode/Cli/CommandHandler.cs`

(`/usage` is added in Task 13 once `UsageTracker` exists.)

- [ ] **Step 1: Add command cases**

In the `switch`, add:

```csharp
            case "/plugins":
                PrintPlugins();
                return true;
            case "/reload":
                HandleReload();
                return true;
            case "/tools":
                PrintTools();
                return true;
            case "/init":
                HandleInit();
                return true;
            case "/compact":
                await HandleCompactAsync();
                return true;
```

- [ ] **Step 2: Add the handlers**

```csharp
    private void PrintPlugins()
    {
        if (_plugins.Plugins.Count == 0) { Console.WriteLine("No plugins loaded. Drop a folder with SKILL.md into .gemini/plugins/."); return; }
        Console.WriteLine($"{AnsiHelper.Bold}Loaded plugins:{AnsiHelper.Reset}");
        foreach (var p in _plugins.Plugins)
            Console.WriteLine($"  {p.Command,-16} {p.Description} {AnsiHelper.Dim}({(p.IsSingleShot ? "skill" : $"{p.Phases.Count}-phase")}){AnsiHelper.Reset}");
    }

    private void HandleReload()
    {
        var (added, removed) = _plugins.Reload();
        Console.WriteLine($"{AnsiHelper.Green}Plugins reloaded: +{added} / -{removed}. Now {_plugins.Plugins.Count} loaded.{AnsiHelper.Reset}");
        foreach (var w in _plugins.Warnings)
            Console.WriteLine($"{AnsiHelper.Yellow}  {w}{AnsiHelper.Reset}");
    }

    private void PrintTools()
    {
        Console.WriteLine($"{AnsiHelper.Bold}Available tools:{AnsiHelper.Reset}");
        foreach (var name in _toolRegistry.ToolNames.OrderBy(n => n))
        {
            var tool = _toolRegistry.GetTool(name)!;
            Console.WriteLine($"  {name,-14} {AnsiHelper.Dim}{Permissions.RiskAssessor.GetRiskLabel(tool)}{AnsiHelper.Reset}");
        }
    }

    private void HandleInit()
    {
        var path = Path.Combine(_sandbox.WorkingDirectory, "GEMINI.md");
        if (File.Exists(path)) { Console.WriteLine($"{AnsiHelper.Yellow}GEMINI.md already exists. Not overwriting.{AnsiHelper.Reset}"); return; }
        var template = """
            # Project Instructions (GEMINI.md)

            ## Overview
            <one paragraph: what this project is>

            ## Conventions
            - <coding conventions, style, frameworks>

            ## Commands
            - Build: <command>
            - Test: <command>
            - Run: <command>

            ## Notes
            <anything GeminiCode should always keep in mind>
            """;
        File.WriteAllText(path, template);
        Console.WriteLine($"{AnsiHelper.Green}Created {path}. Edit it to guide GeminiCode.{AnsiHelper.Reset}");
    }

    private async Task HandleCompactAsync()
    {
        Console.WriteLine($"{AnsiHelper.Dim}Compacting: saving context, starting fresh chat, restoring summary...{AnsiHelper.Reset}");
        _sessionContext.SaveToFile();
        await _browser.StartNewChatAsync();
        await _browser.WaitForPageSettleAsync();
        _conversation.Reset();
        var filePath = _sessionContext.GetFilePath();
        if (File.Exists(filePath))
            await _browser.SendMessageAsync("Previous session context:\n\n" + File.ReadAllText(filePath));
        Console.WriteLine($"{AnsiHelper.Green}Compacted. New chat seeded with prior context summary.{AnsiHelper.Reset}");
    }
```

- [ ] **Step 3: Make `/help` dynamic**

In `PrintHelp()`, after the existing built-in command list and `@Context` block, append plugins. At the end of the method (before the closing of the string or after it) add:

```csharp
        if (_plugins.Plugins.Count > 0)
        {
            Console.WriteLine($"\n{AnsiHelper.Bold}Plugins (skills):{AnsiHelper.Reset}");
            foreach (var p in _plugins.Plugins)
            {
                var hint = string.IsNullOrEmpty(p.ArgHint) ? "" : $" {p.ArgHint}";
                Console.WriteLine($"  {p.Command}{hint,-20} — {p.Description}");
            }
        }
```

Also add to the static help text the new built-ins:

```
  /plugins         — List loaded plugins
  /reload          — Re-scan the plugins folder
  /tools           — List available tools and risk
  /init            — Create a starter GEMINI.md
  /compact         — Summarize context and start fresh
```

- [ ] **Step 4: Build + test**

Run: `dotnet build` → `Build succeeded`.
Run: `dotnet test src/GeminiCode.Tests/GeminiCode.Tests.csproj` → green.

- [ ] **Step 5: Commit**

```bash
git add src/GeminiCode/Cli/CommandHandler.cs
git commit -m "feat: /plugins, /reload, /tools, /init, /compact commands + dynamic help"
```

---

## Task 9: File-management tools (Copy, Move, MakeDir, Delete-to-trash)

**Files:**
- Create: `src/GeminiCode/Tools/CopyFileTool.cs`, `MoveFileTool.cs`, `MakeDirTool.cs`, `DeleteFileTool.cs`
- Test: `src/GeminiCode.Tests/FileManagementToolsTests.cs`
- Modify: `src/GeminiCode/Agent/ToolCallParser.cs` (tags), `SystemPrompt.cs` (docs), `Program.cs` (register), `Permissions/RiskAssessor.cs` (risk label for Delete)

- [ ] **Step 1: Write the failing tests**

```csharp
// src/GeminiCode.Tests/FileManagementToolsTests.cs
using System.Text.Json;
using GeminiCode.Tools;

namespace GeminiCode.Tests;

public class FileManagementToolsTests : IDisposable
{
    private readonly string _workDir;
    private readonly PathSandbox _sandbox;

    public FileManagementToolsTests()
    {
        _workDir = Path.Combine(Path.GetTempPath(), $"fm_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workDir);
        _sandbox = new PathSandbox(_workDir);
    }
    public void Dispose() { if (Directory.Exists(_workDir)) Directory.Delete(_workDir, true); }

    private static Dictionary<string, JsonElement> P(object o)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(o));
        return doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
    }

    [Fact]
    public async Task Copy_DuplicatesFile()
    {
        await File.WriteAllTextAsync(Path.Combine(_workDir, "a.txt"), "hi");
        var r = await new CopyFileTool(_sandbox).ExecuteAsync(P(new { source = "a.txt", destination = "b.txt" }), default);
        Assert.True(r.Success);
        Assert.True(File.Exists(Path.Combine(_workDir, "b.txt")));
        Assert.True(File.Exists(Path.Combine(_workDir, "a.txt")));
    }

    [Fact]
    public async Task Move_RenamesFile()
    {
        await File.WriteAllTextAsync(Path.Combine(_workDir, "a.txt"), "hi");
        var r = await new MoveFileTool(_sandbox).ExecuteAsync(P(new { source = "a.txt", destination = "c.txt" }), default);
        Assert.True(r.Success);
        Assert.False(File.Exists(Path.Combine(_workDir, "a.txt")));
        Assert.True(File.Exists(Path.Combine(_workDir, "c.txt")));
    }

    [Fact]
    public async Task MakeDir_CreatesDirectory()
    {
        var r = await new MakeDirTool(_sandbox).ExecuteAsync(P(new { path = "sub/dir" }), default);
        Assert.True(r.Success);
        Assert.True(Directory.Exists(Path.Combine(_workDir, "sub", "dir")));
    }

    [Fact]
    public async Task Delete_MovesToTrash_NotHardDelete()
    {
        await File.WriteAllTextAsync(Path.Combine(_workDir, "a.txt"), "hi");
        var r = await new DeleteFileTool(_sandbox).ExecuteAsync(P(new { path = "a.txt" }), default);
        Assert.True(r.Success);
        Assert.False(File.Exists(Path.Combine(_workDir, "a.txt")));
        var trash = Path.Combine(_workDir, ".gemini", "trash");
        Assert.True(Directory.Exists(trash));
        Assert.NotEmpty(Directory.GetFiles(trash, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Copy_OutsideSandbox_IsRejected()
    {
        await File.WriteAllTextAsync(Path.Combine(_workDir, "a.txt"), "hi");
        var r = await new CopyFileTool(_sandbox).ExecuteAsync(P(new { source = "a.txt", destination = "../escape.txt" }), default);
        Assert.False(r.Success);
        Assert.Contains("Access denied", r.Output);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test src/GeminiCode.Tests/GeminiCode.Tests.csproj --filter "FullyQualifiedName~FileManagementToolsTests"`
Expected: FAIL — tools do not exist.

- [ ] **Step 3: Implement the four tools**

```csharp
// src/GeminiCode/Tools/CopyFileTool.cs
using System.Text.Json;
namespace GeminiCode.Tools;

public class CopyFileTool : ITool
{
    private readonly PathSandbox _sandbox;
    public string Name => "CopyFile";
    public RiskLevel Risk => RiskLevel.Medium;
    public CopyFileTool(PathSandbox sandbox) => _sandbox = sandbox;

    public Task<ToolResult> ExecuteAsync(Dictionary<string, JsonElement> parameters, CancellationToken ct)
    {
        try
        {
            var src = _sandbox.Resolve(parameters["source"].GetString()!);
            var dst = _sandbox.Resolve(parameters["destination"].GetString()!);
            if (!File.Exists(src)) return Task.FromResult(new ToolResult(Name, false, $"Source not found: {parameters["source"].GetString()}"));
            var dir = Path.GetDirectoryName(dst)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.Copy(src, dst, overwrite: true);
            return Task.FromResult(new ToolResult(Name, true, $"Copied to {parameters["destination"].GetString()}"));
        }
        catch (SandboxViolationException ex) { return Task.FromResult(new ToolResult(Name, false, ex.Message)); }
        catch (Exception ex) { return Task.FromResult(new ToolResult(Name, false, ex.Message)); }
    }

    public string DescribeAction(Dictionary<string, JsonElement> p)
        => $"Copy: {p["source"].GetString()} -> {p["destination"].GetString()}";
}
```

```csharp
// src/GeminiCode/Tools/MoveFileTool.cs
using System.Text.Json;
namespace GeminiCode.Tools;

public class MoveFileTool : ITool
{
    private readonly PathSandbox _sandbox;
    public string Name => "MoveFile";
    public RiskLevel Risk => RiskLevel.Medium;
    public MoveFileTool(PathSandbox sandbox) => _sandbox = sandbox;

    public Task<ToolResult> ExecuteAsync(Dictionary<string, JsonElement> parameters, CancellationToken ct)
    {
        try
        {
            var src = _sandbox.Resolve(parameters["source"].GetString()!);
            var dst = _sandbox.Resolve(parameters["destination"].GetString()!);
            if (!File.Exists(src) && !Directory.Exists(src))
                return Task.FromResult(new ToolResult(Name, false, $"Source not found: {parameters["source"].GetString()}"));
            var dir = Path.GetDirectoryName(dst)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            if (Directory.Exists(src)) Directory.Move(src, dst);
            else File.Move(src, dst, overwrite: true);
            return Task.FromResult(new ToolResult(Name, true, $"Moved to {parameters["destination"].GetString()}"));
        }
        catch (SandboxViolationException ex) { return Task.FromResult(new ToolResult(Name, false, ex.Message)); }
        catch (Exception ex) { return Task.FromResult(new ToolResult(Name, false, ex.Message)); }
    }

    public string DescribeAction(Dictionary<string, JsonElement> p)
        => $"Move: {p["source"].GetString()} -> {p["destination"].GetString()}";
}
```

```csharp
// src/GeminiCode/Tools/MakeDirTool.cs
using System.Text.Json;
namespace GeminiCode.Tools;

public class MakeDirTool : ITool
{
    private readonly PathSandbox _sandbox;
    public string Name => "MakeDir";
    public RiskLevel Risk => RiskLevel.Medium;
    public MakeDirTool(PathSandbox sandbox) => _sandbox = sandbox;

    public Task<ToolResult> ExecuteAsync(Dictionary<string, JsonElement> parameters, CancellationToken ct)
    {
        try
        {
            var path = _sandbox.Resolve(parameters["path"].GetString()!);
            Directory.CreateDirectory(path);
            return Task.FromResult(new ToolResult(Name, true, $"Created directory {parameters["path"].GetString()}"));
        }
        catch (SandboxViolationException ex) { return Task.FromResult(new ToolResult(Name, false, ex.Message)); }
        catch (Exception ex) { return Task.FromResult(new ToolResult(Name, false, ex.Message)); }
    }

    public string DescribeAction(Dictionary<string, JsonElement> p) => $"Mkdir: {p["path"].GetString()}";
}
```

```csharp
// src/GeminiCode/Tools/DeleteFileTool.cs
using System.Text.Json;
namespace GeminiCode.Tools;

/// <summary>Moves the target into &lt;workdir&gt;/.gemini/trash/ instead of a hard delete — recoverable.</summary>
public class DeleteFileTool : ITool
{
    private readonly PathSandbox _sandbox;
    public string Name => "DeleteFile";
    public RiskLevel Risk => RiskLevel.High;
    public DeleteFileTool(PathSandbox sandbox) => _sandbox = sandbox;

    public Task<ToolResult> ExecuteAsync(Dictionary<string, JsonElement> parameters, CancellationToken ct)
    {
        try
        {
            var rel = parameters["path"].GetString()!;
            var target = _sandbox.Resolve(rel);
            if (!File.Exists(target) && !Directory.Exists(target))
                return Task.FromResult(new ToolResult(Name, false, $"Not found: {rel}"));

            var trashRoot = Path.Combine(_sandbox.WorkingDirectory, ".gemini", "trash");
            Directory.CreateDirectory(trashRoot);
            // Unique subfolder so repeated deletes of same name don't collide (no Date.Now in tools — use a GUID).
            var bucket = Path.Combine(trashRoot, Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(bucket);
            var dest = Path.Combine(bucket, Path.GetFileName(target.TrimEnd(Path.DirectorySeparatorChar)));

            if (Directory.Exists(target)) Directory.Move(target, dest);
            else File.Move(target, dest);

            return Task.FromResult(new ToolResult(Name, true, $"Moved {rel} to {Path.GetRelativePath(_sandbox.WorkingDirectory, dest)} (recoverable)"));
        }
        catch (SandboxViolationException ex) { return Task.FromResult(new ToolResult(Name, false, ex.Message)); }
        catch (Exception ex) { return Task.FromResult(new ToolResult(Name, false, ex.Message)); }
    }

    public string DescribeAction(Dictionary<string, JsonElement> p) => $"Delete (to trash): {p["path"].GetString()}";
}
```

- [ ] **Step 4: Run to verify the tests pass**

Run: `dotnet test src/GeminiCode.Tests/GeminiCode.Tests.csproj --filter "FullyQualifiedName~FileManagementToolsTests"`
Expected: PASS (5 tests).

- [ ] **Step 5: Add action tags in `ToolCallParser.cs`**

Add patterns near the other tag patterns:

```csharp
    private static readonly Regex CopyTagPattern = new(
        @"\[\s*COPY\s+([^\]>]+?)\s*>>>\s*([^\]]+?)\s*\]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex MoveTagPattern = new(
        @"\[\s*MOVE\s+([^\]>]+?)\s*>>>\s*([^\]]+?)\s*\]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex MkdirTagPattern = new(
        @"\[\s*MKDIR\s*\](.*?)\[\s*/\s*MKDIR\s*\]",
        RegexOptions.Singleline | RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex DeleteTagPattern = new(
        @"\[\s*DELETE\s*\](.*?)\[\s*/\s*DELETE\s*\]",
        RegexOptions.Singleline | RegexOptions.Compiled | RegexOptions.IgnoreCase);
```

In `Parse`, add replacements in Strategy 1 (after the `[SKILL:]` block):

```csharp
        textContent = CopyTagPattern.Replace(textContent, m =>
        { toolCalls.Add(MakeToolCall("CopyFile", new() { ["source"] = m.Groups[1].Value.Trim(), ["destination"] = m.Groups[2].Value.Trim() })); return ""; });
        textContent = MoveTagPattern.Replace(textContent, m =>
        { toolCalls.Add(MakeToolCall("MoveFile", new() { ["source"] = m.Groups[1].Value.Trim(), ["destination"] = m.Groups[2].Value.Trim() })); return ""; });
        textContent = MkdirTagPattern.Replace(textContent, m =>
        { toolCalls.Add(MakeToolCall("MakeDir", new() { ["path"] = m.Groups[1].Value.Trim() })); return ""; });
        textContent = DeleteTagPattern.Replace(textContent, m =>
        { toolCalls.Add(MakeToolCall("DeleteFile", new() { ["path"] = m.Groups[1].Value.Trim() })); return ""; });
```

- [ ] **Step 6: Register tools in `Program.cs`**

In the tools block:

```csharp
        toolRegistry.Register(new CopyFileTool(sandbox));
        toolRegistry.Register(new MoveFileTool(sandbox));
        toolRegistry.Register(new MakeDirTool(sandbox));
        toolRegistry.Register(new DeleteFileTool(sandbox));
```

- [ ] **Step 7: Document tags in `SystemPrompt.cs`**

In `GenerateTemplate`, add a "File management" subsection under the Action Tags area:

```
### File management
[COPY src/a.txt>>>src/b.txt]            (copy a file)
[MOVE src/a.txt>>>src/b.txt]            (move/rename)
[MKDIR]src/newdir[/MKDIR]               (create directory)
[DELETE]src/old.txt[/DELETE]            (delete — moved to .gemini/trash, recoverable)
```

- [ ] **Step 8: Build + full test**

Run: `dotnet build` → succeeds. Run: `dotnet test src/GeminiCode.Tests/GeminiCode.Tests.csproj` → green.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "feat: secure file-management tools (copy/move/mkdir/delete-to-trash) with action tags"
```

---

## Task 10: GlobTool

**Files:**
- Create: `src/GeminiCode/Tools/GlobTool.cs`
- Test: `src/GeminiCode.Tests/GlobToolTests.cs`
- Modify: `ToolCallParser.cs`, `SystemPrompt.cs`, `Program.cs`

- [ ] **Step 1: Write the failing test**

```csharp
// src/GeminiCode.Tests/GlobToolTests.cs
using System.Text.Json;
using GeminiCode.Tools;

namespace GeminiCode.Tests;

public class GlobToolTests : IDisposable
{
    private readonly string _workDir;
    private readonly PathSandbox _sandbox;
    public GlobToolTests()
    {
        _workDir = Path.Combine(Path.GetTempPath(), $"glob_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_workDir, "src", "sub"));
        File.WriteAllText(Path.Combine(_workDir, "src", "a.cs"), "x");
        File.WriteAllText(Path.Combine(_workDir, "src", "sub", "b.cs"), "x");
        File.WriteAllText(Path.Combine(_workDir, "readme.md"), "x");
        _sandbox = new PathSandbox(_workDir);
    }
    public void Dispose() { if (Directory.Exists(_workDir)) Directory.Delete(_workDir, true); }

    private static Dictionary<string, JsonElement> P(object o)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(o));
        return doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
    }

    [Fact]
    public async Task Glob_RecursivePattern_FindsAllMatches()
    {
        var r = await new GlobTool(_sandbox).ExecuteAsync(P(new { pattern = "**/*.cs" }), default);
        Assert.True(r.Success);
        Assert.Contains("a.cs", r.Output);
        Assert.Contains("b.cs", r.Output);
        Assert.DoesNotContain("readme.md", r.Output);
    }

    [Fact]
    public async Task Glob_NoMatches_ReportsNone()
    {
        var r = await new GlobTool(_sandbox).ExecuteAsync(P(new { pattern = "**/*.py" }), default);
        Assert.True(r.Success);
        Assert.Contains("No files", r.Output);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test src/GeminiCode.Tests/GeminiCode.Tests.csproj --filter "FullyQualifiedName~GlobToolTests"`
Expected: FAIL.

- [ ] **Step 3: Implement GlobTool**

Uses .NET's `Microsoft.Extensions.FileSystemGlobbing` if available; to avoid a new dependency, implement with `Directory.EnumerateFiles` + `**`/`*` translation to regex.

```csharp
// src/GeminiCode/Tools/GlobTool.cs
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GeminiCode.Tools;

public class GlobTool : ITool
{
    private readonly PathSandbox _sandbox;
    private const int MaxResults = 1000;
    private static readonly HashSet<string> SkipDirs = new(StringComparer.OrdinalIgnoreCase)
    { "node_modules", ".git", "bin", "obj", ".vs", "dist", "build", "target", "packages" };

    public string Name => "Glob";
    public RiskLevel Risk => RiskLevel.Low;
    public GlobTool(PathSandbox sandbox) => _sandbox = sandbox;

    public Task<ToolResult> ExecuteAsync(Dictionary<string, JsonElement> parameters, CancellationToken ct)
    {
        try
        {
            var pattern = parameters["pattern"].GetString()!.Replace('\\', '/');
            var regex = GlobToRegex(pattern);
            var matches = new List<string>();

            foreach (var file in EnumerateFiles(_sandbox.WorkingDirectory))
            {
                if (ct.IsCancellationRequested) break;
                var rel = Path.GetRelativePath(_sandbox.WorkingDirectory, file).Replace('\\', '/');
                if (regex.IsMatch(rel))
                {
                    matches.Add(rel);
                    if (matches.Count >= MaxResults) break;
                }
            }

            if (matches.Count == 0)
                return Task.FromResult(new ToolResult(Name, true, $"No files match '{pattern}'."));

            var sb = new StringBuilder($"{matches.Count} match(es):\n");
            foreach (var m in matches.OrderBy(x => x)) sb.AppendLine(m);
            return Task.FromResult(new ToolResult(Name, true, sb.ToString().TrimEnd()));
        }
        catch (SandboxViolationException ex) { return Task.FromResult(new ToolResult(Name, false, ex.Message)); }
    }

    public string DescribeAction(Dictionary<string, JsonElement> p) => $"Glob: {p["pattern"].GetString()}";

    private IEnumerable<string> EnumerateFiles(string root)
    {
        var queue = new Queue<string>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var dir = queue.Dequeue();
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(dir); } catch { continue; }
            foreach (var f in files) yield return f;
            IEnumerable<string> subs;
            try { subs = Directory.EnumerateDirectories(dir); } catch { continue; }
            foreach (var s in subs)
            {
                var n = Path.GetFileName(s);
                if (!SkipDirs.Contains(n) && !n.StartsWith('.')) queue.Enqueue(s);
            }
        }
    }

    private static Regex GlobToRegex(string glob)
    {
        var sb = new StringBuilder("^");
        for (int i = 0; i < glob.Length; i++)
        {
            var c = glob[i];
            if (c == '*')
            {
                if (i + 1 < glob.Length && glob[i + 1] == '*')
                {
                    sb.Append(".*");       // ** → any path including /
                    i++;
                    if (i + 1 < glob.Length && glob[i + 1] == '/') i++; // swallow trailing slash
                }
                else sb.Append("[^/]*");   // * → any non-separator run
            }
            else if (c == '?') sb.Append("[^/]");
            else sb.Append(Regex.Escape(c.ToString()));
        }
        sb.Append('$');
        return new Regex(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test src/GeminiCode.Tests/GeminiCode.Tests.csproj --filter "FullyQualifiedName~GlobToolTests"`
Expected: PASS (2 tests).

- [ ] **Step 5: Add tag, register, document**

`ToolCallParser.cs` pattern + replacement (Strategy 1):

```csharp
    private static readonly Regex GlobTagPattern = new(
        @"\[\s*GLOB\s*\](.*?)\[\s*/\s*GLOB\s*\]",
        RegexOptions.Singleline | RegexOptions.Compiled | RegexOptions.IgnoreCase);
```
```csharp
        textContent = GlobTagPattern.Replace(textContent, m =>
        { toolCalls.Add(MakeToolCall("Glob", new() { ["pattern"] = m.Groups[1].Value.Trim() })); return ""; });
```

`Program.cs`: `toolRegistry.Register(new GlobTool(sandbox));`

`SystemPrompt.cs` Action Tags: add `[GLOB]**/*.cs[/GLOB]   (find files by glob pattern)`.

- [ ] **Step 6: Build + full test + commit**

Run: `dotnet build` → succeeds. Run full tests → green.

```bash
git add -A
git commit -m "feat: Glob tool for fast filename pattern matching"
```

---

## Task 11: Todo tracker tool

**Files:**
- Create: `src/GeminiCode/Tools/TodoTool.cs` (contains `TodoStore`, `TodoItem`)
- Test: `src/GeminiCode.Tests/TodoToolTests.cs`
- Modify: `ToolCallParser.cs`, `SystemPrompt.cs`, `Program.cs`

- [ ] **Step 1: Write the failing test**

```csharp
// src/GeminiCode.Tests/TodoToolTests.cs
using System.Text.Json;
using GeminiCode.Tools;

namespace GeminiCode.Tests;

public class TodoToolTests
{
    private static Dictionary<string, JsonElement> P(object o)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(o));
        return doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
    }

    [Fact]
    public void ParseItems_ReadsStatusMarkers()
    {
        var items = TodoStore.ParseItems("- [ ] pending one\n- [~] in progress\n- [x] done one");
        Assert.Equal(3, items.Count);
        Assert.Equal(TodoStatus.Pending, items[0].Status);
        Assert.Equal(TodoStatus.InProgress, items[1].Status);
        Assert.Equal(TodoStatus.Done, items[2].Status);
        Assert.Equal("pending one", items[0].Text);
    }

    [Fact]
    public async Task Execute_ReplacesListAndRendersCounts()
    {
        var store = new TodoStore();
        var tool = new TodoTool(store);
        var r = await tool.ExecuteAsync(P(new { items = "- [x] a\n- [ ] b" }), default);
        Assert.True(r.Success);
        Assert.Equal(2, store.Items.Count);
        Assert.Contains("1/2", r.Output); // 1 of 2 done
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test src/GeminiCode.Tests/GeminiCode.Tests.csproj --filter "FullyQualifiedName~TodoToolTests"`
Expected: FAIL.

- [ ] **Step 3: Implement**

```csharp
// src/GeminiCode/Tools/TodoTool.cs
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GeminiCode.Tools;

public enum TodoStatus { Pending, InProgress, Done }
public record TodoItem(string Text, TodoStatus Status);

public class TodoStore
{
    private readonly List<TodoItem> _items = new();
    public IReadOnlyList<TodoItem> Items => _items;

    public void Replace(IEnumerable<TodoItem> items) { _items.Clear(); _items.AddRange(items); }

    private static readonly Regex Line = new(@"^\s*-\s*\[\s*([ x~])\s*\]\s*(.+?)\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static List<TodoItem> ParseItems(string text)
    {
        var result = new List<TodoItem>();
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var m = Line.Match(raw);
            if (!m.Success) continue;
            var status = m.Groups[1].Value.ToLowerInvariant() switch
            {
                "x" => TodoStatus.Done,
                "~" => TodoStatus.InProgress,
                _ => TodoStatus.Pending
            };
            result.Add(new TodoItem(m.Groups[2].Value, status));
        }
        return result;
    }

    public string Render()
    {
        if (_items.Count == 0) return "(no todos)";
        var sb = new StringBuilder();
        foreach (var it in _items)
        {
            var (mark, _) = it.Status switch
            {
                TodoStatus.Done => ("[x]", 0),
                TodoStatus.InProgress => ("[~]", 0),
                _ => ("[ ]", 0)
            };
            sb.AppendLine($"  {mark} {it.Text}");
        }
        var done = _items.Count(i => i.Status == TodoStatus.Done);
        sb.Append($"  {done}/{_items.Count} done");
        return sb.ToString();
    }
}

public class TodoTool : ITool
{
    private readonly TodoStore _store;
    public string Name => "Todo";
    public RiskLevel Risk => RiskLevel.Low;
    public TodoTool(TodoStore store) => _store = store;

    public Task<ToolResult> ExecuteAsync(Dictionary<string, JsonElement> parameters, CancellationToken ct)
    {
        var payload = parameters.TryGetValue("items", out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()! : "";
        _store.Replace(TodoStore.ParseItems(payload));
        return Task.FromResult(new ToolResult(Name, true, _store.Render()));
    }

    public string DescribeAction(Dictionary<string, JsonElement> p) => "Update todo list";
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test src/GeminiCode.Tests/GeminiCode.Tests.csproj --filter "FullyQualifiedName~TodoToolTests"`
Expected: PASS (2 tests).

- [ ] **Step 5: Add tag, register (shared store), document**

`ToolCallParser.cs`:
```csharp
    private static readonly Regex TodoTagPattern = new(
        @"\[\s*TODO\s*\](.*?)\[\s*/\s*TODO\s*\]",
        RegexOptions.Singleline | RegexOptions.Compiled | RegexOptions.IgnoreCase);
```
```csharp
        textContent = TodoTagPattern.Replace(textContent, m =>
        { toolCalls.Add(MakeToolCall("Todo", new() { ["items"] = m.Groups[1].Value.Trim() })); return ""; });
```

`Program.cs` — create the store once and register:
```csharp
        var todoStore = new TodoStore();
        toolRegistry.Register(new TodoTool(todoStore));
```

`SystemPrompt.cs` Action Tags: add
```
### Task list (keep the user informed on multi-step work)
[TODO]
- [x] completed step
- [~] in-progress step
- [ ] pending step
[/TODO]
```

- [ ] **Step 6: Build + full test + commit**

Run: `dotnet build` → succeeds. Run full tests → green.

```bash
git add -A
git commit -m "feat: in-CLI todo tracker tool with [TODO] action tag"
```

---

## Task 12: Thinking/loading Spinner

**Files:**
- Create: `src/GeminiCode/Cli/Spinner.cs`
- Modify: `src/GeminiCode/Agent/AgentOrchestrator.cs` (wrap waits)

Spinner is timing/console-bound — verified manually, not unit-tested.

- [ ] **Step 1: Implement the Spinner**

```csharp
// src/GeminiCode/Cli/Spinner.cs
using System.Diagnostics;

namespace GeminiCode.Cli;

/// <summary>A single-line braille spinner with elapsed seconds. Dispose to stop and clear the line.</summary>
public sealed class Spinner : IDisposable
{
    private static readonly char[] Frames = { '⠋','⠙','⠹','⠸','⠼','⠴','⠦','⠧','⠇','⠏' };
    private readonly string _label;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;
    private readonly bool _ansi;

    private Spinner(string label)
    {
        _label = label;
        _ansi = AnsiHelper.Enabled;
        _loop = Run(_cts.Token);
    }

    public static Spinner Start(string label = "Thinking")
    {
        if (!AnsiHelper.Enabled)
            Console.Write($"{label}... ");
        return new Spinner(label);
    }

    private async Task Run(CancellationToken ct)
    {
        if (!_ansi) return; // static "Thinking..." already printed; no animation
        var sw = Stopwatch.StartNew();
        int i = 0;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var frame = Frames[i++ % Frames.Length];
                Console.Write($"\r{AnsiHelper.Cyan}{frame}{AnsiHelper.Reset} {AnsiHelper.Dim}{_label} {sw.Elapsed.TotalSeconds:0.0}s{AnsiHelper.Reset}");
                await Task.Delay(100, ct);
            }
        }
        catch (TaskCanceledException) { }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _loop.Wait(300); } catch { }
        if (_ansi)
            Console.Write("\r" + new string(' ', Math.Min(Console.WindowWidth - 1, 60)) + "\r");
        else
            Console.WriteLine();
        _cts.Dispose();
    }
}
```

- [ ] **Step 2: Wrap the response waits in `AgentOrchestrator.cs`**

Add `using GeminiCode.Cli;` (already present). Wrap each `await _browser.WaitForResponseAsync(...)` with a spinner. Example for the main send path in `SendAndProcessAsync`:

```csharp
        GeminiResponse? response;
        using (Spinner.Start("Waiting for Gemini"))
        {
            response = await _browser.WaitForResponseAsync(_settings.ResponseTimeoutSeconds, ct, baseline.textLen, baseline.preCount);
        }
```

Apply the same wrap to: the init wait in `InitializeSessionAsync`, the follow-up wait in `ExecuteToolCallsAsync`, and the re-orientation ack wait in `ReorientAfterModelSwitchAsync`. Remove the now-redundant static `Console.WriteLine("...Sending results to Gemini...")` only if it duplicates the spinner label (keep the section divider).

- [ ] **Step 3: Build + smoke check**

Run: `dotnet build` → succeeds.
Run full tests → green (no test depends on spinner).

- [ ] **Step 4: Manual verification (record result)**

Launch the app (`dotnet run --project src/GeminiCode`), send a message, and confirm: the braille spinner animates with rising elapsed seconds during the wait, then the line clears cleanly before the response renders (no leftover spinner characters). Note the result in the commit message.

- [ ] **Step 5: Commit**

```bash
git add src/GeminiCode/Cli/Spinner.cs src/GeminiCode/Agent/AgentOrchestrator.cs
git commit -m "feat: thinking spinner with elapsed time around Gemini waits"
```

---

## Task 13: UsageTracker + footer + /usage

**Files:**
- Create: `src/GeminiCode/Agent/UsageTracker.cs`
- Test: `src/GeminiCode.Tests/UsageTrackerTests.cs`
- Modify: `AgentOrchestrator.cs` (record + footer), `CommandHandler.cs` (`/usage`), `Program.cs` (construct + inject)

- [ ] **Step 1: Write the failing test**

```csharp
// src/GeminiCode.Tests/UsageTrackerTests.cs
using GeminiCode.Agent;

namespace GeminiCode.Tests;

public class UsageTrackerTests
{
    [Fact]
    public void Estimate_IsCeilingOfCharsOverFour()
    {
        Assert.Equal(0, UsageTracker.Estimate(""));
        Assert.Equal(1, UsageTracker.Estimate("abcd"));   // 4/4
        Assert.Equal(2, UsageTracker.Estimate("abcde"));  // 5/4 -> 2
    }

    [Fact]
    public void RecordTurn_AccumulatesTotalsAndDelta()
    {
        var u = new UsageTracker();
        u.RecordSent(new string('x', 400));     // 100 tok
        u.RecordReceived(new string('y', 800));  // 200 tok
        Assert.Equal(1, u.TurnCount);
        Assert.Equal(300, u.TotalTokens);
        Assert.Equal(300, u.LastTurnTokens);

        u.RecordSent(new string('x', 40));       // 10 tok
        u.RecordReceived(new string('y', 40));   // 10 tok
        Assert.Equal(2, u.TurnCount);
        Assert.Equal(320, u.TotalTokens);
        Assert.Equal(20, u.LastTurnTokens);
    }

    [Fact]
    public void Footer_ContainsTokensTurnAndDelta()
    {
        var u = new UsageTracker();
        u.RecordSent(new string('x', 4000));
        u.RecordReceived(new string('y', 4000));
        var f = u.Footer();
        Assert.Contains("turn 1", f);
        Assert.Contains("tok", f);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test src/GeminiCode.Tests/GeminiCode.Tests.csproj --filter "FullyQualifiedName~UsageTrackerTests"`
Expected: FAIL.

- [ ] **Step 3: Implement**

```csharp
// src/GeminiCode/Agent/UsageTracker.cs
namespace GeminiCode.Agent;

/// <summary>Estimates token usage (chars/4) — there is no real Gemini token API in this browser-driven setup.</summary>
public class UsageTracker
{
    private long _pending;

    public int TurnCount { get; private set; }
    public long TotalSentTokens { get; private set; }
    public long TotalReceivedTokens { get; private set; }
    public long LastTurnTokens { get; private set; }
    public long TotalTokens => TotalSentTokens + TotalReceivedTokens;

    public static int Estimate(string? text)
        => string.IsNullOrEmpty(text) ? 0 : (int)Math.Ceiling(text.Length / 4.0);

    public void RecordSent(string? text)
    {
        var t = Estimate(text);
        TotalSentTokens += t;
        _pending += t;
    }

    public void RecordReceived(string? text)
    {
        var t = Estimate(text);
        TotalReceivedTokens += t;
        _pending += t;
        TurnCount++;
        LastTurnTokens = _pending;
        _pending = 0;
    }

    private static string Fmt(long tokens)
        => tokens >= 1000 ? $"{tokens / 1000.0:0.0}k" : tokens.ToString();

    public string Footer()
        => $"ctx ≈ {Fmt(TotalTokens)} tok · turn {TurnCount} · +{Fmt(LastTurnTokens)} this msg (est.)";

    public string Breakdown()
        => $"""
            Usage (estimated, chars/4 — no real token API):
              Turns:          {TurnCount}
              Sent total:     {Fmt(TotalSentTokens)} tok
              Received total: {Fmt(TotalReceivedTokens)} tok
              Context total:  {Fmt(TotalTokens)} tok
              Last turn:      {Fmt(LastTurnTokens)} tok
            """;
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test src/GeminiCode.Tests/GeminiCode.Tests.csproj --filter "FullyQualifiedName~UsageTrackerTests"`
Expected: PASS (3 tests).

- [ ] **Step 5: Wire into `AgentOrchestrator.cs`**

Add `private readonly UsageTracker _usage;` field, add `UsageTracker usage` as the final constructor parameter (after `pluginRegistry`), assign it.

- In `SendAndProcessAsync`, after `var message = _conversation.PrepareMessage(userMessage);` add `_usage.RecordSent(message);`. After a non-null `response` is obtained (post-send), add `_usage.RecordReceived(response.Text);`. Just before the final `return result;`, print the footer:

```csharp
        Console.WriteLine($"{AnsiHelper.Dim}{_usage.Footer()}{AnsiHelper.Reset}");
        return result;
```

- In `ExecuteToolCallsAsync`, after building `combinedResults` add `_usage.RecordSent(combinedResults);`, and after a non-null `followUp` add `_usage.RecordReceived(followUp.Text);`. (Do not print the footer here — it prints once per turn in `SendAndProcessAsync`.)

- [ ] **Step 6: Add `/usage` to `CommandHandler.cs`**

Add field `private readonly UsageTracker _usage;`, add `UsageTracker usage` constructor param, assign. Add case:

```csharp
            case "/usage":
                Console.WriteLine(_usage.Breakdown());
                return true;
```

Add `/usage` to the static help text and the new-commands list.

- [ ] **Step 7: Construct + inject in `Program.cs`**

```csharp
        var usage = new UsageTracker();
```
Pass `usage` as the final arg to `AgentOrchestrator(...)` and to `CommandHandler(...)`.

- [ ] **Step 8: Build + full test**

Run: `dotnet build` → succeeds. Run full tests → green.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "feat: estimated usage tracker with per-turn footer and /usage command"
```

---

## Task 14: Final integration build, test, and acceptance

**Files:** none (verification + docs).

- [ ] **Step 1: Full build + test**

Run: `dotnet build` → `Build succeeded`, 0 errors.
Run: `dotnet test src/GeminiCode.Tests/GeminiCode.Tests.csproj` → all green. Confirm the count grew by the new test classes.

- [ ] **Step 2: Acceptance — "drop the brainstorm plugin"**

1. `dotnet run --project src/GeminiCode` → startup line shows `Loaded N plugin(s): brainstorm, simplify, …`.
2. `/plugins` lists brainstorm + simplify; `/help` shows them under Plugins.
3. `/brainstorm add a settings page` runs the multi-phase workflow.
4. Quit. Move `plugins/brainstorm/` out of the output `plugins/` dir (or the source dir + rebuild). Relaunch (or `/reload`). Confirm `/plugins` and `/brainstorm` no longer show brainstorm.
5. Restore the folder, `/reload` → brainstorm is back. Record results.

- [ ] **Step 3: Acceptance — tools, spinner, usage**

Confirm in a live session: `/tools` lists CopyFile/MoveFile/MakeDir/DeleteFile/Glob/Todo/Skill with risk labels; a delete via `[DELETE]` lands a copy in `.gemini/trash/`; the spinner animates during waits; the usage footer prints after each response and `/usage` shows the breakdown.

- [ ] **Step 4: Update memory + final commit**

Update `C:\Users\table\.claude\projects\D--CodeProjects-GeminiCode\memory\project_overview.md` to note the plugin system, expanded tool set, and UX additions (tools count, plugin folders). 

```bash
git add -A
git commit -m "docs: record plugin system, extended tools, and UX in project notes"
```

---

## Self-Review notes (for the implementer)

- **Spec coverage:** Plugin system (Tasks 1–7), new commands (Task 8 + `/usage` in 13), file-mgmt/Glob/Todo tools (Tasks 9–11), spinner + usage (Tasks 12–13), pre-flight (Task 0), tests throughout, acceptance (Task 14). All spec sections map to tasks.
- **Type consistency:** `PluginManifest.FromParsed/ToWorkflow`, `PluginRegistry.ByName/ByCommand/Reload`, `UsageTracker.RecordSent/RecordReceived/Footer/Breakdown/LastTurnTokens`, `TodoStore.ParseItems/Replace/Render`, tool names (`CopyFile`,`MoveFile`,`MakeDir`,`DeleteFile`,`Glob`,`Todo`,`Skill`) are used identically across tasks and in `Program.cs` registration.
- **Constructor ordering:** `AgentOrchestrator` gains `pluginRegistry` then `usage` as the final two params; `CommandHandler` gains `pluginRegistry`, `toolRegistry`, then `usage`. Program.cs must pass them in that exact order.
- **No external requests:** all new tools are local FS only; usage is estimated. Constraint honored.
