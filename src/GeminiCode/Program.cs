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
