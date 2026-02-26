using Microsoft.EntityFrameworkCore;
using SensorPal.Server.Entities;

namespace SensorPal.Server.Storage;

sealed class SessionRepository(IDbContextFactory<SensorPalDbContext> factory, TimeProvider time)
{
    public async Task<long> StartSessionAsync(string backgroundFile)
    {
        await using var db = await factory.CreateDbContextAsync();
        var session = new MonitoringSession
        {
            StartedAt = time.GetUtcNow().UtcDateTime,
            BackgroundFile = backgroundFile
        };
        db.MonitoringSessions.Add(session);
        await db.SaveChangesAsync();
        return session.Id;
    }

    public async Task EndSessionAsync(long sessionId)
    {
        var now = time.GetUtcNow().UtcDateTime;
        await using var db = await factory.CreateDbContextAsync();
        await db.MonitoringSessions
            .Where(s => s.Id == sessionId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.EndedAt, now));
    }

    public async Task IncrementEventCountAsync(long sessionId)
    {
        await using var db = await factory.CreateDbContextAsync();
        await db.MonitoringSessions
            .Where(s => s.Id == sessionId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.EventCount, x => x.EventCount + 1));
    }

    public async Task<IReadOnlyList<MonitoringSession>> GetSessionsAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.MonitoringSessions
            .OrderByDescending(s => s.Id)
            .ToListAsync();
    }

    public async Task<MonitoringSession?> GetCurrentSessionAsync(long sessionId)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.MonitoringSessions.FindAsync(sessionId);
    }

    public async Task<bool> DeleteSessionAsync(long id)
    {
        await using var db = await factory.CreateDbContextAsync();
        var session = await db.MonitoringSessions
            .Include(s => s.Events)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (session is null) return false;

        var files = session.Events
            .Where(e => e.ClipFile is { })
            .Select(e => e.ClipFile!)
            .ToList();

        if (session.BackgroundFile is { } bg)
            files.Add(bg);

        db.MonitoringSessions.Remove(session);
        await db.SaveChangesAsync();

        foreach (var f in files.Where(File.Exists))
            File.Delete(f);

        return true;
    }
}
