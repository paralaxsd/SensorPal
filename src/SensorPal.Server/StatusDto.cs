namespace SensorPal.Server;

sealed class StatusDto
{
    public string Name { get; init; } = string.Empty;
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset Now { get; init; }
    public string Mode { get; init; } = string.Empty;
}

sealed class NoiseEventDto
{
    public long Id { get; init; }
    public DateTimeOffset DetectedAt { get; init; }
    public double PeakDb { get; init; }
    public int DurationMs { get; init; }
    public int ClipDurationMs { get; init; }
    public long BackgroundOffsetMs { get; init; }
    public bool HasClip { get; init; }
}

sealed class MonitoringSessionDto
{
    public long Id { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? EndedAt { get; init; }
    public int EventCount { get; init; }
    public bool IsActive { get; init; }
    public bool HasAudio { get; init; }
}

sealed class LiveSessionStatsDto
{
    public TimeSpan Duration { get; init; }
    public int EventCount { get; init; }
    public double CurrentDb { get; init; }
    public double PeakDb { get; init; }
}

sealed class AudioDeviceDto
{
    public string Name { get; init; } = string.Empty;
    public bool IsDefault { get; init; }
}

sealed record LiveLevelDto(
    double? Db,
    double ThresholdDb,
    bool IsEventActive,
    DateTimeOffset? EventActiveSince,
    DateTimeOffset? ActiveSessionStartedAt,
    int? ActiveSessionEventCount,
    bool IsCalibrating = false);

sealed record SettingsDto(
    double NoiseThresholdDb,
    int PreRollSeconds,
    int PostRollSeconds,
    int BackgroundBitrate);
