using System.Text.Json;

namespace GeminiCode.Browser;

public class BrowserBridge : IDisposable
{
    private BrowserWindow? _window;
    private Thread? _staThread;
    private readonly DomSelectors _selectors;
    private readonly SessionMonitor _sessionMonitor;
    private readonly string _userDataFolder;
    private readonly CancellationTokenSource _closedCts = new();
    private readonly TaskCompletionSource _windowReady = new();

    public CancellationToken BrowserClosedToken => _closedCts.Token;

    public BrowserBridge(DomSelectors selectors, string userDataFolder)
    {
        _selectors = selectors;
        _sessionMonitor = new SessionMonitor(selectors);
        _userDataFolder = userDataFolder;
    }

    public Task StartAsync()
    {
        _staThread = new Thread(RunStaThread);
        _staThread.SetApartmentState(ApartmentState.STA);
        _staThread.IsBackground = true;
        _staThread.Start();
        return _windowReady.Task;
    }

    private void RunStaThread()
    {
        try
        {
            _window = new BrowserWindow();
            _window.FormClosed += (_, _) => _closedCts.Cancel();

            _window.Load += async (_, _) =>
            {
                try
                {
                    await _window.InitializeAsync(_userDataFolder);
                    _window.NavigateTo("https://gemini.google.com");
                    _windowReady.TrySetResult();
                }
                catch (Exception ex)
                {
                    _windowReady.TrySetException(ex);
                }
            };

            Application.Run(_window);
        }
        catch (Exception ex)
        {
            _windowReady.TrySetException(ex);
        }
    }

    public async Task<bool> CheckAuthenticatedAsync()
    {
        var script = _sessionMonitor.GetAuthCheckScript();
        var result = await InvokeOnStaAsync(() =>
            _window!.WebView.CoreWebView2.ExecuteScriptAsync(script));
        return result == "true";
    }

    public async Task<Dictionary<string, bool>> RunHealthCheckAsync()
    {
        var script = _sessionMonitor.GetHealthCheckScript();
        var result = await InvokeOnStaAsync(() =>
            _window!.WebView.CoreWebView2.ExecuteScriptAsync(script));

        // Result is a JSON string escaped inside a JSON string
        var unescaped = JsonSerializer.Deserialize<string>(result) ?? "{}";
        return JsonSerializer.Deserialize<Dictionary<string, bool>>(unescaped) ?? new();
    }

    public async Task SendMessageAsync(string message)
    {
        var escapedMessage = JsonSerializer.Serialize(message);
        var script = $$"""
            (function() {
                var input = document.querySelector("{{EscapeJs(_selectors.ChatInput)}}");
                if (!input) return 'no_input';

                // Focus and set value
                input.focus();
                input.textContent = {{escapedMessage}};
                input.dispatchEvent(new Event('input', { bubbles: true }));

                // Small delay then click send
                setTimeout(function() {
                    var btn = document.querySelector("{{EscapeJs(_selectors.SendButton)}}");
                    if (btn) btn.click();
                }, 200);

                return 'sent';
            })()
            """;
        await InvokeOnStaAsync(() => _window!.WebView.CoreWebView2.ExecuteScriptAsync(script));
    }

    public async Task<string?> WaitForResponseAsync(int timeoutSeconds, CancellationToken ct)
    {
        var pollScript = $$"""
            (function() {
                var indicator = document.querySelector("{{EscapeJs(_selectors.TypingIndicator)}}");
                if (indicator && indicator.offsetParent !== null) return JSON.stringify({done: false});

                var containers = document.querySelectorAll("{{EscapeJs(_selectors.ResponseContainer)}}");
                if (containers.length === 0) return JSON.stringify({done: false});

                var last = containers[containers.length - 1];
                var text = last.innerText || last.textContent || '';
                return JSON.stringify({done: true, text: text});
            })()
            """;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        // Initial delay to let Gemini start processing
        await Task.Delay(2000, cts.Token);

        while (!cts.Token.IsCancellationRequested)
        {
            var resultJson = await InvokeOnStaAsync(() =>
                _window!.WebView.CoreWebView2.ExecuteScriptAsync(pollScript));

            var unescaped = JsonSerializer.Deserialize<string>(resultJson);
            if (unescaped != null)
            {
                using var doc = JsonDocument.Parse(unescaped);
                if (doc.RootElement.GetProperty("done").GetBoolean())
                    return doc.RootElement.GetProperty("text").GetString();
            }

            await Task.Delay(1000, cts.Token);
        }

        return null; // Timed out
    }

    public async Task StartNewChatAsync()
    {
        var script = $$"""
            (function() {
                var btn = document.querySelector("{{EscapeJs(_selectors.NewChatButton)}}");
                if (btn) { btn.click(); return 'ok'; }
                return 'not_found';
            })()
            """;
        await InvokeOnStaAsync(() => _window!.WebView.CoreWebView2.ExecuteScriptAsync(script));
    }

    public void BringToFront()
    {
        _window?.Invoke(() =>
        {
            _window.WindowState = System.Windows.Forms.FormWindowState.Normal;
            _window.BringToFront();
            _window.Activate();
        });
    }

    private Task<string> InvokeOnStaAsync(Func<Task<string>> action)
    {
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _window!.BeginInvoke(async () =>
        {
            try
            {
                var result = await action();
                tcs.TrySetResult(result);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });
        return tcs.Task;
    }

    private static string EscapeJs(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    public void Dispose()
    {
        if (_window != null && !_window.IsDisposed)
        {
            _window.Invoke(() => _window.Close());
            _staThread?.Join(5000);
        }
        _closedCts.Dispose();
    }
}
