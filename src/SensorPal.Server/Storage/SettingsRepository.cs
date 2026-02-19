using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SensorPal.Server.Configuration;
using SensorPal.Server.Entities;

namespace SensorPal.Server.Storage;

sealed class SettingsRepository(
    IDbContextFactory<SensorPalDbContext> factory,
    IOptions<AudioConfig> defaults)
{
    public async Task<AppSettings> GetAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.AppSettings.FindAsync(1)
            ?? new AppSettings
            {
                NoiseThresholdDb = defaults.Value.NoiseThresholdDb,
                PreRollSeconds = defaults.Value.PreRollSeconds,
                PostRollSeconds = defaults.Value.PostRollSeconds,
                BackgroundBitrate = defaults.Value.BackgroundBitrate,
            };
    }

    public async Task SaveAsync(AppSettings settings)
    {
        await using var db = await factory.CreateDbContextAsync();
        var existing = await db.AppSettings.FindAsync(1);

        if (existing is null)
        {
            settings.Id = 1;
            db.AppSettings.Add(settings);
        }
        else
        {
            existing.NoiseThresholdDb = settings.NoiseThresholdDb;
            existing.PreRollSeconds = settings.PreRollSeconds;
            existing.PostRollSeconds = settings.PostRollSeconds;
            existing.BackgroundBitrate = settings.BackgroundBitrate;
        }

        await db.SaveChangesAsync();
    }
}
