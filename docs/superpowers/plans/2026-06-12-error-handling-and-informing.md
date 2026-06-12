# Better Error Handling & Informing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Detect the currently-silent failure modes (especially mid-session logout) and classify every error surface into clear, actionable messages.

**Architecture:** A pure `FailureClassifier` turns already-gathered browser signals (auth, DOM health, usage limit) into a `FailureDiagnosis`; `BrowserBridge.DiagnoseFailureAsync` gathers those signals on a send failure (diagnose-on-failure, zero happy-path cost). A pure `ErrorPresenter` formats each error class into consistent, testable strings. The orchestrator routes a failed send through the diagnosis (with a logout→prompt→auto-resend recovery loop), and `CliEngine`/`CommandHandler` adopt the presenter for generic and model errors.

**Tech Stack:** C# / .NET 9 (net9.0-windows), xUnit, WinForms+WebView2 (unchanged).

---

## Conventions (read once)

- **Build:** `dotnet build` (from repo root `D:\CodeProjects\GeminiCode`).
- **All tests:** `dotnet test src/GeminiCode.Tests/GeminiCode.Tests.csproj`
- **One class:** append `--filter "FullyQualifiedName~ClassName"`.
- Tests are xUnit (`[Fact]`, `Assert.*`). `AnsiHelper.Enabled` is false in tests (no `Initialize()`), so color codes are empty — assert on plain text substrings, which is robust.
- Pure classes return strings/records (no `Console` writes) so they're unit-testable; the browser-bound and interactive parts are verified manually.
- Existing types you will reference (do not redefine): `GeminiCode.Browser.LimitInfo`, `GeminiCode.Browser.GeminiResponse`, `GeminiCode.Cli.AnsiHelper`, `GeminiCode.Cli.Spinner`.

---

## Task 0: Establish green baseline

**Files:** none (verification only).

- [ ] **Step 1: Build**

Run: `dotnet build`
Expected: `Build succeeded`, 0 errors.

- [ ] **Step 2: Run the full test suite**

Run: `dotnet test src/GeminiCode.Tests/GeminiCode.Tests.csproj`
Expected: `Passed! Failed: 0, Passed: 127`. Record the count; later tasks must keep it green.

---

## Task 1: FailureClassifier (pure)

**Files:**
- Create: `src/GeminiCode/Browser/FailureClassifier.cs`
- Test: `src/GeminiCode.Tests/FailureClassifierTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
// src/GeminiCode.Tests/FailureClassifierTests.cs
using GeminiCode.Browser;

namespace GeminiCode.Tests;

public class FailureClassifierTests
{
    private static Dictionary<string, bool> Health(bool input = true, bool send = true, bool resp = true)
        => new() { ["chatInput"] = input, ["sendButton"] = send, ["responseContainer"] = resp };

    [Fact]
    public void Classify_LimitPresent_WinsOverEverything()
    {
        var limit = new LimitInfo("limit reached", null, null);
        var d = FailureClassifier.Classify(authenticated: false, Health(input: false), limit);
        Assert.Equal(FailureKind.UsageLimit, d.Kind);
        Assert.Same(limit, d.Limit);
    }

    [Fact]
    public void Classify_NotAuthenticated_WhenNoLimit()
    {
        var d = FailureClassifier.Classify(authenticated: false, Health(), null);
        Assert.Equal(FailureKind.NotAuthenticated, d.Kind);
    }

    [Fact]
    public void Classify_UiBroken_ListsMissingElements()
    {
        var d = FailureClassifier.Classify(authenticated: true, Health(send: false, resp: false), null);
        Assert.Equal(FailureKind.UiBroken, d.Kind);
        Assert.Contains("sendButton", d.MissingElements);
        Assert.Contains("responseContainer", d.MissingElements);
        Assert.DoesNotContain("chatInput", d.MissingElements);
    }

    [Fact]
    public void Classify_Unknown_WhenHealthyAndAuthedAndNoLimit()
    {
        var d = FailureClassifier.Classify(authenticated: true, Health(), null);
        Assert.Equal(FailureKind.Unknown, d.Kind);
        Assert.Empty(d.MissingElements);
    }
}
```

