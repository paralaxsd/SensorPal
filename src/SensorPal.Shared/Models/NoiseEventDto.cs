namespace SensorPal.Shared.Models;

public sealed record NoiseEventDto(
    long Id,
    DateTimeOffset DetectedAt,
    double PeakDb,
    int DurationMs,
    int ClipDurationMs,
    long BackgroundOffsetMs,
    bool HasClip);
