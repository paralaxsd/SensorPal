using SensorPal.Server.Entities;
using SensorPal.Server.Storage;

namespace SensorPal.Server.Endpoints;

static class EventEndpoints
{
    public static void MapEventEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/events");

        group.MapGet("/days", async (EventRepository repo) =>
            await repo.GetDaysWithEventsAsync())
        .WithSummary("List all days that have noise events")
        .WithDescription("Returns a sorted array of dates (yyyy-MM-dd) for which at least one " +
            "noise event exists. Used by the mobile app to implement skip-empty-days navigation.");

        group.MapGet("/", async (string? date, EventRepository repo, TimeProvider time) =>
        {
            var day = date is not null
                ? DateOnly.Parse(date)
                : DateOnly.FromDateTime(time.GetLocalNow().DateTime);

            var evts = await repo.GetEventsByDateAsync(day);
            return evts.Select(ToDto).ToList();
        })
        .WithSummary("List noise events for a date")
        .WithDescription("Returns all detected noise events for the given date (yyyy-MM-dd). " +
            "Defaults to today. Each event includes peak dBFS, duration, and whether a WAV clip is available.");

        group.MapGet("/{id:long}", async (long id, EventRepository repo) =>
        {
            var ev = await repo.GetEventAsync(id);
            return ev is null ? Results.NotFound() : Results.Ok(ToDto(ev));
        })
        .WithSummary("Get a single noise event")
        .WithDescription("Returns metadata for one noise event by its database ID.");

        group.MapGet("/{id:long}/audio", async (long id, EventRepository repo) =>
        {
            var ev = await repo.GetEventAsync(id);
            if (ev?.ClipFile is null || !File.Exists(ev.ClipFile))
                return Results.NotFound();

            var stream = File.OpenRead(ev.ClipFile);
            return Results.File(stream, "audio/wav", Path.GetFileName(ev.ClipFile));
        })
        .WithSummary("Download WAV clip for a noise event")
        .WithDescription("Streams the short WAV audio clip recorded around the noise event, " +
            "including pre-roll and post-roll silence. Returns 404 if the clip file is missing.");

        group.MapDelete("/{id:long}", async (long id, EventRepository repo, ILogger<Log> logger) =>
        {
            var (found, sessionId, sessionNowEmpty, sessionHasBackground) =
                await repo.DeleteEventAsync(id);

            if (!found) return Results.NotFound();

            logger.LogInformation(
                "Deleted event {Id} (session {SessionId}, session now empty: {Empty})",
                id, sessionId, sessionNowEmpty);

            return Results.Ok(new DeleteEventResultDto(sessionId, sessionNowEmpty, sessionHasBackground));
        })
        .WithSummary("Delete a single noise event")
        .WithDescription("Deletes one noise event record and its associated WAV clip file. " +
            "Returns 404 if not found. SessionNowEmpty=true indicates the owning session has no " +
            "remaining clips; SessionHasBackground indicates a background MP3 still exists.");

        group.MapDelete("/", async (string? date, EventRepository repo, TimeProvider time, ILogger<Log> logger) =>
        {
            var day = date is not null
                ? DateOnly.Parse(date)
                : DateOnly.FromDateTime(time.GetLocalNow().DateTime);

            var count = await repo.DeleteEventsByDateAsync(day);
            logger.LogInformation("Deleted {Count} event(s) for {Date}", count, day);

            return Results.Ok(new { Deleted = count });
        })
        .WithSummary("Delete all events for a date")
        .WithDescription("Deletes noise event records and their associated WAV clip files for the given date. " +
            "Defaults to today. Returns the number of deleted records.");
    }

    static NoiseEventDto ToDto(NoiseEvent e) => new()
    {
        Id = e.Id,
        SessionId = e.SessionId,
        DetectedAt = new DateTimeOffset(e.DetectedAt, TimeSpan.Zero),
        PeakDb = e.PeakDb,
        DurationMs = e.DurationMs,
        ClipDurationMs = e.ClipDurationMs,
        BackgroundOffsetMs = e.BackgroundOffsetMs,
        HasClip = e.ClipFile is not null && File.Exists(e.ClipFile)
    };

    class Log;  // placeholder for Log type
}
