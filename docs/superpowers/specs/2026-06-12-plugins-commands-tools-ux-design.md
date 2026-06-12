# GeminiCode "Claude-like" Upgrade — Design Spec

**Date:** 2026-06-12
**Status:** Approved (pending spec review)
**Scope:** Droppable plugin system, new commands, extended secure tool set, response UX (spinner + estimated usage).

## Goal

Make GeminiCode's experience closer to Claude Code along four axes the user requested:

1. A **droppable plugin/skill system** — dropping a plugin folder makes Gemini able to use it "just like Claude" uses skills.
2. **More slash commands** that improve the workflow.
3. An **extensive, secure tool set** for more efficient usage.
4. **Response UX** — a thinking/loading spinner and an estimated token/usage readout.

## Constraints (from project memory)

- **Zero external network requests except Gemini.** No web-fetch tools. No real token API — usage is *estimated*.
- **Path sandboxing** to the working directory for every tool.
- **Per-action permission gating** stays the gate for all new tools.
- Tools are invoked through **action tags** declared in `SystemPrompt.cs` and parsed by `ToolCallParser` (the live mechanism — not function-call syntax).
- Plugins are **data, never executed code** — dropping a plugin can never run arbitrary code.

## Pre-flight: "up to date"

Before feature work, establish a green baseline:

- `dotnet build` the solution; fix any breakage.
- Run the existing test suite (`src/GeminiCode.Tests`) and confirm pass.

No dependency upgrades or upstream sync. The repository is the source of truth.

---

## 1. Plugin system (marquee feature)

### Discovery

Two roots, scanned at startup and on `/reload`:

- `plugins/` — shipped plugins, repo root.
- `<workdir>/.gemini/plugins/` — user-dropped plugins.

Each plugin is a folder containing a `SKILL.md`.

### `SKILL.md` format

```markdown
---
name: brainstorm
description: Turn ideas into fully formed designs through dialogue
command: /brainstorm
argHint: <topic>
---
## Phase: Understanding context
<instructions…>

## Phase: Exploring approaches
<instructions…>
```

- Frontmatter keys: `name` (required), `description` (required), `command` (optional; defaults to `/<name>`), `argHint` (optional).
- Body: `## Phase: <name>` headers split the body into ordered `WorkflowPhase`s. **No phase headers → the whole body is a single-shot instruction** (one phase, no tool-call orchestration loop required, just one injected prompt).

### Components (`src/GeminiCode/Plugins/`)

- `FrontmatterParser` — minimal `key: value` reader for the `---` block. **No code evaluation.** Returns a `Dictionary<string,string>` + the remaining body.
- `PluginManifest` — record: `Name`, `Description`, `Command`, `ArgHint`, `IReadOnlyList<WorkflowPhase> Phases`, `string Source` (file path), `bool IsSingleShot`.
- `PluginLoader` — scans roots, parses each `SKILL.md`, returns `List<PluginManifest>`. Skips and warns on malformed files (never throws on a bad plugin).
- `PluginRegistry` — holds loaded manifests; lookup by command and by name; `Reload()` re-scans.

### Building a workflow from a manifest

A `PluginManifest` with phases maps directly to a `WorkflowDefinition`:

