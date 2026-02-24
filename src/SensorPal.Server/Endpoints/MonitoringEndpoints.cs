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
        })
        .WithSummary("Start a monitoring session")
        .WithDescription("Transitions the server from Idle to Monitoring. " +
            "Starts WASAPI audio capture, continuous background MP3 recording, and noise event detection.");

        group.MapPost("/stop", (MonitoringStateService state) =>
        {
            state.Stop();
            return Results.Ok();
        })
        .WithSummary("Stop the active monitoring session")
        .WithDescription("Transitions from Monitoring back to Idle. " +
            "Finalizes the background MP3 file and persists the session record to the database.");

        group.MapGet("/sessions", async (SessionRepository repo) =>
        {
            var sessions = await repo.GetSessionsAsync();
            return sessions.Select(ToDto).ToList();
        })
        .WithSummary("List all monitoring sessions")
        .WithDescription("Returns all past and active monitoring sessions, newest first. " +
            "HasAudio indicates whether a streamable background MP3 is available.");

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
        })
        .WithSummary("Stream background MP3 for a session")
        .WithDescription("Streams the continuous background MP3 recorded during the monitoring session. " +
            "Supports HTTP Range requests for seeking. Returns 404 if no background file was recorded.");

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
                capture.ActiveSessionEventCount,
                capture.IsCalibrating));
        })
        .WithSummary("Live input level and event state")
        .WithDescription("Returns the current RMS input level in dBFS, the configured threshold, " +
            "whether a noise event is currently active, and live session statistics. " +
            "Db is null when monitoring is not active. Poll at ~150 ms for a responsive meter.");

        group.MapPost("/calibrate/start", (MonitoringStateService state) =>
        {
            if (state.IsMonitoring)
                return Results.Conflict("Stop monitoring before starting calibration.");
            state.StartCalibration();
            return Results.Ok();
        })
        .WithSummary("Start calibration mode")
        .WithDescription("Starts WASAPI capture and RMS measurement without recording or event detection. " +
            "Use the live level endpoint to tune the noise threshold. Only callable from Idle.");

        group.MapPost("/calibrate/stop", (MonitoringStateService state) =>
        {
            state.StopCalibration();
            return Results.Ok();
        })
        .WithSummary("Stop calibration mode")
        .WithDescription("Stops capture and returns to Idle.");

        group.MapGet("/{id}/markers", async (long id, SessionRepository repo, EventRepository eventRepo) =>
        {
            var session = await repo.GetCurrentSessionAsync(id);
            if (session is null) return Results.NotFound();

            var events = await eventRepo.GetEventsBySessionAsync(id);
            var markers = events.Select(e => new EventMarkerDto
            {
                OffsetSeconds = e.BackgroundOffsetMs / 1000.0,
                DetectedAt = new DateTimeOffset(e.DetectedAt, TimeSpan.Zero),
                PeakDb = e.PeakDb
            }).ToList();

            return Results.Ok(markers);
        })
        .WithSummary("Get event markers for a session")
        .WithDescription("Returns the list of noise events as seek markers, each with the time offset " +
            "into the background MP3 and the wall-clock detection time. Use OffsetSeconds to seek the player.");
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
