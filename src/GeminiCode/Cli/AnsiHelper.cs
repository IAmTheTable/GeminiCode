using System.Runtime.InteropServices;

namespace GeminiCode.Cli;

public static class AnsiHelper
{
    public static bool Enabled { get; set; }

    public static string Reset => Enabled ? "\x1b[0m" : "";
    public static string Bold => Enabled ? "\x1b[1m" : "";
    public static string Dim => Enabled ? "\x1b[2m" : "";
    public static string Italic => Enabled ? "\x1b[3m" : "";
    public static string Underline => Enabled ? "\x1b[4m" : "";

    public static string Red => Enabled ? "\x1b[31m" : "";
    public static string Green => Enabled ? "\x1b[32m" : "";
    public static string Yellow => Enabled ? "\x1b[33m" : "";
    public static string Blue => Enabled ? "\x1b[34m" : "";
    public static string Magenta => Enabled ? "\x1b[35m" : "";
    public static string Cyan => Enabled ? "\x1b[36m" : "";
    public static string White => Enabled ? "\x1b[37m" : "";
    public static string Gray => Enabled ? "\x1b[90m" : "";

    public static string BgDarkGray => Enabled ? "\x1b[48;5;236m" : "";

    public static string Wrap(string text, string code) => Enabled ? $"{code}{text}{Reset}" : text;

    /// <summary>Enable ANSI VT processing on Windows. Call once at startup.</summary>
    public static void Initialize()
    {
        try
        {
            var handle = GetStdHandle(-11); // STD_OUTPUT_HANDLE
            if (GetConsoleMode(handle, out uint mode))
            {
                mode |= 0x0004; // ENABLE_VIRTUAL_TERMINAL_PROCESSING
                if (SetConsoleMode(handle, mode))
                {
                    Enabled = true;
                    return;
                }
            }
        }
        catch { }

        // Check TERM or WT_SESSION env vars (Windows Terminal, etc.)
        Enabled = Environment.GetEnvironmentVariable("WT_SESSION") != null
               || Environment.GetEnvironmentVariable("TERM_PROGRAM") != null;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(IntPtr handle, out uint mode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleMode(IntPtr handle, uint mode);
}
