namespace SensorPal.Shared.Models;

public sealed record NoiseEventDto(
    long Id,
    DateTimeOffset DetectedAt,
    double PeakDb,
    int DurationMs,
    long BackgroundOffsetMs,
    bool HasClip);
