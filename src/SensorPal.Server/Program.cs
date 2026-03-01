using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Events;
using SensorPal.Server.Configuration;
using SensorPal.Server.Endpoints;
using SensorPal.Server.Services;
using SensorPal.Server.Storage;

namespace SensorPal.Server;

static class Program
{
    public class EntryPoint;

    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            // Resolve appsettings.json from the executable directory, not the
            // working directory, so the server can be started from anywhere.
            ContentRootPath = AppContext.BaseDirectory
        });
        builder.PrepareServices();

        var app = await builder.BuildApplicationAsync();
        await app.RunAsync();
    }

    static void PrepareServices(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        var configuration = builder.Configuration;

        ConfigureLogging(builder);
        var rawStoragePath = ConfigureServices(services, configuration);
        AddServices(services, rawStoragePath);
    }

    static async Task<WebApplication> BuildApplicationAsync(this WebApplicationBuilder builder)
    {
        var app = builder.Build();

        app.Logger.LogInformation("SensorPal Server {Version} [{CommitId}]",
            ThisAssembly.AssemblyShortFileVersion, ThisAssembly.GitCommitIdShort);

        await EnsureAndMigrateDbAsync(app);
        await LogApiKeyAsync(app);

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
            app.MapScalarApiReference();
        }

        app.MapEndpoints();
        return app;
    }

    static void ConfigureLogging(WebApplicationBuilder builder) =>
        builder.Host.UseSerilog((_, config) => config
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .WriteTo.Logger(lc => lc
                .Filter.ByIncludingOnly(e =>
                    e.Properties.TryGetValue("SourceContext", out var sc) &&
                    sc.ToString().Contains("SensorPal"))
                .WriteTo.Console(
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"))
            .WriteTo.File(
                Path.Combine(AppContext.BaseDirectory, "logs", "sensorpal-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}"));

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
        services.AddSingleton<StatsRepository>();

        // Core services
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<MonitoringStateService>();
        // IAudioCaptureService owns the singleton lifecycle (disposal).
        // IHostedService reuses the same instance via cast — no double-dispose.
        services.AddSingleton<IAudioCaptureService, AudioCaptureService>();
        services.AddHostedService(sp => (AudioCaptureService)sp.GetRequiredService<IAudioCaptureService>());
    }

    static async Task EnsureAndMigrateDbAsync(WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IDbContextFactory<SensorPalDbContext>>();
        await using var ctx = await db.CreateDbContextAsync();
        await ctx.Database.MigrateAsync();
        await FixDanglingActiveSessions(app, ctx);
    }

    static async Task FixDanglingActiveSessions(WebApplication app, SensorPalDbContext ctx)
    {
        // Close any sessions left open by a previous crash
        var now = app.Services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;
        var closed = await ctx.MonitoringSessions
            .Where(s => s.EndedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.EndedAt, now));
        if (closed > 0)
            app.Logger.LogWarning("Closed {Count} stale session(s) from previous crash", closed);
    }

    static async Task LogApiKeyAsync(WebApplication app)
    {
        var settings = await app.Services.GetRequiredService<SettingsRepository>().GetAsync();
        app.Logger.LogWarning("API Key (copy into mobile app Settings): {ApiKey}", settings.ApiKey);
    }

    static void MapEndpoints(this WebApplication app)
    {
        // Auth middleware: require Bearer token on all endpoints except /status and dev-only docs.
        // Set "DisableApiKey": true in appsettings.Development.json to bypass auth locally (Scalar, curl).
        var disableApiKey = app.Configuration.GetValue<bool>("DisableApiKey");
        app.Use(async (ctx, next) =>
        {
            if (!disableApiKey)
            {
                var path = ctx.Request.Path.Value ?? "";
                var isPublic = path.Equals("/status", StringComparison.OrdinalIgnoreCase)
                    || path.StartsWith("/openapi", StringComparison.OrdinalIgnoreCase)
                    || path.StartsWith("/scalar", StringComparison.OrdinalIgnoreCase);

                if (!isPublic)
                {
                    var settings = ctx.RequestServices.GetRequiredService<SettingsRepository>();
                    var s = await settings.GetAsync();

                    var bearerOk = ctx.Request.Headers.TryGetValue("Authorization", out var header)
                        && header.ToString() == $"Bearer {s.ApiKey}";

                    // Allow ?token= query param for media streaming endpoints where setting
                    // request headers is not possible (e.g. MediaElement URL source).
                    var tokenOk = ctx.Request.Query.TryGetValue("token", out var queryToken)
                        && queryToken.ToString() == s.ApiKey;

                    if (!bearerOk && !tokenOk)
                    {
                        ctx.Response.StatusCode = 401;
                        return;
                    }
                }
            }

            await next(ctx);
        });

        app.MapStatusEndpoints(app.Services.GetRequiredService<TimeProvider>());
        app.MapMonitoringEndpoints();
        app.MapAudioDeviceEndpoints();
        app.MapEventEndpoints();
        app.MapSettingsEndpoints();
        app.MapStatsEndpoints();
    }
}
