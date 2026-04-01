# Design: File Upload, Context Preservation, Agent Profiles & Workflow Commands

**Date:** 2026-04-01
**Approach:** Unified Context Engine — shared infrastructure across all four features

---

## 1. File Upload & Enhanced @file

### File Upload via WebView2

- New method `BrowserBridge.UploadFileAsync(string filePath)` automates Gemini's file attachment UI
- Uses JavaScript to set files on the hidden `<input type="file">` element or DataTransfer API for drag-drop simulation
- Supports any file Gemini accepts (images, PDFs, text files, etc.)
- Exposed via `@upload <path>` context tag for explicit file upload

### Enhanced @file Syntax

| Syntax | Behavior |
|--------|----------|
| `@file path/to/file` | Uploads the actual file to Gemini (new default) |
| `@file path/to/file L200-L500` | Injects lines 200-500 as text into the message |
| `@file path/to/file L200` | Injects from line 200 to EOF |

- Line-range variant stays as text injection (selecting a specific slice, not the whole file)
- `@upload` is an alias that always uploads regardless

### Implementation

- `BrowserBridge.UploadFileAsync(string filePath)`:
  1. Execute JS to find file input element in Gemini's DOM
  2. Create a `DataTransfer` object with the file
  3. Dispatch `change` event on the input element
  4. Wait for upload indicator in DOM (file attached confirmation)
- `ContextProcessor` updated:
  - Parse new `L200-L500` syntax via regex `@file\s+(\S+)(?:\s+L(\d+)(?:-L(\d+))?)?`
  - If no line range: call `UploadFileAsync` instead of text injection
  - If line range present: existing text injection behavior with updated syntax

---

## 2. Context Preservation

### SessionContext Class

- Located at `Agent/SessionContext.cs`
- Auto-maintains `.gemini/session-context.md` in the working directory
- Updated incrementally as the session progresses

### Context File Structure

```markdown
# Session Context
- **Started:** 2026-04-01 14:30:00
- **Agent:** code-reviewer
- **Working Directory:** D:\Projects\MyApp

## Modified Files
- src/App.cs (lines 10-50 edited)
- src/Models/User.cs (created)

## Key Decisions
- Using repository pattern for data access
- SQLite chosen over PostgreSQL for local dev

## Tool Call Summary
- ReadFile: src/App.cs (turn 2)
- EditFile: src/App.cs lines 10-50 (turn 3)
- RunCommand: dotnet build (turn 4, success)

## Conversation Summary
- User asked to implement user authentication
- Decided on JWT-based auth with refresh tokens
- Implemented login endpoint, working on registration next
```

### Auto-tracking

- `AgentOrchestrator` calls `SessionContext.LogToolCall()` after each tool execution
- `SessionContext.LogFileModified()` called when WriteFile/EditFile tools succeed
- Conversation summaries: after every 5 turns, append a 2-3 sentence summary extracted from recent exchanges (Gemini asked to summarize via a lightweight prompt)
- Key decisions: extracted when Gemini's response contains decision language ("decided", "chose", "going with", "approach:")

### Commands

| Command | Behavior |
|---------|----------|
| `/save` | Force-write current context to `.gemini/session-context.md` |
| `/restore` | Upload session-context.md to new chat + re-send system prompt |
| `/context` | Show current session context in terminal |

### Restore Flow

1. User runs `/restore` (or `/new --restore`)
2. GeminiCode uploads `.gemini/session-context.md` as a file attachment
3. Sends system prompt with active agent profile
4. Sends rehydration message: "Review the attached session context file. This is a continuation of a previous session. Acknowledge what was being worked on and continue from where we left off."
5. Waits for Gemini acknowledgment

---

## 3. Agent Profiles

### Built-in Profiles

Stored as embedded markdown files in `Agent/Profiles/`:

| Profile | Focus |
|---------|-------|
| `general` | Default — current system prompt behavior |
| `code-reviewer` | Code quality, bugs, security, best practices |
| `architect` | System design, patterns, high-level planning |
| `debugger` | Diagnosing issues, root cause analysis, fix strategies |
| `refactorer` | Improving code structure, reducing duplication, clean code |

### Profile File Format

```markdown
# Role
You are a senior code reviewer in an automated coding environment.

# Focus
- Code quality and readability
- Security vulnerabilities (OWASP top 10)
- Performance anti-patterns
- Test coverage gaps

# Behavior
- Always read the full file before commenting
- Cite specific line numbers
- Suggest fixes, don't just flag problems
- Prioritize: security > correctness > performance > style
```

### Custom Instructions (GEMINI.md)

- GeminiCode checks for `GEMINI.md` in the working directory root at startup
- Contents appended to any active profile's system prompt
- Supports project-specific rules, conventions, context
- Re-read on `/cd` (working directory change)

### System Prompt Composition

```
[Base Instructions (action tags, environment, rules)]
[Active Profile Content]
[GEMINI.md Content (if exists)]
```

### Commands

| Command | Behavior |
|---------|----------|
| `/agent` | List available profiles (built-in + any in `.gemini/agents/`) |
| `/agent <name>` | Switch to profile, starts new chat with new system prompt |
| `/agent info` | Show current active profile details |

