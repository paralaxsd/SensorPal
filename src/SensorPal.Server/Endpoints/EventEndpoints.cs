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
        });

        group.MapGet("/{id:long}", async (long id, EventRepository repo) =>
        {
            var ev = await repo.GetEventAsync(id);
            return ev is null ? Results.NotFound() : Results.Ok(ToDto(ev));
        });

        group.MapGet("/{id:long}/audio", async (long id, EventRepository repo) =>
        {
            var ev = await repo.GetEventAsync(id);
            if (ev?.ClipFile is null || !File.Exists(ev.ClipFile))
                return Results.NotFound();

            var stream = File.OpenRead(ev.ClipFile);
            return Results.File(stream, "audio/wav", Path.GetFileName(ev.ClipFile));
        });
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
}
