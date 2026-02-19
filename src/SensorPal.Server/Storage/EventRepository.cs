using Microsoft.EntityFrameworkCore;
using SensorPal.Server.Entities;

namespace SensorPal.Server.Storage;

sealed class EventRepository(IDbContextFactory<SensorPalDbContext> factory)
{
    public async Task<long> SaveEventAsync(long sessionId, DateTime detectedAt,
        double peakDb, int durationMs, int clipDurationMs, long backgroundOffsetMs, string? clipFile)
    {
        await using var db = await factory.CreateDbContextAsync();
        var ev = new NoiseEvent
        {
            SessionId = sessionId,
            DetectedAt = detectedAt,
            PeakDb = peakDb,
            DurationMs = durationMs,
            ClipDurationMs = clipDurationMs,
            BackgroundOffsetMs = backgroundOffsetMs,
            ClipFile = clipFile
        };
        db.NoiseEvents.Add(ev);
        await db.SaveChangesAsync();
        return ev.Id;
    }

    public async Task<IReadOnlyList<NoiseEvent>> GetEventsByDateAsync(DateOnly date)
    {
        await using var db = await factory.CreateDbContextAsync();
        var start = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime();
        var end = date.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Local).ToUniversalTime();
        return await db.NoiseEvents
            .Where(e => e.DetectedAt >= start && e.DetectedAt <= end)
            .OrderBy(e => e.DetectedAt)
            .ToListAsync();
    }

    public async Task UpdateClipFileAsync(long id, string clipFile)
    {
        await using var db = await factory.CreateDbContextAsync();
        await db.NoiseEvents
            .Where(e => e.Id == id)
            .ExecuteUpdateAsync(e => e.SetProperty(x => x.ClipFile, clipFile));
    }

    public async Task<NoiseEvent?> GetEventAsync(long id)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.NoiseEvents.FindAsync(id);
    }
}
