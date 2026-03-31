using System.Text.Json;

namespace GeminiCode.Browser;

public record CodeBlock(string Language, string Code);
public record GeminiResponse(string Text, List<CodeBlock> CodeBlocks);

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

    /// <summary>Runs a diagnostic script to discover the actual DOM selectors on the Gemini page.</summary>
    public async Task<string> DiscoverSelectorsAsync()
    {
        var script = """
            (function() {
                var results = {};

                // Find chat input candidates
                var inputs = [];
                // contenteditable divs (common for chat inputs)
                document.querySelectorAll('[contenteditable="true"]').forEach(function(el) {
                    inputs.push({tag: el.tagName, role: el.getAttribute('role'), ariaLabel: el.getAttribute('aria-label'), className: el.className.substring(0,100), id: el.id});
                });
                // textareas
                document.querySelectorAll('textarea').forEach(function(el) {
                    inputs.push({tag: 'textarea', role: el.getAttribute('role'), ariaLabel: el.getAttribute('aria-label'), placeholder: el.placeholder, className: el.className.substring(0,100), id: el.id});
                });
                // inputs
                document.querySelectorAll('input[type="text"]').forEach(function(el) {
                    inputs.push({tag: 'input', role: el.getAttribute('role'), ariaLabel: el.getAttribute('aria-label'), placeholder: el.placeholder, className: el.className.substring(0,100), id: el.id});
                });
                // rich text editors
                document.querySelectorAll('[role="textbox"]').forEach(function(el) {
                    inputs.push({tag: el.tagName, role: 'textbox', ariaLabel: el.getAttribute('aria-label'), className: el.className.substring(0,100), id: el.id});
                });
                results.inputCandidates = inputs;

                // Find send button candidates
                var buttons = [];
                document.querySelectorAll('button').forEach(function(el) {
                    var label = el.getAttribute('aria-label') || el.innerText || '';
                    if (label.toLowerCase().includes('send') || label.toLowerCase().includes('submit') || el.querySelector('svg'))
                    {
                        buttons.push({ariaLabel: el.getAttribute('aria-label'), text: (el.innerText||'').substring(0,50), className: el.className.substring(0,100), id: el.id, matTooltip: el.getAttribute('mattooltip') || el.getAttribute('data-tooltip') || ''});
                    }
                });
                results.sendButtonCandidates = buttons;

                // Find response containers
                var responses = [];
                document.querySelectorAll('[class*="response"], [class*="message"], [class*="answer"], [class*="model-response"], [data-message-author-role="model"]').forEach(function(el) {
                    responses.push({tag: el.tagName, className: el.className.substring(0,100), role: el.getAttribute('role'), dataRole: el.getAttribute('data-message-author-role')});
                });
                results.responseCandidates = responses.slice(0, 10);

                // Page URL and title for context
                results.url = window.location.href;
                results.title = document.title;

                // Check if signed in (look for common signed-in indicators)
                results.hasAvatar = document.querySelector('img[aria-label*="Account"], img[alt*="avatar"], [data-ogsr-up]') !== null;

                return JSON.stringify(results, null, 2);
            })()
            """;
        var result = await InvokeOnStaAsync(() =>
            _window!.WebView.CoreWebView2.ExecuteScriptAsync(script));

        // Unescape the JSON string wrapper
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<string>(result) ?? result;
        }
        catch
        {
            return result;
        }
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

    /// <summary>Count current response elements so we can detect new ones after sending.</summary>
    public async Task<int> GetResponseCountAsync()
    {
        var script = """
            (function() {
                // Count all model response containers in the conversation
                var all = document.querySelectorAll('[class*="response"], [class*="message-content"], .model-response-text, .markdown-main-panel');
                // Also try broader: any container with substantial text that isn't the input or ToS
                var turns = document.querySelectorAll('[class*="conversation-turn"], [class*="turn-container"]');
                return Math.max(all.length, turns.length);
            })()
            """;
        var result = await InvokeOnStaAsync(() =>
            _window!.WebView.CoreWebView2.ExecuteScriptAsync(script));
        try { return int.Parse(result.Trim('"')); }
        catch { return 0; }
    }

    public async Task SendMessageAsync(string message)
    {
        var escapedMessage = JsonSerializer.Serialize(message);

        // Strategy: Use Quill's API with 'user' source to trigger Angular change detection,
        // then fire DOM events to enable the send button.
        var insertScript = $$"""
            (function() {
                var editor = document.querySelector("{{EscapeJs(_selectors.ChatInput)}}");
                if (!editor) return 'no_input';

                // Find Quill instance
                var container = editor.closest('.ql-container');
                var quill = container ? container.__quill : null;

                if (quill) {
                    // Clear and insert with 'user' source — this triggers Quill's text-change event
                    // which Angular listens to for enabling the send button
                    var len = quill.getLength();
                    if (len > 1) quill.deleteText(0, len - 1, 'user');
                    quill.insertText(0, {{escapedMessage}}, 'user');

                    // Also fire DOM events to ensure Angular picks up the change
                    editor.dispatchEvent(new Event('input', { bubbles: true }));
                    editor.dispatchEvent(new Event('change', { bubbles: true }));
                    editor.dispatchEvent(new Event('keyup', { bubbles: true }));

                    return 'quill_inserted';
                }

                // Fallback: execCommand approach (works for short text)
                editor.focus();
                var sel = window.getSelection();
                var range = document.createRange();
                range.selectNodeContents(editor);
                sel.removeAllRanges();
                sel.addRange(range);
                document.execCommand('delete', false);
                document.execCommand('insertText', false, {{escapedMessage}});

                return 'execcommand_inserted';
            })()
            """;
        await InvokeOnStaAsync(() =>
            _window!.WebView.CoreWebView2.ExecuteScriptAsync(insertScript));

        // Wait for Angular/Quill change detection to process
        await Task.Delay(800);

        // Click send — retry loop in case button needs a moment to enable
        for (int attempt = 0; attempt < 5; attempt++)
        {
            var sendScript = $$"""
                (function() {
                    var btn = document.querySelector("{{EscapeJs(_selectors.SendButton)}}");
                    if (!btn) return 'no_button';
                    if (btn.disabled) return 'disabled';
                    btn.click();
                    return 'clicked';
                })()
                """;
            var sendResult = await InvokeOnStaAsync(() =>
                _window!.WebView.CoreWebView2.ExecuteScriptAsync(sendScript));

            var result = sendResult.Trim('"');
            if (result == "clicked") return;
            if (result == "no_button") return;

            // Button still disabled — try to nudge it
            if (attempt < 4)
            {
                var nudgeScript = $$"""
                    (function() {
                        var editor = document.querySelector("{{EscapeJs(_selectors.ChatInput)}}");
                        if (!editor) return;
                        // Simulate a keypress to trigger Angular's change detection
                        editor.dispatchEvent(new KeyboardEvent('keydown', {key: ' ', bubbles: true}));
                        editor.dispatchEvent(new KeyboardEvent('keyup', {key: ' ', bubbles: true}));
                        editor.dispatchEvent(new Event('input', {bubbles: true}));
                        // Also try triggering on the form/parent
                        var form = editor.closest('form') || editor.closest('[class*="input-area"]');
                        if (form) form.dispatchEvent(new Event('input', {bubbles: true}));
                    })()
                    """;
                await InvokeOnStaAsync(() =>
                    _window!.WebView.CoreWebView2.ExecuteScriptAsync(nudgeScript));
                await Task.Delay(300);
            }
        }
    }

    /// <summary>Capture the current page state so WaitForResponseAsync can detect new content.</summary>
    public async Task<(int textLen, int preCount)> CaptureBaselineAsync()
    {
        var script = """
            (function() {
                var main = document.querySelector('[class*="chat-container"]')
                    || document.querySelector('.content-container')
                    || document.querySelector('.main-content');
                if (!main) return JSON.stringify({textLen: 0, preCount: 0});
                var textLen = (main.innerText || '').length;
                var preCount = main.querySelectorAll('pre').length;
                return JSON.stringify({textLen: textLen, preCount: preCount});
            })()
            """;
        try
        {
            var result = await InvokeOnStaAsync(() =>
                _window!.WebView.CoreWebView2.ExecuteScriptAsync(script));
            var unescaped = JsonSerializer.Deserialize<string>(result);
            if (unescaped != null)
            {
                using var doc = JsonDocument.Parse(unescaped);
                return (doc.RootElement.GetProperty("textLen").GetInt32(),
                        doc.RootElement.GetProperty("preCount").GetInt32());
            }
        }
        catch { }
        return (0, 0);
    }

    public async Task<GeminiResponse?> WaitForResponseAsync(int timeoutSeconds, CancellationToken ct, int baselineTextLen = 0, int baselinePreCount = 0)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        // Step 2: Build poll script that uses baseline to extract only NEW content
        var pollScript = $$"""
            (function() {
                var baseTextLen = {{baselineTextLen}};
                var basePreCount = {{baselinePreCount}};

                var main = document.querySelector('[class*="chat-container"]')
                    || document.querySelector('.content-container')
                    || document.querySelector('.main-content');
                if (!main) return JSON.stringify({done: false, reason: 'no_main'});

                var fullText = main.innerText || '';

                // Only return NEW text (after baseline)
                // Use a safety margin: back up 200 chars to avoid cutting into the response start
                // (the user's message gets added to the DOM between baseline capture and response)
                var safeStart = Math.max(0, baseTextLen - 200);
                var newText = fullText.length > safeStart ? fullText.substring(safeStart).trim() : '';
                if (!newText || newText.length < 5) return JSON.stringify({done: false, reason: 'no_new_text'});

                // Only extract NEW code blocks (after baseline pre count)
                var allPres = main.querySelectorAll('pre');
                var newCodeBlocks = [];
                for (var i = basePreCount; i < allPres.length; i++) {
                    var el = allPres[i];
                    var code = el.innerText || el.textContent || '';
                    if (code.trim().length > 20) {
                        var lang = '';
                        var codeEl = el.querySelector('code');
                        var classes = ((codeEl ? codeEl.className : '') + ' ' + el.className + ' ' + (el.getAttribute('data-lang') || '')).toLowerCase();
                        var langMatch = classes.match(/language-(\w+)|lang-(\w+)|(\bpython\b|\bjavascript\b|\btypescript\b|\bcsharp\b|\bjava\b|\bcpp\b|\brust\b|\bgo\b|\bruby\b|\bphp\b|\bbash\b|\bshell\b)/);
                        if (langMatch) lang = langMatch[1] || langMatch[2] || langMatch[3] || '';
                        if (!lang && code.includes('import ') && (code.includes('def ') || code.includes('print('))) lang = 'python';
                        if (!lang && (code.includes('function ') || code.includes('const ') || code.includes('=>'))) lang = 'javascript';
                        if (!lang && code.includes('using ') && code.includes('namespace ')) lang = 'csharp';
                        newCodeBlocks.push({language: lang, code: code.trim()});
                    }
                }

                return JSON.stringify({done: true, text: newText, codeBlocks: newCodeBlocks});
            })()
            """;

        // Wait for Gemini to start processing
        await Task.Delay(3000, cts.Token);

        string? lastText = null;
        int stableCount = 0;

        while (!cts.Token.IsCancellationRequested)
        {
            try
            {
                var resultJson = await InvokeOnStaAsync(() =>
                    _window!.WebView.CoreWebView2.ExecuteScriptAsync(pollScript));

                var unescaped = JsonSerializer.Deserialize<string>(resultJson);
                if (unescaped != null)
                {
                    using var doc = JsonDocument.Parse(unescaped);
                    if (doc.RootElement.GetProperty("done").GetBoolean())
                    {
                        var responseText = doc.RootElement.GetProperty("text").GetString() ?? "";

                        // Wait for text to stabilize (2 consecutive identical polls = done)
                        if (responseText == lastText)
                        {
                            stableCount++;
                            if (stableCount >= 2)
                            {
                                var codeBlocks = new List<CodeBlock>();
                                if (doc.RootElement.TryGetProperty("codeBlocks", out var blocksEl))
                                {
                                    foreach (var block in blocksEl.EnumerateArray())
                                    {
                                        var lang = block.GetProperty("language").GetString() ?? "";
                                        var code = block.GetProperty("code").GetString() ?? "";
                                        if (!string.IsNullOrWhiteSpace(code))
                                            codeBlocks.Add(new CodeBlock(lang, code));
                                    }
                                }
                                return new GeminiResponse(responseText, codeBlocks);
                            }
                        }
                        else
                        {
                            lastText = responseText;
                            stableCount = 0;
                        }
                    }
                }
            }
            catch { }

            await Task.Delay(1000, cts.Token);
        }

        return null;
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

    /// <summary>Switches the Gemini model mode (e.g., "Flash", "Pro", "Thinking").</summary>
    public async Task<string> SwitchModelAsync(string modeName)
    {
        // Step 1: Click the mode picker button to open the dropdown
        var openScript = """
            (function() {
                var btn = document.querySelector('[data-test-id="bard-mode-menu-button"]');
                if (!btn) return 'no_picker';
                btn.click();
                return 'opened';
            })()
            """;
        var openResult = await InvokeOnStaAsync(() =>
            _window!.WebView.CoreWebView2.ExecuteScriptAsync(openScript));

        // Wait for menu to appear
        await Task.Delay(500);

        // Step 2: Find and click the menu item matching the mode name
        var escapedMode = JsonSerializer.Serialize(modeName.ToLowerInvariant());
        var selectScript = $$$"""
            (function() {
                var modeName = {{{escapedMode}}};
                // Look for menu items in the dropdown
                var items = document.querySelectorAll(
                    '[role="menuitem"], [role="option"], mat-option, .mat-mdc-menu-item, ' +
                    '[class*="mode-option"], [class*="model-option"], ' +
                    '.cdk-overlay-pane button, .cdk-overlay-pane [role="menuitemradio"]'
                );
                var found = null;
                var available = [];
                items.forEach(function(el) {
                    var text = (el.innerText || el.textContent || '').trim().toLowerCase();
                    available.push(text);
                    if (text.includes(modeName)) {
                        found = el;
                    }
                });
                if (found) {
                    found.click();
                    return JSON.stringify({success: true, selected: found.innerText.trim(), available: available});
                }
                return JSON.stringify({success: false, available: available});
            })()
            """;
        var selectResult = await InvokeOnStaAsync(() =>
            _window!.WebView.CoreWebView2.ExecuteScriptAsync(selectScript));

        try { return JsonSerializer.Deserialize<string>(selectResult) ?? selectResult; }
        catch { return selectResult; }
    }

    /// <summary>Gets the currently selected model mode using multiple detection strategies.</summary>
    public async Task<string> GetCurrentModelAsync()
    {
        var script = """
            (function() {
                // Strategy 1: Mode menu button (most reliable)
                var btn = document.querySelector('[data-test-id="bard-mode-menu-button"]');
                if (btn) {
                    var text = (btn.innerText || btn.textContent || '').trim().toLowerCase();
                    if (text) return text;
                }

                // Strategy 2: Look for active/selected mode indicator in UI
                var activeItems = document.querySelectorAll(
                    '[aria-selected="true"][role="tab"], [aria-checked="true"][role="menuitemradio"], ' +
                    '[class*="selected"][class*="mode"], [class*="active"][class*="model"], ' +
                    '[class*="selected"][class*="model"], .mdc-chip--selected'
                );
                for (var i = 0; i < activeItems.length; i++) {
                    var text = (activeItems[i].innerText || '').trim().toLowerCase();
                    if (text && (text.includes('flash') || text.includes('pro') || text.includes('thinking') ||
                        text.includes('2.5') || text.includes('2.0') || text.includes('deep'))) {
                        return text;
                    }
                }

                // Strategy 3: Scan for model name near the top of the page (chips, headers)
                var chips = document.querySelectorAll(
                    '[class*="chip"], [class*="badge"], [class*="model-name"], [class*="mode-label"], ' +
                    '[class*="model-indicator"], [class*="mode-indicator"]'
                );
                for (var i = 0; i < chips.length; i++) {
                    var text = (chips[i].innerText || '').trim().toLowerCase();
                    if (text.includes('flash') || text.includes('pro') || text.includes('thinking') || text.includes('deep')) {
                        return text;
                    }
                }

                // Strategy 4: Look in the page title or URL
                var title = document.title.toLowerCase();
                if (title.includes('flash')) return 'flash';
                if (title.includes('thinking')) return 'thinking';
                if (title.includes('deep')) return 'deep think';
                if (title.includes('pro')) return 'pro';

                return 'unknown';
            })()
            """;
        var result = await InvokeOnStaAsync(() =>
            _window!.WebView.CoreWebView2.ExecuteScriptAsync(script));
        try
        {
            var raw = JsonSerializer.Deserialize<string>(result) ?? result;
            return NormalizeModelName(raw);
        }
        catch { return "unknown"; }
    }

    /// <summary>Normalizes messy DOM text into a clean model identifier.</summary>
    public static string NormalizeModelName(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText)) return "unknown";

        var lower = rawText.ToLowerInvariant().Trim();

        // Remove common noise from DOM text
        lower = lower.Replace("gemini", "").Replace("google", "").Replace("mode", "").Trim();

        // Match known models (order matters — check more specific first)
        if (lower.Contains("deep") && (lower.Contains("think") || lower.Contains("research"))) return "Deep Think";
        if (lower.Contains("thinking")) return "Thinking";
        if (lower.Contains("flash")) return "Flash";
        if (lower.Contains("pro")) return "Pro";
        if (lower.Contains("ultra")) return "Ultra";
        if (lower.Contains("nano")) return "Nano";
        if (lower.Contains("exp")) return "Experimental";

        // If it contains a version number, include it
        if (lower.Contains("2.5")) return rawText.Trim();
        if (lower.Contains("2.0")) return rawText.Trim();

        // Fall back to cleaned raw text
        var cleaned = rawText.Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "unknown" : cleaned;
    }

    /// <summary>Discovers model selector elements in the Gemini UI.</summary>
    public async Task<string> DiscoverModelSelectorAsync()
    {
        var script = """
            (function() {
                var results = {};

                // Look for model/mode selector buttons and dropdowns
                var selectors = [];
                document.querySelectorAll('button, [role="button"], [role="listbox"], [role="combobox"], [role="tab"], [role="menuitem"], mat-select, [class*="model"], [class*="selector"], [class*="dropdown"], [class*="mode"], [class*="chip"]').forEach(function(el) {
                    var text = (el.innerText || el.textContent || '').trim();
                    var label = el.getAttribute('aria-label') || '';
                    if (text.length > 0 && text.length < 100) {
                        selectors.push({
                            tag: el.tagName,
                            text: text.substring(0, 80),
                            ariaLabel: label,
                            className: el.className.substring(0, 150),
                            role: el.getAttribute('role'),
                            id: el.id,
                            dataAttrs: Array.from(el.attributes).filter(a => a.name.startsWith('data-')).map(a => a.name + '=' + a.value.substring(0,50)).join(', ')
                        });
                    }
                });
                results.selectorCandidates = selectors;

                // Specifically look for anything mentioning model names
                var modelMentions = [];
                var allText = document.body.querySelectorAll('*');
                for (var i = 0; i < allText.length; i++) {
                    var el = allText[i];
                    var t = (el.innerText || '').trim().toLowerCase();
                    if ((t.includes('flash') || t.includes('thinking') || t.includes('pro') || t.includes('2.5') || t.includes('2.0') || t.includes('gemini')) && t.length < 100 && el.children.length < 5) {
                        modelMentions.push({
                            tag: el.tagName,
                            text: (el.innerText||'').substring(0, 80),
                            className: el.className.substring(0, 150),
                            ariaLabel: el.getAttribute('aria-label') || '',
                            parent: el.parentElement ? el.parentElement.className.substring(0, 100) : ''
                        });
                    }
                }
                results.modelMentions = modelMentions.slice(0, 20);

                return JSON.stringify(results, null, 2);
            })()
            """;
        var result = await InvokeOnStaAsync(() =>
            _window!.WebView.CoreWebView2.ExecuteScriptAsync(script));
        try { return JsonSerializer.Deserialize<string>(result) ?? result; }
        catch { return result; }
    }

    /// <summary>Dumps all text-bearing elements after a response to find the right response container.</summary>
    public async Task<string> DiscoverResponseDomAsync()
    {
        var script = """
            (function() {
                var results = {};

                // Find model turn containers (Gemini typically wraps each turn)
                var turns = [];
                document.querySelectorAll('[data-turn-id], [class*="model-response"], [class*="response-container"], [class*="message-content"]').forEach(function(el) {
                    turns.push({tag: el.tagName, className: el.className.substring(0,150), text: (el.innerText||'').substring(0,200), dataAttrs: Array.from(el.attributes).filter(a => a.name.startsWith('data-')).map(a => a.name + '=' + a.value).join(', ')});
                });
                results.turnContainers = turns.slice(0, 10);

                // Find all elements with 'message' in class
                var msgs = [];
                document.querySelectorAll('[class*="message"]').forEach(function(el) {
                    var text = (el.innerText || '').trim();
                    if (text.length > 10 && text.length < 5000) {
                        msgs.push({tag: el.tagName, className: el.className.substring(0,150), textPreview: text.substring(0,200), childCount: el.children.length});
                    }
                });
                results.messageElements = msgs.slice(0, 15);

                // Find all elements with markdown-like content (code blocks, etc)
                var markdown = [];
                document.querySelectorAll('code, pre, [class*="markdown"], [class*="code-block"], [class*="response"]').forEach(function(el) {
                    markdown.push({tag: el.tagName, className: el.className.substring(0,150), textPreview: (el.innerText||'').substring(0,200), parent: el.parentElement ? el.parentElement.className.substring(0,100) : ''});
                });
                results.markdownElements = markdown.slice(0, 10);

                // Find the Gemini conversation container
                var convos = [];
                document.querySelectorAll('[class*="conversation"], [class*="chat-history"], [class*="turn"]').forEach(function(el) {
                    convos.push({tag: el.tagName, className: el.className.substring(0,150), childCount: el.children.length});
                });
                results.conversationContainers = convos.slice(0, 5);

                // Brute force: find any large text block that looks like an AI response
                var largeText = [];
                document.querySelectorAll('div, section, article').forEach(function(el) {
                    var text = (el.innerText || '').trim();
                    if (text.length > 100 && text.length < 10000 && el.children.length < 50) {
                        // Check it's not a nav, header, or footer
                        var tag = el.closest('nav, header, footer, [role="navigation"], [role="banner"]');
                        if (!tag) {
                            largeText.push({tag: el.tagName, className: el.className.substring(0,150), textPreview: text.substring(0,300), len: text.length});
                        }
                    }
                });
                // Sort by text length descending
                largeText.sort(function(a,b) { return b.len - a.len; });
                results.largeTextBlocks = largeText.slice(0, 10);

                return JSON.stringify(results, null, 2);
            })()
            """;
        var result = await InvokeOnStaAsync(() =>
            _window!.WebView.CoreWebView2.ExecuteScriptAsync(script));
        try { return JsonSerializer.Deserialize<string>(result) ?? result; }
        catch { return result; }
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
