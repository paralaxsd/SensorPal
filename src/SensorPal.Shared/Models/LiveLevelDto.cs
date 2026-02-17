namespace SensorPal.Shared.Models;

/// <summary>
/// Current input level from the audio capture device.
/// Db is null when capture is not active.
/// </summary>
public sealed record LiveLevelDto(double? Db, double ThresholdDb, bool IsEventActive, DateTimeOffset? EventActiveSince);
