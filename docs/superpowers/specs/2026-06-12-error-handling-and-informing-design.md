# Better Error Handling & Informing — Design Spec

**Date:** 2026-06-12
**Status:** Approved (pending spec review)
**Scope:** Detect and clearly inform on the currently-silent failure modes — especially mid-session logout — and classify all error surfaces into consistent, actionable messages.

## Goal

Replace silent failures and bare error strings with clear, actionable messaging. Specifically:

1. **Mid-session logout** (highest priority) — detect it, pause, prompt the user to sign in, and auto-resume the failed message.
2. **Model unavailable / switch failures** — clear messaging listing available modes.
3. **UI / DOM breakage** — when Gemini changes its page and selectors stop matching, say so explicitly instead of timing out silently.
4. **Tool failures** — distinguish failure causes with tailored hints.
5. **Generic / unexpected errors** — classify caught exceptions into friendly categories with next steps.

## Constraints

- **Zero added latency on the happy path.** No per-send readiness check — diagnose only *after* a failure (`diagnose-on-failure`).
- Reuse existing browser capabilities: `CheckAuthenticatedAsync`, `RunHealthCheckAsync` (returns `Dictionary<string,bool>` of `chatInput`/`sendButton`/`responseContainer`), `CheckForLimitAsync` (returns `LimitInfo?`), `BringToFront`.
- Keep the existing usage-limit panel (`AgentOrchestrator.HandleLimitDetected`) as the canonical limit UX.
- Decision logic must be pure and unit-testable without a live browser.

## Current behavior (what we're improving)

- Auth is checked only at startup (`Program.WaitForAuth`). A mid-session logout causes `WaitForResponseAsync` to return null → generic "Gemini response timed out. Type your message to retry, or /new to start fresh."
- `AgentOrchestrator.SendAndProcessAsync` already re-checks `CheckForLimitAsync()` on timeout and calls `HandleLimitDetected` — that path stays.
- `CommandHandler.HandleModelAsync` already lists available modes on a not-found switch — we route it through the new presenter for consistency.
- `AgentOrchestrator.ExecuteToolCallsAsync` shows `✗ {first line of output}` for tool failures.
- `CliEngine` catches `OperationCanceledException` (browser-closed → restart/exit prompt) and generic `Exception ex` → bare `Error: {ex.Message}`.

---

## 1. FailureClassifier (pure, unit-tested)

**File:** `src/GeminiCode/Browser/FailureClassifier.cs`

```csharp
namespace GeminiCode.Browser;

public enum FailureKind { Unknown, UsageLimit, NotAuthenticated, UiBroken }

public record FailureDiagnosis(
    FailureKind Kind,
    IReadOnlyList<string> MissingElements,   // populated for UiBroken
    LimitInfo? Limit);                        // populated for UsageLimit

public static class FailureClassifier
{
    /// <summary>Pure classification of a send failure from already-gathered signals.
    /// Precedence: UsageLimit → NotAuthenticated → UiBroken → Unknown.</summary>
    public static FailureDiagnosis Classify(bool authenticated, IReadOnlyDictionary<string, bool> domHealth, LimitInfo? limit)
    {
        if (limit != null)
            return new FailureDiagnosis(FailureKind.UsageLimit, Array.Empty<string>(), limit);

        if (!authenticated)
            return new FailureDiagnosis(FailureKind.NotAuthenticated, Array.Empty<string>(), null);

        var missing = domHealth.Where(kv => !kv.Value).Select(kv => kv.Key).ToList();
        if (missing.Count > 0)
            return new FailureDiagnosis(FailureKind.UiBroken, missing, null);

        return new FailureDiagnosis(FailureKind.Unknown, Array.Empty<string>(), null);
    }
}
```

`LimitInfo` is the existing type in `GeminiCode.Browser`.

## 2. BrowserBridge.DiagnoseFailureAsync (thin, browser-bound)

**File:** `src/GeminiCode/Browser/BrowserBridge.cs` (add method)

```csharp
/// <summary>Gather signals and classify why a send failed. Browser-bound; logic lives in FailureClassifier.</summary>
public async Task<FailureDiagnosis> DiagnoseFailureAsync()
{
    LimitInfo? limit = null;
    bool authed = true;
    Dictionary<string, bool> health = new();
    try { limit = await CheckForLimitAsync(); } catch { }
    try { authed = await CheckAuthenticatedAsync(); } catch { authed = false; }
    try { health = await RunHealthCheckAsync(); } catch { }
    return FailureClassifier.Classify(authed, health, limit);
}
```

