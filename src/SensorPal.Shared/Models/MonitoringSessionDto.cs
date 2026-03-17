namespace SensorPal.Shared.Models;

public sealed record MonitoringSessionDto(
    long Id,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    int EventCount,
    bool IsActive,
    bool HasAudio = false)
{
    public string? AudioFilePath { get; init; }
    public long? AudioFileSizeBytes { get; init; }
    public int? AudioBitRateKbps { get; init; }

    public bool CanDelete => !IsActive;

    public string StatusText => IsActive
        ? "● Active"
        : EndedAt is { } end ? FormatDuration(end - StartedAt) : "—";

    public string AudioSizeText => AudioFileSizeBytes is { } bytes
        ? bytes >= 1_000_000 ? $"{bytes / 1_000_000.0:F1} MB" : $"{bytes / 1_000.0:F0} KB"
        : string.Empty;

    public string AudioBitRateText => AudioBitRateKbps is { } kbps ? $"{kbps} kbps" : string.Empty;

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
