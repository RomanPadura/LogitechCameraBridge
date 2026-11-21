using System.Globalization;

namespace LogitechCameraBridge.Common.Options;

public sealed class ButtonRepeatOptions
{
    public const string SectionName = "ButtonRepeat";
    public const int DefaultRepeatIntervalMs = 100;
    public const int DefaultInitialDelayMs = 400;
    public const int MaximumTimingMs = 60_000;

    public int RepeatIntervalMs { get; private set; } = DefaultRepeatIntervalMs;
    public int InitialDelayMs { get; private set; } = DefaultInitialDelayMs;

    // Read raw configuration values: binding invalid text directly to int would throw at startup.
    public void SetTimings(string? repeatIntervalMs, string? initialDelayMs)
    {
        RepeatIntervalMs = Parse(repeatIntervalMs, DefaultRepeatIntervalMs);
        InitialDelayMs = Parse(initialDelayMs, DefaultInitialDelayMs);
    }

    private static int Parse(string? value, int fallback) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result)
            && result is >= 1 and <= MaximumTimingMs ? result : fallback;
}