Best-effort try/catch on each probe: a probe that itself throws should not crash diagnosis (e.g., if the WebView is mid-teardown).

## 3. ErrorPresenter (pure string formatters, unit-tested)

**File:** `src/GeminiCode/Cli/ErrorPresenter.cs`

A static class returning formatted multi-line strings (using `AnsiHelper`). Returning strings (not writing to `Console`) keeps it testable. Methods:

```csharp
namespace GeminiCode.Cli;

public enum GenericErrorCategory { BrowserClosed, WebViewScript, Timeout, Other }

public static class ErrorPresenter
{
    public static string AuthLost();                                  // sign-in instructions + browser-forward note
    public static string UiBroken(IReadOnlyList<string> missing);     // names missing elements + --discover-selectors
    public static string ModelUnavailable(string requested, IEnumerable<string> available);
    public static string ToolFailure(string toolName, string output); // classifies output → tailored hint
    public static string Generic(GenericErrorCategory category, string message);
}
```

Behavioral requirements per method (asserted by tests):

- **`AuthLost`**: contains "signed out", instruction to sign in via the browser window, that the last message will be re-sent, and the "press Enter to retry / type exit to abort" affordance.
- **`UiBroken`**: contains each missing element name and the literal `--discover-selectors`.
- **`ModelUnavailable`**: contains the requested name and every available mode, plus `/model`.
- **`ToolFailure`**: classifies `output` into a hint —
  - contains "Access denied" → sandbox hint ("path is outside the working directory; use a path inside it").
  - contains "not found" → file/target-not-found hint.
  - contains "Unknown tool" → unknown-tool hint.
  - contains "Permission denied" → permission hint (re-run and approve).
  - otherwise → generic "tool failed" with the raw first line.
- **`Generic`**: maps each `GenericErrorCategory` to a friendly line + next step (BrowserClosed → restart/exit; WebViewScript → page may have changed, try /new or --discover-selectors; Timeout → retry or /new; Other → shows message).

## 4. Wiring

### 4a. Auth-loss recovery + auto-resume — `AgentOrchestrator`

In `SendAndProcessAsync`, the current post-timeout block re-checks the limit and then prints the generic timeout message. Replace the "still null after limit check" tail with a diagnosis switch:

```csharp
if (response == null)
{
    var diagnosis = await _browser.DiagnoseFailureAsync();
    switch (diagnosis.Kind)
    {
        case FailureKind.UsageLimit:
            HandleLimitDetected(diagnosis.Limit!);
            return null;
        case FailureKind.NotAuthenticated:
            return await RecoverAuthAndResendAsync(message, ct);
        case FailureKind.UiBroken:
            Console.WriteLine(ErrorPresenter.UiBroken(diagnosis.MissingElements));
            return null;
        default:
            Console.WriteLine(ErrorPresenter.Generic(GenericErrorCategory.Timeout,
                "Gemini did not respond in time."));
            return null;
    }
}
```

(The existing pre-send `CheckForLimitAsync` and the `response.Limit != null` checks remain unchanged.)

New helper on `AgentOrchestrator`:

```csharp
/// <summary>Logged-out recovery: prompt the user to sign in, wait, then re-send the original message.</summary>
private async Task<string?> RecoverAuthAndResendAsync(string message, CancellationToken ct)
{
    _browser.BringToFront();
    while (true)
    {
        Console.WriteLine(ErrorPresenter.AuthLost());
        Console.Write("> ");
        var input = Console.ReadLine()?.Trim();
        if (input != null && (input.Equals("exit", StringComparison.OrdinalIgnoreCase)
                           || input.Equals("/exit", StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine($"{AnsiHelper.Dim}Aborted this message. Sign in and resend when ready.{AnsiHelper.Reset}");
            return null;
        }

        bool authed;
        try { authed = await _browser.CheckAuthenticatedAsync(); } catch { authed = false; }
        if (!authed)
        {
            Console.WriteLine($"{AnsiHelper.Yellow}Still signed out — finish signing in, then press Enter.{AnsiHelper.Reset}");
            continue;
        }

        Console.WriteLine($"{AnsiHelper.Green}Signed in. Re-sending your message...{AnsiHelper.Reset}");
        var baseline = await _browser.CaptureBaselineAsync();
        await _browser.SendMessageAsync(message);
        GeminiResponse? resent;
        using (Spinner.Start("Waiting for Gemini"))
            resent = await _browser.WaitForResponseAsync(_settings.ResponseTimeoutSeconds, ct, baseline.textLen, baseline.preCount);

        if (resent == null)
        {
            Console.WriteLine(ErrorPresenter.Generic(GenericErrorCategory.Timeout,
                "Still no response after re-sending. Try /new."));
            return null;
        }
        if (resent.Limit != null) { HandleLimitDetected(resent.Limit); return null; }
        return await ProcessResponseAsync(resent, ct);
    }
}
```