Note: `LimitInfo` is defined as `record LimitInfo(string Message, string? RetryAfter, string? SuggestedModel)` in `BrowserBridge.cs` — the test constructs it with all three args. The classifier itself never constructs `LimitInfo`; it only passes it through.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/GeminiCode.Tests/GeminiCode.Tests.csproj --filter "FullyQualifiedName~FailureClassifierTests"`
Expected: FAIL — `FailureClassifier` / `FailureKind` / `FailureDiagnosis` do not exist.

- [ ] **Step 3: Write the implementation**

```csharp
// src/GeminiCode/Browser/FailureClassifier.cs
namespace GeminiCode.Browser;

public enum FailureKind { Unknown, UsageLimit, NotAuthenticated, UiBroken }

public record FailureDiagnosis(
    FailureKind Kind,
    IReadOnlyList<string> MissingElements,
    LimitInfo? Limit);

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

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/GeminiCode.Tests/GeminiCode.Tests.csproj --filter "FullyQualifiedName~FailureClassifierTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Run full suite**

Run: `dotnet test src/GeminiCode.Tests/GeminiCode.Tests.csproj`
Expected: 131 passing (127 + 4).

- [ ] **Step 6: Commit**

```bash
git add src/GeminiCode/Browser/FailureClassifier.cs src/GeminiCode.Tests/FailureClassifierTests.cs
git commit -m "feat: pure failure classifier for send-failure diagnosis"
```

---

## Task 2: ErrorPresenter (pure)

**Files:**
- Create: `src/GeminiCode/Cli/ErrorPresenter.cs`
- Test: `src/GeminiCode.Tests/ErrorPresenterTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
// src/GeminiCode.Tests/ErrorPresenterTests.cs
using GeminiCode.Cli;

namespace GeminiCode.Tests;

public class ErrorPresenterTests
{
    [Fact]
    public void AuthLost_HasSignInAndRetryAffordances()
    {
        var s = ErrorPresenter.AuthLost();
        Assert.Contains("signed out", s, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sign in", s, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Enter", s);
        Assert.Contains("exit", s, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("re-sent", s, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UiBroken_NamesEachMissingElementAndDiscoverFlag()
    {
        var s = ErrorPresenter.UiBroken(new[] { "sendButton", "responseContainer" });
        Assert.Contains("sendButton", s);
        Assert.Contains("responseContainer", s);
        Assert.Contains("--discover-selectors", s);
    }

    [Fact]
    public void ModelUnavailable_ListsRequestedAndAvailableAndCommand()
    {
        var s = ErrorPresenter.ModelUnavailable("ultra", new[] { "flash", "pro", "thinking" });
        Assert.Contains("ultra", s);
        Assert.Contains("flash", s);
        Assert.Contains("pro", s);
        Assert.Contains("thinking", s);
        Assert.Contains("/model", s);
    }

    [Theory]
    [InlineData("Access denied: path 'x' is outside the working directory '...'.", "working directory")]
    [InlineData("Not found: foo.txt", "exist")]
    [InlineData("Unknown tool: Frobnicate", "not a recognized tool")]
    [InlineData("Permission denied by user", "approve")]
    [InlineData("git exploded unexpectedly", "git exploded unexpectedly")]
    public void ToolFailure_ClassifiesOutputIntoHints(string output, string expectedHintFragment)
    {
        var s = ErrorPresenter.ToolFailure("SomeTool", output);
        Assert.Contains("SomeTool", s);
        Assert.Contains(expectedHintFragment, s, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(GenericErrorCategory.BrowserClosed, "restart")]
    [InlineData(GenericErrorCategory.WebViewScript, "--discover-selectors")]
    [InlineData(GenericErrorCategory.Timeout, "/new")]
    [InlineData(GenericErrorCategory.Other, "boom")]
    public void Generic_MapsCategoryToNextStep(GenericErrorCategory cat, string expectedFragment)
    {
        var s = ErrorPresenter.Generic(cat, "boom");
        Assert.Contains(expectedFragment, s, StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/GeminiCode.Tests/GeminiCode.Tests.csproj --filter "FullyQualifiedName~ErrorPresenterTests"`
