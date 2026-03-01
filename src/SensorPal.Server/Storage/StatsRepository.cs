using Microsoft.EntityFrameworkCore;

namespace SensorPal.Server.Storage;

sealed class StatsRepository(IDbContextFactory<SensorPalDbContext> factory)
{
    public async Task<StatsDto> GetStatsAsync(DateOnly from, DateOnly to)
    {
        var utcFrom = from.ToDateTime(TimeOnly.MinValue).ToUniversalTime();
        var utcTo   = to.AddDays(1).ToDateTime(TimeOnly.MinValue).ToUniversalTime();

        await using var db = await factory.CreateDbContextAsync();
        var raw = await db.NoiseEvents
            .Where(e => e.DetectedAt >= utcFrom && e.DetectedAt < utcTo)
            .Select(e => new { e.DetectedAt, e.PeakDb })
            .ToListAsync();

        // Group in memory — avoids timezone-unsafe DB-side date grouping.
        var nightly = raw
            .GroupBy(e => DateOnly.FromDateTime(
                DateTime.SpecifyKind(e.DetectedAt, DateTimeKind.Utc).ToLocalTime()))
            .Select(g => new NightlyStatDto(
                g.Key,
                g.Count(),
                Math.Round(g.Average(e => e.PeakDb), 1),
                Math.Round(g.Max(e => e.PeakDb), 1)))
            .OrderBy(s => s.Date)
            .ToList();

        var hourly = Enumerable.Range(0, 24)
            .Select(h => new HourlyStatDto(h, raw.Count(e =>
                DateTime.SpecifyKind(e.DetectedAt, DateTimeKind.Utc).ToLocalTime().Hour == h)))
            .ToList();

        var summary = new StatsSummaryDto(
            TotalEvents:  raw.Count,
            ActiveDays:   nightly.Count,
            AvgPerNight:  nightly.Count > 0 ? Math.Round(raw.Count / (double)nightly.Count, 1) : 0,
            PeakDb:       raw.Count > 0 ? Math.Round(raw.Max(e => e.PeakDb), 1) : 0);

        return new StatsDto(nightly, hourly, summary);
    }
}