Note: `message` here is the already-prepared message (`_conversation.PrepareMessage(userMessage)` result) so the resend is byte-identical to the original attempt.

### 4b. Tool failures — `AgentOrchestrator.ExecuteToolCallsAsync`

Where a failed `toolResult` is currently printed as `✗ {first line}`, route through the presenter:

```csharp
else
{
    Console.WriteLine($" {AnsiHelper.Red}✗{AnsiHelper.Reset}");
    Console.WriteLine(ErrorPresenter.ToolFailure(tool.Name, toolResult.Output));
}
```

And the unknown-tool branch (`tool == null`) uses `ErrorPresenter.ToolFailure(toolCall.Name, "Unknown tool")`.

### 4c. Generic exceptions — `CliEngine.RunAsync`

The existing `catch (OperationCanceledException) when (browser closed)` block stays (it already offers restart/exit). The generic `catch (Exception ex)` becomes:

```csharp
catch (Exception ex)
{
    var category = ex is System.Runtime.InteropServices.COMException
                   || ex.Message.Contains("WebView", StringComparison.OrdinalIgnoreCase)
                   || ex.Message.Contains("ExecuteScript", StringComparison.OrdinalIgnoreCase)
        ? GenericErrorCategory.WebViewScript
        : GenericErrorCategory.Other;
    Console.WriteLine(ErrorPresenter.Generic(category, ex.Message));
}
```

### 4d. Model unavailable — `CommandHandler.HandleModelAsync`

The not-found branch (which currently prints "Mode 'X' not found." then enumerates `available`) is replaced with:

```csharp
var available = doc.RootElement.GetProperty("available").EnumerateArray()
    .Select(e => e.GetString() ?? "").Where(s => s.Length > 0);
Console.WriteLine(ErrorPresenter.ModelUnavailable(modeName, available));
```

## 5. File summary

**New:**
- `src/GeminiCode/Browser/FailureClassifier.cs` (`FailureKind`, `FailureDiagnosis`, `FailureClassifier`)
- `src/GeminiCode/Cli/ErrorPresenter.cs` (`GenericErrorCategory`, `ErrorPresenter`)
- `src/GeminiCode.Tests/FailureClassifierTests.cs`
- `src/GeminiCode.Tests/ErrorPresenterTests.cs`

**Modified:**
- `src/GeminiCode/Browser/BrowserBridge.cs` — add `DiagnoseFailureAsync`
- `src/GeminiCode/Agent/AgentOrchestrator.cs` — diagnosis switch + `RecoverAuthAndResendAsync` + tool-failure presenter
- `src/GeminiCode/Cli/CliEngine.cs` — classified generic exception handling
- `src/GeminiCode/Cli/CommandHandler.cs` — model-unavailable via presenter

## 6. Testing

Unit tests (`src/GeminiCode.Tests/`):

- **`FailureClassifierTests`** — UsageLimit precedence (limit present wins even if unauth/UI-broken); NotAuthenticated when `authenticated=false` and no limit; UiBroken lists exactly the false-valued health keys; Unknown when authed + all health true + no limit.
- **`ErrorPresenterTests`** — `AuthLost` contains the sign-in + retry/exit affordances; `UiBroken` names each missing element + `--discover-selectors`; `ModelUnavailable` contains requested + each available + `/model`; `ToolFailure` returns the sandbox hint for "Access denied", not-found hint for "not found", unknown-tool hint for "Unknown tool", permission hint for "Permission denied", and a generic hint otherwise; `Generic` maps each category to its expected next-step text.

Manual verification (needs live WebView2): the end-to-end logout → prompt → sign-in → auto-resend flow, `BringToFront` behavior, and that the diagnosis correctly fires on a real expired session.

## Out of scope (YAGNI)

- Automatic re-login (the user signs in; we detect and resume).
- Per-send pre-flight readiness check (diagnose-on-failure only).
- Mid-conversation model-unavailability detection via DOM (unreliable; model messaging is switch-time only).
