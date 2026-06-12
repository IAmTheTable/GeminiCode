// src/GeminiCode/Agent/UsageTracker.cs
namespace GeminiCode.Agent;

/// <summary>Estimates token usage (chars/4) — there is no real Gemini token API in this browser-driven setup.</summary>
public class UsageTracker
{
    private long _pending;

    public int TurnCount { get; private set; }
    public long TotalSentTokens { get; private set; }
    public long TotalReceivedTokens { get; private set; }
    public long LastTurnTokens { get; private set; }
    public long TotalTokens => TotalSentTokens + TotalReceivedTokens;

    public static int Estimate(string? text)
        => string.IsNullOrEmpty(text) ? 0 : (int)Math.Ceiling(text.Length / 4.0);

    public void RecordSent(string? text)
    {
        var t = Estimate(text);
        TotalSentTokens += t;
        _pending += t;
    }

    public void RecordReceived(string? text)
    {
        var t = Estimate(text);
        TotalReceivedTokens += t;
        _pending += t;
        TurnCount++;
        LastTurnTokens = _pending;
        _pending = 0;
    }

    private static string Fmt(long tokens)
        => tokens >= 1000 ? $"{tokens / 1000.0:0.0}k" : tokens.ToString();

    public string Footer()
        => $"ctx ≈ {Fmt(TotalTokens)} tok · turn {TurnCount} · +{Fmt(LastTurnTokens)} this msg (est.)";

    public string Breakdown()
        => $"""
            Usage (estimated, chars/4 — no real token API):
              Turns:          {TurnCount}
              Sent total:     {Fmt(TotalSentTokens)} tok
              Received total: {Fmt(TotalReceivedTokens)} tok
              Context total:  {Fmt(TotalTokens)} tok
              Last turn:      {Fmt(LastTurnTokens)} tok
            """;
}
