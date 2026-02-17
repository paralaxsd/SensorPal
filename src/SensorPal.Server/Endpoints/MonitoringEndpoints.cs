using Microsoft.Extensions.Options;
using SensorPal.Server.Configuration;
using SensorPal.Server.Entities;
using SensorPal.Server.Services;
using SensorPal.Server.Storage;

namespace SensorPal.Server.Endpoints;

static class MonitoringEndpoints
{
    public static void MapMonitoringEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/monitoring");

        group.MapPost("/start", (MonitoringStateService state) =>
        {
            state.Start();
            return Results.Ok();
        });

        group.MapPost("/stop", (MonitoringStateService state) =>
        {
            state.Stop();
            return Results.Ok();
        });

        group.MapGet("/sessions", async (SessionRepository repo) =>
        {
            var sessions = await repo.GetSessionsAsync();
            return sessions.Select(ToDto).ToList();
        });

        group.MapGet("/level", (AudioCaptureService capture, IOptions<AudioConfig> options) =>
        {
            var eventSince = capture.EventStartedAt.HasValue
                ? new DateTimeOffset(capture.EventStartedAt.Value, TimeSpan.Zero)
                : (DateTimeOffset?)null;
            return Results.Ok(new LiveLevelDto(
                capture.CurrentDb,
                options.Value.NoiseThresholdDb,
                capture.IsEventActive,
                eventSince));
        });
    }

    static MonitoringSessionDto ToDto(MonitoringSession s) => new()
    {
        Id = s.Id,
        StartedAt = s.StartedAt,
        EndedAt = s.EndedAt,
        EventCount = s.EventCount,
        IsActive = s.EndedAt is null
    };
}
