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
        var existing = await FindOrPrepareInsertAsync(db, settings);

        if (existing is null)
        {
            // Preserve key if caller already has one; otherwise carry over from cache.
            if (string.IsNullOrEmpty(settings.ApiKey))
                settings.ApiKey = _cache?.ApiKey ?? GenerateKey();
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

        var existing = await FindOrPrepareInsertAsync(db, settings);
        if (existing is { })
            existing.ApiKey = settings.ApiKey;

        await db.SaveChangesAsync();
        logger.LogWarning("API Key generated — copy this into the mobile app Settings: {ApiKey}", settings.ApiKey);
    }

    /// <summary>
    /// Looks up the singleton AppSettings row (Id = 1).
    /// If it does not exist, prepares <paramref name="settings"/> for insertion by setting
    /// its Id and registering it with the context, then returns <see langword="null"/>.
    /// If it does exist, returns the tracked entity; the caller is responsible for mutating it.
    /// The caller must still call <see cref="DbContext.SaveChangesAsync"/> in both cases.
    /// </summary>
    static async Task<AppSettings?> FindOrPrepareInsertAsync(SensorPalDbContext db, AppSettings settings)
    {
        var existing = await db.AppSettings.FindAsync(1);
        if (existing is { }) return existing;
        settings.Id = 1;
        db.AppSettings.Add(settings);
        return null;
    }

    static string GenerateKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
}
