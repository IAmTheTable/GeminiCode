# GeminiCode CLI — Design Specification

## Overview

GeminiCode is a C# .NET 9 console application that provides a Claude Code-like agentic CLI experience interfacing with Google Gemini through an embedded WebView2 browser. It enables developers to chat with Gemini and have it autonomously read/write files, search codebases, and run shell commands — all with per-action permission prompts.

### Key Constraints

- **Browser-based only**: Interfaces with Gemini via the web UI, not the API
- **Zero external requests**: No network traffic except to Gemini (google.com domains)
- **Business-friendly licensing**: All dependencies must be MIT, BSD, Apache 2.0, or Microsoft Software License (WebView2 SDK). Each dependency's license must be reviewed before inclusion.
- **Manual sign-in**: User authenticates to Google manually in the browser window
- **Windows target**: Windows 10/11, leveraging pre-installed WebView2 runtime
- **Path sandboxing**: All file tools are restricted to the working directory. Shell commands execute within the working directory.

## Architecture

Single .NET 9 console process with a WinForms WebView2 window on a separate STA thread.

```
┌─────────────────────────────────────────────────┐
│                  GeminiCode.exe                  │
│                                                  │
│  ┌──────────────┐       ┌─────────────────────┐ │
│  │  CLI Engine   │◄────►│  Agent Orchestrator  │ │
│  │  (Console UI) │       │                      │ │
│  └──────────────┘       └──────────┬──────────┘ │
│                                     │            │
│                          ┌──────────┴──────────┐ │
│                          │                      │ │
│  ┌──────────────┐  ┌─────┴──────┐  ┌─────────┐ │
│  │   Browser     │  │   Tool     │  │Permission│ │
│  │   Bridge      │  │   System   │  │  Gate    │ │
│  │  (WebView2)   │  │            │  │          │ │
│  └──────────────┘  └────────────┘  └─────────┘ │
│         │                                        │
│  ┌──────┴───────┐                               │
│  │  WebView2     │  ← Separate WinForms window  │
│  │  Window       │    on STA thread              │
│  └──────────────┘                               │
└─────────────────────────────────────────────────┘
```

### Components

- **CLI Engine**: Renders terminal UI (prompt, markdown responses, diffs, permission dialogs). Runs on the main console thread. Uses `System.Console` with ANSI escape codes — no heavy TUI framework dependencies.
- **Agent Orchestrator**: Core loop — sends prompts to Gemini, parses tool calls from responses, routes through Permission Gate, executes tools, sends results back to Gemini.
- **Browser Bridge**: Manages WebView2 lifecycle, injects JavaScript to send messages and scrape responses. All public methods return `Task` and internally dispatch to the STA thread via `Control.Invoke`/`Control.BeginInvoke`. Callers (on the console thread) `await` these methods.
- **Tool System**: Implements the 6 tools (ReadFile, WriteFile, EditFile, ListFiles, SearchFiles, RunCommand). All file tools enforce path sandboxing to the working directory.
- **Permission Gate**: Intercepts every tool call, prompts user for approval before execution.
- **WebView2 Window**: Minimal WinForms `Form` hosting a `WebView2` control, running on a dedicated STA thread.

### Threading Model

- **Main thread**: Console input/output, Agent Orchestrator loop, Permission Gate prompts.
- **STA thread**: WinForms message pump, WebView2 control. All WebView2 API calls (`ExecuteScriptAsync`, `NavigateToString`, etc.) MUST run on this thread.
- **Cross-thread contract**: `BrowserBridge` exposes `async Task<T>` methods. Internally, each method dispatches work to the STA thread via `Control.Invoke` and returns the result. The `AgentOrchestrator` on the main thread calls these with `await`.
- **Shutdown protocol**: If the user closes the WebView2 window directly, the `FormClosed` event fires on the STA thread, which sets a `CancellationToken` observed by the main thread. The CLI then prompts "Browser closed. Restart browser or exit? [r/e]". If the CLI `/exit` command is used, it signals the STA thread to close the form, waits for thread join, then exits the process.

## Browser Bridge & Gemini Interaction

### WebView2 Lifecycle

1. App starts, spawns a WinForms STA thread, creates a `Form` with a `WebView2` control
2. Navigates to `https://gemini.google.com`
3. User signs in manually in the visible window
4. CLI detects authentication by polling for DOM elements indicating logged-in state (chat input textarea becomes available). Polls every 2 seconds with a 5-minute timeout. On timeout, prompts user: "Still waiting for sign-in. Press Enter to keep waiting, or type 'exit' to quit."
5. **DOM health-check**: Before declaring "Ready", verifies all critical DOM elements are detectable (chat input, send button, response container). If any are missing, warns: "Some UI elements not found — Gemini may have updated. Check selectors.json."
6. CLI signals "Ready" and presents the prompt

