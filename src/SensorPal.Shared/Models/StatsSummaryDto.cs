namespace SensorPal.Shared.Models;

public sealed record StatsSummaryDto(int TotalEvents, int ActiveDays, double AvgPerNight, double PeakDb);
