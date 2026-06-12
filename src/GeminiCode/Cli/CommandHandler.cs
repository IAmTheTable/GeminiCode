// src/GeminiCode/Cli/CommandHandler.cs
using GeminiCode.Browser;
using GeminiCode.Agent;
using GeminiCode.Permissions;
using GeminiCode.Plugins;
using GeminiCode.Tools;

namespace GeminiCode.Cli;

public class CommandHandler
{
    private readonly BrowserBridge _browser;
    private readonly ConversationManager _conversation;
    private readonly SessionAllowlist _allowlist;
    private readonly PathSandbox _sandbox;
    private readonly AgentProfile _profile;
    private readonly SessionContext _sessionContext;
    private readonly WorkflowRunner _workflowRunner;
    private readonly PluginRegistry _plugins;
    private readonly ToolRegistry _toolRegistry;
    private readonly UsageTracker _usage;

    public CommandHandler(
        BrowserBridge browser,
        ConversationManager conversation,
        SessionAllowlist allowlist,
        PathSandbox sandbox,
        AgentProfile profile,
        SessionContext sessionContext,
        WorkflowRunner workflowRunner,
        PluginRegistry plugins,
        ToolRegistry toolRegistry,
        UsageTracker usage)
    {
        _browser = browser;
        _conversation = conversation;
        _allowlist = allowlist;
        _sandbox = sandbox;
        _profile = profile;
        _sessionContext = sessionContext;
        _workflowRunner = workflowRunner;
        _plugins = plugins;
        _toolRegistry = toolRegistry;
        _usage = usage;
    }