### Sending Messages

JavaScript injected via `CoreWebView2.ExecuteScriptAsync()`:
1. Locates the chat input element
2. Sets its value to the prompt text (system prompt + user message + tool results)
3. Triggers the send button click

The system prompt is prepended to the first message of each conversation, instructing Gemini on the tool-use protocol.

### Reading Responses

After sending, a JS polling script is injected that:
1. Watches for Gemini's response to finish (streaming/typing indicator disappears)
2. Extracts the full response text from the last assistant message DOM element
3. Returns it to C# via `ExecuteScriptAsync` return value or `WebMessageReceived` event

**Response polling timeout**: 120 seconds (default). If the response is not complete within this window, the agent loop aborts the current turn and displays: "Gemini response timed out. Type your message to retry, or /new to start fresh." Configurable via `settings.json` in `%APPDATA%/GeminiCode/` (key: `responseTimeoutSeconds`).

### Session Management

- WebView2 persists cookies/session in a user data folder (`%APPDATA%/GeminiCode/WebView2Data`) — sign in once
- If session expires mid-conversation, CLI detects DOM changes and notifies user to re-authenticate
- Each CLI session can start a new Gemini conversation or continue the current one
- New conversation: inject JS to click "New chat"
- Long conversations: monitor for context limits and warn user

### DOM Resilience

Gemini's DOM can change without notice. Mitigations:
- Use flexible selectors (aria labels, roles, data attributes over brittle class names)
- Centralize all selectors in a `selectors.json` file in `%APPDATA%/GeminiCode/` — editable by users without recompilation. The app ships a default `selectors.json` and copies it on first run. Format: `{"chatInput": "selector", "sendButton": "selector", "responseContainer": "selector", "typingIndicator": "selector", "newChatButton": "selector"}`
- Log warnings when expected elements aren't found
- Startup DOM health-check validates all selectors before declaring ready (see WebView2 Lifecycle step 5)

## Tool-Use Protocol

### System Prompt

Prepended to the first message of each conversation:

```
When you need to perform actions, respond with tool calls in this exact format:

<tool_call>
{"name": "ToolName", "parameters": {"param1": "value1"}}
</tool_call>

You may include multiple tool calls in one response. You may also include explanatory text before/after tool calls.

Available tools:
- ReadFile: {"path": "string"}
- WriteFile: {"path": "string", "content": "string"}
- EditFile: {"path": "string", "old_string": "string", "new_string": "string"}
- ListFiles: {"pattern": "string", "path": "string (optional)"}
- SearchFiles: {"pattern": "string", "path": "string (optional)", "include": "string (optional)"}
- RunCommand: {"command": "string", "timeout_ms": "number (optional)"}

After each tool execution, you will receive the result. Continue your work based on the results.

IMPORTANT: Always use tool calls for file operations. Never just show code in a code block and ask the user to save it manually.
```

### Parsing

1. Receive full response text from Gemini
2. **Pre-processing**: Strip markdown code fences that may wrap tool_call blocks. Gemini often wraps XML in ` ```xml ... ``` ` or ` ```json ... ``` `. The parser first unwraps these before looking for tool_call tags.
3. Regex extract all `<tool_call>...</tool_call>` blocks
4. Deserialize each as JSON, validate against known tool schemas. Tolerate minor key variations (e.g., `"args"` treated as alias for `"parameters"`).
5. Text outside tool_call blocks is displayed to the user as conversational output

### Multi-Turn Enforcement

If Gemini responds without proper `<tool_call>` format when it should have (e.g., shows a code block saying "save this to file.cs"):
1. Auto-send correction: `"Please use the tool_call format to perform file operations instead of showing code blocks. Wrap your action in <tool_call>...</tool_call> as specified."`
2. Parse the retry response
3. After 2 failed retries, display raw response and let user decide

**Protocol drift prevention**: In conversations exceeding 10 turns, append a condensed tool-format reminder to each user message: `"(Remember: use <tool_call> for all file/shell actions.)"` This helps prevent Gemini from drifting away from the structured format as the conversation grows.

