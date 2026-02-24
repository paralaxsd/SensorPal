namespace SensorPal.Shared.Models;

public sealed record MonitoringSessionDto(
    long Id,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    int EventCount,
    bool IsActive,
    bool HasAudio = false)
{
    public string StatusText => IsActive
        ? "● Active"
        : EndedAt is { } end ? FormatDuration(end - StartedAt) : "—";

    static string FormatDuration(TimeSpan t) =>
        t.TotalHours >= 1
            ? $"{(int)t.TotalHours}h {t.Minutes:D2}m"
            : $"{(int)t.TotalMinutes}m {t.Seconds:D2}s";
}

public sealed record LiveSessionStatsDto(
    TimeSpan Duration,
    int EventCount,
    double CurrentDb,
    double PeakDb);
