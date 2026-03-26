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
        var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(
            userDataFolder: userDataFolder);
        await WebView.EnsureCoreWebView2Async(env);
        _initTcs.TrySetResult();
    }

    public Task WaitForInitialization() => _initTcs.Task;

    public void NavigateTo(string url)
    {
        WebView.CoreWebView2.Navigate(url);
    }
}
