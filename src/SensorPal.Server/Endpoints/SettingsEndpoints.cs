using SensorPal.Server.Entities;
using SensorPal.Server.Storage;

namespace SensorPal.Server.Endpoints;

static class SettingsEndpoints
{
    public static void MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/settings", async (SettingsRepository repo) =>
        {
            var s = await repo.GetAsync();
            return new SettingsDto(
                s.NoiseThresholdDb, s.PreRollSeconds, s.PostRollSeconds, s.BackgroundBitrate,
                s.AutoStopTime);
        })
        .WithSummary("Get noise detection settings")
        .WithDescription("Returns the current noise detection configuration: threshold (dBFS), " +
            "pre/post-roll durations for WAV clips, and background MP3 bitrate.");

        app.MapPut("/settings", async (SettingsDto dto, SettingsRepository repo) =>
        {
            // Validate AutoStopTime if provided.
            if (dto.AutoStopTime is { } rawTime
                && !TimeOnly.TryParse(rawTime, out _))
            {
                return Results.BadRequest("AutoStopTime must be in HH:mm format.");
            }

            await repo.SaveAsync(new AppSettings
            {
                NoiseThresholdDb = dto.NoiseThresholdDb,
                PreRollSeconds = dto.PreRollSeconds,
                PostRollSeconds = dto.PostRollSeconds,
                BackgroundBitrate = dto.BackgroundBitrate,
                AutoStopTime = dto.AutoStopTime,
            });
            return Results.NoContent();
        })
        .WithSummary("Update noise detection settings")
        .WithDescription("Persists updated settings to the SQLite database. Changes take effect immediately " +
            "on the next noise detection cycle without restarting the server.");
    }
}
