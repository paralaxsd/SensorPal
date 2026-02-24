namespace SensorPal.Shared.Models;

public sealed record EventMarkerDto(
    double OffsetSeconds,
    DateTimeOffset DetectedAt,
    double PeakDb);
