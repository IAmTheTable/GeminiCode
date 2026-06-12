namespace GeminiCode.Browser;

public enum FailureKind { Unknown, UsageLimit, NotAuthenticated, UiBroken }

public record FailureDiagnosis(
    FailureKind Kind,
    IReadOnlyList<string> MissingElements,
    LimitInfo? Limit);

public static class FailureClassifier
{
    /// <summary>Pure classification of a send failure from already-gathered signals.
    /// Precedence: UsageLimit → NotAuthenticated → UiBroken → Unknown.</summary>
    public static FailureDiagnosis Classify(bool authenticated, IReadOnlyDictionary<string, bool> domHealth, LimitInfo? limit)
    {
        if (limit != null)
            return new FailureDiagnosis(FailureKind.UsageLimit, Array.Empty<string>(), limit);

        if (!authenticated)
            return new FailureDiagnosis(FailureKind.NotAuthenticated, Array.Empty<string>(), null);

        var missing = domHealth.Where(kv => !kv.Value).Select(kv => kv.Key).ToList();
        if (missing.Count > 0)
            return new FailureDiagnosis(FailureKind.UiBroken, missing, null);

        return new FailureDiagnosis(FailureKind.Unknown, Array.Empty<string>(), null);
    }
}