    /// <summary>Returns true if the input was a command (handled), false if it's a regular message.</summary>
    public async Task<bool> TryHandleAsync(string input, CancellationToken ct = default)
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
            case "/model":
                await HandleModelAsync(arg);
                return true;
            case "/limit":
                await HandleLimitCheckAsync();
                return true;
            case "/paste":
                Console.WriteLine("Paste mode: enter text, then type END on a new line to finish.");
                return true;
            case "/agent":
                await HandleAgentAsync(arg);
                return true;
            case "/save":
                HandleSave();
                return true;
            case "/restore":
                await HandleRestoreAsync();
                return true;
            case "/context":
                HandleShowContext();
                return true;
            case "/exit":
                HandleExit();
                return true;
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
            case "/usage":
                Console.WriteLine(_usage.Breakdown());
                return true;
            default:
                if (await TryRunPluginAsync(command, arg, ct))
                    return true;
                Console.WriteLine($"Unknown command: {command}. Type /help for available commands.");
                return true;
        }
    }

    private void PrintHelp()
    {
        Console.WriteLine($"""
            {AnsiHelper.Bold}Commands:{AnsiHelper.Reset}
              /help            — Show this help
              /clear           — Clear terminal
              /new             — Start a new Gemini conversation
              /model <name>    — Switch Gemini mode (flash/pro/thinking)
              /limit           — Check usage limit status
              /browser         — Bring browser window to foreground
              /history         — Show conversation turn count
              /allowlist       — Show current session allowlist
              /status          — Show session state
              /cd <path>       — Change working directory
              /paste           — Multi-line paste mode
              /agent [name]    — List or switch agent profiles
              /save            — Save session context to .gemini/session-context.md
              /restore         — Restore previous session context in new chat
              /context         — Show current session context
              /exit            — Quit GeminiCode
              /plugins         — List loaded plugins
              /reload          — Re-scan the plugins folder
              /tools           — List available tools and risk
              /init            — Create a starter GEMINI.md
              /compact         — Summarize context and start fresh
              /usage           — Show estimated token usage breakdown

            {AnsiHelper.Bold}@Context References:{AnsiHelper.Reset} (attach files/data to your message)
            {Agent.ContextProcessor.GetHelpText()}
            {AnsiHelper.Bold}Examples:{AnsiHelper.Reset}
              > fix the bug in @file src/App.cs
              > explain @file src/App.cs:10-30
              > what changed? @diff
              > refactor @grep "TODO" include=*.cs
            """);
        if (_plugins.Plugins.Count > 0)
        {
            Console.WriteLine($"\n{AnsiHelper.Bold}Plugins (skills):{AnsiHelper.Reset}");
            foreach (var p in _plugins.Plugins)
            {
                var hint = string.IsNullOrEmpty(p.ArgHint) ? "" : $" {p.ArgHint}";
                Console.WriteLine($"  {p.Command}{hint,-20} — {p.Description}");
            }
        }
    }

    private async Task HandleNewChatAsync()
    {
        await _browser.StartNewChatAsync();
        Console.Write($"{AnsiHelper.Dim}Waiting for page to load...{AnsiHelper.Reset}");
        await _browser.WaitForPageSettleAsync();
        _conversation.Reset();
        Console.WriteLine($"\r{AnsiHelper.Green}New conversation started.        {AnsiHelper.Reset}");
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
        var model = await _browser.GetCurrentModelAsync();
        var trackedModel = _conversation.CurrentModel ?? "not yet detected";
        var startModel = _conversation.SessionStartModel;
        var switchCount = _conversation.ModelSwitchCount;

        Console.WriteLine($"""
            {AnsiHelper.Bold}Status:{AnsiHelper.Reset}
              Auth:       {(authCheck ? $"{AnsiHelper.Green}Authenticated{AnsiHelper.Reset}" : $"{AnsiHelper.Red}Not authenticated{AnsiHelper.Reset}")}
              Model:      {AnsiHelper.Bold}{model}{AnsiHelper.Reset}
              Tracked:    {trackedModel}{(startModel != null && trackedModel != startModel ? $" (started as {startModel})" : "")}
              Switches:   {switchCount}
              Work dir:   {_sandbox.WorkingDirectory}
              Turns:      {_conversation.TurnCount}
              Allowlist:  {_allowlist.GetEntries().Count} tools auto-approved
            """);
    }

    private async Task HandleModelAsync(string? modeName)
    {
        if (string.IsNullOrWhiteSpace(modeName))
        {
            var current = await _browser.GetCurrentModelAsync();
            Console.WriteLine($"Current model: {AnsiHelper.Bold}{current}{AnsiHelper.Reset}");
            Console.WriteLine("Usage: /model <flash | flash-lite | pro>");
            Console.WriteLine($"  {AnsiHelper.Dim}Thinking level (Standard/Extended) is a separate submenu — set it in the browser for now.{AnsiHelper.Reset}");
            return;
        }

        // Thinking level (Standard/Extended) is a submenu in Gemini's new UI, not a top-level model.
        // CLI switching for it is being wired up against the live submenu DOM.
        var lowerMode = modeName.ToLowerInvariant().Trim();
        if (lowerMode is "thinking" or "extended" or "standard")
        {
            Console.WriteLine($"{AnsiHelper.Yellow}'Thinking level' (Standard/Extended) is a submenu in Gemini's new UI.{AnsiHelper.Reset}");
            Console.WriteLine("  CLI switching for it is being wired up against the live page.");
            Console.WriteLine($"  For now, set it in the browser window: the {AnsiHelper.Bold}Flash ▾{AnsiHelper.Reset} menu → Thinking level.");
            return;
        }

        Console.WriteLine($"Switching to {modeName}...");
        var result = await _browser.SwitchModelAsync(modeName);

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(result);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                var selected = doc.RootElement.GetProperty("selected").GetString();
                Console.WriteLine($"{AnsiHelper.Green}Switched to: {selected}{AnsiHelper.Reset}");
                // Start new conversation with the new model
                await HandleNewChatAsync();
            }
            else
            {
                var available = doc.RootElement.GetProperty("available").EnumerateArray()
                    .Select(e => e.GetString() ?? "").Where(s => s.Length > 0);
                Console.WriteLine(ErrorPresenter.ModelUnavailable(modeName!, available));
            }
        }
        catch
        {
            Console.WriteLine($"Result: {result}");
        }
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
        _profile.UpdateWorkingDirectory(resolved);
        _allowlist.Clear();
        Console.WriteLine($"Working directory changed to {resolved}. Allowlist cleared.");
    }

    private async Task HandleLimitCheckAsync()
    {
        Console.Write($"{AnsiHelper.Dim}Checking for usage limits...{AnsiHelper.Reset}");
        var limit = await _browser.CheckForLimitAsync();
        if (limit != null)
        {
            Console.WriteLine($"\r{AnsiHelper.Yellow}Limit detected:{AnsiHelper.Reset} {limit.Message}");
            if (!string.IsNullOrEmpty(limit.RetryAfter))
                Console.WriteLine($"  Retry after: {limit.RetryAfter}");
            Console.WriteLine($"  Try /model to switch or wait.");
        }
        else
        {
            Console.WriteLine($"\r{AnsiHelper.Green}No usage limits detected.              {AnsiHelper.Reset}");
        }
    }

    private async Task HandleAgentAsync(string? arg)
    {
        if (string.IsNullOrWhiteSpace(arg) || arg == "list")
        {
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

        if (!_profile.SetActive(arg))
        {
            Console.WriteLine($"{AnsiHelper.Red}Profile '{arg}' not found.{AnsiHelper.Reset}");
            Console.WriteLine("Available: " + string.Join(", ", _profile.ListProfiles()));
            return;
        }

        Console.WriteLine($"{AnsiHelper.Green}Switched to profile: {arg}{AnsiHelper.Reset}");
        await _browser.StartNewChatAsync();
        await _browser.WaitForPageSettleAsync();
        _conversation.Reset();
        Console.WriteLine($"{AnsiHelper.Green}New conversation started with {arg} profile.{AnsiHelper.Reset}");
    }

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

        // Send saved context as text message
        Console.WriteLine($"{AnsiHelper.Dim}Restoring session context...{AnsiHelper.Reset}");
        var content = File.ReadAllText(filePath);
        await _browser.SendMessageAsync("Previous session context:\n\n" + content);

        Console.WriteLine($"{AnsiHelper.Green}Session context restored. New chat initialized with previous context.{AnsiHelper.Reset}");
    }

    private void HandleShowContext()
    {
        var md = _sessionContext.GenerateMarkdown();
        Console.WriteLine(md);
    }

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

    private async Task<bool> TryRunPluginAsync(string command, string? arg, CancellationToken ct)
    {
        var manifest = _plugins.ByCommand(command);
        if (manifest == null) return false;

        var variables = new Dictionary<string, string> { ["input"] = arg ?? "" };
        await _workflowRunner.RunAsync(manifest.ToWorkflow(), variables, ct);
        return true;
    }

    private void HandleExit()
    {
        _sessionContext.SaveToFile();
        Console.WriteLine($"{AnsiHelper.Dim}Session context saved.{AnsiHelper.Reset}");
        Console.WriteLine("Goodbye.");
        _browser.Dispose();
        Environment.Exit(0);
    }
}