Expected: FAIL — `ErrorPresenter` / `GenericErrorCategory` do not exist.

- [ ] **Step 3: Write the implementation**

```csharp
// src/GeminiCode/Cli/ErrorPresenter.cs
namespace GeminiCode.Cli;

public enum GenericErrorCategory { BrowserClosed, WebViewScript, Timeout, Other }

/// <summary>Pure formatters for error/failure messaging. Return strings (no Console writes) so they are testable.</summary>
public static class ErrorPresenter
{
    public static string AuthLost() =>
        $"""
        {AnsiHelper.Yellow}⚠ You appear to be signed out of Gemini.{AnsiHelper.Reset}
          The browser window has been brought forward.
          Sign in there, then press {AnsiHelper.Bold}Enter{AnsiHelper.Reset} to retry — your last message will be re-sent.
          {AnsiHelper.Dim}(or type 'exit' to abort just this message){AnsiHelper.Reset}
        """;

    public static string UiBroken(IReadOnlyList<string> missing) =>
        $"""
        {AnsiHelper.Red}✗ Gemini's page doesn't look the way GeminiCode expects.{AnsiHelper.Reset}
          Missing UI element(s): {AnsiHelper.Bold}{string.Join(", ", missing)}{AnsiHelper.Reset}
          Gemini may have changed its layout. Re-discover selectors by launching with
          {AnsiHelper.Cyan}--discover-selectors{AnsiHelper.Reset} and updating selectors.json.
        """;

    public static string ModelUnavailable(string requested, IEnumerable<string> available) =>
        $"""
        {AnsiHelper.Red}✗ Model '{requested}' isn't available.{AnsiHelper.Reset}
          Available: {string.Join(", ", available.Select(a => $"[{a}]"))}
          Switch with {AnsiHelper.Cyan}/model <name>{AnsiHelper.Reset}.
        """;

    public static string ToolFailure(string toolName, string output)
    {
        var firstLine = (output ?? "").Split('\n')[0];
        bool Has(string s) => (output ?? "").Contains(s, StringComparison.OrdinalIgnoreCase);

        string hint =
            Has("Access denied")   ? "The path is outside the working directory — use a path inside it."
          : Has("Unknown tool")    ? "That isn't a recognized tool name."
          : Has("Permission denied")? "Action was not approved — re-run and approve it at the prompt."
          : Has("not found")       ? "The target file or directory doesn't exist — check the path."
          :                          firstLine;

        return $"  {AnsiHelper.Red}✗ {toolName} failed:{AnsiHelper.Reset} {firstLine}\n  {AnsiHelper.Dim}{hint}{AnsiHelper.Reset}";
    }

    public static string Generic(GenericErrorCategory category, string message) => category switch
    {
        GenericErrorCategory.BrowserClosed =>
            $"{AnsiHelper.Yellow}The browser window closed.{AnsiHelper.Reset} Restart the browser or /exit to quit.",
        GenericErrorCategory.WebViewScript =>
            $"{AnsiHelper.Red}Couldn't talk to the Gemini page.{AnsiHelper.Reset} It may have changed — try /new, or relaunch with --discover-selectors.\n  {AnsiHelper.Dim}{message}{AnsiHelper.Reset}",
        GenericErrorCategory.Timeout =>
            $"{AnsiHelper.Yellow}{message}{AnsiHelper.Reset} Type your message to retry, or /new to start fresh.",
        _ =>
            $"{AnsiHelper.Red}Error:{AnsiHelper.Reset} {message}",
    };
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/GeminiCode.Tests/GeminiCode.Tests.csproj --filter "FullyQualifiedName~ErrorPresenterTests"`
Expected: PASS (the 3 single facts + 5 ToolFailure cases + 4 Generic cases).

