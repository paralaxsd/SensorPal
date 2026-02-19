namespace SensorPal.Shared.Models;

public sealed record SettingsDto(
    double NoiseThresholdDb,
    int PreRollSeconds,
    int PostRollSeconds,
    int BackgroundBitrate);
