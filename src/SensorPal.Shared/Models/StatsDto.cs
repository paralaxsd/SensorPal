namespace SensorPal.Shared.Models;

public sealed record StatsDto(
    List<NightlyStatDto> Nightly,
    List<HourlyStatDto> Hourly,
    StatsSummaryDto Summary);