- Each `## Phase: <name>` section → `WorkflowPhase(name, bodyText, activeForm, allowToolCalls: true)`.
- The last phase defaults to `allowToolCalls: true` as well (workflows already vary this per-phase; brainstorm's final "Summary" phase will set it via a frontmatter or convention — see below).
- `ActiveForm` defaults to `"<name>..."`.

Convention for `allowToolCalls`: a phase whose name is `Summary` (case-insensitive) gets `allowToolCalls: false`, matching the existing brainstorm workflow. This keeps the converted plugin behavior identical. (Documented in the shipped plugin files.)

### Invocation paths (both "just like Claude")

1. **User-triggered:** `CommandHandler.TryHandleAsync` keeps its hardcoded built-in commands, then on an unknown `/command` consults `PluginRegistry`. A match runs the plugin: phased → `WorkflowRunner.RunAsync`; single-shot → `AgentOrchestrator.SendAndProcessAsync` with the body as the prompt. **Argument convention:** the text after the command (e.g. the brainstorm topic) is passed as the `{input}` variable, available to every phase via the existing `WorkflowPhase.ExpandPrompt` `{key}` substitution. Shipped plugins reference `{input}` where the old factories used `{userInput}`.
2. **Gemini self-invoked:** `SystemPrompt` gains an `## Available Skills` section listing each plugin (`name` · `description` · trigger) plus a `[SKILL:name]args[/SKILL]` action tag. `ToolCallParser` recognizes `[SKILL:...]`; the orchestrator resolves it against the registry and injects the skill body as the next step. **Self-invocation runs the single-shot body** (not the multi-phase loop) to avoid re-entrancy into `WorkflowRunner`; phased workflows remain user-triggered via `/command`. This is documented in the prompt so Gemini's expectation matches behavior.

### Migration: brainstorm + simplify become dropped plugins

- Delete `src/GeminiCode/Agent/Workflows/BrainstormWorkflow.cs` and `SimplifyWorkflow.cs`.
- Re-ship their content as `plugins/brainstorm/SKILL.md` and `plugins/simplify/SKILL.md`.
- `CommandHandler.HandleBrainstormAsync` / `HandleSimplifyAsync` are removed; `/brainstorm` and `/simplify` now resolve through `PluginRegistry`.
- **Acceptance test of the whole system:** deleting `plugins/brainstorm/` removes the skill from `/plugins`, `/help`, the system prompt, and `/brainstorm`; dropping it back (and `/reload` or restart) restores all of it.

---

## 2. New commands

Added to `CommandHandler` (built-ins) and `/help` made dynamic (appends loaded plugins):

| Command | Behavior |
|---|---|
| `/plugins` | List loaded plugins (name, description, source root). |
| `/reload` | Re-scan plugin roots live; report added/removed. No restart needed. |
| `/tools` | List registered tools with their risk level. |
| `/usage` | Print the estimated-usage breakdown (session total, per-turn, turn count). |
| `/init` | Generate a starter `GEMINI.md` in the working directory (skeleton: project overview, conventions, commands). |
| `/compact` | Summarize current context + start new chat + restore summary — reuses `SessionContext` save/new/restore. |

`/help` lists built-ins, then an "Available skills (plugins)" section sourced from `PluginRegistry`.

---

## 3. Extended tool set (secure)

All new tools: implement `ITool`, registered in `Program.cs`, gated by `PermissionGate`, sandboxed via `PathSandbox`, with a new action tag documented in `SystemPrompt` and parsed by `ToolCallParser`. `RiskAssessor` updated for the new tools.

### File management (`src/GeminiCode/Tools/`)

| Tool | Tag | Risk | Notes |
|---|---|---|---|
| `CopyFile` | `[COPY src>>>dst]` | Medium | Both paths sandboxed. |
| `MoveFile` | `[MOVE src>>>dst]` | Medium | Rename/move, both sandboxed. |
| `MakeDir` | `[MKDIR]path[/MKDIR]` | Medium | Creates directories within sandbox. |
| `DeleteFile` | `[DELETE]path[/DELETE]` | **High** | **Moves target to `<workdir>/.gemini/trash/<timestamped>` instead of hard delete** — reversible, secure. |

Delete-to-trash makes destructive ops recoverable and is the security story for the highest-risk tool.

### Glob

| Tool | Tag | Risk |
|---|---|---|
| `GlobTool` | `[GLOB]**/*.cs[/GLOB]` | Low |

Fast filename pattern matching, complements `SearchFiles`/`Grep`. Sandboxed root; returns matching relative paths.

### Todo tracker

| Tool | Tag | Risk |
|---|---|---|
| `TodoTool` | `[TODO]...[/TODO]` | Low |

Local task list Gemini maintains; rendered in the terminal like Claude's task list. Pure in-process state (a `TodoStore` holding items with status `pending`/`in_progress`/`done`). The `[TODO]` payload is a small line-based format (`- [ ] task` / `- [~] task` / `- [x] task`) that replaces the current list. No external calls, no disk required (optionally mirrored to `.gemini/todos.md` for visibility — included).

---

## 4. Response UX

### Spinner (`src/GeminiCode/Cli/Spinner.cs`)

- Braille frames `⠋⠙⠹⠸⠼⠴⠦⠧⠇⠏` + elapsed seconds: `Thinking ⠋ 3.2s`.
- Runs on a background `Task`; `Start(label)` returns an `IDisposable` that stops + clears the line on dispose.
- Wrapped around every `WaitForResponseAsync` call site in `AgentOrchestrator` (send, tool-result follow-up, init, re-orientation) and in `WorkflowRunner`.
- Single-line, carriage-return based, self-clearing — never corrupts subsequent output. Respects the existing `AnsiHelper` capability detection (degrades to a static "Thinking..." when ANSI unsupported).

### Usage tracker (`src/GeminiCode/Agent/UsageTracker.cs`)

- Estimates tokens as `ceil(chars / 4)` for each sent message and each received response.
- Tracks: session running total, per-message delta, turn count.
- After each response, the orchestrator prints a footer: `ctx ≈ 12.4k tok · turn 7 · +1.1k this msg` (clearly labeled estimate).
- `/usage` prints a fuller breakdown (sent vs received, totals, turns).
- Injected into `AgentOrchestrator` via constructor; updated in `SendAndProcessAsync` / `ExecuteToolCallsAsync` where messages are sent and responses received.

---

## 5. Wiring (`Program.cs`)

- Construct `PluginLoader` (roots: appdir `plugins/` + `<workdir>/.gemini/plugins/`), load into `PluginRegistry`.
- Register new tools in the `toolRegistry` block.
- Construct `UsageTracker`; pass to `AgentOrchestrator`.
- Pass `PluginRegistry` to `CommandHandler` and `SystemPrompt` generation (so `## Available Skills` reflects loaded plugins).
- `SystemPrompt.Generate(...)` gains a `plugins` parameter for the skills list + new tool tag docs.

---

## 6. Testing

Unit tests (`src/GeminiCode.Tests`):

- `FrontmatterParser` — valid block, missing block, malformed lines, body extraction.
- `PluginLoader` — phased plugin → ordered phases; single-shot plugin → one phase; malformed `SKILL.md` skipped with warning; both roots merged; `.gemini/plugins/` overrides shipped same-name.
- `UsageTracker` — estimation math, running totals, per-turn deltas.
- `GlobTool` — pattern matches, sandbox confinement, no matches.
- File-management tools — copy/move/mkdir success in sandbox; rejection outside sandbox; `DeleteFile` moves to `.gemini/trash/` and original is gone.
- `TodoTool` — parse `[TODO]` payload into items; status transitions; list replacement.

Manual verification (resists unit testing): spinner rendering/elapsed/clear, usage footer formatting, end-to-end "drop the brainstorm plugin" acceptance.

## Out of scope (YAGNI)

- MultiEdit / batch edit (not selected).
- Any web/network tool (violates zero-external-requests).
- DLL/executable plugins (markdown-data only — security).
- Real token counts (no API; estimates only).

## File summary

**New:**
- `src/GeminiCode/Plugins/FrontmatterParser.cs`
- `src/GeminiCode/Plugins/PluginManifest.cs`
- `src/GeminiCode/Plugins/PluginLoader.cs`
- `src/GeminiCode/Plugins/PluginRegistry.cs`
- `src/GeminiCode/Tools/CopyFileTool.cs`
- `src/GeminiCode/Tools/MoveFileTool.cs`
- `src/GeminiCode/Tools/MakeDirTool.cs`
- `src/GeminiCode/Tools/DeleteFileTool.cs`
- `src/GeminiCode/Tools/GlobTool.cs`
- `src/GeminiCode/Tools/TodoTool.cs` (+ `TodoStore`)
- `src/GeminiCode/Cli/Spinner.cs`
- `src/GeminiCode/Agent/UsageTracker.cs`
- `plugins/brainstorm/SKILL.md`, `plugins/simplify/SKILL.md`
- Corresponding test files.

**Modified:**
- `Program.cs` (wiring), `CommandHandler.cs` (new commands + plugin fallthrough + dynamic help), `AgentOrchestrator.cs` (spinner + usage + `[SKILL:]`), `WorkflowRunner.cs` (spinner), `SystemPrompt.cs` (skills + new tags), `ToolCallParser.cs` (new tags), `RiskAssessor.cs` (new tools).

**Deleted:**
- `src/GeminiCode/Agent/Workflows/BrainstormWorkflow.cs`, `SimplifyWorkflow.cs` (migrated to plugins).
