using System.Security.Cryptography;
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
        var settings = await db.AppSettings.FindAsync(1)
            ?? new AppSettings
            {
                NoiseThresholdDb = defaults.Value.NoiseThresholdDb,
                PreRollSeconds = defaults.Value.PreRollSeconds,
                PostRollSeconds = defaults.Value.PostRollSeconds,
                BackgroundBitrate = defaults.Value.BackgroundBitrate,
            };

        if (string.IsNullOrEmpty(settings.ApiKey))
            await GenerateAndSaveApiKeyAsync(db, settings);

        _cache = settings;
        return _cache;
    }

    public async Task SaveAsync(AppSettings settings)
    {
        await using var db = await factory.CreateDbContextAsync();
        var existing = await db.AppSettings.FindAsync(1);

        if (existing is null)
        {
            settings.Id = 1;
            // Preserve key if caller already has one; otherwise keep existing
            if (string.IsNullOrEmpty(settings.ApiKey))
                settings.ApiKey = _cache?.ApiKey ?? GenerateKey();
            db.AppSettings.Add(settings);
        }
        else
        {
            existing.NoiseThresholdDb = settings.NoiseThresholdDb;
            existing.PreRollSeconds = settings.PreRollSeconds;
            existing.PostRollSeconds = settings.PostRollSeconds;
            existing.BackgroundBitrate = settings.BackgroundBitrate;
            // ApiKey is intentionally NOT updated here — use a dedicated endpoint or DB tool.
            settings.ApiKey = existing.ApiKey;
        }

        await db.SaveChangesAsync();
        _cache = settings;

        logger.LogInformation(
            "Settings updated: Threshold={Threshold:F1} dBFS, PreRoll={Pre}s, PostRoll={Post}s, Bitrate={Bitrate} kbps",
            settings.NoiseThresholdDb, settings.PreRollSeconds, settings.PostRollSeconds, settings.BackgroundBitrate);
    }

    async Task GenerateAndSaveApiKeyAsync(SensorPalDbContext db, AppSettings settings)
    {
        settings.ApiKey = GenerateKey();

        var existing = await db.AppSettings.FindAsync(1);
        if (existing is null)
        {
            settings.Id = 1;
            db.AppSettings.Add(settings);
        }
        else
        {
            existing.ApiKey = settings.ApiKey;
        }

        await db.SaveChangesAsync();
        logger.LogWarning("API Key generated — copy this into the mobile app Settings: {ApiKey}", settings.ApiKey);
    }

    static string GenerateKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
}