- [ ] **Step 5: Run full suite**

Run: `dotnet test src/GeminiCode.Tests/GeminiCode.Tests.csproj`
Expected: 143 passing (131 + 12 new test cases).

- [ ] **Step 6: Commit**

```bash
git add src/GeminiCode/Cli/ErrorPresenter.cs src/GeminiCode.Tests/ErrorPresenterTests.cs
git commit -m "feat: ErrorPresenter for consistent, actionable error messaging"
```

---

## Task 3: BrowserBridge.DiagnoseFailureAsync

**Files:**
- Modify: `src/GeminiCode/Browser/BrowserBridge.cs` (add one method)

No unit test (browser-bound; the logic it delegates to is covered by Task 1). Verified by build + the orchestrator wiring in Task 4.

- [ ] **Step 1: Add the method**

Add this method to the `BrowserBridge` class (place it near `CheckForLimitAsync`, around line 618). It uses existing methods `CheckForLimitAsync()`, `CheckAuthenticatedAsync()`, `RunHealthCheckAsync()`:

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

`FailureClassifier`/`FailureDiagnosis` are in the same namespace (`GeminiCode.Browser`), so no `using` is needed.

- [ ] **Step 2: Build**

Run: `dotnet build`
Expected: `Build succeeded`, 0 errors.

- [ ] **Step 3: Run full suite (no regressions)**

Run: `dotnet test src/GeminiCode.Tests/GeminiCode.Tests.csproj`
Expected: 143 passing (unchanged).

- [ ] **Step 4: Commit**

```bash
git add src/GeminiCode/Browser/BrowserBridge.cs
git commit -m "feat: BrowserBridge.DiagnoseFailureAsync gathers signals for failure diagnosis"
```

---

## Task 4: Orchestrator wiring — diagnosis switch, auth recovery, tool-failure presenter

**Files:**
- Modify: `src/GeminiCode/Agent/AgentOrchestrator.cs`

No new unit tests (all decision logic is in the tested `FailureClassifier`/`ErrorPresenter`; the recovery loop is interactive/manual). Verify by build + full suite + the manual acceptance in Task 6.

- [ ] **Step 1: Replace the timeout block with a diagnosis switch**

In `SendAndProcessAsync`, find this exact block (currently around lines 123–134):

```csharp
        if (response == null)
        {
            // Timeout could be caused by a limit — check again
            var postLimit = await _browser.CheckForLimitAsync();
            if (postLimit != null)
            {
                HandleLimitDetected(postLimit);
                return null;
            }
            Console.WriteLine($"{AnsiHelper.Yellow}Gemini response timed out. Type your message to retry, or /new to start fresh.{AnsiHelper.Reset}");
            return null;
        }
```

Replace it with:

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

- [ ] **Step 2: Add the `using` for the Browser namespace if needed**

`AgentOrchestrator.cs` already has `using GeminiCode.Browser;` and `using GeminiCode.Cli;` (it uses `BrowserBridge`, `GeminiResponse`, `AnsiHelper`, `Spinner`). `FailureKind`, `GenericErrorCategory`, and `ErrorPresenter` resolve through those. Confirm both usings are present at the top of the file; add any that is missing.

- [ ] **Step 3: Add the recovery helper**

Add this private method to `AgentOrchestrator` (e.g., right after `SendAndProcessAsync`):

```csharp
    /// <summary>Logged-out recovery: prompt the user to sign in, wait, then re-send the original prepared message.</summary>
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
            _usage.RecordSent(message);
            var baseline = await _browser.CaptureBaselineAsync();
            await _browser.SendMessageAsync(message);

            GeminiResponse? resent;
            using (Spinner.Start("Waiting for Gemini"))
                resent = await _browser.WaitForResponseAsync(_settings.ResponseTimeoutSeconds, ct, baseline.textLen, baseline.preCount);

            if (resent == null)
            {
                Console.WriteLine(ErrorPresenter.Generic(GenericErrorCategory.Timeout,
                    "Still no response after re-sending."));
                return null;
            }
            if (resent.Limit != null) { HandleLimitDetected(resent.Limit); return null; }

            _usage.RecordReceived(resent.Text);
            return await ProcessResponseAsync(resent, ct);
        }
    }
```

