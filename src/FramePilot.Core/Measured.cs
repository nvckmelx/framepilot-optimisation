namespace FramePilot.Core;

/// <summary>
/// A value that was either actually obtained from a named source, or is explicitly unavailable.
/// FramePilot never substitutes guesses for missing data: an unavailable value always carries
/// a reason, the source that was queried and the time of the attempt.
/// </summary>
public sealed record Measured<T>(
    T? Value,
    bool IsAvailable,
    string Source,
    DateTimeOffset Timestamp,
    string? UnavailableReason,
    string? Note = null)
{
    public static Measured<T> Of(T value, string source, string? note = null) =>
        new(value, true, source, DateTimeOffset.Now, null, note);

    public static Measured<T> Missing(string reason, string source) =>
        new(default, false, source, DateTimeOffset.Now, reason);

    /// <summary>Returns the value if available, otherwise the supplied fallback (for computations only, never for display).</summary>
    public T? OrDefault(T? fallback = default) => IsAvailable ? Value : fallback;

    public bool TryGet(out T value)
    {
        if (IsAvailable && Value is not null)
        {
            value = Value;
            return true;
        }
        value = default!;
        return false;
    }

    public override string ToString() => IsAvailable ? Value?.ToString() ?? "" : $"unavailable ({UnavailableReason})";
}

public static class Measured
{
    /// <summary>Runs a probe and converts any exception into an explicit unavailable value.</summary>
    public static Measured<T> Try<T>(string source, Func<T?> probe, string? emptyReason = null)
    {
        try
        {
            var value = probe();
            if (value is null)
                return Measured<T>.Missing(emptyReason ?? "Source returned no value", source);
            if (value is string s && string.IsNullOrWhiteSpace(s))
                return Measured<T>.Missing(emptyReason ?? "Source returned an empty value", source);
            return Measured<T>.Of(value, source);
        }
        catch (Exception ex)
        {
            return Measured<T>.Missing($"{ex.GetType().Name}: {ex.Message}", source);
        }
    }
}
