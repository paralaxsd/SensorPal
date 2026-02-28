using Alba;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using SensorPal.Server;
using SensorPal.Server.Services;
using SensorPal.Server.Storage;
using Xunit;

namespace SensorPal.IntegrationTests;

/// <summary>
/// Shared xunit fixture: spins up the SensorPal server with an in-memory SQLite
/// database, a deterministic FakeTimeProvider, and a no-op audio capture service.
/// One instance is created per test class that uses IClassFixture&lt;AppFixture&gt;.
/// </summary>
public sealed class AppFixture : IAsyncLifetime
{
    readonly string _connStr = $"Data Source=sensorpal_test_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    SqliteConnection? _keepAlive;

    public IAlbaHost Host { get; private set; } = null!;
    public FakeTimeProvider Time { get; } = new();

    public async Task InitializeAsync()
    {
        // Keep one connection open so the named in-memory SQLite DB survives
        // across the multiple connections that DbContextFactory creates.
        _keepAlive = new SqliteConnection(_connStr);
        _keepAlive.Open();

        Host = await AlbaHost.For<Program.EntryPoint>(x =>
        {
            x.ConfigureAppConfiguration((_, cfg) =>
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AudioConfig:StoragePath"] = Path.GetTempPath(),
                    ["DisableApiKey"] = "true",
                }));

            x.ConfigureServices(services =>
            {
                // Replace SQLite DB with in-memory instance
                services.RemoveAll<IDbContextFactory<SensorPalDbContext>>();
                services.RemoveAll<DbContextOptions<SensorPalDbContext>>();
                services.AddDbContextFactory<SensorPalDbContext>(o =>
                    o.UseSqlite(_connStr));

                // Replace real clock with controllable fake
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(Time);

                // Replace WASAPI-backed AudioCaptureService with a no-op.
                // The real service fires Task.Run(StartCaptureAsync) which tries to
                // initialise a WASAPI device — unavailable in CI and in unit test runs.
                // Framework IHostedServices use ImplementationType; only user-added
                // factory registrations (like ours) have ImplementationFactory != null.
                services.RemoveAll<IAudioCaptureService>();

                var factoryHosted = services
                    .Where(d => d.ServiceType == typeof(IHostedService)
                                && d.ImplementationFactory != null)
                    .ToList();
                foreach (var d in factoryHosted)
                    services.Remove(d);

                var nullCapture = new NullAudioCaptureService();
                services.AddSingleton<IAudioCaptureService>(nullCapture);
                services.AddSingleton<IHostedService>(nullCapture);
            });
        });
    }

    /// <summary>
    /// Resets shared server state between tests: stops any active monitoring session
    /// and truncates the sessions and events tables.
    /// Call from IAsyncLifetime.InitializeAsync on the test class when tests in a
    /// class share this fixture and modify server state.
    /// </summary>
    public async Task ResetAsync()
    {
        var state = Host.Services.GetRequiredService<MonitoringStateService>();
        state.Stop();

        await using var scope = Host.Services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<SensorPalDbContext>>();
        await using var ctx = await dbFactory.CreateDbContextAsync();
        await ctx.NoiseEvents.ExecuteDeleteAsync();
        await ctx.MonitoringSessions.ExecuteDeleteAsync();
    }

    public async Task DisposeAsync()
    {
        await Host.DisposeAsync();
        _keepAlive?.Dispose();
    }
}