Notes for the implementer: `_browser`, `_usage`, `_settings`, `HandleLimitDetected`, `ProcessResponseAsync`, and `Spinner` are all existing members/types in this class. `CaptureBaselineAsync()` returns a tuple used as `baseline.textLen` / `baseline.preCount` elsewhere in this file — mirror that usage exactly.

- [ ] **Step 4: Route tool failures through the presenter**

In `ExecuteToolCallsAsync`, find the unknown-tool branch:

```csharp
            if (tool == null)
            {
                Console.WriteLine($"  {AnsiHelper.Red}✗ {toolCall.Name}: unknown tool{AnsiHelper.Reset}");
                var result = new ToolResult(toolCall.Name, false, $"Unknown tool: {toolCall.Name}");
                results.Add(result.ToProtocolString());
                continue;
            }
```

Replace the first `Console.WriteLine` line in it with:

```csharp
                Console.WriteLine();
                Console.WriteLine(ErrorPresenter.ToolFailure(toolCall.Name, $"Unknown tool: {toolCall.Name}"));
```

Then find the failed-execution branch:

```csharp
            else
            {
                Console.WriteLine($" {AnsiHelper.Red}✗ {toolResult.Output.Split('\n')[0]}{AnsiHelper.Reset}");
            }
```

Replace it with:

```csharp
            else
            {
                Console.WriteLine($" {AnsiHelper.Red}✗{AnsiHelper.Reset}");
                Console.WriteLine(ErrorPresenter.ToolFailure(tool.Name, toolResult.Output));
            }
```

- [ ] **Step 5: Build**

Run: `dotnet build`
Expected: `Build succeeded`, 0 errors. If you get "name does not exist", confirm the `using GeminiCode.Browser;` / `using GeminiCode.Cli;` imports (Step 2).

- [ ] **Step 6: Run full suite**

Run: `dotnet test src/GeminiCode.Tests/GeminiCode.Tests.csproj`
Expected: 143 passing (no regressions; this task adds no tests).

- [ ] **Step 7: Commit**

```bash
git add src/GeminiCode/Agent/AgentOrchestrator.cs
git commit -m "feat: diagnose send failures, logout recovery with auto-resend, richer tool errors"
```

---

## Task 5: CliEngine generic-error classification + CommandHandler model-unavailable

**Files:**
- Modify: `src/GeminiCode/Cli/CliEngine.cs`
- Modify: `src/GeminiCode/Cli/CommandHandler.cs`

- [ ] **Step 1: Classify generic exceptions in `CliEngine.RunAsync`**

The existing `catch (OperationCanceledException) when (_browser.BrowserClosedToken.IsCancellationRequested)` block stays unchanged (it already offers restart/exit). Find the generic catch:

```csharp
            catch (Exception ex)
            {
                Console.WriteLine($"{AnsiHelper.Red}Error: {ex.Message}{AnsiHelper.Reset}");
            }
```

Replace it with:

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

`CliEngine.cs` is in namespace `GeminiCode.Cli`, so `ErrorPresenter`/`GenericErrorCategory` resolve without a new `using`.

- [ ] **Step 2: Use the presenter for model-unavailable in `CommandHandler.HandleModelAsync`**

Find the not-found branch (inside the `try` that parses the switch result):

```csharp
            else
            {
                var available = doc.RootElement.GetProperty("available");
                Console.WriteLine($"{AnsiHelper.Red}Mode '{modeName}' not found.{AnsiHelper.Reset}");
                Console.Write("Available: ");
                foreach (var item in available.EnumerateArray())
                    Console.Write($"[{item.GetString()}] ");
                Console.WriteLine();
            }
```

