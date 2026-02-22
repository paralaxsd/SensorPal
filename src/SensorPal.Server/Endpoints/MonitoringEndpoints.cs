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

        group.MapGet("/{id}/audio", async (long id, SessionRepository repo, AudioStorage storage,
            ILogger<Log> logger) =>
        {
            logger.LogInformation("Audio requested for session {SessionId}", id);
            var session = await repo.GetCurrentSessionAsync(id);

            if (session is null)
            {
                logger.LogWarning("Audio request: session {SessionId} not found in DB", id);
                return Results.NotFound();
            }

            if (session.BackgroundFile is not { } path)
            {
                logger.LogWarning("Audio request: session {SessionId} has no BackgroundFile", id);
                return Results.NotFound();
            }

            var exists = File.Exists(path);
            logger.LogInformation("Audio request: session {SessionId} → file={Path} exists={Exists}",
                id, path, exists);

            if (!exists) return Results.NotFound();
            return Results.File(path, "audio/mpeg", enableRangeProcessing: true);
        });

        group.MapGet("/level", async (AudioCaptureService capture, SettingsRepository settingsRepo) =>
        {
            var settings = await settingsRepo.GetAsync();
            var eventSince = capture.EventStartedAt.HasValue
                ? new DateTimeOffset(capture.EventStartedAt.Value, TimeSpan.Zero)
                : (DateTimeOffset?)null;
            return Results.Ok(new LiveLevelDto(
                capture.CurrentDb,
                settings.NoiseThresholdDb,
                capture.IsEventActive,
                eventSince,
                capture.ActiveSessionStartedAt,
                capture.ActiveSessionEventCount));
        });
    }

    class Log;

    static MonitoringSessionDto ToDto(MonitoringSession s) => new()
    {
        Id = s.Id,
        StartedAt = new DateTimeOffset(s.StartedAt, TimeSpan.Zero),
        EndedAt = s.EndedAt.HasValue ? new DateTimeOffset(s.EndedAt.Value, TimeSpan.Zero) : null,
        EventCount = s.EventCount,
        IsActive = s.EndedAt is null,
        HasAudio = s.BackgroundFile is not null
    };
}
