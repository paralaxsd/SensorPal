using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SensorPal.Server.Configuration;
using SensorPal.Server.Entities;

namespace SensorPal.Server.Storage;

sealed class SettingsRepository(
    IDbContextFactory<SensorPalDbContext> factory,
    IOptions<AudioConfig> defaults,
    ILogger<SettingsRepository> logger)
{
    AppSettings? _cache;

    public async Task<AppSettings> GetAsync()
    {
        if (_cache is not null) return _cache;

        await using var db = await factory.CreateDbContextAsync();
        _cache = await db.AppSettings.FindAsync(1)
            ?? new AppSettings
            {
                NoiseThresholdDb = defaults.Value.NoiseThresholdDb,
                PreRollSeconds = defaults.Value.PreRollSeconds,
                PostRollSeconds = defaults.Value.PostRollSeconds,
                BackgroundBitrate = defaults.Value.BackgroundBitrate,
            };
        return _cache;
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
        _cache = settings;

        logger.LogInformation(
            "Settings updated: Threshold={Threshold:F1} dBFS, PreRoll={Pre}s, PostRoll={Post}s, Bitrate={Bitrate} kbps",
            settings.NoiseThresholdDb, settings.PreRollSeconds, settings.PostRollSeconds, settings.BackgroundBitrate);
    }
}