Replace it with:

```csharp
            else
            {
                var available = doc.RootElement.GetProperty("available").EnumerateArray()
                    .Select(e => e.GetString() ?? "").Where(s => s.Length > 0);
                Console.WriteLine(ErrorPresenter.ModelUnavailable(modeName!, available));
            }
```

`CommandHandler.cs` is in `GeminiCode.Cli`. Ensure `using System.Linq;` is available — it is provided by `ImplicitUsings`, so no change needed. `modeName` is non-null in this branch (guarded earlier in the method); the `!` documents that.

- [ ] **Step 3: Build**

Run: `dotnet build`
Expected: `Build succeeded`, 0 errors.

- [ ] **Step 4: Run full suite**

Run: `dotnet test src/GeminiCode.Tests/GeminiCode.Tests.csproj`
Expected: 143 passing (no regressions).

- [ ] **Step 5: Commit**

```bash
git add src/GeminiCode/Cli/CliEngine.cs src/GeminiCode/Cli/CommandHandler.cs
git commit -m "feat: classified generic errors and model-unavailable messaging via ErrorPresenter"
```

---

## Task 6: Final verification + manual acceptance

**Files:** none.

- [ ] **Step 1: Full build + test**

Run: `dotnet build` → `Build succeeded`, 0 errors.
Run: `dotnet test src/GeminiCode.Tests/GeminiCode.Tests.csproj` → 143 passing, 0 failures.

- [ ] **Step 2: Manual acceptance (needs live WebView2 + Gemini)**

Launch `dotnet run --project src/GeminiCode`, sign in, then verify:
1. **Logout recovery:** sign out of Gemini in the browser (or let the session expire), send a message → GeminiCode brings the browser forward and shows the `AuthLost` panel; sign back in, press Enter → the message is re-sent and answered. Typing `exit` at the prompt aborts just that message and returns to the prompt.
2. **Model unavailable:** `/model bogus` → shows the `ModelUnavailable` panel listing real modes + `/model`.
3. **Tool failure:** trigger a sandbox denial (ask it to write outside the working dir) → the tool error shows the "outside the working directory" hint.
Record the outcomes.

- [ ] **Step 3: Update memory**

Append to `C:\Users\table\.claude\projects\D--CodeProjects-GeminiCode\memory\project_overview.md` a note that failure handling is centralized in `Browser/FailureClassifier.cs` (diagnose-on-failure) + `Cli/ErrorPresenter.cs`, with logout→prompt→auto-resend recovery in `AgentOrchestrator.RecoverAuthAndResendAsync`.

```bash
git add -A
git commit -m "docs: record error-handling architecture in project notes"
```

---

## Self-Review notes (for the implementer)

- **Spec coverage:** FailureClassifier (Task 1), ErrorPresenter (Task 2), DiagnoseFailureAsync (Task 3), auth recovery + diagnosis switch + tool errors (Task 4), generic errors + model-unavailable (Task 5), tests throughout, manual acceptance (Task 6). All spec sections map to tasks.
- **Type consistency:** `FailureKind` {Unknown, UsageLimit, NotAuthenticated, UiBroken}, `FailureDiagnosis(Kind, MissingElements, Limit)`, `FailureClassifier.Classify(bool, IReadOnlyDictionary<string,bool>, LimitInfo?)`, `ErrorPresenter.{AuthLost, UiBroken, ModelUnavailable, ToolFailure, Generic}`, `GenericErrorCategory` {BrowserClosed, WebViewScript, Timeout, Other}, `BrowserBridge.DiagnoseFailureAsync()` — used identically across tasks.
- **Diagnose-on-failure / zero happy-path cost:** the diagnosis only runs inside the `response == null` branch; the success path is untouched.
- **No new constructor params:** unlike the previous feature, nothing here changes constructor signatures — `ErrorPresenter`/`FailureClassifier` are static, and `RecoverAuthAndResendAsync` uses existing `AgentOrchestrator` members.