**Fallback if Gemini categorically refuses**: If Gemini blocks or strips `<tool_call>` tags (e.g., content policy), fall back to detecting fenced code blocks with file path annotations (e.g., ` ```cs:src/Foo.cs `) and presenting them as proposed writes for user confirmation. This is a degraded mode — the CLI warns the user that structured tool calls are unavailable.

### Tool Result Delivery

Results sent back to Gemini as follow-up messages:

```
<tool_result>
{"name": "ToolName", "success": true, "output": "..."}
</tool_result>
```

Errors:
```
<tool_result>
{"name": "ToolName", "success": false, "error": "Permission denied by user"}
</tool_result>
```

**Tool output truncation**: Tool results are capped at 100KB. If output exceeds this (e.g., large file read, verbose command output), it is truncated with a note appended: `"[Output truncated at 100KB. Request specific sections if needed.]"` This prevents exceeding Gemini's input field limits and context window.

### Multiple Tool Calls

When Gemini returns multiple `<tool_call>` blocks in a single response:
- **Execution order**: Sequential, in the order they appear in the response. No parallel execution.
- **Denial handling**: If the user denies a tool call, its result is sent as `"Permission denied"` but remaining tool calls in the batch still execute (each with its own permission prompt). This gives Gemini partial results to adapt.
- **Result bundling**: All results from a batch are sent back as a single message containing multiple `<tool_result>` blocks, in the same order as the original tool calls.

## Permission Gate

### Per-Action Approval

Every tool call passes through the Permission Gate before execution:

```
Tool Call Received
       │
       ▼
  Display to user:
  • Tool name
  • Parameters
  • Risk summary
       │
  User prompted:
  [y]es / [n]o / [a]lways
       │
   ┌───┴───┐
   │       │
  y/a      n
   │       │
Execute   Send "Permission denied"
tool      back to Gemini as tool_result
```

### CLI Display

```
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
 WriteFile
 Path: src/Program.cs
 Content: (42 lines)
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
 Allow? [y]es / [n]o / [a]lways
```

- `EditFile`: show a diff view
- `RunCommand`: show full command string, flag destructive patterns (rm, del, format, etc.)

### Path Sandboxing

All file tools (`ReadFile`, `WriteFile`, `EditFile`, `ListFiles`, `SearchFiles`) enforce path restriction:
- Paths are resolved to absolute paths and normalized (resolving `../` traversal, symlinks)
- If the resolved path is outside the working directory, the tool returns an error: `"Access denied: path is outside the working directory."`
- `RunCommand` sets the subprocess working directory to the configured working directory
- This is **hard enforcement**, not best-effort. The path check happens before any file I/O.

### Session Allowlist

- `[a]lways` adds that tool type to a session allowlist, **bound to the current working directory**. If the working directory changes via `/cd`, the allowlist is cleared (see `/cd` behavior below).
- When the user chooses `[a]lways`, display a confirmation: `"Auto-approving all [ToolName] calls within [working directory] for this session."`
- **Design decision**: Allowlist granularity is tool-type-level (not path-pattern-level). The working directory sandbox is the accepted safety boundary. This is a deliberate trade-off: simpler UX at the cost of blanket approval within the workdir. Users who need finer control should use `[y]es` per-action.
- Allowlist resets on app restart (never persisted to disk)
- `RunCommand` cannot be added to allowlist — always prompts individually

### Risk Indicators

- **Low risk** (read-only): ReadFile, ListFiles, SearchFiles
- **Medium risk** (writes): WriteFile, EditFile — show affected path
- **High risk** (shell): RunCommand — always show full command, flag destructive patterns

### EditFile Matching Semantics

- `old_string` matching is **case-sensitive** and **exact** (after line-ending normalization)
- Line endings are normalized to `\n` for matching purposes, then restored to the file's original line-ending style on write
- If `old_string` is not found: return error to Gemini: `"Edit failed: old_string not found in file. Read the file first to get exact content."`
- If `old_string` matches multiple locations: return error to Gemini: `"Edit failed: old_string matches N locations. Provide more context to make the match unique."`
- The diff shown to the user for approval highlights the exact lines being changed

## CLI Engine & User Experience

### Startup

```
$ GeminiCode.exe [optional: working directory path]

GeminiCode v0.1.0
Opening Gemini browser...
Waiting for sign-in... (sign in via the browser window)
Authenticated. Ready.

Working directory: D:\MyProject
>
```

### Prompt Loop

- `>` prompt for user input
- Multi-line input via trailing `\` or `/paste` command
- User message sent to Gemini (system prompt on first message)
- Gemini's conversational text rendered to terminal with markdown formatting
- Tool calls intercepted and routed through Permission Gate

### Slash Commands

- `/help` — list available commands
- `/clear` — clear terminal
- `/new` — start a new Gemini conversation
- `/browser` — bring browser window to foreground
- `/history` — show conversation history
- `/allowlist` — show current session allowlist
- `/status` — show session state: auth status, working directory, conversation turn count, active allowlist entries
- `/cd <path>` — change working directory. **Side effects**: (1) updates the sandbox root to the new path, (2) clears the session allowlist entirely, (3) displays confirmation: `"Working directory changed to [path]. Allowlist cleared."`
- `/exit` — quit

### Markdown Rendering

- Code blocks: syntax-highlighted with ANSI colors, language label
- Inline code: highlighted background
- Bold/italic: ANSI bold/italic
- Lists, headers: basic indentation and formatting
- Minimal approach — good enough, not pixel-perfect

### Error Handling

- Browser crash: detect via WebView2 events, attempt restart, notify user
- Gemini rate limits: detect "too many requests" in DOM, back off and notify
- Network loss: detect navigation failures, notify user

## Dependencies

All business-friendly:

| Dependency | Purpose | License |
|---|---|---|
| .NET 9 | Runtime | MIT |
| Microsoft.Web.WebView2 | Embedded browser | Microsoft Software License (redistribution-friendly, no royalties, reviewed for business use) |
| System.Text.Json | JSON parsing | MIT (part of .NET) |
| System.Windows.Forms | WebView2 host window | MIT (part of .NET) |

No other external dependencies. Markdown rendering and ANSI formatting implemented in-house with standard `System.Console`.

## Project Structure

```
GeminiCode/
├── src/
│   ├── GeminiCode/
│   │   ├── Program.cs                 # Entry point
│   │   ├── GeminiCode.csproj          # Project file
│   │   ├── Cli/
│   │   │   ├── CliEngine.cs           # Main prompt loop, input/output
│   │   │   ├── CommandHandler.cs      # Slash command routing
│   │   │   ├── MarkdownRenderer.cs    # ANSI markdown rendering
│   │   │   └── AnsiHelper.cs          # ANSI escape code utilities
│   │   ├── Browser/
│   │   │   ├── BrowserBridge.cs       # WebView2 lifecycle & JS injection
│   │   │   ├── BrowserWindow.cs       # WinForms Form hosting WebView2
│   │   │   ├── DomSelectors.cs        # C# model class that deserializes selectors.json (no hard-coded selectors)
│   │   │   └── SessionMonitor.cs      # Auth state detection
│   │   ├── Agent/
│   │   │   ├── AgentOrchestrator.cs   # Core agent loop
│   │   │   ├── ToolCallParser.cs      # Parse <tool_call> from responses
│   │   │   ├── SystemPrompt.cs        # System prompt template
│   │   │   └── ConversationManager.cs # Conversation state tracking
│   │   ├── Tools/
│   │   │   ├── ITool.cs               # Tool interface
│   │   │   ├── ToolRegistry.cs        # Tool registration & lookup
│   │   │   ├── ReadFileTool.cs
│   │   │   ├── WriteFileTool.cs
│   │   │   ├── EditFileTool.cs
│   │   │   ├── ListFilesTool.cs
│   │   │   ├── SearchFilesTool.cs
│   │   │   └── RunCommandTool.cs
│   │   └── Permissions/
│   │       ├── PermissionGate.cs      # Per-action approval logic
│   │       ├── RiskAssessor.cs        # Risk level classification
│   │       └── SessionAllowlist.cs    # Session-scoped allowlist
│   └── GeminiCode.Tests/
│       ├── ToolCallParserTests.cs     # Parsing, markdown unwrapping, key aliasing
│       ├── EditFileToolTests.cs       # String matching, line endings, multi-match
│       ├── PathSandboxTests.cs        # Path traversal, normalization, rejection
│       ├── RiskAssessorTests.cs       # Destructive pattern detection
│       ├── PermissionGateTests.cs     # Allowlist logic, RunCommand exclusion
│       └── MarkdownRendererTests.cs   # ANSI output for code blocks, headers, etc.
├── docs/
│   └── superpowers/
│       └── specs/
│           └── 2026-03-26-geminicode-cli-design.md
└── README.md
```

## Testing Strategy

Unit tests cover the critical-path components that are most likely to break:

- **ToolCallParser**: Regex extraction, markdown fence unwrapping, JSON deserialization, key alias handling (`args` vs `parameters`), malformed input resilience
- **EditFileTool**: Exact match, line-ending normalization, not-found error, multi-match error
- **Path sandboxing**: `../` traversal rejection, symlink resolution, absolute path outside workdir
- **RiskAssessor**: Destructive command patterns (rm, del, format, git clean, etc.)
- **PermissionGate**: Allowlist add/check, RunCommand exclusion, session reset
- **MarkdownRenderer**: Code block rendering, header/list formatting, ANSI output correctness

Integration testing of the Browser Bridge and Gemini interaction requires manual testing against the live Gemini web UI — these cannot be reliably automated due to DOM dependency. A manual test checklist will be maintained for this.
