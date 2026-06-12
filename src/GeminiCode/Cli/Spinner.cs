// src/GeminiCode/Cli/Spinner.cs
using System.Diagnostics;

namespace GeminiCode.Cli;

/// <summary>A single-line braille spinner with elapsed seconds. Dispose to stop and clear the line.</summary>
public sealed class Spinner : IDisposable
{
    private static readonly char[] Frames = { '⠋','⠙','⠹','⠸','⠼','⠴','⠦','⠧','⠇','⠏' };
    private readonly string _label;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;
    private readonly bool _ansi;

    private Spinner(string label)
    {
        _label = label;
        _ansi = AnsiHelper.Enabled;
        _loop = Run(_cts.Token);
    }

    public static Spinner Start(string label = "Thinking")
    {
        if (!AnsiHelper.Enabled)
            Console.Write($"{label}... ");
        return new Spinner(label);
    }

    private async Task Run(CancellationToken ct)
    {
        if (!_ansi) return; // static "Thinking..." already printed; no animation
        var sw = Stopwatch.StartNew();
        int i = 0;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var frame = Frames[i++ % Frames.Length];
                Console.Write($"\r{AnsiHelper.Cyan}{frame}{AnsiHelper.Reset} {AnsiHelper.Dim}{_label} {sw.Elapsed.TotalSeconds:0.0}s{AnsiHelper.Reset}");
                await Task.Delay(100, ct);
            }
        }
        catch (TaskCanceledException) { }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _loop.Wait(300); } catch { }
        if (_ansi)
            Console.Write("\r" + new string(' ', Math.Min(Console.WindowWidth - 1, 60)) + "\r");
        else
            Console.WriteLine();
        _cts.Dispose();
    }
}
