using SensorPal.Server.Entities;
using SensorPal.Server.Storage;

namespace SensorPal.Server.Endpoints;

static class EventEndpoints
{
    public static void MapEventEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/events");

        group.MapGet("/", async (string? date, EventRepository repo) =>
        {
            var day = date is not null
                ? DateOnly.Parse(date)
                : DateOnly.FromDateTime(DateTime.Now);

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

        group.MapDelete("/", async (string? date, EventRepository repo, ILogger<Log> logger) =>
        {
            var day = date is not null
                ? DateOnly.Parse(date)
                : DateOnly.FromDateTime(DateTime.Now);

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
        DetectedAt = new DateTimeOffset(e.DetectedAt, TimeSpan.Zero),
        PeakDb = e.PeakDb,
        DurationMs = e.DurationMs,
        ClipDurationMs = e.ClipDurationMs,
        BackgroundOffsetMs = e.BackgroundOffsetMs,
        HasClip = e.ClipFile is not null && File.Exists(e.ClipFile)
    };

    class Log;  // placeholder for Log type
}
