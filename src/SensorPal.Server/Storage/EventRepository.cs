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
        var (start, end) = GetDayRange(date);
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

    public async Task<IReadOnlyList<NoiseEvent>> GetEventsBySessionAsync(long sessionId)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.NoiseEvents
            .Where(e => e.SessionId == sessionId)
            .OrderBy(e => e.DetectedAt)
            .ToListAsync();
    }

    public async Task<DateOnly[]> GetDaysWithEventsAsync()
    {
        await using var db = await factory.CreateDbContextAsync();

        // Load full UTC timestamps — converting .Date first and then ToLocalTime() would
        // yield the wrong local date for events that cross midnight in UTC (e.g. 23:30 UTC
        // = 00:30 the next day locally). We convert each full timestamp instead.
        var utcTimestamps = await db.NoiseEvents
            .Select(e => e.DetectedAt)
            .ToListAsync();

        return [.. utcTimestamps
            .Select(dt => DateOnly.FromDateTime(DateTime.SpecifyKind(dt, DateTimeKind.Utc).ToLocalTime()))
            .Distinct()
            .OrderBy(d => d)];
    }

    public async Task<(bool Found, long SessionId, bool SessionNowEmpty, bool SessionHasBackground)>
        DeleteEventAsync(long id)
    {
        await using var db = await factory.CreateDbContextAsync();
        var ev = await db.NoiseEvents
            .Include(e => e.Session)
            .FirstOrDefaultAsync(e => e.Id == id);

        if (ev is null) return (false, 0, false, false);

        var sessionId = ev.SessionId;
        var clipFile = ev.ClipFile;
        var hasBackground = ev.Session.BackgroundFile is not null &&
            File.Exists(ev.Session.BackgroundFile);

        db.NoiseEvents.Remove(ev);
        await db.SaveChangesAsync();

        if (clipFile is not null && File.Exists(clipFile))
            File.Delete(clipFile);

        var remaining = await db.NoiseEvents.CountAsync(e => e.SessionId == sessionId);

        return (true, sessionId, remaining == 0, hasBackground);
    }

    public async Task<int> DeleteEventsByDateAsync(DateOnly date)
    {
        await using var db = await factory.CreateDbContextAsync();
        var (start, end) = GetDayRange(date);

        var clipFiles = await db.NoiseEvents
            .Where(e => e.DetectedAt >= start && e.DetectedAt <= end && e.ClipFile != null)
            .Select(e => e.ClipFile!)
            .ToListAsync();

        var count = await db.NoiseEvents
            .Where(e => e.DetectedAt >= start && e.DetectedAt <= end)
            .ExecuteDeleteAsync();

        foreach (var file in clipFiles.Where(File.Exists))
            File.Delete(file);

        return count;
    }

    static (DateTime Start, DateTime End) GetDayRange(DateOnly date) => (
        date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime(),
        date.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Local).ToUniversalTime());
}
