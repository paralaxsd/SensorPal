using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using SensorPal.Server.Configuration;
using SensorPal.Server.Endpoints;
using SensorPal.Server.Services;
using SensorPal.Server.Storage;

namespace SensorPal.Server;

static class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.PrepareServices();

        var app = await builder.BuildApplicationAsync();
        await app.RunAsync();
    }

    static void PrepareServices(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        var configuration = builder.Configuration;

        var rawStoragePath = ConfigureServices(services, configuration);
        AddServices(services, rawStoragePath);
    }

    static async Task<WebApplication> BuildApplicationAsync(this WebApplicationBuilder builder)
    {
        var app = builder.Build();

        app.Logger.LogInformation("SensorPal Server {Version} [{CommitId}]",
            ThisAssembly.AssemblyShortFileVersion, ThisAssembly.GitCommitIdShort);

        await EnsureAndMigrateDbAsync(app);

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
            app.MapScalarApiReference();
        }

        app.MapEndpoints();
        return app;
    }

    static string ConfigureServices(IServiceCollection services, ConfigurationManager configuration)
    {
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default));

        var audioConfigSection = configuration.GetRequiredSection(nameof(AudioConfig));
        services.Configure<AudioConfig>(audioConfigSection);

        var rawStoragePath = audioConfigSection["StoragePath"] ?? "recordings";
        return rawStoragePath;
    }

    static void AddServices(IServiceCollection services, string rawStoragePath)
    {
        services.AddOpenApi();

        // Storage — AudioStorage resolves relative paths from AppContext.BaseDirectory
        var audioStorage = new AudioStorage(rawStoragePath);
        services.AddSingleton(audioStorage);
        services.AddDbContextFactory<SensorPalDbContext>(o =>
            o.UseSqlite($"Data Source={audioStorage.DatabasePath}"));

        // Repositories
        services.AddSingleton<SessionRepository>();
        services.AddSingleton<EventRepository>();
        services.AddSingleton<SettingsRepository>();

        // Core services
        services.AddSingleton<MonitoringStateService>();
        services.AddSingleton<AudioCaptureService>();
        services.AddHostedService(sp => sp.GetRequiredService<AudioCaptureService>());
    }

    static async Task EnsureAndMigrateDbAsync(WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IDbContextFactory<SensorPalDbContext>>();
        await using var ctx = await db.CreateDbContextAsync();
        await ctx.Database.MigrateAsync();

        // Close any sessions left open by a previous crash
        var closed = await ctx.MonitoringSessions
            .Where(s => s.EndedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.EndedAt, DateTime.UtcNow));
        if (closed > 0)
            app.Logger.LogWarning("Closed {Count} stale session(s) from previous crash", closed);
    }

    static void MapEndpoints(this WebApplication app)
    {
        app.MapStatusEndpoints();
        app.MapMonitoringEndpoints();
        app.MapAudioDeviceEndpoints();
        app.MapEventEndpoints();
        app.MapSettingsEndpoints();
    }
}
