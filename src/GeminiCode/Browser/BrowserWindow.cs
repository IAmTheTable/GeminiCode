using Microsoft.Web.WebView2.WinForms;
using System.Windows.Forms;

namespace GeminiCode.Browser;

public class BrowserWindow : Form
{
    public WebView2 WebView { get; }
    private readonly TaskCompletionSource _initTcs = new();

    public BrowserWindow()
    {
        Text = "GeminiCode - Gemini";
        Width = 1200;
        Height = 800;
        StartPosition = FormStartPosition.CenterScreen;

        WebView = new WebView2
        {
            Dock = DockStyle.Fill
        };
        Controls.Add(WebView);
    }

    public async Task InitializeAsync(string userDataFolder)
    {
        // Enable a CDP remote-debugging endpoint so the Gemini page can be inspected live
        // (open http://localhost:9222 in Chrome → click the page to attach DevTools).
        // Override the port with GEMINICODE_DEBUG_PORT, or set it to "off" to disable.
        var portSetting = Environment.GetEnvironmentVariable("GEMINICODE_DEBUG_PORT");
        Microsoft.Web.WebView2.Core.CoreWebView2EnvironmentOptions? options = null;
        if (!string.Equals(portSetting, "off", StringComparison.OrdinalIgnoreCase))
        {
            var port = int.TryParse(portSetting, out var p) ? p : 9222;
            options = new Microsoft.Web.WebView2.Core.CoreWebView2EnvironmentOptions
            {
                AdditionalBrowserArguments = $"--remote-debugging-port={port}"
            };
        }

        var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: null, userDataFolder: userDataFolder, options: options);
        await WebView.EnsureCoreWebView2Async(env);
        _initTcs.TrySetResult();
    }

    public Task WaitForInitialization() => _initTcs.Task;

    public void NavigateTo(string url)
    {
        WebView.CoreWebView2.Navigate(url);
    }
}
