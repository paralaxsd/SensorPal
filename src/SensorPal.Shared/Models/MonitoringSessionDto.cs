namespace SensorPal.Shared.Models;

public sealed record MonitoringSessionDto(
    long Id,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    int EventCount,
    bool IsActive,
    bool HasAudio = false);

public sealed record LiveSessionStatsDto(
    TimeSpan Duration,
    int EventCount,
    double CurrentDb,
    double PeakDb);