### Custom Profile Discovery

- Built-in profiles from embedded resources
- Project-local profiles from `.gemini/agents/<name>.md`
- Project-local profiles override built-in ones with the same name

---

## 4. Workflow Commands (/simplify, /brainstorm)

### WorkflowRunner

- Located at `Agent/WorkflowRunner.cs`
- Orchestrates multi-phase prompts to Gemini
- Each workflow is a list of `WorkflowPhase` objects
- Terminal renders progress with phase status indicators

### WorkflowPhase

```csharp
class WorkflowPhase
{
    string Name;           // "Code Reuse Review"
    string Prompt;         // Template with {diff}, {context} placeholders
    string ActiveForm;     // "Reviewing code reuse..." (spinner text)
    bool AllowToolCalls;   // Whether Gemini can use tools during this phase
}
```

### Terminal Progress Display

```
/simplify
── Workflow: Simplify ──────────────────────────
  [1/4] Analyzing changes          ✓
  [2/4] Code Reuse Review          ✓
  [3/4] Code Quality Review        ...
  [4/4] Efficiency Review
────────────────────────────────────────────────
```

### /simplify Workflow

**Phase 1: Analyze Changes**
- CLI runs `git diff` (or `git diff HEAD` for staged)
- If no git changes, uses recently modified files
- Collects diff text for subsequent phases

**Phase 2: Code Reuse Review**
- Prompt: "Review this diff for code reuse opportunities. Search for existing utilities and helpers that could replace newly written code. Flag duplicated functionality and inline logic that could use existing utilities. Be specific — cite file paths and function names.\n\n{diff}"
- Tool calls enabled (so Gemini can search the codebase)

**Phase 3: Code Quality Review**
- Prompt: "Review this diff for code quality issues: redundant state, parameter sprawl, copy-paste patterns, leaky abstractions, stringly-typed code, unnecessary comments. Be specific.\n\n{diff}"
- Tool calls enabled

**Phase 4: Efficiency Review**
- Prompt: "Review this diff for efficiency issues: unnecessary work, missed concurrency, hot-path bloat, recurring no-op updates, unnecessary existence checks, memory leaks, overly broad operations. Be specific.\n\n{diff}"
- Tool calls enabled

**Phase 5: Fix**
- Aggregates findings from phases 2-4
- Prompt: "Here are the review findings. Fix each issue directly. If a finding is a false positive, skip it.\n\n{aggregated_findings}"
- Tool calls enabled

### /brainstorm Workflow

**Phase 1: Understand**
- Prompt: "The user wants to brainstorm: {user_input}. First, explore the current project context — check relevant files, recent git history, and existing patterns. Then ask 3-5 clarifying questions to understand purpose, constraints, and success criteria. Present questions as a numbered list."
- Tool calls enabled

**Phase 2: Explore Approaches** (after user answers questions)
- Prompt: "Based on the requirements gathered, propose 2-3 different approaches with trade-offs. Lead with your recommendation and explain why."
- Tool calls enabled

**Phase 3: Design**
- Prompt: "Based on the chosen approach, present a detailed design covering: architecture, components, data flow, error handling, and testing strategy. Be specific about file locations and interfaces."
- Tool calls enabled

**Phase 4: Refine** (interactive — user can give feedback)
- If user provides feedback, send: "Revise the design based on this feedback: {feedback}"
- Loop until user approves

### Workflow Registration

- Workflows registered in a `WorkflowRegistry` dictionary
- `/simplify` and `/brainstorm` handled by `CommandHandler` which delegates to `WorkflowRunner`
- Extensible — new workflows can be added by defining phase lists

---

## 5. New Files Summary

| File | Purpose |
|------|---------|
| `Agent/SessionContext.cs` | Context preservation and session logging |
| `Agent/AgentProfile.cs` | Profile loading and composition |
| `Agent/WorkflowRunner.cs` | Multi-phase workflow orchestration |
| `Agent/Workflows/SimplifyWorkflow.cs` | /simplify phase definitions |
| `Agent/Workflows/BrainstormWorkflow.cs` | /brainstorm phase definitions |
| `Agent/Profiles/general.md` | Default agent profile |
| `Agent/Profiles/code-reviewer.md` | Code reviewer profile |
| `Agent/Profiles/architect.md` | Architect profile |
| `Agent/Profiles/debugger.md` | Debugger profile |
| `Agent/Profiles/refactorer.md` | Refactorer profile |

## 6. Modified Files Summary

| File | Changes |
|------|---------|
| `Browser/BrowserBridge.cs` | Add `UploadFileAsync()` method |
| `Cli/ContextProcessor.cs` | Enhanced @file parsing, @upload tag |
| `Cli/CommandHandler.cs` | New commands: /agent, /save, /restore, /context, /simplify, /brainstorm |
| `Agent/AgentOrchestrator.cs` | Integrate SessionContext logging, profile-aware system prompts |
| `Agent/SystemPrompt.cs` | Compose prompts from base + profile + GEMINI.md |
| `Program.cs` | Initialize SessionContext, AgentProfile, WorkflowRunner; load GEMINI.md |
