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
