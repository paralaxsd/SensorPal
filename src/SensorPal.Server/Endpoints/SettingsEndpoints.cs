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
            return new SettingsDto(s.NoiseThresholdDb, s.PreRollSeconds, s.PostRollSeconds, s.BackgroundBitrate);
        });

        app.MapPut("/settings", async (SettingsDto dto, SettingsRepository repo) =>
        {
            await repo.SaveAsync(new AppSettings
            {
                NoiseThresholdDb = dto.NoiseThresholdDb,
                PreRollSeconds = dto.PreRollSeconds,
                PostRollSeconds = dto.PostRollSeconds,
                BackgroundBitrate = dto.BackgroundBitrate,
            });
            return Results.NoContent();
        });
    }
}
