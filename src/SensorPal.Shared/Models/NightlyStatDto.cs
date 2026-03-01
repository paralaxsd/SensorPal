namespace SensorPal.Shared.Models;

public sealed record NightlyStatDto(DateOnly Date, int EventCount, double AvgDb, double PeakDb);
